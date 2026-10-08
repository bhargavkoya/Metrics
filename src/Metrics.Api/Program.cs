using System.Text.Json.Serialization;
using Metrics.Api;
using Metrics.Application.Auth;
using Metrics.Application.Caching;
using Metrics.Application.Automations;
using Metrics.Application.Documents;
using Metrics.Application.Formulas;
using Metrics.Application.Logs;
using Metrics.Application.Metrics;
using Metrics.Infrastructure.Documents;
using Metrics.Infrastructure;
using Metrics.Infrastructure.Auth;
using Metrics.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Optional, gitignored override for real secrets; placeholders stay tracked in appsettings.json.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

builder.Services.AddControllers(o => o.Filters.Add<ApiExceptionFilter>())
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IAutomationService, AutomationService>();
builder.Services.AddScoped<IDocumentService, DocumentService>();
builder.Services.AddSingleton<IFormulaEngine, FormulaEngine>();
builder.Services.AddScoped<IMetricService, MetricService>();
builder.Services.AddScoped<ILogService, LogService>();
// ROI reads go through a read-through cache; RoiService does the actual assembly.
builder.Services.AddScoped<RoiService>();
builder.Services.AddScoped<CacheTrace>();
builder.Services.AddScoped<IRoiService>(sp => new CachedRoiService(
    sp.GetRequiredService<RoiService>(), sp.GetRequiredService<ILogRepository>(), sp.GetRequiredService<IMetricCache>(),
    sp.GetRequiredService<RoiSettings>(), sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<CacheTrace>()));
builder.Services.AddScoped<IRoiLongPoll, RoiLongPollService>();
builder.Services.AddScoped<IStaleFlagStore, CacheStaleFlagStore>();
builder.Services.AddScoped<RoiWarmer>();
// Resolve a relative uploads path against the content root so the folder does not depend on the working directory.
builder.Services.PostConfigure<StorageOptions>(o =>
    o.UploadsPath = Path.GetFullPath(o.UploadsPath, builder.Environment.ContentRootPath));

// Fail fast on an unusable signing key. A leftover CHANGE_ME placeholder is only tolerated in Development.
var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
if (jwt.KeyIsTooShort)
    throw new InvalidOperationException($"Jwt:Key must be at least {JwtOptions.MinKeyLength} characters.");
var usingPlaceholderKey = jwt.KeyIsPlaceholder;
if (usingPlaceholderKey && !builder.Environment.IsDevelopment())
    throw new InvalidOperationException("Jwt:Key is still a CHANGE_ME placeholder. Set a real key in appsettings.Local.json.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false; // keep claim names as issued: sub, email, name, team
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = JwtTokenService.SigningKey(jwt),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = "name"
        };
    });

builder.Services.AddAuthorization(o =>
{
    o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
    o.AddPolicy(Policies.Technical, p => p.RequireAuthenticatedUser().RequireClaim(JwtTokenService.TeamClaim, "Technical"));
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Paste the token returned by /api/auth/login."
    });
    o.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = []
    });
});
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins("http://localhost:5173").AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

if (usingPlaceholderKey)
    app.Logger.LogWarning("Jwt:Key is a CHANGE_ME placeholder. Fine for local dev; replace it in appsettings.Local.json.");

app.UseExceptionHandler();
app.UseSwagger();
app.UseSwaggerUI();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

if (app.Environment.IsDevelopment())
    await DevDataSeeder.SeedAsync(app.Services);

app.Run();

public partial class Program;
