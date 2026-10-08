using Metrics.Application.Common;
using Metrics.Application.Formulas;
using Metrics.Application.Logs;
using Metrics.Application.Metrics;
using Metrics.Domain;
using Metrics.Domain.Entities;
using Moq;

namespace Metrics.Tests.Logs;

internal static class Defs
{
    public static MetricDefinition Input(string label, MetricValueType type, string? currency = null) => new()
    {
        Id = Guid.NewGuid(), Label = label, Kind = MetricKind.Input, ValueType = type, CurrencyCode = currency
    };

    public static MetricDefinition Computed(string label, string formula, MetricValueType type, string? currency = null) => new()
    {
        Id = Guid.NewGuid(), Label = label, Kind = MetricKind.Computed, ValueType = type, FormulaText = formula, CurrencyCode = currency
    };
}

public class LogSnapshotBuilderTests
{
    private static readonly FormulaEngine Engine = new();
    private static readonly Guid AutomationId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime At = new(2030, 3, 4, 9, 0, 0, DateTimeKind.Utc);

    private readonly MetricDefinition _manual = Defs.Input("Manual", MetricValueType.Duration);
    private readonly MetricDefinition _auto = Defs.Input("Auto", MetricValueType.Duration);
    private readonly MetricDefinition _cost = Defs.Input("Cost", MetricValueType.Currency, "EUR");
    private readonly MetricDefinition _runs = Defs.Input("Runs", MetricValueType.Number);

    private MetricLog Build(IReadOnlyList<MetricDefinition> live, Dictionary<Guid, decimal> values) =>
        LogSnapshotBuilder.Build(AutomationId, UserId, At, live, values, Engine);

    [Fact]
    public void BuildsOneRowPerInput_PlusOnePerComputed_WithFullSnapshots()
    {
        var saved = Defs.Computed("Saved", "[Manual] - [Auto]", MetricValueType.Duration);
        var costPerRun = Defs.Computed("Cost x runs", "[Cost] * [Runs]", MetricValueType.Currency, "EUR");

        var log = Build([_manual, _auto, _cost, _runs, saved, costPerRun],
            new() { [_manual.Id] = 3600, [_auto.Id] = 600, [_cost.Id] = 2.5m, [_runs.Id] = 4 });

        Assert.Equal(AutomationId, log.AutomationId);
        Assert.Equal(UserId, log.ReportedBy);
        Assert.Equal(At, log.ReportedAt);
        Assert.Equal(6, log.Values.Count);
        Assert.All(log.Values, v => Assert.Equal(log.Id, v.MetricLogId));

        var inputRow = log.Values.Single(v => v.MetricDefinitionId == _cost.Id);
        Assert.Equal(LogValueRole.Input, inputRow.Role);
        Assert.Equal("Cost", inputRow.LabelSnapshot);
        Assert.Equal(MetricValueType.Currency, inputRow.ValueTypeSnapshot);
        Assert.Equal("EUR", inputRow.CurrencyCodeSnapshot);
        Assert.Null(inputRow.FormulaSnapshot);
        Assert.Equal(2.5m, inputRow.Value);

        var savedRow = log.Values.Single(v => v.MetricDefinitionId == saved.Id);
        Assert.Equal(LogValueRole.Computed, savedRow.Role);
        Assert.Equal(3000m, savedRow.Value);
        Assert.Equal("[Manual] - [Auto]", savedRow.FormulaSnapshot);
        Assert.Equal(MetricValueType.Duration, savedRow.ValueTypeSnapshot);

        var costRow = log.Values.Single(v => v.MetricDefinitionId == costPerRun.Id);
        Assert.Equal(10m, costRow.Value);
        Assert.Equal("EUR", costRow.CurrencyCodeSnapshot);
    }

