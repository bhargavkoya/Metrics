using Metrics.Application.Auth;
using Metrics.Application.Formulas;
using Metrics.Application.Logs;
using Metrics.Application.Metrics;
using Metrics.Domain;
using Metrics.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Metrics.Infrastructure.Persistence;

public class SeedOptions
{
    public const string SectionName = "Seed";
    public string DevPassword { get; set; } = "";
}

/// <summary>Development-only: applies migrations and inserts demo users. Idempotent.</summary>
public static class DevDataSeeder
{
    private static readonly (string Email, string Name, Team Team)[] Users =
    [
        ("tech.alice@demo.local", "Alice (Technical)", Team.Technical),
        ("tech.bob@demo.local", "Bob (Technical)", Team.Technical),
        ("biz.carol@demo.local", "Carol (Business)", Team.Business),
        ("biz.dave@demo.local", "Dave (Business)", Team.Business),
    ];

    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MetricsDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var seed = scope.ServiceProvider.GetRequiredService<IOptions<SeedOptions>>().Value;

        await db.Database.MigrateAsync(ct);

        foreach (var (email, name, team) in Users)
        {
            if (await db.Users.AnyAsync(u => u.Email == email, ct)) continue;
            db.Users.Add(new User
            {
                Id = Guid.NewGuid(),
                Email = email,
                Name = name,
                Team = team,
                PasswordHash = hasher.Hash(seed.DevPassword)
            });
        }
        await SeedAutomationsAsync(db, ct);
        await db.SaveChangesAsync(ct);
        await SeedDemoMetricsAsync(scope.ServiceProvider, db, ct);
    }

    private const string DemoAutomation = "Trade Reconciliation Bot";

    /// <summary>
    /// Gives one automation typed metrics, two formulas and ~12 backdated logs. Definitions go through the real
    /// MetricService and logs through the real snapshot builder, so seed data obeys exactly the same rules as live data.
    /// </summary>
    private static async Task SeedDemoMetricsAsync(IServiceProvider sp, MetricsDbContext db, CancellationToken ct)
    {
        var automation = await db.Automations.FirstOrDefaultAsync(a => a.Name == DemoAutomation, ct);
        var alice = await db.Users.FirstOrDefaultAsync(u => u.Email == "tech.alice@demo.local", ct);
        if (automation is null || alice is null) return;
        // Soft-deleted definitions don't count; any live definition or any existing log means someone already set this up.
        if (await db.MetricDefinitions.AnyAsync(m => m.AutomationId == automation.Id && !m.IsDeleted, ct) ||
            await db.MetricLogs.AnyAsync(l => l.AutomationId == automation.Id, ct)) return;

        var metrics = sp.GetRequiredService<IMetricService>();
        Task<MetricDefinitionDto> Input(string label, MetricValueType type, string? currency = null) =>
            metrics.CreateAsync(automation.Id, alice.Id, new CreateMetricRequest(label, MetricKind.Input, type, currency, null), ct);
        Task<MetricDefinitionDto> Computed(string label, string formula) =>
            metrics.CreateAsync(automation.Id, alice.Id, new CreateMetricRequest(label, MetricKind.Computed, null, null, formula), ct);

        var records = await Input("Records processed", MetricValueType.Number);
        var manualTime = await Input("Manual time per run", MetricValueType.Duration);
        var autoTime = await Input("Automated time per run", MetricValueType.Duration);
        var manualCost = await Input("Manual cost per run", MetricValueType.Currency, "USD");
        var autoCost = await Input("Automated cost per run", MetricValueType.Currency, "USD");
        await Computed("Time saved per run", "[Manual time per run] - [Automated time per run]");
        await Computed("Cost saved per run", "[Manual cost per run] - [Automated cost per run]");

        var live = await sp.GetRequiredService<IMetricRepository>().ListLiveAsync(automation.Id, ct);
        var logs = sp.GetRequiredService<ILogRepository>();
        var engine = sp.GetRequiredService<IFormulaEngine>();

        // Oldest first, so LastActivityAt ends up at the newest log. A fixed seed keeps the demo data stable.
        int[] daysAgo = [31, 28, 25, 22, 19, 16, 13, 10, 7, 5, 3, 2];
        var rnd = new Random(42);
        for (var i = 0; i < daysAgo.Length; i++)
        {
            var progress = (decimal)i / (daysAgo.Length - 1); // 0 -> 1: the automation gets faster and cheaper over time
            var values = new Dictionary<Guid, decimal>
            {
                [records.Id] = Math.Round(3000 + 3000 * progress) + rnd.Next(-150, 150),
                [manualTime.Id] = 7200 + rnd.Next(-300, 300),
                [autoTime.Id] = Math.Round(1800 - 900 * progress) + rnd.Next(-60, 60),
                [manualCost.Id] = 180 + rnd.Next(-8, 8),
                [autoCost.Id] = Math.Round(60 - 30 * progress) + rnd.Next(-3, 3),
            };
            var at = DateTime.UtcNow.Date.AddDays(-daysAgo[i]).AddHours(9);
            var log = LogSnapshotBuilder.Build(automation.Id, alice.Id, at, live, values, engine);
            await logs.AddAsync(log, at, ct);
        }
    }

    // Days-ago offsets spread LastActivityAt so the date-range filter has something to show.
    private static readonly (string Name, string Description, string Client, string Requirement, string Department, int DaysAgo)[] Automations =
    [
        ("Trade Reconciliation Bot",
         "Matches executed trades against custodian statements and flags breaks.",
         "Global Custody Operations",
         "Replace the daily manual tick-and-tie of trade confirmations; breaks must be surfaced before the 10:00 settlement cut-off.",
         "Investment Operations", 2),
        ("Capital Allocation Report Builder",
         "Assembles the weekly capital allocation pack from source ledgers.",
         "Treasury and Capital Planning",
         "Automate consolidation of desk-level allocations into the weekly pack, removing spreadsheet copy-paste.",
         "Capital Allocation", 5),
        ("Risk Limit Breach Notifier",
         "Monitors exposure limits and alerts desk heads on breaches.",
         "Market Risk Management",
         "Detect limit breaches within minutes of position updates and notify the responsible desk head with context.",
         "Risk and Compliance", 11),
        ("Fund Fee Accrual Calculator",
         "Computes daily management and performance fee accruals per fund.",
         "Fund Accounting",
         "Eliminate manual fee accrual workbooks; accruals must tie to the fund administrator's figures.",
         "Investment Operations", 24),
        ("Regulatory Filing Pre-Check",
         "Validates regulatory filing data for completeness before submission.",
         "Regulatory Reporting",
         "Catch missing or inconsistent fields in quarterly filings before they reach compliance review.",
         "Risk and Compliance", 38),
        ("Allocation Scenario Modeller",
         "Generates what-if allocation scenarios across asset classes.",
         "Portfolio Strategy",
         "Produce side-by-side allocation scenarios on demand instead of a multi-day analyst turnaround.",
         "Capital Allocation", 57),
    ];

    private static async Task SeedAutomationsAsync(MetricsDbContext db, CancellationToken ct)
    {
        var existing = await db.Automations.Select(a => a.Name).ToListAsync(ct);
        var now = DateTime.UtcNow;

        foreach (var (name, description, client, requirement, department, daysAgo) in Automations)
        {
            if (existing.Contains(name)) continue;
            var activity = now.AddDays(-daysAgo);
            db.Automations.Add(new Automation
            {
                Id = Guid.NewGuid(),
                Name = name,
                Description = description,
                Client = client,
                Requirement = requirement,
                Department = department,
                CreatedAt = activity.AddDays(-30),
                LastActivityAt = activity,
                DataVersion = 0
            });
        }
    }
}
