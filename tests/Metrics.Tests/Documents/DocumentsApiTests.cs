using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Metrics.Application.Auth;
using Metrics.Application.Documents;
using Metrics.Domain.Entities;
using Metrics.Infrastructure.Persistence;
using Metrics.Tests.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Metrics.Tests.Documents;

public class DocumentsApiTests : IClassFixture<ApiFactory>
{
    private static readonly Guid AutomationId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OtherAutomationId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static readonly System.Text.Json.JsonSerializerOptions Json = new(System.Text.Json.JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private readonly ApiFactory _factory;

    public DocumentsApiTests(ApiFactory factory)
    {
        _factory = factory;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MetricsDbContext>();
        foreach (var id in new[] { AutomationId, OtherAutomationId })
        {
            if (db.Automations.Any(a => a.Id == id)) continue;
            db.Automations.Add(new Automation
            {
                Id = id, Name = "A" + id.ToString()[..4], Description = "d", Client = "c", Requirement = "r", Department = "Ops",
                CreatedAt = DateTime.UtcNow.AddDays(-30), LastActivityAt = DateTime.UtcNow.AddDays(-30)
            });
        }
        db.SaveChanges();
    }

    private async Task<HttpClient> ClientAsync(string team)
    {
        var anon = _factory.CreateClient();
        var res = await anon.PostAsJsonAsync("/api/auth/register",
            new { email = $"{Guid.NewGuid():N}@test.local", password = "password123", name = "Uploader " + team, team });
        var auth = (await res.Content.ReadFromJsonAsync<AuthResponse>(Json))!;
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        return c;
    }

    private static byte[] PdfBytes(int total = 200)
    {
        var b = new byte[total];
        new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D }.CopyTo(b, 0);
        new Random(7).NextBytes(b.AsSpan(5));
        return b;
    }