    [Fact]
    public void ComputedValue_IsNull_WhenDataMakesItUndefined_ButTheLogIsStillBuilt()
    {
        var rate = Defs.Computed("Per run", "[Manual] / [Runs]", MetricValueType.Duration);

        var log = Build([_manual, _runs, rate], new() { [_manual.Id] = 100, [_runs.Id] = 0 });

        var row = log.Values.Single(v => v.MetricDefinitionId == rate.Id);
        Assert.Null(row.Value);
        Assert.Equal("[Manual] / [Runs]", row.FormulaSnapshot);
        Assert.Equal(2, log.Values.Count(v => v.Role == LogValueRole.Input));
    }

    [Fact]
    public void NoComputedMetrics_MeansInputRowsOnly()
    {
        var log = Build([_manual, _runs], new() { [_manual.Id] = 1, [_runs.Id] = 2 });

        Assert.Equal(2, log.Values.Count);
        Assert.All(log.Values, v => Assert.Equal(LogValueRole.Input, v.Role));
    }

    [Fact]
    public void PercentageTimesCurrency_UsesTheScaledEvaluation()
    {
        var rate = Defs.Input("Rate", MetricValueType.Percentage);
        var share = Defs.Computed("Share", "[Cost] * [Rate]", MetricValueType.Currency, "EUR");

        var log = Build([_cost, rate, share], new() { [_cost.Id] = 200, [rate.Id] = 25 });

        Assert.Equal(50m, log.Values.Single(v => v.MetricDefinitionId == share.Id).Value);
    }
}

public class LogServiceTests
{
    private static readonly Guid AutomationId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2030, 7, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IMetricRepository> _metrics = new();
    private readonly Mock<ILogRepository> _logs = new();
    private readonly List<MetricDefinition> _live = [];
    private MetricLog? _saved;
    private DateTime? _activity;
    private readonly LogService _sut;

