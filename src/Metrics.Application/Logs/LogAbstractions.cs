using Metrics.Domain;
using Metrics.Domain.Entities;

namespace Metrics.Application.Logs;

/// <summary>
/// Values use one canonical unit per type: Number raw, Percentage in percent points (12.5 = 12.5%),
/// Currency as the amount in the metric's own currency, Duration in seconds.
/// </summary>
public record ReportedValue(Guid MetricId, decimal Value);

public record ReportLogRequest(IReadOnlyList<ReportedValue>? Values);

public record LogValueDto(
    Guid MetricDefinitionId, string Label, MetricValueType ValueType, string? CurrencyCode,
    LogValueRole Role, decimal? Value, string? Formula);

public record LogDto(Guid Id, DateTime ReportedAt, string ReportedBy, IReadOnlyList<LogValueDto> Values);

public record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

/// <summary>A live metric's latest figure. HasValue is false when the latest log predates the metric.</summary>
public record CurrentFigureDto(
    Guid MetricDefinitionId, string Label, MetricKind Kind, MetricValueType ValueType,
    string? CurrencyCode, string? Formula, bool HasValue, decimal? Value);

public record SeriesPointDto(Guid LogId, DateTime ReportedAt, decimal? Value);

public record SeriesDto(
    Guid MetricDefinitionId, string Label, MetricKind Kind, MetricValueType ValueType,
    string? CurrencyCode, IReadOnlyList<SeriesPointDto> Points);

/// <param name="DataVersion">Bumps on every write that affects this payload; used for cache keys and long polling.</param>
public record RoiDto(
    Guid AutomationId, long DataVersion, DateTime? AsOf, string? ReportedBy,
    IReadOnlyList<CurrentFigureDto> Current, IReadOnlyList<SeriesDto> Series);

public record LogWithReporter(MetricLog Log, string Reporter);

public interface ILogRepository
{
    /// <summary>Adds the log and, in the same save, sets the automation's LastActivityAt and bumps DataVersion.</summary>
    Task AddAsync(MetricLog log, DateTime activityAt, CancellationToken ct);

    Task<LogWithReporter?> GetAsync(Guid logId, CancellationToken ct);

    /// <summary>Newest first.</summary>
    Task<(List<LogWithReporter> Items, int Total)> GetPageAsync(Guid automationId, int page, int pageSize, CancellationToken ct);

    /// <summary>The most recent <paramref name="take"/> logs, newest first.</summary>
    Task<List<LogWithReporter>> GetRecentAsync(Guid automationId, int take, CancellationToken ct);

    Task<long> GetDataVersionAsync(Guid automationId, CancellationToken ct);
}

public interface ILogService
{
    Task<LogDto> ReportAsync(Guid automationId, Guid userId, ReportLogRequest request, CancellationToken ct);
    Task<PagedResult<LogDto>> ListAsync(Guid automationId, int page, int pageSize, CancellationToken ct);
}

public interface IRoiService
{
    /// <param name="points">How many of the most recent logs feed the chart series.</param>
    Task<RoiDto> GetAsync(Guid automationId, int points, CancellationToken ct);
}
