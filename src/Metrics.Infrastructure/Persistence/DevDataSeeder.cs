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
        await db.SaveChangesAsync(ct);
    }
}
