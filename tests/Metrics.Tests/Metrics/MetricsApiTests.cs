using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Metrics.Application.Auth;
using Metrics.Application.Metrics;
using Metrics.Domain.Entities;
using Metrics.Infrastructure.Persistence;
using Metrics.Tests.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Metrics.Tests.Metrics;

public class MetricsApiTests : IClassFixture<ApiFactory>
{
    private static readonly Guid AutomationId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static readonly System.Text.Json.JsonSerializerOptions Json = new(System.Text.Json.JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private readonly ApiFactory _factory;

    public MetricsApiTests(ApiFactory factory)
    {
        _factory = factory;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MetricsDbContext>();
        if (!db.Automations.Any(a => a.Id == AutomationId))
        {
            db.Automations.Add(new Automation
            {
                Id = AutomationId, Name = "Metrics host", Description = "d", Client = "c", Requirement = "r", Department = "Ops",
                CreatedAt = DateTime.UtcNow, LastActivityAt = DateTime.UtcNow
            });
            db.SaveChanges();
        }
    }

    private async Task<HttpClient> ClientAsync(string team)
    {
        var res = await _factory.CreateClient().PostAsJsonAsync("/api/auth/register",
            new { email = $"{Guid.NewGuid():N}@test.local", password = "password123", name = "T", team });
        var auth = (await res.Content.ReadFromJsonAsync<AuthResponse>(Json))!;
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        return c;
    }

    // Each test uses its own labels so the shared automation never collides across tests.
    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..20];

    private static string Url(string suffix = "") => $"/api/automations/{AutomationId}/metrics{suffix}";

