using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Metrics.Api;
using Metrics.Application.Auth;
using Metrics.Application.Common;
using Metrics.Application.Formulas;
using Metrics.Domain.Entities;
using Metrics.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Metrics.Tests.Auth;

public sealed class CapturedLog(string category, LogLevel level, string message)
{
    public string Category { get; } = category;
    public LogLevel Level { get; } = level;
    public string Message { get; } = message;
}

public sealed class CaptureLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<CapturedLog> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new CaptureLogger(categoryName, Entries);
    public void Dispose() { }

    private sealed class CaptureLogger(string category, ConcurrentQueue<CapturedLog> sink) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            sink.Enqueue(new CapturedLog(category, logLevel, formatter(state, exception)));
    }
}

public sealed class ExplodingFormulaEngine : IFormulaEngine
{
    public FormulaAnalysis Analyze(string? formula, IReadOnlyDictionary<string, MetricType> inputTypes) =>
        throw new InvalidOperationException("simulated bug");

    public decimal? Evaluate(string formula, IReadOnlyDictionary<string, decimal> values, IReadOnlyDictionary<string, MetricType> inputTypes) =>
        throw new InvalidOperationException("simulated bug");
}

public sealed class LoggingApiFactory : ApiFactory
{
    public CaptureLoggerProvider Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureLogging(l => l.AddProvider(Logs));
        // A deliberately broken dependency, so a genuine unexpected exception can be provoked through a real endpoint.
        builder.ConfigureServices(s =>
        {
            s.RemoveAll<IFormulaEngine>();
            s.AddSingleton<IFormulaEngine, ExplodingFormulaEngine>();
        });
    }
}

public class ExceptionHandlingApiTests : IClassFixture<LoggingApiFactory>
{
    private readonly LoggingApiFactory _factory;

    public ExceptionHandlingApiTests(LoggingApiFactory factory) => _factory = factory;

    private async Task<HttpClient> TechnicalClientAsync()
    {
        var res = await _factory.CreateClient().PostAsJsonAsync("/api/auth/register",
            new { email = $"{Guid.NewGuid():N}@test.local", password = "password123", name = "T", team = "Technical" });
        var auth = (await res.Content.ReadFromJsonAsync<AuthResponse>(new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        }))!;
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        return c;
    }

    private Guid NewAutomation()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MetricsDbContext>();
        var id = Guid.NewGuid();
        db.Automations.Add(new Automation
        {
            Id = id, Name = "E" + id.ToString("N")[..8], Description = "d", Client = "c", Requirement = "r", Department = "Ops",
            CreatedAt = DateTime.UtcNow, LastActivityAt = DateTime.UtcNow
        });
        db.SaveChanges();
        return id;
    }

    [Fact]
    public async Task ExpectedRejections_ReturnProblemDetails_WithoutAnyErrorLogs()
    {
        var tech = await TechnicalClientAsync();
        var anon = _factory.CreateClient();
        var a = NewAutomation();
        _factory.Logs.Entries.Clear();

        var validation = await tech.PostAsJsonAsync($"/api/automations/{a}/metrics", new { label = "", kind = "Input", valueType = "Number" });
        var notFound = await tech.GetAsync($"/api/automations/{Guid.NewGuid()}/metrics");
        await tech.PostAsJsonAsync($"/api/automations/{a}/metrics", new { label = "Dup", kind = "Input", valueType = "Number" });
        var conflict = await tech.PostAsJsonAsync($"/api/automations/{a}/metrics", new { label = "dup", kind = "Input", valueType = "Number" });
        var unauthorized = await anon.PostAsJsonAsync("/api/auth/login", new { email = "nobody@test.local", password = "whatever1" });

        Assert.Equal(HttpStatusCode.BadRequest, validation.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Equal("application/problem+json", validation.Content.Headers.ContentType!.MediaType);
        Assert.Contains("\"label\"", await validation.Content.ReadAsStringAsync());

        var errors = _factory.Logs.Entries.Where(e => e.Level >= LogLevel.Error).Select(e => $"{e.Category}: {e.Message}").ToList();
        Assert.True(errors.Count == 0, "expected rejections must not log errors, but saw:\n" + string.Join("\n", errors));
    }

    [Fact]
    public async Task ARealUnexpectedException_StillReturns500_AndStillLogsAnError()
    {
        var tech = await TechnicalClientAsync();
        var a = NewAutomation();
        _factory.Logs.Entries.Clear();

        // validate-formula calls the formula engine, which this factory has rigged to throw a non-application exception.
        var res = await tech.PostAsJsonAsync($"/api/automations/{a}/metrics/validate-formula", new { formula = "[x] + 1" });

        Assert.Equal(HttpStatusCode.InternalServerError, res.StatusCode);
        Assert.Contains(_factory.Logs.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("unhandled exception"));
    }
}

