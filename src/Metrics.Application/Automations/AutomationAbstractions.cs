using Metrics.Domain.Entities;

namespace Metrics.Application.Automations;

/// <summary>Raw query parameters as received from the API.</summary>
public record AutomationFilter(DateTime? From, DateTime? To, string? Department, string? Q);

/// <param name="IsStale">Set from the background worker's flags: reporting is overdue for this automation.</param>
public record AutomationCardDto(Guid Id, string Name, string Description, string Department, DateTime LastActivityAt, bool IsStale = false);

public record AutomationDetailDto(
    Guid Id, string Name, string Description, string Client, string Requirement,
    string Department, DateTime CreatedAt, DateTime LastActivityAt);

/// <summary>Normalized, ready-to-query filter: UTC bounds, <c>ToExclusive</c> is an exclusive upper bound.</summary>
public record AutomationQuery(DateTime? From, DateTime? ToExclusive, string? DepartmentLower, string? QLower);

public interface IAutomationRepository
{
    Task<List<Automation>> ListAsync(AutomationQuery query, CancellationToken ct);
    Task<Automation?> FindAsync(Guid id, CancellationToken ct);
    Task<List<string>> DepartmentsAsync(CancellationToken ct);
}

public interface IAutomationService
{
    Task<IReadOnlyList<AutomationCardDto>> ListAsync(AutomationFilter filter, CancellationToken ct);
    Task<AutomationDetailDto> GetAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<string>> DepartmentsAsync(CancellationToken ct);
}
