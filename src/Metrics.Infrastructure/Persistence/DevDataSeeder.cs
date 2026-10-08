using Metrics.Application.Auth;
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
