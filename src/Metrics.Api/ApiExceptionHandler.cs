using Metrics.Application.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Metrics.Api;

/// <summary>Maps application exceptions to ProblemDetails responses; anything else falls through to a 500.</summary>
public class ApiExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        ProblemDetails problem;
        switch (ex)
        {
            case ValidationFailedException v:
                problem = new ValidationProblemDetails(v.Errors.ToDictionary(e => e.Key, e => e.Value))
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "Validation failed"
                };
                break;
            case ConflictException c:
                problem = new ProblemDetails { Status = StatusCodes.Status409Conflict, Title = "Conflict", Detail = c.Message };
                break;
            case NotFoundException n:
                problem = new ProblemDetails { Status = StatusCodes.Status404NotFound, Title = "Not found", Detail = n.Message };
                break;
            case UnauthorizedException u:
                problem = new ProblemDetails { Status = StatusCodes.Status401Unauthorized, Title = "Unauthorized", Detail = u.Message };
                break;
            default:
                return false;
        }

        ctx.Response.StatusCode = problem.Status!.Value;
        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = ctx, ProblemDetails = problem });
    }
}
