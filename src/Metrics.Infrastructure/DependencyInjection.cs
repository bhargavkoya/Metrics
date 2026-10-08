using Metrics.Application.Abstractions;
using Metrics.Application.Auth;
using Metrics.Application.Automations;
using Metrics.Application.Documents;
using Metrics.Application.Logs;
using Metrics.Application.Metrics;
using Metrics.Infrastructure.Logs;
using Metrics.Infrastructure.Metrics;
using Metrics.Infrastructure.Documents;
using Metrics.Infrastructure.Automations;
using Metrics.Infrastructure.Auth;
using Metrics.Infrastructure.Health;
using Metrics.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Metrics.Application.Caching;
using Metrics.Infrastructure.Caching;
using StackExchange.Redis;

namespace Metrics.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<MetricsDbContext>(o => o.UseNpgsql(config.GetConnectionString("Postgres")));

        services.AddStackExchangeRedisCache(o =>
        {
            var redis = ConfigurationOptions.Parse(config.GetConnectionString("Redis")!);
            redis.AbortOnConnectFail = false;
            redis.ConnectTimeout = 2000;
            // Commands queued on a broken connection otherwise wait the 5s default before failing. Keep the first
            // request after a Redis outage quick; the cache's backoff then protects every request after it.
            redis.AsyncTimeout = 1000;
            redis.SyncTimeout = 1000;
            o.ConfigurationOptions = redis;
            o.InstanceName = "metrics:";
        });

        services.Configure<JwtOptions>(config.GetSection(JwtOptions.SectionName));
        services.Configure<SeedOptions>(config.GetSection(SeedOptions.SectionName));
        services.AddSingleton(TimeProvider.System);

        var roi = config.GetSection(RoiSettings.SectionName).Get<RoiSettings>() ?? new RoiSettings();
        services.AddSingleton(roi);
        services.AddSingleton<IMetricCache, DistributedMetricCache>();
        services.AddSingleton<IChangeNotifier, InProcessChangeNotifier>();
        if (roi.WarmerEnabled) services.AddHostedService<RoiCacheWarmerService>();
        services.AddSingleton<IPasswordHasher, IdentityPasswordHasher>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddScoped<IUserRepository, EfUserRepository>();
        services.AddScoped<IAutomationRepository, EfAutomationRepository>();
        services.Configure<StorageOptions>(config.GetSection(StorageOptions.SectionName));
        services.AddSingleton<IFileStorage, LocalDiskFileStorage>();
        services.AddScoped<IDocumentRepository, EfDocumentRepository>();
        services.AddScoped<IMetricRepository, EfMetricRepository>();
        services.AddScoped<ILogRepository, EfLogRepository>();

        services.AddScoped<IHealthProbe, DbHealthProbe>();
        services.AddScoped<IHealthProbe, RedisHealthProbe>();
        return services;
    }
}
