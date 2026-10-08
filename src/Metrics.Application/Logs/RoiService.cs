using Metrics.Application.Common;
using Metrics.Application.Metrics;

namespace Metrics.Application.Logs;

public class RoiService(IMetricRepository metrics, ILogRepository logs) : IRoiService
{
    public const int DefaultPoints = 30;
    public const int MaxPoints = 200;

    public async Task<RoiDto> GetAsync(Guid automationId, int points, CancellationToken ct)
    {
        if (!await metrics.AutomationExistsAsync(automationId, ct))
            throw new NotFoundException("Automation not found.");

        points = Math.Clamp(points, 1, MaxPoints);
        var live = await metrics.ListLiveAsync(automationId, ct);
        var recent = await logs.GetRecentAsync(automationId, points, ct); // newest first
        var version = await logs.GetDataVersionAsync(automationId, ct);

        var latest = recent.FirstOrDefault();

        // Current figures: the latest log's values, limited to metrics that are still live. A deleted metric drops
        // out going forward; a metric created after the latest log has no value until the next report.
        var current = live.Select(d =>
        {
            var row = latest?.Log.Values.FirstOrDefault(v => v.MetricDefinitionId == d.Id);
            return new CurrentFigureDto(d.Id, d.Label, d.Kind, d.ValueType, d.CurrencyCode, d.FormulaText, row is not null, row?.Value);
        }).ToList();

        // Series are keyed by definition id (not label), so a deleted-and-recreated label never mixes two definitions.
        // Only logs that actually contain the metric contribute a point: older logs are never backfilled.
        var ascending = Enumerable.Reverse(recent).ToList();
        var series = live.Select(d => new SeriesDto(
            d.Id, d.Label, d.Kind, d.ValueType, d.CurrencyCode,
            ascending
                .Select(l => (l.Log, Row: l.Log.Values.FirstOrDefault(v => v.MetricDefinitionId == d.Id)))
                .Where(x => x.Row is not null)
                .Select(x => new SeriesPointDto(x.Log.Id, x.Log.ReportedAt, x.Row!.Value))
                .ToList())).ToList();

        return new RoiDto(automationId, version, latest?.Log.ReportedAt, latest?.Reporter, current, series);
    }
}
