using Metrics.Application.Automations;
using Metrics.Domain.Entities;
using Metrics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Metrics.Infrastructure.Automations;

public class EfAutomationRepository(MetricsDbContext db) : IAutomationRepository
{
    public Task<List<Automation>> ListAsync(AutomationQuery q, CancellationToken ct)
    {
        var query = db.Automations.AsNoTracking().AsQueryable();

        if (q.From is { } from) query = query.Where(a => a.LastActivityAt >= from);
        if (q.ToExclusive is { } to) query = query.Where(a => a.LastActivityAt < to);
        if (q.DepartmentLower is { } dept) query = query.Where(a => a.Department.ToLower() == dept);
        if (q.QLower is { } text)
            query = query.Where(a => a.Name.ToLower().Contains(text) || a.Description.ToLower().Contains(text));

        return query.OrderByDescending(a => a.LastActivityAt).ThenBy(a => a.Name).ToListAsync(ct);
    }

    public Task<Automation?> FindAsync(Guid id, CancellationToken ct) =>
        db.Automations.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);

    public Task<List<string>> DepartmentsAsync(CancellationToken ct) =>
        db.Automations.AsNoTracking().Select(a => a.Department).Distinct().OrderBy(d => d).ToListAsync(ct);
}
