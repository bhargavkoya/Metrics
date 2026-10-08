using Metrics.Application.Common;
using Metrics.Application.Metrics;
using Metrics.Domain.Entities;
using Metrics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Metrics.Infrastructure.Metrics;

public class EfMetricRepository(MetricsDbContext db) : IMetricRepository
{
    public Task<bool> AutomationExistsAsync(Guid automationId, CancellationToken ct) =>
        db.Automations.AnyAsync(a => a.Id == automationId, ct);

    public Task<List<MetricDefinition>> ListLiveAsync(Guid automationId, CancellationToken ct) =>
        db.MetricDefinitions.AsNoTracking()
            .Where(m => m.AutomationId == automationId && !m.IsDeleted)
            .OrderBy(m => m.CreatedAt).ThenBy(m => m.Label)
            .ToListAsync(ct);

    public Task<MetricDefinition?> FindLiveAsync(Guid automationId, Guid metricId, CancellationToken ct) =>
        db.MetricDefinitions.FirstOrDefaultAsync(m => m.Id == metricId && m.AutomationId == automationId && !m.IsDeleted, ct);

    public async Task AddAsync(MetricDefinition definition, CancellationToken ct)
    {
        db.MetricDefinitions.Add(definition);
        await BumpVersionAsync(definition.AutomationId, ct);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two concurrent creates with the same label: the partial unique index on live labels rejects the loser.
            db.Entry(definition).State = EntityState.Detached;
            var taken = await db.MetricDefinitions.AnyAsync(m =>
                m.AutomationId == definition.AutomationId && !m.IsDeleted && m.Label == definition.Label, ct);
            if (taken) throw new ConflictException($"A metric labelled \"{definition.Label}\" already exists on this automation.");
            throw;
        }
    }

    public async Task SoftDeleteAsync(MetricDefinition definition, DateTime deletedAt, CancellationToken ct)
    {
        definition.IsDeleted = true;
        definition.DeletedAt = deletedAt;
        await BumpVersionAsync(definition.AutomationId, ct);
        await db.SaveChangesAsync(ct);
    }

    // DataVersion keys the Phase 6 cache. A plain increment is enough here; a lost update under a race
    // would still change the version, which is all invalidation needs.
    private async Task BumpVersionAsync(Guid automationId, CancellationToken ct)
    {
        var automation = await db.Automations.FirstAsync(a => a.Id == automationId, ct);
        automation.DataVersion++;
    }
}