public class ApiExceptionFilterTests
{
    private sealed class TestProblemFactory : ProblemDetailsFactory
    {
        public override ProblemDetails CreateProblemDetails(HttpContext httpContext, int? statusCode = null, string? title = null,
            string? type = null, string? detail = null, string? instance = null) =>
            new() { Status = statusCode, Title = title, Detail = detail };

        public override ValidationProblemDetails CreateValidationProblemDetails(HttpContext httpContext, ModelStateDictionary modelStateDictionary,
            int? statusCode = null, string? title = null, string? type = null, string? detail = null, string? instance = null) =>
            new(modelStateDictionary) { Status = statusCode, Title = title };
    }

    private sealed class RecordingLogger : ILogger<ApiExceptionFilter>
    {
        public List<LogLevel> Levels { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Levels.Add(logLevel);
    }

    private readonly RecordingLogger _log = new();

    private ExceptionContext Run(Exception ex)
    {
        var ctx = new ExceptionContext(
            new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()), new List<IFilterMetadata>())
        { Exception = ex };
        new ApiExceptionFilter(new TestProblemFactory(), _log).OnException(ctx);
        return ctx;
    }

    [Theory]
    [InlineData(typeof(ConflictException), 409)]
    [InlineData(typeof(NotFoundException), 404)]
    [InlineData(typeof(UnauthorizedException), 401)]
    public void MapsEachExpectedException_ToItsStatus(Type type, int status)
    {
        var ctx = Run((Exception)Activator.CreateInstance(type, "boom")!);

        Assert.True(ctx.ExceptionHandled);
        var result = Assert.IsType<ObjectResult>(ctx.Result);
        Assert.Equal(status, result.StatusCode);
        Assert.Equal("boom", Assert.IsType<ProblemDetails>(result.Value).Detail);
    }

    [Fact]
    public void ValidationFailure_Becomes400_WithEveryFieldError()
    {
        var ctx = Run(new ValidationFailedException(new Dictionary<string, string[]>
        {
            ["email"] = ["bad email"],
            ["password"] = ["too short", "too boring"]
        }));

        var result = Assert.IsType<ObjectResult>(ctx.Result);
        Assert.Equal(400, result.StatusCode);
        var problem = Assert.IsType<ValidationProblemDetails>(result.Value);
        Assert.Equal(["bad email"], problem.Errors["email"]);
        Assert.Equal(["too short", "too boring"], problem.Errors["password"]);
    }

    [Theory]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(NullReferenceException))]
    [InlineData(typeof(OperationCanceledException))]
    public void AnythingElse_IsLeftAlone_SoItStillBecomesA500(Type type)
    {
        var ctx = Run((Exception)Activator.CreateInstance(type)!);

        Assert.False(ctx.ExceptionHandled);
        Assert.Null(ctx.Result);
        Assert.Empty(_log.Levels);
    }

    [Fact]
    public void ExpectedRejections_AreLoggedAtInformation_NeverAsErrors()
    {
        Run(new NotFoundException("x"));
        Run(new ConflictException("y"));

        Assert.Equal([LogLevel.Information, LogLevel.Information], _log.Levels);
    }
}
