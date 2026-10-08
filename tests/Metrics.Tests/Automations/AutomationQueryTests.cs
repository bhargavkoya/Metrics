using Metrics.Application.Automations;
using Metrics.Application.Common;
using Metrics.Domain.Entities;
using Metrics.Infrastructure.Automations;
using Metrics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Metrics.Tests.Automations;

public class AutomationNormalizeTests
{
    [Fact]
    public void BareToDate_IncludesWholeDay()
    {
        var q = AutomationService.Normalize(new AutomationFilter(null, new DateTime(2026, 10, 8), null, null));

        Assert.Equal(new DateTime(2026, 10, 9, 0, 0, 0, DateTimeKind.Utc), q.ToExclusive);
    }

    [Fact]
    public void ToWithTime_IsInclusiveOfThatInstant()
    {
        var to = new DateTime(2026, 10, 8, 13, 30, 0);
        var q = AutomationService.Normalize(new AutomationFilter(null, to, null, null));

        Assert.True(q.ToExclusive > DateTime.SpecifyKind(to, DateTimeKind.Utc));
    }

    [Fact]
    public void Dates_AreMarkedUtc()
    {
        var q = AutomationService.Normalize(new AutomationFilter(new DateTime(2026, 1, 1), null, null, null));

        Assert.Equal(DateTimeKind.Utc, q.From!.Value.Kind);
    }

    [Fact]
    public void FromAfterTo_Throws()
    {
        var f = new AutomationFilter(new DateTime(2026, 10, 10), new DateTime(2026, 10, 1), null, null);

        Assert.Throws<ValidationFailedException>(() => AutomationService.Normalize(f));
    }

    [Fact]
    public void SameDay_FromAndTo_IsValid()
    {
        var d = new DateTime(2026, 10, 8);

        var q = AutomationService.Normalize(new AutomationFilter(d, d, null, null));

        Assert.True(q.ToExclusive > q.From);
    }

    [Fact]
    public void BlankTextFilters_BecomeNull_AndOthersAreLowercased()
    {
        var q = AutomationService.Normalize(new AutomationFilter(null, null, "  ", " Risk "));

        Assert.Null(q.DepartmentLower);
        Assert.Equal("risk", q.QLower);
    }
}

public class EfAutomationRepositoryTests : IDisposable
{
    private readonly MetricsDbContext _db;
    private readonly AutomationService _sut;
    private static readonly DateTime Now = new(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);

    public EfAutomationRepositoryTests()
    {
        var opts = new DbContextOptionsBuilder<MetricsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new MetricsDbContext(opts);
        _sut = new AutomationService(new EfAutomationRepository(_db));

        Add("Trade Reconciliation Bot", "Matches trades", "Investment Operations", daysAgo: 2);
        Add("Capital Report Builder", "Weekly pack from ledgers", "Capital Allocation", daysAgo: 10);
        Add("Risk Notifier", "Alerts on limit breaches", "Risk and Compliance", daysAgo: 30);
        Add("Fee Calculator", "Daily fee accruals", "Investment Operations", daysAgo: 60);
        _db.SaveChanges();
    }

    private void Add(string name, string description, string department, int daysAgo) =>
        _db.Automations.Add(new Automation
        {
            Id = Guid.NewGuid(), Name = name, Description = description, Department = department,
            CreatedAt = Now.AddDays(-daysAgo - 30), LastActivityAt = Now.AddDays(-daysAgo)
        });

    public void Dispose() => _db.Dispose();

    private async Task<string[]> Names(AutomationFilter f) =>
        (await _sut.ListAsync(f, default)).Select(c => c.Name).ToArray();

    [Fact]
    public async Task NoFilter_ReturnsAll_MostRecentActivityFirst()
    {
        var names = await Names(new(null, null, null, null));

        Assert.Equal(["Trade Reconciliation Bot", "Capital Report Builder", "Risk Notifier", "Fee Calculator"], names);
    }

    [Fact]
    public async Task DepartmentFilter_IsCaseInsensitive_AndExact()
    {
        var names = await Names(new(null, null, "investment operations", null));

        Assert.Equal(["Trade Reconciliation Bot", "Fee Calculator"], names);
        Assert.Empty(await Names(new(null, null, "investment", null)));
    }

    [Fact]
    public async Task DateRange_FiltersOnLastActivity_Inclusive()
    {
        // Capital Report Builder was active exactly 10 days ago; boundary dates must include it.
        var from = Now.AddDays(-10).Date;
        var to = Now.AddDays(-10).Date;

        var names = await Names(new(from, to, null, null));

        Assert.Equal(["Capital Report Builder"], names);
    }

    [Fact]
    public async Task DateRange_ExcludesOutside()
    {
        var names = await Names(new(Now.AddDays(-35).Date, Now.AddDays(-5).Date, null, null));

        Assert.Equal(["Capital Report Builder", "Risk Notifier"], names);
    }

    [Fact]
    public async Task Search_MatchesNameOrDescription_CaseInsensitive()
    {
        Assert.Equal(["Trade Reconciliation Bot"], await Names(new(null, null, null, "RECONCILIATION")));
        Assert.Equal(["Risk Notifier"], await Names(new(null, null, null, "limit breaches")));
    }

    [Fact]
    public async Task Search_PercentIsLiteral_NotWildcard()
    {
        Assert.Empty(await Names(new(null, null, null, "%")));
    }

    [Fact]
    public async Task CombinedFilters_AreAnded()
    {
        var names = await Names(new(Now.AddDays(-20).Date, null, "Investment Operations", "trade"));

        Assert.Equal(["Trade Reconciliation Bot"], names);
    }

    [Fact]
    public async Task Departments_AreDistinctAndSorted()
    {
        var deps = await _sut.DepartmentsAsync(default);

        Assert.Equal(["Capital Allocation", "Investment Operations", "Risk and Compliance"], deps);
    }

    [Fact]
    public async Task Get_UnknownId_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetAsync(Guid.NewGuid(), default));
    }
}