    private static async Task<MetricDefinitionDto> CreateInput(HttpClient c, string label, string type, string? currency = null)
    {
        var res = await c.PostAsJsonAsync(Url(), new { label, kind = "Input", valueType = type, currencyCode = currency });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<MetricDefinitionDto>(Json))!;
    }

    [Fact]
    public async Task Anonymous_Is401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync(Url())).StatusCode);
    }

    [Fact]
    public async Task Business_CanList_ButNotCreateValidateOrDelete()
    {
        var tech = await ClientAsync("Technical");
        var biz = await ClientAsync("Business");
        var m = await CreateInput(tech, Unique("biz-read"), "Number");

        Assert.Equal(HttpStatusCode.OK, (await biz.GetAsync(Url())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await biz.PostAsJsonAsync(Url(), new { label = "x", kind = "Input", valueType = "Number" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await biz.PostAsJsonAsync(Url("/validate-formula"), new { formula = "[a]+1" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await biz.DeleteAsync(Url($"/{m.Id}"))).StatusCode);
    }

    [Fact]
    public async Task Technical_DefinesInputsAndAComputedMetric_EndToEnd()
    {
        var tech = await ClientAsync("Technical");
        var manual = Unique("manual");
        var auto = Unique("auto");
        await CreateInput(tech, manual, "Duration");
        await CreateInput(tech, auto, "Duration");

        var res = await tech.PostAsJsonAsync(Url(), new { label = Unique("saved"), kind = "Computed", formula = $"[{manual}] - [{auto}]" });

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var dto = (await res.Content.ReadFromJsonAsync<MetricDefinitionDto>(Json))!;
        Assert.Equal("Duration", dto.ValueType.ToString());
        Assert.Equal("Computed", dto.Kind.ToString());

        var list = (await (await tech.GetAsync(Url())).Content.ReadFromJsonAsync<List<MetricDefinitionDto>>(Json))!;
        Assert.Contains(list, m => m.Id == dto.Id && m.FormulaText == $"[{manual}] - [{auto}]");
    }

    [Fact]
    public async Task Create_InvalidFormula_Returns400_WithMessageAndPosition()
    {
        var tech = await ClientAsync("Technical");
        var records = Unique("records");
        var time = Unique("time");
        await CreateInput(tech, records, "Number");
        await CreateInput(tech, time, "Duration");

        var res = await tech.PostAsJsonAsync(Url(), new { label = Unique("bad"), kind = "Computed", formula = $"[{records}] / [{time}]" });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("Cannot divide Number by Duration", body);
        Assert.Contains("position", body);
    }

    [Fact]
    public async Task ValidateFormula_Returns200_ForValidAndInvalid()
    {
        var tech = await ClientAsync("Technical");
        var records = Unique("rec");
        await CreateInput(tech, records, "Number");

        var ok = await tech.PostAsJsonAsync(Url("/validate-formula"), new { formula = $"[{records}] * 2" });
        var okBody = (await ok.Content.ReadFromJsonAsync<ValidateFormulaResponse>(Json))!;
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.True(okBody.Valid);
        Assert.Equal("Number", okBody.ResultType.ToString());

        var bad = await tech.PostAsJsonAsync(Url("/validate-formula"), new { formula = $"[{records}] / 0" });
        var badBody = (await bad.Content.ReadFromJsonAsync<ValidateFormulaResponse>(Json))!;
        Assert.Equal(HttpStatusCode.OK, bad.StatusCode);
        Assert.False(badBody.Valid);
        Assert.Equal("DivisionByZeroLiteral", badBody.Errors.Single().Code);
    }

    [Fact]
    public async Task DuplicateLabel_Returns409()
    {
        var tech = await ClientAsync("Technical");
        var label = Unique("dupe");
        await CreateInput(tech, label, "Number");

        var again = await tech.PostAsJsonAsync(Url(), new { label = label.ToUpperInvariant(), kind = "Input", valueType = "Number" });

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task DeletingAnInputUsedByAFormula_Returns409_UntilTheFormulaIsDeleted()
    {
        var tech = await ClientAsync("Technical");
        var a = Unique("a");
        var inputA = await CreateInput(tech, a, "Number");
        var computed = (await (await tech.PostAsJsonAsync(Url(), new { label = Unique("dbl"), kind = "Computed", formula = $"[{a}] * 2" }))
            .Content.ReadFromJsonAsync<MetricDefinitionDto>(Json))!;

        Assert.Equal(HttpStatusCode.Conflict, (await tech.DeleteAsync(Url($"/{inputA.Id}"))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await tech.DeleteAsync(Url($"/{computed.Id}"))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await tech.DeleteAsync(Url($"/{inputA.Id}"))).StatusCode);

        var list = (await (await tech.GetAsync(Url())).Content.ReadFromJsonAsync<List<MetricDefinitionDto>>(Json))!;
        Assert.DoesNotContain(list, m => m.Id == inputA.Id || m.Id == computed.Id);
    }

    [Fact]
    public async Task SoftDelete_KeepsTheRow_AndFreesTheLabel()
    {
        var tech = await ClientAsync("Technical");
        var label = Unique("soft");
        var m = await CreateInput(tech, label, "Number");

        await tech.DeleteAsync(Url($"/{m.Id}"));

        using var scope = _factory.Services.CreateScope();
        var row = await scope.ServiceProvider.GetRequiredService<MetricsDbContext>()
            .MetricDefinitions.AsNoTracking().SingleAsync(x => x.Id == m.Id);
        Assert.True(row.IsDeleted);
        Assert.NotNull(row.DeletedAt);

        Assert.Equal(HttpStatusCode.Created,
            (await tech.PostAsJsonAsync(Url(), new { label, kind = "Input", valueType = "Duration" })).StatusCode);
    }

    [Fact]
    public async Task Formulas_AreImmutable_NoUpdateRouteExists()
    {
        var tech = await ClientAsync("Technical");
        var a = Unique("imm");
        var input = await CreateInput(tech, a, "Number");
        var computed = (await (await tech.PostAsJsonAsync(Url(), new { label = Unique("c"), kind = "Computed", formula = $"[{a}] + 1" }))
            .Content.ReadFromJsonAsync<MetricDefinitionDto>(Json))!;
        var body = JsonContent.Create(new { label = "renamed", kind = "Computed", formula = $"[{a}] + 2" });

        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await tech.PutAsync(Url($"/{computed.Id}"), body)).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed,
            (await tech.PatchAsync(Url($"/{computed.Id}"), JsonContent.Create(new { formula = "[x]+1" }))).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await tech.PutAsync(Url(), body)).StatusCode);

        var list = (await (await tech.GetAsync(Url())).Content.ReadFromJsonAsync<List<MetricDefinitionDto>>(Json))!;
        Assert.Equal($"[{a}] + 1", list.Single(m => m.Id == computed.Id).FormulaText);
        Assert.Contains(list, m => m.Id == input.Id);
    }

    [Fact]
    public async Task Changes_BumpDataVersion_ButNotLastActivity()
    {
        var tech = await ClientAsync("Technical");
        (long Version, DateTime Activity) Read()
        {
            using var scope = _factory.Services.CreateScope();
            var a = scope.ServiceProvider.GetRequiredService<MetricsDbContext>().Automations.AsNoTracking().Single(x => x.Id == AutomationId);
            return (a.DataVersion, a.LastActivityAt);
        }
        var before = Read();

        var m = await CreateInput(tech, Unique("ver"), "Number");
        var afterCreate = Read();
        await tech.DeleteAsync(Url($"/{m.Id}"));
        var afterDelete = Read();

        Assert.True(afterCreate.Version > before.Version);
        Assert.True(afterDelete.Version > afterCreate.Version);
        Assert.Equal(before.Activity, afterDelete.Activity);
    }

    [Fact]
    public async Task UnknownAutomationOrMetric_Returns404()
    {
        var tech = await ClientAsync("Technical");

        Assert.Equal(HttpStatusCode.NotFound, (await tech.GetAsync($"/api/automations/{Guid.NewGuid()}/metrics")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await tech.DeleteAsync(Url($"/{Guid.NewGuid()}"))).StatusCode);
    }

    [Fact]
    public async Task EnumsBindAsStrings_AndRejectGarbage()
    {
        var tech = await ClientAsync("Technical");

        var res = await tech.PostAsJsonAsync(Url(), new { label = "x", kind = "Input", valueType = "Bananas" });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}