    private readonly MetricDefinition _records = Defs.Input("Records", MetricValueType.Number);
    private readonly MetricDefinition _time = Defs.Input("Time", MetricValueType.Duration);
    private readonly MetricDefinition _rate = Defs.Input("Rate", MetricValueType.Percentage);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    public LogServiceTests()
    {
        _live.AddRange([_records, _time, _rate]);
        _metrics.Setup(m => m.AutomationExistsAsync(AutomationId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _metrics.Setup(m => m.ListLiveAsync(AutomationId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _live.ToList());
        _logs.Setup(l => l.AddAsync(It.IsAny<MetricLog>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Callback<MetricLog, DateTime, CancellationToken>((log, at, _) => { _saved = log; _activity = at; })
            .Returns(Task.CompletedTask);
        _logs.Setup(l => l.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new LogWithReporter(_saved!, "Alice"));
        _sut = new LogService(_metrics.Object, _logs.Object, new FormulaEngine(), new FixedClock(Now));
    }

    private ReportLogRequest Full(decimal records = 10, decimal time = 60, decimal rate = 5) => new(
    [
        new ReportedValue(_records.Id, records), new ReportedValue(_time.Id, time), new ReportedValue(_rate.Id, rate)
    ]);

    private static async Task<ValidationFailedException> Rejected(Task t) =>
        await Assert.ThrowsAsync<ValidationFailedException>(() => t);

    [Fact]
    public async Task Report_StoresSnapshot_WithServerTimestamp_AndReturnsReporterName()
    {
        _live.Add(Defs.Computed("Doubled", "[Records] * 2", MetricValueType.Number));

        var dto = await _sut.ReportAsync(AutomationId, UserId, Full(records: 21), default);

        Assert.Equal(Now.UtcDateTime, _saved!.ReportedAt);
        Assert.Equal(Now.UtcDateTime, _activity);
        Assert.Equal(UserId, _saved.ReportedBy);
        Assert.Equal("Alice", dto.ReportedBy);
        Assert.Equal(4, dto.Values.Count);
        Assert.Equal(42m, dto.Values.Single(v => v.Label == "Doubled").Value);
        Assert.Equal(LogValueRole.Input, dto.Values[0].Role); // inputs listed before computed
    }

    [Fact]
    public async Task Report_NeverTouchesDefinitions()
    {
        await _sut.ReportAsync(AutomationId, UserId, Full(), default);

        _metrics.Verify(m => m.AddAsync(It.IsAny<MetricDefinition>(), It.IsAny<CancellationToken>()), Times.Never);
        _metrics.Verify(m => m.SoftDeleteAsync(It.IsAny<MetricDefinition>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MissingInput_IsRejected_NamingTheMetric_AndNothingSaved()
    {
        var req = new ReportLogRequest([new ReportedValue(_records.Id, 1), new ReportedValue(_time.Id, 1)]);

        var ex = await Rejected(_sut.ReportAsync(AutomationId, UserId, req, default));

        Assert.Contains("Rate", Assert.Single(ex.Errors[_rate.Id.ToString()]));
        Assert.Null(_saved);
    }

    [Fact]
    public async Task UnknownMetricId_ComputedId_AndDuplicates_AreRejected()
    {
        var computed = Defs.Computed("Doubled", "[Records] * 2", MetricValueType.Number);
        _live.Add(computed);
        var unknown = Guid.NewGuid();

        var ex = await Rejected(_sut.ReportAsync(AutomationId, UserId, new ReportLogRequest(
        [
            new ReportedValue(_records.Id, 1), new ReportedValue(_records.Id, 2),
            new ReportedValue(_time.Id, 1), new ReportedValue(_rate.Id, 1),
            new ReportedValue(unknown, 5), new ReportedValue(computed.Id, 9)
        ]), default));

        Assert.Contains("more than once", ex.Errors[_records.Id.ToString()][0]);
        Assert.Contains("Not an input metric", ex.Errors[unknown.ToString()][0]);
        Assert.Contains("Not an input metric", ex.Errors[computed.Id.ToString()][0]);
        Assert.Null(_saved);
    }

    [Fact]
    public async Task NullValues_AreRejected()
    {
        var ex = await Rejected(_sut.ReportAsync(AutomationId, UserId, new ReportLogRequest(null), default));

        Assert.Contains("values", ex.Errors.Keys);
    }

    [Fact]
    public async Task EmptyValues_AreRejected()
    {
        var ex = await Rejected(_sut.ReportAsync(AutomationId, UserId, new ReportLogRequest([]), default));

        Assert.Contains("values", ex.Errors.Keys);
    }

    [Fact]
    public async Task NoInputMetricsDefined_IsRejected()
    {
        _live.Clear();

        var ex = await Rejected(_sut.ReportAsync(AutomationId, UserId, new ReportLogRequest([]), default));

        Assert.Contains("Define at least one input metric", Assert.Single(ex.Errors["values"]));
    }

    [Fact]
    public async Task NegativeDuration_IsRejected_ButNegativeNumberPercentageAndCurrencyAreAllowed()
    {
        var ex = await Rejected(_sut.ReportAsync(AutomationId, UserId, Full(time: -1), default));
        Assert.Contains("negative", ex.Errors[_time.Id.ToString()][0]);

        await _sut.ReportAsync(AutomationId, UserId, Full(records: -5, rate: -2.5m), default);
        Assert.Equal(-5m, _saved!.Values.Single(v => v.MetricDefinitionId == _records.Id).Value);
    }

    [Fact]
    public async Task TooManyDecimals_AndTooLarge_AreRejected_ButTrailingZerosAreFine()
    {
        var ex1 = await Rejected(_sut.ReportAsync(AutomationId, UserId, Full(records: 0.12345678901m), default));
        Assert.Contains("decimal places", ex1.Errors[_records.Id.ToString()][0]);

        var ex2 = await Rejected(_sut.ReportAsync(AutomationId, UserId, Full(records: 1e17m), default));
        Assert.Contains("too large", ex2.Errors[_records.Id.ToString()][0]);

        await _sut.ReportAsync(AutomationId, UserId, Full(records: 0.1234567890m), default);     // exactly 10 places
        await _sut.ReportAsync(AutomationId, UserId, Full(records: 5.00000000000000m), default); // trailing zeros only
        await _sut.ReportAsync(AutomationId, UserId, Full(records: 99_999_999_999_999_999m), default); // just under 10^17
    }

    [Fact]
    public async Task ErrorsForSeveralMetrics_AreReportedTogether()
    {
        var ex = await Rejected(_sut.ReportAsync(AutomationId, UserId, Full(records: 0.123456789012m, time: -4), default));

        Assert.Equal(2, ex.Errors.Count);
    }

    [Fact]
    public async Task UnknownAutomation_IsNotFound()
    {
        var other = Guid.NewGuid();
        _metrics.Setup(m => m.AutomationExistsAsync(other, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.ReportAsync(other, UserId, Full(), default));
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.ListAsync(other, 1, 10, default));
    }

    [Theory]
    [InlineData(0, 0, 1, 1)]
    [InlineData(-3, 1000, 1, 100)]
    [InlineData(2, 25, 2, 25)]
    public async Task List_ClampsPaging(int page, int size, int expectedPage, int expectedSize)
    {
        _logs.Setup(l => l.GetPageAsync(AutomationId, expectedPage, expectedSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync(([], 0));

        var result = await _sut.ListAsync(AutomationId, page, size, default);

        Assert.Equal(expectedPage, result.Page);
        Assert.Equal(expectedSize, result.PageSize);
    }
}

public class RoiServiceTests
{
    private static readonly Guid AutomationId = Guid.NewGuid();
    private readonly Mock<IMetricRepository> _metrics = new();
    private readonly Mock<ILogRepository> _logs = new();
    private readonly List<MetricDefinition> _live = [];
    private List<LogWithReporter> _recent = [];
    private readonly RoiService _sut;

    private readonly MetricDefinition _records = Defs.Input("Records", MetricValueType.Number);
    private readonly MetricDefinition _doubled = Defs.Computed("Doubled", "[Records] * 2", MetricValueType.Number);

    public RoiServiceTests()
    {
        _live.AddRange([_records, _doubled]);
        _metrics.Setup(m => m.AutomationExistsAsync(AutomationId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _metrics.Setup(m => m.ListLiveAsync(AutomationId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _live.ToList());
        _logs.Setup(l => l.GetRecentAsync(AutomationId, It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => _recent);
        _logs.Setup(l => l.GetDataVersionAsync(AutomationId, It.IsAny<CancellationToken>())).ReturnsAsync(7);
        _sut = new RoiService(_metrics.Object, _logs.Object);
    }

    private static LogWithReporter Log(DateTime at, string reporter, params (MetricDefinition Def, decimal? Value)[] rows)
    {
        var log = new MetricLog { Id = Guid.NewGuid(), AutomationId = AutomationId, ReportedAt = at };
        foreach (var (def, value) in rows)
            log.Values.Add(new MetricLogValue
            {
                MetricDefinitionId = def.Id, Value = value, LabelSnapshot = def.Label, ValueTypeSnapshot = def.ValueType,
                Role = def.Kind == MetricKind.Input ? LogValueRole.Input : LogValueRole.Computed
            });
        return new LogWithReporter(log, reporter);
    }

    private static readonly DateTime T1 = new(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task NoLogs_GivesLiveMetricsWithNoValues_AndNoSeriesPoints()
    {
        var roi = await _sut.GetAsync(AutomationId, 30, default);

        Assert.Null(roi.AsOf);
        Assert.Equal(7, roi.DataVersion);
        Assert.All(roi.Current, c => Assert.False(c.HasValue));
        Assert.All(roi.Series, s => Assert.Empty(s.Points));
    }

    [Fact]
    public async Task Current_IsTheLatestLogsValues_AndSeriesAreOldestFirst()
    {
        _recent =
        [
            Log(T1.AddDays(2), "Bob", (_records, 30), (_doubled, 60)),   // newest first, as the repository returns them
            Log(T1.AddDays(1), "Alice", (_records, 20), (_doubled, 40)),
            Log(T1, "Alice", (_records, 10), (_doubled, 20)),
        ];

        var roi = await _sut.GetAsync(AutomationId, 30, default);

        Assert.Equal(T1.AddDays(2), roi.AsOf);
        Assert.Equal("Bob", roi.ReportedBy);
        Assert.Equal(60m, roi.Current.Single(c => c.Label == "Doubled").Value);
        Assert.Equal([10m, 20m, 30m], roi.Series.Single(s => s.Label == "Records").Points.Select(p => p.Value));
        Assert.Equal([T1, T1.AddDays(1), T1.AddDays(2)], roi.Series[0].Points.Select(p => p.ReportedAt));
    }

    [Fact]
    public async Task DeletedMetrics_DropOutOfCurrentAndSeries_ButStayInTheLogs()
    {
        var removed = Defs.Computed("Removed", "[Records] * 3", MetricValueType.Number);
        _recent = [Log(T1, "Alice", (_records, 10), (_doubled, 20), (removed, 30))];

        var roi = await _sut.GetAsync(AutomationId, 30, default);

        Assert.DoesNotContain(roi.Current, c => c.MetricDefinitionId == removed.Id);
        Assert.DoesNotContain(roi.Series, s => s.MetricDefinitionId == removed.Id);
        Assert.Equal(3, _recent[0].Log.Values.Count); // the stored log is untouched
    }

    [Fact]
    public async Task MetricAddedAfterTheLatestLog_HasNoValueYet_AndNoBackfilledPoints()
    {
        var late = Defs.Computed("Tripled", "[Records] * 3", MetricValueType.Number);
        _live.Add(late);
        _recent = [Log(T1, "Alice", (_records, 10), (_doubled, 20))];

        var roi = await _sut.GetAsync(AutomationId, 30, default);

        var figure = roi.Current.Single(c => c.MetricDefinitionId == late.Id);
        Assert.False(figure.HasValue);
        Assert.Empty(roi.Series.Single(s => s.MetricDefinitionId == late.Id).Points);
    }

    [Fact]
    public async Task NullComputedValue_IsReportedAsHasValueWithNull()
    {
        _recent = [Log(T1, "Alice", (_records, 0), (_doubled, null))];

        var figure = (await _sut.GetAsync(AutomationId, 30, default)).Current.Single(c => c.Label == "Doubled");

        Assert.True(figure.HasValue);
        Assert.Null(figure.Value);
    }

    [Fact]
    public async Task SeriesAreKeyedByDefinitionId_SoARecreatedLabelDoesNotMixHistories()
    {
        var oldDoubled = Defs.Computed("Doubled", "[Records] * 2", MetricValueType.Number); // deleted earlier, same label
        _recent =
        [
            Log(T1.AddDays(1), "Alice", (_records, 20), (_doubled, 999)),
            Log(T1, "Alice", (_records, 10), (oldDoubled, 20)),
        ];

        var series = (await _sut.GetAsync(AutomationId, 30, default)).Series.Single(s => s.MetricDefinitionId == _doubled.Id);

        Assert.Equal([999m], series.Points.Select(p => p.Value));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(10_000, 200)]
    [InlineData(45, 45)]
    public async Task Points_AreClamped(int requested, int expected)
    {
        await _sut.GetAsync(AutomationId, requested, default);

        _logs.Verify(l => l.GetRecentAsync(AutomationId, expected, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UnknownAutomation_IsNotFound()
    {
        var other = Guid.NewGuid();
        _metrics.Setup(m => m.AutomationExistsAsync(other, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetAsync(other, 30, default));
    }
}
