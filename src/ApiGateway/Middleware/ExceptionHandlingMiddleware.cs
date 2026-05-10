using System.Net;
using System.Text.Json;

namespace ApiGateway.Middleware;

/// <summary>
/// Global exception handling middleware.
/// Catches all unhandled exceptions and returns a structured RFC 7807 Problem Details response.
/// This keeps controller actions clean — they only handle happy-path logic.
/// </summary>
public class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception for {Method} {Path}",
                context.Request.Method, context.Request.Path);

            await WriteErrorResponse(context, ex);
        }
    }

    /// <summary>
    /// Maps exception types to HTTP status codes and writes a JSON error body.
    /// </summary>
    private static Task WriteErrorResponse(HttpContext context, Exception ex)
    {
        context.Response.ContentType = "application/problem+json";
        context.Response.StatusCode = ex switch
        {
            ArgumentException      => (int)HttpStatusCode.BadRequest,
            KeyNotFoundException   => (int)HttpStatusCode.NotFound,
            _                      => (int)HttpStatusCode.InternalServerError
        };

        var problem = new
        {
            title = ex switch
            {
                ArgumentException    => "Bad Request",
                KeyNotFoundException => "Not Found",
                _                    => "Internal Server Error"
            },
            // Hide internal details from clients on unexpected errors
            detail = context.Response.StatusCode == 500
                ? "An unexpected error occurred."
                : ex.Message,
            status = context.Response.StatusCode
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(problem));
    }
}
