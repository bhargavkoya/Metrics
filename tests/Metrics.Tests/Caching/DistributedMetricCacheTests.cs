using Metrics.Application.Caching;
using Metrics.Application.Logs;
using Metrics.Domain;
using Metrics.Infrastructure.Caching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Metrics.Tests.Caching;

/// <summary>An IDistributedCache that fails on demand and counts calls that reach it.</summary>
internal sealed class ScriptedDistributedCache : IDistributedCache
{
    private readonly MemoryDistributedCache _inner = new(Options.Create(new MemoryDistributedCacheOptions()));
    public Func<Exception?> FailWith { get; set; } = () => null;
    public int Calls { get; private set; }
    public DistributedCacheEntryOptions? LastOptions { get; private set; }

    private void Check()
    {
        Calls++;
        if (FailWith() is { } ex) throw ex;
    }

    public byte[]? Get(string key) { Check(); return _inner.Get(key); }
    public Task<byte[]?> GetAsync(string key, CancellationToken token = default) { Check(); return _inner.GetAsync(key, token); }
    public void Set(string key, byte[] value, DistributedCacheEntryOptions options) { Check(); LastOptions = options; _inner.Set(key, value, options); }
    public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
    { Check(); LastOptions = options; return _inner.SetAsync(key, value, options, token); }
    public void Refresh(string key) => _inner.Refresh(key);
    public Task RefreshAsync(string key, CancellationToken token = default) => _inner.RefreshAsync(key, token);
    public void Remove(string key) { Check(); _inner.Remove(key); }
    public Task RemoveAsync(string key, CancellationToken token = default) { Check(); return _inner.RemoveAsync(key, token); }

    public void SeedRaw(string key, byte[] bytes) => _inner.Set(key, bytes);
}

public class DistributedMetricCacheTests
{
    private readonly ScriptedDistributedCache _backing = new();
    private readonly AdjustableClock _clock = new(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly DistributedMetricCache _sut;

    public DistributedMetricCacheTests()
    {
        _sut = new DistributedMetricCache(_backing, _clock, NullLogger<DistributedMetricCache>.Instance);
    }

    private static RoiDto SampleRoi() => new(
        Guid.NewGuid(), 42, new DateTime(2030, 1, 1, 9, 30, 0, DateTimeKind.Utc), "Alice",
        [
            new CurrentFigureDto(Guid.NewGuid(), "Cost saved", MetricKind.Computed, MetricValueType.Currency, "USD", "[A] - [B]", true, 145.2500000000m),
            new CurrentFigureDto(Guid.NewGuid(), "Per run", MetricKind.Computed, MetricValueType.Number, null, "[A] / [B]", true, null),
            new CurrentFigureDto(Guid.NewGuid(), "New one", MetricKind.Input, MetricValueType.Duration, null, null, false, null),
        ],
        [new SeriesDto(Guid.NewGuid(), "Cost saved", MetricKind.Computed, MetricValueType.Currency, "USD",
            [new SeriesPointDto(Guid.NewGuid(), new DateTime(2030, 1, 1, 9, 30, 0, DateTimeKind.Utc), 145.25m)])]);

    [Fact]
    public async Task RoundTrip_PreservesEnumsDecimalsNullsAndDates()
    {
        var roi = SampleRoi();

        Assert.True(await _sut.SetAsync("k", roi, TimeSpan.FromMinutes(5), default));
        var hit = await _sut.GetAsync<RoiDto>("k", default);

        Assert.Equal(CacheOutcome.Hit, hit.Outcome);
        var back = hit.Value!;
        Assert.Equal(roi.DataVersion, back.DataVersion);
        Assert.Equal(roi.AsOf, back.AsOf);
        Assert.Equal(MetricValueType.Currency, back.Current[0].ValueType);
        Assert.Equal(MetricKind.Computed, back.Current[0].Kind);
        Assert.Equal(145.25m, back.Current[0].Value);
        Assert.Null(back.Current[1].Value);
        Assert.True(back.Current[1].HasValue);
        Assert.False(back.Current[2].HasValue);
        Assert.Equal(roi.Series[0].Points[0].LogId, back.Series[0].Points[0].LogId);
    }

    [Fact]
    public async Task AbsentKey_IsAMiss()
    {
        Assert.Equal(CacheOutcome.Miss, (await _sut.GetAsync<RoiDto>("nope", default)).Outcome);
    }

    [Fact]
    public async Task Set_PassesTheTtlAsAnAbsoluteExpiry()
    {
        await _sut.SetAsync("k", SampleRoi(), TimeSpan.FromMinutes(7), default);

        Assert.Equal(TimeSpan.FromMinutes(7), _backing.LastOptions!.AbsoluteExpirationRelativeToNow);
    }

    [Fact]
    public async Task Remove_DeletesTheEntry()
    {
        await _sut.SetAsync("k", SampleRoi(), TimeSpan.FromMinutes(5), default);

        await _sut.RemoveAsync("k", default);

        Assert.Equal(CacheOutcome.Miss, (await _sut.GetAsync<RoiDto>("k", default)).Outcome);
    }

    [Fact]
    public async Task UnreadableEntry_IsAMiss_NotAFailure_AndDoesNotTriggerTheBackoff()
    {
        _backing.SeedRaw("bad", "this is not json"u8.ToArray());

        Assert.Equal(CacheOutcome.Miss, (await _sut.GetAsync<RoiDto>("bad", default)).Outcome);
        var callsBefore = _backing.Calls;
        await _sut.GetAsync<RoiDto>("bad", default);

        Assert.True(_backing.Calls > callsBefore, "the cache must still be consulted after an unreadable entry");
    }

    [Fact]
    public async Task WhenTheBackingCacheFails_CallersGetUnavailable_NotAnException()
    {
        _backing.FailWith = () => new InvalidOperationException("redis down");

        Assert.Equal(CacheOutcome.Unavailable, (await _sut.GetAsync<RoiDto>("k", default)).Outcome);
        Assert.False(await _sut.SetAsync("k", SampleRoi(), TimeSpan.FromMinutes(1), default));
        await _sut.RemoveAsync("k", default); // must not throw
    }

    [Fact]
    public async Task AfterAFailure_TheCacheIsSkippedForTheBackoffWindow_ThenRetried()
    {
        _backing.FailWith = () => new InvalidOperationException("redis down");
        await _sut.GetAsync<RoiDto>("k", default);
        var callsAfterFailure = _backing.Calls;

        // Inside the window: no calls reach the backing cache, so requests don't each wait out the connect timeout.
        _clock.Advance(TimeSpan.FromSeconds(10));
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(CacheOutcome.Unavailable, (await _sut.GetAsync<RoiDto>("k", default)).Outcome);
            Assert.False(await _sut.SetAsync("k", SampleRoi(), TimeSpan.FromMinutes(1), default));
        }
        Assert.Equal(callsAfterFailure, _backing.Calls);

        // Window over and Redis healthy again: the cache is used again.
        _clock.Advance(DistributedMetricCache.BackoffAfterFailure);
        _backing.FailWith = () => null;
        Assert.Equal(CacheOutcome.Miss, (await _sut.GetAsync<RoiDto>("k", default)).Outcome);
        Assert.True(await _sut.SetAsync("k", SampleRoi(), TimeSpan.FromMinutes(1), default));
    }

