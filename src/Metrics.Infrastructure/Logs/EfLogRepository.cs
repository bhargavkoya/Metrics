using Metrics.Application.Logs;
using Metrics.Domain.Entities;
using Metrics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Metrics.Infrastructure.Logs;

public class EfLogRepository(MetricsDbContext db) : ILogRepository
{
    public async Task AddAsync(MetricLog log, DateTime activityAt, CancellationToken ct)
    {
        db.MetricLogs.Add(log);

        // Same save as the log: the catalog date filter sees the activity, and the ROI cache key changes.
        var automation = await db.Automations.FirstAsync(a => a.Id == log.AutomationId, ct);
        automation.LastActivityAt = activityAt;
        automation.DataVersion++;

        await db.SaveChangesAsync(ct);
    }

    public async Task<LogWithReporter?> GetAsync(Guid logId, CancellationToken ct)
    {
        var log = await db.MetricLogs.AsNoTracking().Include(l => l.Values).FirstOrDefaultAsync(l => l.Id == logId, ct);
        if (log is null) return null;
        return (await WithReportersAsync([log], ct))[0];
    }

    public async Task<(List<LogWithReporter> Items, int Total)> GetPageAsync(
        Guid automationId, int page, int pageSize, CancellationToken ct)
    {
        var query = db.MetricLogs.AsNoTracking().Where(l => l.AutomationId == automationId);
        var total = await query.CountAsync(ct);
        var logs = await query
            .Include(l => l.Values)
            .OrderByDescending(l => l.ReportedAt).ThenByDescending(l => l.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(ct);
        return (await WithReportersAsync(logs, ct), total);
    }

    public async Task<List<LogWithReporter>> GetRecentAsync(Guid automationId, int take, CancellationToken ct)
    {
        var logs = await db.MetricLogs.AsNoTracking()
            .Where(l => l.AutomationId == automationId)
            .Include(l => l.Values)
            .OrderByDescending(l => l.ReportedAt).ThenByDescending(l => l.Id)
            .Take(take)
            .ToListAsync(ct);
        return await WithReportersAsync(logs, ct);
    }

    public Task<long> GetDataVersionAsync(Guid automationId, CancellationToken ct) =>
        db.Automations.AsNoTracking().Where(a => a.Id == automationId).Select(a => a.DataVersion).FirstOrDefaultAsync(ct);

    public Task<List<Guid>> GetAutomationIdsWithLogsAsync(CancellationToken ct) =>
        db.MetricLogs.AsNoTracking().Select(l => l.AutomationId).Distinct().ToListAsync(ct);

    // Reporter names come from a second query rather than a join, which keeps the ordered, paged entity query simple.
    private async Task<List<LogWithReporter>> WithReportersAsync(List<MetricLog> logs, CancellationToken ct)
    {
        var ids = logs.Select(l => l.ReportedBy).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name, ct);
        return logs.Select(l => new LogWithReporter(l, names.GetValueOrDefault(l.ReportedBy, "Unknown"))).ToList();
    }
}
