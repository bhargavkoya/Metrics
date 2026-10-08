using System.Diagnostics;
using Metrics.Application.Caching;
using Metrics.Application.Common;
using Metrics.Application.Logs;
using Metrics.Application.Metrics;
using Metrics.Infrastructure.Caching;
using Moq;

namespace Metrics.Tests.Caching;

public class InProcessChangeNotifierTests
{
    private readonly InProcessChangeNotifier _sut = new();
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();

    [Fact]
    public void WaitersAreNotCompleted_UntilNotified()
    {
        Assert.False(_sut.NextChangeAsync(A).IsCompleted);
    }

    [Fact]
    public async Task Notify_CompletesEveryCurrentWaiter()
    {
        var one = _sut.NextChangeAsync(A);
        var two = _sut.NextChangeAsync(A);

        _sut.Notify(A);

        await one.WaitAsync(TimeSpan.FromSeconds(2));
        await two.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task AfterANotification_TheNextWaiterGetsAFreshPendingSignal()
    {
        var first = _sut.NextChangeAsync(A);
        _sut.Notify(A);
        await first;

        var next = _sut.NextChangeAsync(A);

        Assert.False(next.IsCompleted);
        _sut.Notify(A);
        await next.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Signals_AreIsolatedPerAutomation()
    {
        var a = _sut.NextChangeAsync(A);
        var b = _sut.NextChangeAsync(B);

        _sut.Notify(A);

        Assert.True(a.IsCompleted);
        Assert.False(b.IsCompleted);
    }

    [Fact]
    public void NotifyWithNobodyWaiting_IsHarmless()
    {
        _sut.Notify(A);
        Assert.False(_sut.NextChangeAsync(A).IsCompleted); // earlier notifications are not remembered
    }
}

public class RoiLongPollServiceTests
{
    private static readonly Guid Id = Guid.NewGuid();
    private readonly Mock<IMetricRepository> _metrics = new();
    private readonly Mock<ILogRepository> _logs = new();
    private readonly Mock<IRoiService> _roi = new();
    private readonly InProcessChangeNotifier _notifier = new();
    private long _version = 5;
    private readonly RoiLongPollService _sut;

    public RoiLongPollServiceTests()
    {
        _metrics.Setup(m => m.AutomationExistsAsync(Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _logs.Setup(l => l.GetDataVersionAsync(Id, It.IsAny<CancellationToken>())).ReturnsAsync(() => _version);
        _roi.Setup(r => r.GetAsync(Id, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new RoiDto(Id, _version, null, null, [], []));
        _sut = new RoiLongPollService(_metrics.Object, _logs.Object, _roi.Object, _notifier, TimeProvider.System)
        {
            RecheckInterval = TimeSpan.FromSeconds(30) // long, so only a signal can wake these tests
        };
    }

    [Fact]
    public async Task ReturnsImmediately_WhenTheVersionIsAlreadyNewer()
    {
        var sw = Stopwatch.StartNew();

        var dto = await _sut.WaitForChangeAsync(Id, sinceVersion: 4, 30, TimeSpan.FromSeconds(10), default);

        Assert.Equal(5, dto!.DataVersion);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task ReturnsNull_WhenNothingChangesWithinTheTimeout()
    {
        var sw = Stopwatch.StartNew();

        var dto = await _sut.WaitForChangeAsync(Id, sinceVersion: 5, 30, TimeSpan.FromMilliseconds(300), default);

        Assert.Null(dto);
        Assert.True(sw.Elapsed >= TimeSpan.FromMilliseconds(250));
        _roi.Verify(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WakesPromptly_WhenAWriteSignalsAChange()
    {
        var waiting = _sut.WaitForChangeAsync(Id, sinceVersion: 5, 30, TimeSpan.FromSeconds(20), default);
        await Task.Delay(150);
        Assert.False(waiting.IsCompleted);

        _version = 6;
        _notifier.Notify(Id);

        var dto = await waiting.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(6, dto!.DataVersion);
    }

    [Fact]
    public async Task AChangeCommittingBetweenTakingTheSignalAndReadingTheVersion_IsNotMissed()
    {
        // The version read returns the OLD value, but a write lands (and notifies) just after it. Because the signal was
        // taken before the read, it is already completed, so the loop re-checks at once instead of sleeping.
        var reads = 0;
        _logs.Setup(l => l.GetDataVersionAsync(Id, It.IsAny<CancellationToken>())).ReturnsAsync(() =>
        {
            if (reads++ > 0) return _version;
            var stale = _version;
            _version = 6;
            _notifier.Notify(Id);
            return stale;
        });

        var dto = await _sut.WaitForChangeAsync(Id, sinceVersion: 5, 30, TimeSpan.FromSeconds(20), default)
            .WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(6, dto!.DataVersion);
    }

    [Fact]
    public async Task PeriodicRecheck_CatchesAChangeNobodySignalled()
    {
        var sut = new RoiLongPollService(_metrics.Object, _logs.Object, _roi.Object, _notifier, TimeProvider.System)
        {
            RecheckInterval = TimeSpan.FromMilliseconds(100)
        };
        var waiting = sut.WaitForChangeAsync(Id, sinceVersion: 5, 30, TimeSpan.FromSeconds(20), default);
        await Task.Delay(150);

        _version = 6; // e.g. written by another server instance: no in-process signal

        Assert.Equal(6, (await waiting.WaitAsync(TimeSpan.FromSeconds(3)))!.DataVersion);
    }

    [Fact]
    public async Task Cancellation_StopsTheWait()
    {
        using var cts = new CancellationTokenSource();
        var waiting = _sut.WaitForChangeAsync(Id, sinceVersion: 5, 30, TimeSpan.FromSeconds(20), cts.Token);
        await Task.Delay(100);

        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting.WaitAsync(TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public async Task UnknownAutomation_IsNotFound()
    {
        _metrics.Setup(m => m.AutomationExistsAsync(Id, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        await Assert.ThrowsAsync<NotFoundException>(
            () => _sut.WaitForChangeAsync(Id, 0, 30, TimeSpan.FromSeconds(1), default));
    }

    [Fact]
    public async Task ManyConcurrentWaiters_AreAllWokenByOneChange()
    {
        var waiters = Enumerable.Range(0, 25)
            .Select(_ => _sut.WaitForChangeAsync(Id, sinceVersion: 5, 30, TimeSpan.FromSeconds(20), default))
            .ToList();
        await Task.Delay(150);

        _version = 6;
        _notifier.Notify(Id);

        var results = await Task.WhenAll(waiters).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.All(results, r => Assert.Equal(6, r!.DataVersion));
    }
}

public class RoiWarmerTests
{
    private static readonly Guid Fresh = Guid.NewGuid();
    private static readonly Guid Overdue = Guid.NewGuid();
    private static readonly Guid Broken = Guid.NewGuid();

    private readonly Mock<ILogRepository> _logs = new();
    private readonly Mock<IRoiService> _roi = new();
    private readonly FakeCache _cache = new();
    private readonly RoiSettings _settings = new() { WarmIntervalSeconds = 60 };
    private readonly RoiWarmer _sut;

    public RoiWarmerTests()
    {
        _logs.Setup(l => l.GetAutomationIdsWithLogsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([Fresh, Overdue, Broken]);
        _roi.Setup(r => r.GetAsync(Fresh, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RoiDto(Fresh, 1, null, null, [], [], IsStale: false));
        _roi.Setup(r => r.GetAsync(Overdue, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RoiDto(Overdue, 1, null, null, [], [], IsStale: true));
        _roi.Setup(r => r.GetAsync(Broken, It.IsAny<int>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("boom"));
        _sut = new RoiWarmer(_logs.Object, _roi.Object, new CacheStaleFlagStore(_cache), _settings);
    }

    [Fact]
    public async Task WarmsEachAutomationWithReports_ThroughTheCachedReadAtTheDefaultPoints()
    {
        await _sut.RunOnceAsync(default);

        _roi.Verify(r => r.GetAsync(Fresh, RoiService.DefaultPoints, It.IsAny<CancellationToken>()), Times.Once);
        _roi.Verify(r => r.GetAsync(Overdue, RoiService.DefaultPoints, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FlagsOnlyTheStaleAutomations()
    {
        var result = await _sut.RunOnceAsync(default);

        Assert.Equal([Overdue], result.Stale);
        Assert.Equal(new HashSet<Guid> { Overdue }, await new CacheStaleFlagStore(_cache).GetAsync(default));
    }

    [Fact]
    public async Task OneFailingAutomation_DoesNotStopTheOthers_AndIsReported()
    {
        var result = await _sut.RunOnceAsync(default);

        Assert.Equal(2, result.Warmed);
        var failure = Assert.Single(result.Failures);
        Assert.Equal(Broken, failure.AutomationId);
        Assert.Equal("boom", failure.Error.Message);
    }

    [Fact]
    public async Task FlagsExpireAfterThreeMissedCycles_SoADeadWorkerStopsFlagging()
    {
        await _sut.RunOnceAsync(default);

        Assert.Equal(TimeSpan.FromSeconds(180), Assert.Single(_cache.Sets).Ttl);
    }

    [Fact]
    public async Task WhenNothingIsStale_PreviousFlagsAreCleared()
    {
        await new CacheStaleFlagStore(_cache).SetAsync([Overdue], TimeSpan.FromMinutes(1), default);
        _roi.Setup(r => r.GetAsync(Overdue, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RoiDto(Overdue, 2, null, null, [], [], IsStale: false)); // they reported since

        await _sut.RunOnceAsync(default);

        Assert.Empty(await new CacheStaleFlagStore(_cache).GetAsync(default));
    }

    [Fact]
    public async Task NoAutomationsWithReports_WritesAnEmptyFlagSet()
    {
        _logs.Setup(l => l.GetAutomationIdsWithLogsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var result = await _sut.RunOnceAsync(default);

        Assert.Equal(0, result.Warmed);
        Assert.Empty(await new CacheStaleFlagStore(_cache).GetAsync(default));
    }

    [Fact]
    public async Task Cancellation_Propagates_InsteadOfBeingCountedAsAFailure()
    {
        _roi.Setup(r => r.GetAsync(Fresh, It.IsAny<int>(), It.IsAny<CancellationToken>())).ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(() => _sut.RunOnceAsync(default));
    }
}
