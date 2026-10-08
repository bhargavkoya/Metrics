using Metrics.Application.Common;
using Metrics.Application.Logs;
using Metrics.Domain.Entities;

namespace Metrics.Application.Automations;

public class AutomationService(IAutomationRepository repo, IStaleFlagStore staleFlags) : IAutomationService
{
    public async Task<IReadOnlyList<AutomationCardDto>> ListAsync(AutomationFilter filter, CancellationToken ct)
    {
        var query = Normalize(filter);
        var items = await repo.ListAsync(query, ct);
        var stale = await staleFlags.GetAsync(ct);

        return items
            .Select(a => new AutomationCardDto(a.Id, a.Name, a.Description, a.Department, a.LastActivityAt, stale.Contains(a.Id)))
            .ToList();
    }

    public async Task<AutomationDetailDto> GetAsync(Guid id, CancellationToken ct)
    {
        var a = await repo.FindAsync(id, ct) ?? throw new NotFoundException("Automation not found.");
        return ToDetail(a);
    }

    public async Task<IReadOnlyList<string>> DepartmentsAsync(CancellationToken ct) =>
        await repo.DepartmentsAsync(ct);

    public static AutomationQuery Normalize(AutomationFilter f)
    {
        var from = f.From is { } fr ? AsUtc(fr) : (DateTime?)null;
        DateTime? toExclusive = null;
        if (f.To is { } to)
        {
            var utc = AsUtc(to);
            // A bare date ("2026-10-08") means the whole day, so the bound becomes the next midnight.
            toExclusive = utc.TimeOfDay == TimeSpan.Zero ? utc.AddDays(1) : utc.AddTicks(1);
        }

        if (from is not null && toExclusive is not null && from >= toExclusive)
            throw new ValidationFailedException("to", "'To' must be on or after 'From'.");

        return new AutomationQuery(
            from,
            toExclusive,
            string.IsNullOrWhiteSpace(f.Department) ? null : f.Department.Trim().ToLowerInvariant(),
            string.IsNullOrWhiteSpace(f.Q) ? null : f.Q.Trim().ToLowerInvariant());
    }

    // Npgsql only accepts Kind=Utc for timestamptz; query-string dates arrive Unspecified.
    private static DateTime AsUtc(DateTime d) => d.Kind switch
    {
        DateTimeKind.Utc => d,
        DateTimeKind.Local => d.ToUniversalTime(),
        _ => DateTime.SpecifyKind(d, DateTimeKind.Utc)
    };

    private static AutomationDetailDto ToDetail(Automation a) =>
        new(a.Id, a.Name, a.Description, a.Client, a.Requirement, a.Department, a.CreatedAt, a.LastActivityAt);
}
