using Metrics.Application.Caching;
using Metrics.Application.Common;
using Metrics.Application.Logs;
using Moq;

namespace Metrics.Tests.Caching;

public class CachedRoiServiceTests
{
    private static readonly Guid Id = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2030, 6, 30, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IRoiService> _inner = new();
    private readonly Mock<ILogRepository> _versions = new();
    private readonly FakeCache _cache = new();
    private readonly CacheTrace _trace = new();
    private readonly RoiSettings _settings = new() { CacheTtlMinutes = 10, StaleAfterDays = 14 };
    private long _version = 5;
    private DateTime? _asOf = Now.UtcDateTime.AddDays(-1);
    private readonly CachedRoiService _sut;

    public CachedRoiServiceTests()
    {
        _versions.Setup(v => v.GetDataVersionAsync(Id, It.IsAny<CancellationToken>())).ReturnsAsync(() => _version);
        _inner.Setup(i => i.GetAsync(Id, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Dto(_version, _asOf));
        _sut = new CachedRoiService(_inner.Object, _versions.Object, _cache, _settings, new AdjustableClock(Now), _trace);
    }

    private static RoiDto Dto(long version, DateTime? asOf) => new(Id, version, asOf, "Alice", [], []);

    private void InnerCalls(int expected) =>
        _inner.Verify(i => i.GetAsync(Id, It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Exactly(expected));

    [Fact]
    public async Task FirstReadIsAMiss_ThenAHit_WithoutRebuilding()
    {
        await _sut.GetAsync(Id, 30, default);
        Assert.Equal(CacheTrace.Miss, _trace.Status);

        var second = await _sut.GetAsync(Id, 30, default);

        Assert.Equal(CacheTrace.Hit, _trace.Status);
        Assert.Equal(5, second.DataVersion);
        InnerCalls(1);
    }

    [Fact]
    public async Task Key_ContainsTheDataVersion_SoAWriteMakesTheNextReadAMiss()
    {
        await _sut.GetAsync(Id, 30, default);
        _version = 6; // a report or a metric change bumped the version

        await _sut.GetAsync(Id, 30, default);

        Assert.Equal(CacheTrace.Miss, _trace.Status);
        InnerCalls(2);
        Assert.Contains(CachedRoiService.Key(Id, 5, 30), _cache.Store.Keys); // the old entry is simply never asked for again
        Assert.Contains(CachedRoiService.Key(Id, 6, 30), _cache.Store.Keys);
    }

    [Fact]
    public async Task StaleVersionEntry_IsNeverServedAsCurrent()
    {
        var old = Dto(5, _asOf) with { ReportedBy = "OLD" };
        _cache.Store[CachedRoiService.Key(Id, 5, 30)] = old;
        _version = 6;
        _inner.Setup(i => i.GetAsync(Id, 30, It.IsAny<CancellationToken>())).ReturnsAsync(Dto(6, _asOf) with { ReportedBy = "NEW" });

        var result = await _sut.GetAsync(Id, 30, default);

        Assert.Equal("NEW", result.ReportedBy);
    }

    [Fact]
    public async Task Key_ContainsThePointsParameter_AndPointsAreClamped()
    {
        await _sut.GetAsync(Id, 30, default);
        await _sut.GetAsync(Id, 10, default);
        await _sut.GetAsync(Id, 0, default);
        await _sut.GetAsync(Id, 10_000, default);

        InnerCalls(4);
        foreach (var p in new[] { 30, 10, 1, 200 })
            Assert.Contains(CachedRoiService.Key(Id, 5, p), _cache.Store.Keys);
    }

    [Fact]
    public async Task PayloadIsStoredUnderTheVersionItWasBuiltFrom_EvenIfNewerThanTheOneRead()
    {
        // A write landed between reading the version (5) and building the payload (which saw 6).
        _inner.Setup(i => i.GetAsync(Id, 30, It.IsAny<CancellationToken>())).ReturnsAsync(Dto(6, _asOf));

        await _sut.GetAsync(Id, 30, default);

        Assert.Contains(CachedRoiService.Key(Id, 6, 30), _cache.Store.Keys);
        Assert.DoesNotContain(CachedRoiService.Key(Id, 5, 30), _cache.Store.Keys);
    }

    [Fact]
    public async Task EntriesExpire_AfterTheConfiguredTtl()
    {
        _settings.CacheTtlMinutes = 3;

        await _sut.GetAsync(Id, 30, default);

        Assert.Equal(TimeSpan.FromMinutes(3), Assert.Single(_cache.Sets).Ttl);
    }

    [Fact]
    public async Task WhenTheCacheIsDown_TheDatabaseServesEveryRead_AndNothingFails()
    {
        _cache.Down = true;

        var a = await _sut.GetAsync(Id, 30, default);
        var b = await _sut.GetAsync(Id, 30, default);

        Assert.Equal(CacheTrace.Bypass, _trace.Status);
        Assert.Equal(5, a.DataVersion);
        Assert.Equal(5, b.DataVersion);
        InnerCalls(2);
        Assert.Empty(_cache.Sets);
    }

    [Fact]
    public async Task Staleness_IsComputedPerRequest_AndNeverStoredInTheCache()
    {
        _asOf = Now.UtcDateTime.AddDays(-20);

        var result = await _sut.GetAsync(Id, 30, default);

        Assert.True(result.IsStale);
        var stored = (RoiDto)_cache.Store[CachedRoiService.Key(Id, 5, 30)];
        Assert.False(stored.IsStale);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(14, false)] // exactly at the threshold is not yet stale
    [InlineData(15, true)]
    public async Task Staleness_FollowsTheThreshold(int daysSinceReport, bool expected)
    {
        _asOf = Now.UtcDateTime.AddDays(-daysSinceReport);

        Assert.Equal(expected, (await _sut.GetAsync(Id, 30, default)).IsStale);
    }

    [Fact]
    public async Task NeverReported_IsNotStale()
    {
        _asOf = null;

        Assert.False((await _sut.GetAsync(Id, 30, default)).IsStale);
    }

    [Fact]
    public async Task HitsAlsoGetFreshStaleness()
    {
        _asOf = Now.UtcDateTime.AddDays(-20);
        await _sut.GetAsync(Id, 30, default);
        _settings.StaleAfterDays = 30; // the threshold changed; the cached payload must not freeze the old answer

        var hit = await _sut.GetAsync(Id, 30, default);

        Assert.Equal(CacheTrace.Hit, _trace.Status);
        Assert.False(hit.IsStale);
    }

    [Fact]
    public async Task UnknownAutomation_PropagatesNotFound_AndCachesNothing()
    {
        _inner.Setup(i => i.GetAsync(Id, It.IsAny<int>(), It.IsAny<CancellationToken>())).ThrowsAsync(new NotFoundException("nope"));

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetAsync(Id, 30, default));

        Assert.Empty(_cache.Store);
    }
}

public class StalenessPolicyTests
{
    private static readonly DateTime Now = new(2030, 1, 15, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void NeverReported_IsNeverStale() => Assert.False(StalenessPolicy.IsStale(null, Now, 14));

    [Fact]
    public void ExactlyAtTheThreshold_IsNotStale() => Assert.False(StalenessPolicy.IsStale(Now.AddDays(-14), Now, 14));

    [Fact]
    public void JustPastTheThreshold_IsStale() => Assert.True(StalenessPolicy.IsStale(Now.AddDays(-14).AddSeconds(-1), Now, 14));

    [Fact]
    public void ThresholdOfOneDay_Works() => Assert.True(StalenessPolicy.IsStale(Now.AddDays(-2), Now, 1));
}
