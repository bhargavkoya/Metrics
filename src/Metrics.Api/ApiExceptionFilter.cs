using Metrics.Application.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace Metrics.Api;

/// <summary>
/// Turns the application's expected rejections (validation, not found, conflict, unauthorized) into ProblemDetails responses.
/// Handling them here, inside MVC, keeps them out of the exception-handler middleware, which logs everything it sees at
/// error level with a stack trace. Anything this filter does not recognise is left alone, so genuine bugs still reach that
/// middleware, still log as errors, and still return a 500.
/// </summary>
public class ApiExceptionFilter(ProblemDetailsFactory problems, ILogger<ApiExceptionFilter> log) : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        var http = context.HttpContext;

        ProblemDetails? problem = context.Exception switch
        {
            ValidationFailedException v => ValidationProblem(context, v),
            ConflictException c => problems.CreateProblemDetails(http, StatusCodes.Status409Conflict, "Conflict", detail: c.Message),
            NotFoundException n => problems.CreateProblemDetails(http, StatusCodes.Status404NotFound, "Not found", detail: n.Message),
            UnauthorizedException u => problems.CreateProblemDetails(http, StatusCodes.Status401Unauthorized, "Unauthorized", detail: u.Message),
            _ => null
        };

        if (problem is null) return; // not ours: let it surface as a 500

        // One line at Information: these are normal outcomes of bad input, not faults.
        log.LogInformation("Request rejected with {Status}: {Message}", problem.Status, context.Exception.Message);

        context.Result = new ObjectResult(problem) { StatusCode = problem.Status };
        context.ExceptionHandled = true;
    }

    private ProblemDetails ValidationProblem(ExceptionContext context, ValidationFailedException v)
    {
        foreach (var (field, messages) in v.Errors)
            foreach (var message in messages)
                context.ModelState.AddModelError(field, message);

        return problems.CreateValidationProblemDetails(context.HttpContext, context.ModelState, StatusCodes.Status400BadRequest, "Validation failed");
    }
}
