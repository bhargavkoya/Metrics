using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Metrics.Application.Auth;
using Metrics.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Metrics.Tests.Auth;

public class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    /// <summary>Per-factory temp folder so upload tests never touch the real uploads directory.</summary>
    public string UploadsPath { get; } = Path.Combine(Path.GetTempPath(), "metrics-tests-" + Guid.NewGuid().ToString("N"));

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(UploadsPath)) Directory.Delete(UploadsPath, recursive: true);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        // Real-looking key so the startup placeholder check passes outside Development.
        builder.UseSetting("Jwt:Key", "api-test-signing-key-that-is-long-enough-0123456789");
        builder.UseSetting("Storage:UploadsPath", UploadsPath);
        builder.UseSetting("Roi:WarmerEnabled", "false"); // tests drive the warmer directly; no background timers
        builder.ConfigureServices(services =>
        {
            var existing = services.Single(d => d.ServiceType == typeof(DbContextOptions<MetricsDbContext>));
            services.Remove(existing);
            services.AddDbContext<MetricsDbContext>(o => o.UseInMemoryDatabase(_dbName));

            // No Redis in tests: swap the distributed cache for the in-memory implementation.
            foreach (var d in services.Where(d => d.ServiceType == typeof(Microsoft.Extensions.Caching.Distributed.IDistributedCache)).ToList())
                services.Remove(d);
            services.AddDistributedMemoryCache();
        });
    }
}

public class AuthApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<AuthResponse> RegisterAsync(string team)
    {
        var email = $"{Guid.NewGuid():N}@test.local";
        var res = await _client.PostAsJsonAsync("/api/auth/register",
            new { email, password = "password123", name = "Tester", team });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<AuthResponse>(Json))!;
    }

    private static readonly System.Text.Json.JsonSerializerOptions Json = new(System.Text.Json.JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private HttpRequestMessage Get(string url, string? token)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (token is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return req;
    }

    [Fact]
    public async Task ProtectedEndpoint_WithoutToken_Returns401()
    {
        var res = await _client.SendAsync(Get("/api/auth/me", null));
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Me_WithValidToken_ReturnsUser()
    {
        var reg = await RegisterAsync("Business");

        var res = await _client.SendAsync(Get("/api/auth/me", reg.Token));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var me = await res.Content.ReadFromJsonAsync<UserDto>(Json);
        Assert.Equal(reg.User.Id, me!.Id);
    }

    [Fact]
    public async Task TechnicalPolicy_Business_Gets403_Technical_PassesAuthorization()
    {
        var biz = await RegisterAsync("Business");
        var tech = await RegisterAsync("Technical");
        // A real Technical-only endpoint. The automation does not exist, so a Technical user is authorized and then gets a
        // 404, while a Business user is stopped by the policy before the action runs.
        var url = $"/api/automations/{Guid.NewGuid()}/metrics/validate-formula";

        HttpRequestMessage Post(string token) => new(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(new { formula = "[a] + 1" }),
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) }
        };

        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendAsync(Post(biz.Token))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.SendAsync(Post(tech.Token))).StatusCode);
    }

    [Fact]
    public async Task Register_DuplicateEmail_Returns409()
    {
        var body = new { email = "dupe@test.local", password = "password123", name = "D", team = "Business" };
        await _client.PostAsJsonAsync("/api/auth/register", body);

        var res = await _client.PostAsJsonAsync("/api/auth/register", body);

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task Register_WeakPassword_Returns400()
    {
        var res = await _client.PostAsJsonAsync("/api/auth/register",
            new { email = "weak@test.local", password = "short", name = "W", team = "Business" });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401()
    {
        var res = await _client.PostAsJsonAsync("/api/auth/login",
            new { email = "nobody@test.local", password = "whatever1" });

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Health_IsAnonymous()
    {
        // Redis/Postgres aren't available to the test host, so only assert it isn't auth-blocked.
        var res = await _client.GetAsync("/api/health");
        Assert.NotEqual(HttpStatusCode.Unauthorized, res.StatusCode);
    }
}
