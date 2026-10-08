using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Metrics.Application.Automations;
using Metrics.Application.Auth;
using Metrics.Domain.Entities;
using Metrics.Infrastructure.Persistence;
using Metrics.Tests.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace Metrics.Tests.Automations;

public class AutomationsApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _client;
    // Fixed: xUnit creates a new instance per test, but the factory (and its DB) is shared.
    private static readonly Guid _seededId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public AutomationsApiTests(ApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MetricsDbContext>();
        if (!db.Automations.Any())
        {
            db.Automations.Add(new Automation
            {
                Id = _seededId, Name = "Seeded", Description = "d", Client = "c", Requirement = "r",
                Department = "Ops", CreatedAt = DateTime.UtcNow, LastActivityAt = DateTime.UtcNow
            });
            db.SaveChanges();
        }
    }

    private async Task<HttpClient> LoginAsClientAsync(string team)
    {
        var res = await _client.PostAsJsonAsync("/api/auth/register",
            new { email = $"{Guid.NewGuid():N}@test.local", password = "password123", name = "T", team });
        var auth = (await res.Content.ReadFromJsonAsync<AuthResponse>(
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
            { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } }))!;
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        return c;
    }

    [Fact]
    public async Task List_WithoutToken_Returns401()
    {
        var res = await _client.GetAsync("/api/automations");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Theory]
    [InlineData("Business")]
    [InlineData("Technical")]
    public async Task List_AnyTeam_Returns200WithCards(string team)
    {
        var c = await LoginAsClientAsync(team);

        var res = await c.GetAsync("/api/automations");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var cards = await res.Content.ReadFromJsonAsync<List<AutomationCardDto>>();
        Assert.Contains(cards!, x => x.Id == _seededId);
    }

    [Fact]
    public async Task Get_Known_Returns200_Unknown_Returns404()
    {
        var c = await LoginAsClientAsync("Business");

        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync($"/api/automations/{_seededId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/automations/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task List_FromAfterTo_Returns400()
    {
        var c = await LoginAsClientAsync("Business");

        var res = await c.GetAsync("/api/automations?from=2026-10-10&to=2026-10-01");

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Departments_Returns200()
    {
        var c = await LoginAsClientAsync("Technical");

        var res = await c.GetAsync("/api/automations/departments");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("Ops", (await res.Content.ReadFromJsonAsync<List<string>>())!);
    }
}
