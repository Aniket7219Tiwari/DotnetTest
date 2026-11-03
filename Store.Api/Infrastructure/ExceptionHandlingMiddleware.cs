using Microsoft.AspNetCore.Mvc;
using Store.Api.Application;

namespace Store.Api.Infrastructure;

public sealed class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try { await next(context); }
        catch (Exception exception)
        {
            if (context.Response.HasStarted) throw;
            var status = exception is ApiException api
                ? api.StatusCode
                : DatabaseErrors.IsDeadlock(exception)
                    ? StatusCodes.Status409Conflict
                    : StatusCodes.Status500InternalServerError;
            if (status >= StatusCodes.Status500InternalServerError)
                if (status == StatusCodes.Status500InternalServerError)
                    logger.LogError(exception, "Unhandled request failure for {Method} {Path}",
                        context.Request.Method, context.Request.Path);
                else
                    logger.LogInformation(exception, "Database write conflict for {Method} {Path}",
                        context.Request.Method, context.Request.Path);
            else
                logger.LogInformation(exception, "Request rejected with {StatusCode} for {Method} {Path}",
                    status, context.Request.Method, context.Request.Path);
            context.Response.Clear();
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/problem+json";
            var details = new ProblemDetails
            {
                Status = status,
                Title = status == 500 ? "An unexpected error occurred." : "Request failed.",
                Detail = status == 500 ? "The request could not be completed." : exception.Message,
                Instance = context.Request.Path
            };
            details.Extensions["traceId"] = context.TraceIdentifier;
            await context.Response.WriteAsJsonAsync(details);
        }
    }
}