    private static MultipartFormDataContent Form(byte[] bytes, string fileName, string contentType = "application/pdf")
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { file, "file", fileName } };
    }

    private static string Url(Guid automationId, string suffix = "") => $"/api/automations/{automationId}/documents{suffix}";

    [Fact]
    public async Task Technical_Upload_Download_RoundTripsBytes_WithSafeHeaders()
    {
        var tech = await ClientAsync("Technical");
        var bytes = PdfBytes(5000);

        var up = await tech.PostAsync(Url(AutomationId), Form(bytes, "Design Spec.pdf"));
        Assert.Equal(HttpStatusCode.Created, up.StatusCode);
        var dto = (await up.Content.ReadFromJsonAsync<DocumentDto>(Json))!;
        Assert.Equal("Design Spec.pdf", dto.OriginalFileName);
        Assert.Equal(5000, dto.SizeBytes);
        Assert.StartsWith("Uploader", dto.UploadedBy);

        var down = await tech.GetAsync(Url(AutomationId, $"/{dto.Id}/download"));
        Assert.Equal(HttpStatusCode.OK, down.StatusCode);
        Assert.Equal(bytes, await down.Content.ReadAsByteArrayAsync());
        Assert.Equal("application/pdf", down.Content.Headers.ContentType!.MediaType);
        Assert.Equal("attachment", down.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Contains("nosniff", down.Headers.GetValues("X-Content-Type-Options"));
    }

    [Fact]
    public async Task StoredFileOnDisk_UsesGuidName_NotClientName()
    {
        var tech = await ClientAsync("Technical");
        var up = await tech.PostAsync(Url(AutomationId), Form(PdfBytes(), "../../secret-name.pdf"));
        var dto = (await up.Content.ReadFromJsonAsync<DocumentDto>(Json))!;

        Assert.Equal("secret-name.pdf", dto.OriginalFileName);
        var files = Directory.GetFiles(_factory.UploadsPath).Select(Path.GetFileName).ToList();
        Assert.DoesNotContain(files, f => f!.Contains("secret"));
        Assert.All(files, f => Assert.Matches(@"^[0-9a-f]{32}\.[a-z]+$", f!));
    }

    [Fact]
    public async Task Business_CanListAndDownload_ButNotUploadOrDelete()
    {
        var tech = await ClientAsync("Technical");
        var biz = await ClientAsync("Business");
        var dto = (await (await tech.PostAsync(Url(AutomationId), Form(PdfBytes(), "shared.pdf")))
            .Content.ReadFromJsonAsync<DocumentDto>(Json))!;

        Assert.Equal(HttpStatusCode.OK, (await biz.GetAsync(Url(AutomationId))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await biz.GetAsync(Url(AutomationId, $"/{dto.Id}/download"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await biz.PostAsync(Url(AutomationId), Form(PdfBytes(), "x.pdf"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await biz.DeleteAsync(Url(AutomationId, $"/{dto.Id}"))).StatusCode);
    }

    [Fact]
    public async Task Anonymous_IsUnauthorized()
    {
        var anon = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync(Url(AutomationId))).StatusCode);
    }

    [Fact]
    public async Task Delete_RemovesRowAndFile_ThenDownloadIs404()
    {
        var tech = await ClientAsync("Technical");
        var before = Directory.Exists(_factory.UploadsPath) ? Directory.GetFiles(_factory.UploadsPath).Length : 0;
        var dto = (await (await tech.PostAsync(Url(AutomationId), Form(PdfBytes(), "gone.pdf")))
            .Content.ReadFromJsonAsync<DocumentDto>(Json))!;
        Assert.Equal(before + 1, Directory.GetFiles(_factory.UploadsPath).Length);

        Assert.Equal(HttpStatusCode.NoContent, (await tech.DeleteAsync(Url(AutomationId, $"/{dto.Id}"))).StatusCode);

        Assert.Equal(before, Directory.GetFiles(_factory.UploadsPath).Length);
        Assert.Equal(HttpStatusCode.NotFound, (await tech.GetAsync(Url(AutomationId, $"/{dto.Id}/download"))).StatusCode);
    }

    [Fact]
    public async Task Document_IsNotReachableThroughAnotherAutomationsUrl()
    {
        var tech = await ClientAsync("Technical");
        var dto = (await (await tech.PostAsync(Url(AutomationId), Form(PdfBytes(), "mine.pdf")))
            .Content.ReadFromJsonAsync<DocumentDto>(Json))!;

        Assert.Equal(HttpStatusCode.NotFound, (await tech.GetAsync(Url(OtherAutomationId, $"/{dto.Id}/download"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await tech.DeleteAsync(Url(OtherAutomationId, $"/{dto.Id}"))).StatusCode);
    }

    [Fact]
    public async Task Upload_DisallowedExtension_Returns400()
    {
        var tech = await ClientAsync("Technical");
        var res = await tech.PostAsync(Url(AutomationId), Form(PdfBytes(), "run.exe", "application/octet-stream"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Upload_SpoofedExtension_Returns400()
    {
        var tech = await ClientAsync("Technical");
        var exe = new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00 };
        var res = await tech.PostAsync(Url(AutomationId), Form(exe, "totally-a.pdf"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Upload_OverTenMegabytes_Returns400()
    {
        var tech = await ClientAsync("Technical");
        var res = await tech.PostAsync(Url(AutomationId), Form(PdfBytes((int)FileValidator.MaxBytes + 1), "big.pdf"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Upload_UnknownAutomation_Returns404()
    {
        var tech = await ClientAsync("Technical");
        var res = await tech.PostAsync(Url(Guid.NewGuid()), Form(PdfBytes(), "a.pdf"));
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Upload_BumpsAutomationLastActivity()
    {
        var tech = await ClientAsync("Technical");
        DateTime Read()
        {
            using var scope = _factory.Services.CreateScope();
            return scope.ServiceProvider.GetRequiredService<MetricsDbContext>().Automations
                .AsNoTracking().Single(a => a.Id == OtherAutomationId).LastActivityAt;
        }
        var before = Read();

        await tech.PostAsync(Url(OtherAutomationId), Form(PdfBytes(), "touch.pdf"));

        Assert.True(Read() > before);
    }
}
