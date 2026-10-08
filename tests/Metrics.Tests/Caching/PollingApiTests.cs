using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Metrics.Application.Auth;
using Metrics.Application.Automations;
using Metrics.Application.Logs;
using Metrics.Application.Metrics;
using Metrics.Domain.Entities;
using Metrics.Infrastructure.Persistence;
using Metrics.Tests.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace Metrics.Tests.Caching;

public class PollingApiTests : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private readonly ApiFactory _factory;

    public PollingApiTests(ApiFactory factory) => _factory = factory;

    private Guid NewAutomation()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MetricsDbContext>();
        var id = Guid.NewGuid();
        db.Automations.Add(new Automation
        {
            Id = id, Name = "P" + id.ToString("N")[..8], Description = "d", Client = "c", Requirement = "r", Department = "Ops",
            CreatedAt = DateTime.UtcNow, LastActivityAt = DateTime.UtcNow
        });
        db.SaveChanges();
        return id;
    }

    private async Task<HttpClient> ClientAsync(string team)
    {
        var res = await _factory.CreateClient().PostAsJsonAsync("/api/auth/register",
            new { email = $"{Guid.NewGuid():N}@test.local", password = "password123", name = "T " + team, team });
        var auth = (await res.Content.ReadFromJsonAsync<AuthResponse>(Json))!;
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        return c;
    }

    private static async Task<MetricDefinitionDto> DefineInput(HttpClient c, Guid a, string label)
    {
        var res = await c.PostAsJsonAsync($"/api/automations/{a}/metrics", new { label, kind = "Input", valueType = "Number" });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<MetricDefinitionDto>(Json))!;
    }

    private static Task<HttpResponseMessage> Report(HttpClient c, Guid a, MetricDefinitionDto m, decimal value) =>
        c.PostAsJsonAsync($"/api/automations/{a}/logs", new { values = new[] { new { metricId = m.Id, value } } });

    private static string CacheHeader(HttpResponseMessage r) => r.Headers.GetValues("X-Cache").Single();

    private static async Task<RoiDto> Body(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<RoiDto>(Json))!;

    // ---- caching ----

    [Fact]
    public async Task Roi_FirstReadMisses_RepeatReadsHit()
    {
        var tech = await ClientAsync("Technical");
        var a = NewAutomation();
        await DefineInput(tech, a, "Records");

        var first = await tech.GetAsync($"/api/automations/{a}/roi");
        var second = await tech.GetAsync($"/api/automations/{a}/roi");
        var third = await tech.GetAsync($"/api/automations/{a}/roi");

        Assert.Equal("MISS", CacheHeader(first));
        Assert.Equal("HIT", CacheHeader(second));
        Assert.Equal("HIT", CacheHeader(third));
        Assert.Equal((await Body(first)).DataVersion, (await Body(second)).DataVersion);
    }

    [Fact]
    public async Task AReport_InvalidatesTheCache_AndTheNextReadSeesTheNewData()
    {
        var tech = await ClientAsync("Technical");
        var a = NewAutomation();
        var records = await DefineInput(tech, a, "Records");
        await Report(tech, a, records, 1);
        var warm = await tech.GetAsync($"/api/automations/{a}/roi");
        await tech.GetAsync($"/api/automations/{a}/roi");
        var before = await Body(warm);

        await Report(tech, a, records, 2);
        var after = await tech.GetAsync($"/api/automations/{a}/roi");

        Assert.Equal("MISS", CacheHeader(after));
        var body = await Body(after);
        Assert.True(body.DataVersion > before.DataVersion);
        Assert.Equal(2m, body.Current.Single().Value);
        Assert.Equal(2, body.Series.Single().Points.Count);
        Assert.Equal("HIT", CacheHeader(await tech.GetAsync($"/api/automations/{a}/roi")));
    }

    [Fact]
    public async Task ChangingAMetricDefinition_AlsoInvalidates()
    {
        var tech = await ClientAsync("Technical");
        var a = NewAutomation();
        var records = await DefineInput(tech, a, "Records");
        await tech.GetAsync($"/api/automations/{a}/roi");
        Assert.Equal("HIT", CacheHeader(await tech.GetAsync($"/api/automations/{a}/roi")));

        await tech.DeleteAsync($"/api/automations/{a}/metrics/{records.Id}");

        var after = await tech.GetAsync($"/api/automations/{a}/roi");
        Assert.Equal("MISS", CacheHeader(after));
        Assert.Empty((await Body(after)).Current);
    }

    [Fact]
    public async Task DifferentPointsValues_AreCachedSeparately()
    {
        var tech = await ClientAsync("Technical");
        var a = NewAutomation();
        var records = await DefineInput(tech, a, "Records");
        for (var i = 1; i <= 3; i++) await Report(tech, a, records, i);

        var all = await Body(await tech.GetAsync($"/api/automations/{a}/roi?points=30"));
        var two = await tech.GetAsync($"/api/automations/{a}/roi?points=2");

        Assert.Equal("MISS", CacheHeader(two));
        Assert.Equal(3, all.Series.Single().Points.Count);
        Assert.Equal(2, (await Body(two)).Series.Single().Points.Count);
    }

    // ---- long polling ----

    [Fact]
    public async Task Changes_ReturnsImmediately_WhenTheClientIsBehind()
    {
        var tech = await ClientAsync("Technical");
        var a = NewAutomation();
        await DefineInput(tech, a, "Records");
        var version = (await Body(await tech.GetAsync($"/api/automations/{a}/roi"))).DataVersion;
        var sw = Stopwatch.StartNew();

        var res = await tech.GetAsync($"/api/automations/{a}/roi/changes?sinceVersion={version - 1}&timeoutSeconds=10");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(version, (await Body(res)).DataVersion);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task Changes_Returns204_WhenNothingChangesBeforeTheTimeout()
    {
        var tech = await ClientAsync("Technical");
        var a = NewAutomation();
        await DefineInput(tech, a, "Records");
        var version = (await Body(await tech.GetAsync($"/api/automations/{a}/roi"))).DataVersion;
        var sw = Stopwatch.StartNew();

        var res = await tech.GetAsync($"/api/automations/{a}/roi/changes?sinceVersion={version}&timeoutSeconds=1");

        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
        Assert.True(sw.Elapsed >= TimeSpan.FromMilliseconds(800));
    }

    [Fact]
    public async Task AWaitingLongPoll_IsWokenByAReportFromAnotherClient()
    {
        var tech = await ClientAsync("Technical");
        var viewer = await ClientAsync("Business");
        var a = NewAutomation();
        var records = await DefineInput(tech, a, "Records");
        await Report(tech, a, records, 1);
        var version = (await Body(await viewer.GetAsync($"/api/automations/{a}/roi"))).DataVersion;

        var waiting = viewer.GetAsync($"/api/automations/{a}/roi/changes?sinceVersion={version}&timeoutSeconds=20");
        await Task.Delay(300);
        Assert.False(waiting.IsCompleted, "the request should be held open while nothing has changed");
        var sw = Stopwatch.StartNew();

        await Report(tech, a, records, 99);

        var res = await waiting.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3), "should wake promptly, not wait for the timeout");
        var body = await Body(res);
        Assert.True(body.DataVersion > version);
        Assert.Equal(99m, body.Current.Single().Value);
    }

    [Fact]
    public async Task AWaitingLongPoll_IsWokenByAMetricChange()
    {
        var tech = await ClientAsync("Technical");
        var a = NewAutomation();
        await DefineInput(tech, a, "Records");
        var version = (await Body(await tech.GetAsync($"/api/automations/{a}/roi"))).DataVersion;

        var waiting = tech.GetAsync($"/api/automations/{a}/roi/changes?sinceVersion={version}&timeoutSeconds=20");
        await Task.Delay(300);
        await DefineInput(tech, a, "Another");

        var res = await waiting.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(2, (await Body(res)).Current.Count);
    }

    [Fact]
    public async Task Changes_RespectsAuthAndExistence()
    {
        var tech = await ClientAsync("Technical");
        var a = NewAutomation();

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await _factory.CreateClient().GetAsync($"/api/automations/{a}/roi/changes?sinceVersion=0&timeoutSeconds=1")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await tech.GetAsync($"/api/automations/{Guid.NewGuid()}/roi/changes?sinceVersion=0&timeoutSeconds=1")).StatusCode);
    }

    [Fact]
    public async Task Changes_TimeoutIsCappedByTheServer()
    {
        var tech = await ClientAsync("Technical");
        var a = NewAutomation();
        await DefineInput(tech, a, "Records");
        var version = (await Body(await tech.GetAsync($"/api/automations/{a}/roi"))).DataVersion;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        // timeoutSeconds=0 is raised to the 1-second minimum rather than meaning "wait forever" or "fail".
        var res = await tech.GetAsync($"/api/automations/{a}/roi/changes?sinceVersion={version}&timeoutSeconds=0", cts.Token);

        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
    }

    // ---- stale flags ----

    [Fact]
    public async Task Catalog_ShowsTheStaleBadge_OnlyForFlaggedAutomations()
    {
        var tech = await ClientAsync("Technical");
        var flagged = NewAutomation();
        var other = NewAutomation();

        using (var scope = _factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IStaleFlagStore>().SetAsync([flagged], TimeSpan.FromMinutes(5), default);

        var cards = await tech.GetFromJsonAsync<List<AutomationCardDto>>("/api/automations", Json);

        Assert.True(cards!.Single(c => c.Id == flagged).IsStale);
        Assert.False(cards!.Single(c => c.Id == other).IsStale);
    }

    [Fact]
    public async Task WarmerCycle_FlagsOverdueAutomations_AndTheCatalogReflectsIt()
    {
        var tech = await ClientAsync("Technical");
        var a = NewAutomation();
        var records = await DefineInput(tech, a, "Records");
        await Report(tech, a, records, 1);
        var fresh = await tech.GetFromJsonAsync<List<AutomationCardDto>>("/api/automations", Json);
        Assert.False(fresh!.Single(c => c.Id == a).IsStale);

        // Backdate the only report so the automation is overdue, then run one warmer cycle directly.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MetricsDbContext>();
            foreach (var log in db.MetricLogs.Where(l => l.AutomationId == a)) log.ReportedAt = DateTime.UtcNow.AddDays(-30);
            db.Automations.Single(x => x.Id == a).DataVersion++; // as any real write would, so the cache key moves on
            await db.SaveChangesAsync();
        }
        using (var scope = _factory.Services.CreateScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<RoiWarmer>().RunOnceAsync(default);
            Assert.Contains(a, result.Stale);
        }

        var cards = await tech.GetFromJsonAsync<List<AutomationCardDto>>("/api/automations", Json);
        Assert.True(cards!.Single(c => c.Id == a).IsStale);
        Assert.True((await Body(await tech.GetAsync($"/api/automations/{a}/roi"))).IsStale);
    }
}