    [Fact]
    public async Task StillFailingAfterTheWindow_StartsAnotherWindow()
    {
        _backing.FailWith = () => new InvalidOperationException("redis down");
        await _sut.GetAsync<RoiDto>("k", default);
        _clock.Advance(DistributedMetricCache.BackoffAfterFailure + TimeSpan.FromSeconds(1));
        await _sut.GetAsync<RoiDto>("k", default); // retried, fails again
        var calls = _backing.Calls;

        await _sut.GetAsync<RoiDto>("k", default); // back inside a fresh window

        Assert.Equal(calls, _backing.Calls);
    }

    [Fact]
    public async Task Cancellation_IsNotTreatedAsACacheFailure()
    {
        _backing.FailWith = () => new OperationCanceledException();

        await Assert.ThrowsAsync<OperationCanceledException>(() => _sut.GetAsync<RoiDto>("k", default));

        _backing.FailWith = () => null;
        Assert.Equal(CacheOutcome.Miss, (await _sut.GetAsync<RoiDto>("k", default)).Outcome); // no backoff was started
    }
}

public class StaleFlagStoreTests
{
    private readonly FakeCache _cache = new();
    private readonly CacheStaleFlagStore _sut;

    public StaleFlagStoreTests() => _sut = new CacheStaleFlagStore(_cache);

    [Fact]
    public async Task RoundTrips_AndStoresWithTheGivenTtl()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        await _sut.SetAsync([a, b], TimeSpan.FromSeconds(180), default);

        Assert.Equal(new HashSet<Guid> { a, b }, await _sut.GetAsync(default));
        Assert.Equal(TimeSpan.FromSeconds(180), Assert.Single(_cache.Sets).Ttl);
    }

    [Fact]
    public async Task ExpiredOrAbsentFlags_MeanNothingIsStale()
    {
        Assert.Empty(await _sut.GetAsync(default));
    }

    [Fact]
    public async Task WhenTheCacheIsDown_NothingIsFlagged_RatherThanAnythingWrong()
    {
        await _sut.SetAsync([Guid.NewGuid()], TimeSpan.FromSeconds(60), default);
        _cache.Down = true;

        Assert.Empty(await _sut.GetAsync(default));
    }

    [Fact]
    public async Task EmptySet_ClearsPreviousFlags()
    {
        await _sut.SetAsync([Guid.NewGuid()], TimeSpan.FromSeconds(60), default);

        await _sut.SetAsync([], TimeSpan.FromSeconds(60), default);

        Assert.Empty(await _sut.GetAsync(default));
    }
}
