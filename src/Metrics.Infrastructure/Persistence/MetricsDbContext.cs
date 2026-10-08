using Metrics.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Metrics.Infrastructure.Persistence;

public class MetricsDbContext(DbContextOptions<MetricsDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Automation> Automations => Set<Automation>();
    public DbSet<AutomationDocument> Documents => Set<AutomationDocument>();
    public DbSet<MetricDefinition> MetricDefinitions => Set<MetricDefinition>();
    public DbSet<MetricLog> MetricLogs => Set<MetricLog>();
    public DbSet<MetricLogValue> MetricLogValues => Set<MetricLogValue>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.Email).HasMaxLength(256);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Team).HasConversion<string>().HasMaxLength(20);
        });

        b.Entity<Automation>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Client).HasMaxLength(200);
            e.Property(x => x.Department).HasMaxLength(100);
            e.HasIndex(x => x.LastActivityAt);
            e.HasIndex(x => x.Department);
            e.HasMany(x => x.Documents).WithOne().HasForeignKey(d => d.AutomationId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Metrics).WithOne().HasForeignKey(m => m.AutomationId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Logs).WithOne().HasForeignKey(l => l.AutomationId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<AutomationDocument>(e =>
        {
            e.Property(x => x.OriginalFileName).HasMaxLength(260);
            e.Property(x => x.StoredFileName).HasMaxLength(100);
            e.Property(x => x.ContentType).HasMaxLength(200);
        });

        b.Entity<MetricDefinition>(e =>
        {
            e.Property(x => x.Label).HasMaxLength(200);
            e.Property(x => x.ValueType).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.FormulaText).HasMaxLength(1000);
            e.Property(x => x.CurrencyCode).HasMaxLength(3);
            // Label unique per automation among live (non-deleted) definitions.
            e.HasIndex(x => new { x.AutomationId, x.Label }).IsUnique().HasFilter("\"IsDeleted\" = false");
        });

        b.Entity<MetricLog>(e =>
        {
            e.HasIndex(x => new { x.AutomationId, x.ReportedAt });
            e.HasMany(x => x.Values).WithOne().HasForeignKey(v => v.MetricLogId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<MetricLogValue>(e =>
        {
            e.Property(x => x.Value).HasPrecision(28, 10);
            e.Property(x => x.LabelSnapshot).HasMaxLength(200);
            e.Property(x => x.CurrencyCodeSnapshot).HasMaxLength(3);
            e.Property(x => x.FormulaSnapshot).HasMaxLength(1000);
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.ValueTypeSnapshot).HasConversion<string>().HasMaxLength(20);
            // No FK to MetricDefinition on purpose: history must survive definition changes.
        });
    }
}
