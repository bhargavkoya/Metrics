using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Metrics.Application.Auth;
using Metrics.Application.Logs;
using Metrics.Application.Metrics;
using Metrics.Domain.Entities;
using Metrics.Infrastructure.Persistence;
using Metrics.Tests.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Metrics.Tests.Logs;

public class LogsApiTests : IClassFixture<ApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private readonly ApiFactory _factory;

    public LogsApiTests(ApiFactory factory) => _factory = factory;

    // ---- helpers ----

    /// <summary>Each test gets its own automation, so tests sharing the factory's database never interfere.</summary>
    private Guid NewAutomation()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MetricsDbContext>();
        var id = Guid.NewGuid();
        db.Automations.Add(new Automation
        {
            Id = id, Name = "A" + id.ToString("N")[..8], Description = "d", Client = "c", Requirement = "r", Department = "Ops",
            CreatedAt = DateTime.UtcNow.AddDays(-60), LastActivityAt = DateTime.UtcNow.AddDays(-60)
        });
        db.SaveChanges();
        return id;
    }

    private async Task<HttpClient> ClientAsync(string team)
    {
        var res = await _factory.CreateClient().PostAsJsonAsync("/api/auth/register",
            new { email = $"{Guid.NewGuid():N}@test.local", password = "password123", name = "Reporter " + team, team });
        var auth = (await res.Content.ReadFromJsonAsync<AuthResponse>(Json))!;
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        return c;
    }

    private static async Task<MetricDefinitionDto> Define(HttpClient c, Guid a, object body)
    {
        var res = await c.PostAsJsonAsync($"/api/automations/{a}/metrics", body);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<MetricDefinitionDto>(Json))!;
    }

    private static Task<MetricDefinitionDto> DefineInput(HttpClient c, Guid a, string label, string type) =>
        Define(c, a, new { label, kind = "Input", valueType = type });

    private static Task<MetricDefinitionDto> DefineComputed(HttpClient c, Guid a, string label, string formula) =>
        Define(c, a, new { label, kind = "Computed", formula });

    private static Task<HttpResponseMessage> Report(HttpClient c, Guid a, params (MetricDefinitionDto Metric, decimal Value)[] values) =>
        c.PostAsJsonAsync($"/api/automations/{a}/logs", new { values = values.Select(v => new { metricId = v.Metric.Id, v.Value }) });

    private static async Task<LogDto> Reported(HttpResponseMessage res)
    {
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<LogDto>(Json))!;
    }

    private static async Task<PagedResult<LogDto>> Logs(HttpClient c, Guid a, string query = "") =>
        (await c.GetFromJsonAsync<PagedResult<LogDto>>($"/api/automations/{a}/logs{query}", Json))!;

    private static Task<RoiDto> Roi(HttpClient c, Guid a, string query = "") =>
        c.GetFromJsonAsync<RoiDto>($"/api/automations/{a}/roi{query}", Json)!;

    private (long Version, DateTime Activity) Automation(Guid id)
    {
        using var scope = _factory.Services.CreateScope();
        var a = scope.ServiceProvider.GetRequiredService<MetricsDbContext>().Automations.AsNoTracking().Single(x => x.Id == id);
        return (a.DataVersion, a.LastActivityAt);
    }

    // ---- tests ----

    [Fact]
    public async Task Report_ComputesTheComputedMetrics_AndShowsUpInHistoryAndRoi()
    {
        var tech = await ClientAsync("Technical");
        var a = NewAutomation();
        var manual = await DefineInput(tech, a, "Manual", "Duration");
        var auto = await DefineInput(tech, a, "Auto", "Duration");
        var saved = await DefineComputed(tech, a, "Saved", "[Manual] - [Auto]");

        var log = await Reported(await Report(tech, a, (manual, 3600), (auto, 600)));

        Assert.Equal("Reporter Technical", log.ReportedBy);
        Assert.Equal(3000m, log.Values.Single(v => v.Label == "Saved").Value);
        Assert.Equal("[Manual] - [Auto]", log.Values.Single(v => v.Label == "Saved").Formula);

        var history = await Logs(tech, a);
        Assert.Equal(1, history.Total);
        Assert.Equal(log.Id, history.Items[0].Id);

        var roi = await Roi(tech, a);
        Assert.Equal(3000m, roi.Current.Single(c => c.MetricDefinitionId == saved.Id).Value);
        Assert.Single(roi.Series.Single(s => s.MetricDefinitionId == saved.Id).Points);
    }

    [Fact]
    public async Task History_IsNeverRewritten_WhenAFormulaIsDeletedAndRecreated()
    {
        var tech = await ClientAsync("Technical");
        var a = NewAutomation();
        var manual = await DefineInput(tech, a, "Manual", "Number");
        var auto = await DefineInput(tech, a, "Auto", "Number");
        var oldFormula = await DefineComputed(tech, a, "Result", "[Manual] - [Auto]");

        var first = await Reported(await Report(tech, a, (manual, 100), (auto, 40)));
        var firstAsRecorded = JsonSerializer.Serialize(first, Json);
        Assert.Equal(60m, first.Values.Single(v => v.Label == "Result").Value);

        // Change the formula the only way allowed: delete, then recreate under the same label.
        Assert.Equal(HttpStatusCode.NoContent, (await tech.DeleteAsync($"/api/automations/{a}/metrics/{oldFormula.Id}")).StatusCode);
        var newFormula = await DefineComputed(tech, a, "Result", "[Manual] + [Auto]");
        var second = await Reported(await Report(tech, a, (manual, 100), (auto, 40)));

        var history = await Logs(tech, a);
        var firstNow = history.Items.Single(l => l.Id == first.Id);
        Assert.Equal(firstAsRecorded, JsonSerializer.Serialize(firstNow, Json)); // identical to what was recorded
        Assert.Equal(60m, firstNow.Values.Single(v => v.Label == "Result").Value);
        Assert.Equal("[Manual] - [Auto]", firstNow.Values.Single(v => v.Label == "Result").Formula);
        Assert.Equal(oldFormula.Id, firstNow.Values.Single(v => v.Label == "Result").MetricDefinitionId);

        var secondNow = history.Items.Single(l => l.Id == second.Id);
        Assert.Equal(140m, secondNow.Values.Single(v => v.Label == "Result").Value);
        Assert.Equal("[Manual] + [Auto]", secondNow.Values.Single(v => v.Label == "Result").Formula);
        Assert.Equal(newFormula.Id, secondNow.Values.Single(v => v.Label == "Result").MetricDefinitionId);

        // The chart keeps the two definitions apart even though they share a label.
        var roi = await Roi(tech, a);
        var series = Assert.Single(roi.Series, s => s.Label == "Result");
        Assert.Equal(newFormula.Id, series.MetricDefinitionId);
        Assert.Equal([140m], series.Points.Select(p => p.Value));
    }

    [Fact]
    public async Task DeletedMetric_LeavesCurrentFigures_ButStaysInHistory()
    {
        var tech = await ClientAsync("Technical");
        var a = NewAutomation();
        var records = await DefineInput(tech, a, "Records", "Number");
        var doubled = await DefineComputed(tech, a, "Doubled", "[Records] * 2");
        await Reported(await Report(tech, a, (records, 5)));

        await tech.DeleteAsync($"/api/automations/{a}/metrics/{doubled.Id}");

        var roi = await Roi(tech, a);
        Assert.DoesNotContain(roi.Current, c => c.Label == "Doubled");
        var history = await Logs(tech, a);
        Assert.Equal(10m, history.Items[0].Values.Single(v => v.Label == "Doubled").Value);
    }

    [Fact]
    public async Task MetricAddedAfterAReport_HasNoValueUntilTheNextReport_AndOldLogsAreNotBackfilled()
    {
        var tech = await ClientAsync("Technical");
        var a = NewAutomation();
        var records = await DefineInput(tech, a, "Records", "Number");
        var before = await Reported(await Report(tech, a, (records, 5)));

        var tripled = await DefineComputed(tech, a, "Tripled", "[Records] * 3");

        var roi = await Roi(tech, a);
        var figure = roi.Current.Single(c => c.MetricDefinitionId == tripled.Id);
        Assert.False(figure.HasValue);
        Assert.Empty(roi.Series.Single(s => s.MetricDefinitionId == tripled.Id).Points);
        Assert.DoesNotContain((await Logs(tech, a)).Items.Single(l => l.Id == before.Id).Values, v => v.Label == "Tripled");

        await Reported(await Report(tech, a, (records, 7)));
        Assert.Equal(21m, (await Roi(tech, a)).Current.Single(c => c.MetricDefinitionId == tripled.Id).Value);
    }

    [Fact]
    public async Task DivisionByZeroInData_StoresNullComputedValue_AndReportStillSucceeds()
    {
        var tech = await ClientAsync("Technical");
        var a = NewAutomation();
        var records = await DefineInput(tech, a, "Records", "Number");
        var runs = await DefineInput(tech, a, "Runs", "Number");
        var perRun = await DefineComputed(tech, a, "Per run", "[Records] / [Runs]");

        var log = await Reported(await Report(tech, a, (records, 100), (runs, 0)));

        Assert.Null(log.Values.Single(v => v.Label == "Per run").Value);
        var figure = (await Roi(tech, a)).Current.Single(c => c.MetricDefinitionId == perRun.Id);
        Assert.True(figure.HasValue);
        Assert.Null(figure.Value);
    }

    [Fact]
    public async Task Business_CanReadButNotReport()
    {
        var tech = await ClientAsync("Technical");
        var biz = await ClientAsync("Business");
        var a = NewAutomation();
        var records = await DefineInput(tech, a, "Records", "Number");
        await Reported(await Report(tech, a, (records, 1)));

        Assert.Equal(HttpStatusCode.Forbidden, (await Report(biz, a, (records, 2))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await biz.GetAsync($"/api/automations/{a}/logs")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await biz.GetAsync($"/api/automations/{a}/roi")).StatusCode);
        Assert.Equal(1, (await Logs(biz, a)).Total);
    }

    [Fact]
    public async Task Anonymous_IsUnauthorized()
    {
        var anon = _factory.CreateClient();
        var a = NewAutomation();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync($"/api/automations/{a}/logs")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync($"/api/automations/{a}/roi")).StatusCode);
    }

    [Fact]
    public async Task Validation_ReturnsFieldErrorsKeyedByMetricId()
    {
        var tech = await ClientAsync("Technical");
        var a = NewAutomation();
        var time = await DefineInput(tech, a, "Time", "Duration");
        var records = await DefineInput(tech, a, "Records", "Number");

        var res = await Report(tech, a, (time, -5)); // negative duration, and Records missing

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains(time.Id.ToString(), body);
        Assert.Contains(records.Id.ToString(), body);
        Assert.Empty((await Logs(tech, a)).Items);
    }

    [Fact]
    public async Task Report_WithNoMetricsDefined_Returns400()
    {
        var tech = await ClientAsync("Technical");
        var a = NewAutomation();

        var res = await tech.PostAsJsonAsync($"/api/automations/{a}/logs", new { values = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Report_BumpsLastActivityAndDataVersion()
    {
        var tech = await ClientAsync("Technical");
        var a = NewAutomation();
        var records = await DefineInput(tech, a, "Records", "Number");
        var before = Automation(a);

        await Reported(await Report(tech, a, (records, 1)));
        var after = Automation(a);

        Assert.True(after.Version > before.Version);
        Assert.True(after.Activity > before.Activity);
        Assert.Equal(after.Version, (await Roi(tech, a)).DataVersion);
    }

    [Fact]
    public async Task History_IsNewestFirst_AndPaginates()
    {
        var tech = await ClientAsync("Technical");
        var a = NewAutomation();
        var records = await DefineInput(tech, a, "Records", "Number");
        var ids = new List<Guid>();
        for (var i = 1; i <= 5; i++)
        {
            ids.Add((await Reported(await Report(tech, a, (records, i)))).Id);
            await Task.Delay(5); // distinct timestamps
        }

        var page1 = await Logs(tech, a, "?page=1&pageSize=2");
        var page3 = await Logs(tech, a, "?page=3&pageSize=2");

        Assert.Equal(5, page1.Total);
        Assert.Equal([ids[4], ids[3]], page1.Items.Select(l => l.Id));
        Assert.Equal([ids[0]], page3.Items.Select(l => l.Id));
    }

    [Fact]
    public async Task Roi_PointsParameter_LimitsTheSeries_ToTheMostRecentLogs()
    {
        var tech = await ClientAsync("Technical");
        var a = NewAutomation();
        var records = await DefineInput(tech, a, "Records", "Number");
        for (var i = 1; i <= 4; i++)
        {
            await Reported(await Report(tech, a, (records, i)));
            await Task.Delay(5);
        }

        var roi = await Roi(tech, a, "?points=2");

        Assert.Equal([3m, 4m], roi.Series.Single().Points.Select(p => p.Value));
        Assert.Equal(4m, roi.Current.Single().Value);
    }

    [Fact]
    public async Task UnknownAutomation_Returns404()
    {
        var tech = await ClientAsync("Technical");
        var nope = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await tech.GetAsync($"/api/automations/{nope}/logs")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await tech.GetAsync($"/api/automations/{nope}/roi")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await tech.PostAsJsonAsync($"/api/automations/{nope}/logs", new { values = Array.Empty<object>() })).StatusCode);
    }

    [Fact]
    public async Task Catalog_SeesTheNewActivity()
    {
        var tech = await ClientAsync("Technical");
        var a = NewAutomation();
        var records = await DefineInput(tech, a, "Records", "Number");
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");

        var before = await tech.GetFromJsonAsync<List<AutomationCardDtoLite>>($"/api/automations?from={today}&to={today}", Json);
        await Reported(await Report(tech, a, (records, 1)));
        var after = await tech.GetFromJsonAsync<List<AutomationCardDtoLite>>($"/api/automations?from={today}&to={today}", Json);

        Assert.DoesNotContain(before!, c => c.Id == a);
        Assert.Contains(after!, c => c.Id == a);
    }

    private record AutomationCardDtoLite(Guid Id, string Name);
}
