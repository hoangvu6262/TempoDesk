using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Diagnostics;
using System.Security;
using Microsoft.AspNetCore.Mvc;

namespace TempoDesk.Api;

public sealed class RequestBoundaryMiddleware(RequestDelegate next, ILogger<RequestBoundaryMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var timer = Stopwatch.StartNew();
        using var activity = new Activity("TempoDesk.Request");
        if (Activity.Current is null && ActivityContext.TryParse(context.Request.Headers["traceparent"], null, out var parent))
            activity.SetParentId(parent.TraceId, parent.SpanId, parent.TraceFlags);
        activity.Start();
        var traceId = activity.TraceId.ToString();
        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["UserId"] = null, ["WorkspaceId"] = null, ["JobId"] = null
        });
        try
        {
            try { await next(context); }
            catch (Exception exception) when (!context.Response.HasStarted)
            {
                context.Response.Clear();
                context.Response.StatusCode = exception switch
                {
                    ValidationException or ArgumentException or BadHttpRequestException => 400,
                    UnauthorizedAccessException => 401,
                    SecurityException => 403,
                    KeyNotFoundException => 404,
                    DBConcurrencyException => 409,
                    _ => 500
                };
            }
            if (context.Response.StatusCode >= 400 && !context.Response.HasStarted && context.Response.ContentType is null)
                await WriteProblemAsync(context, traceId);
        }
        finally
        {
            if (!context.Request.Path.StartsWithSegments("/health"))
                FoundationLog.RequestCompleted(logger,
                    context.Request.Method, (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "unmatched",
                    context.Response.StatusCode, timer.Elapsed.TotalMilliseconds, traceId);
        }
    }

    private static async Task WriteProblemAsync(HttpContext context, string traceId)
    {
        var status = context.Response.StatusCode;
        var title = status switch
        {
            400 => "Bad Request", 401 => "Unauthorized", 403 => "Forbidden", 404 => "Not Found",
            409 => "Conflict", _ => "Internal Server Error"
        };
        ProblemDetails problem = status == 400
            ? new ValidationProblemDetails(new Dictionary<string, string[]> { ["request"] = ["The request is invalid."] })
            : new ProblemDetails();
        problem.Type = $"https://httpstatuses.com/{status}";
        problem.Title = title;
        problem.Status = status;
        problem.Detail = status == 500 ? "An unexpected error occurred." : $"The request could not be completed: {title}.";
        problem.Extensions["traceId"] = traceId;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync((object)problem, options: (System.Text.Json.JsonSerializerOptions?)null, contentType: "application/problem+json");
    }
}

internal static partial class FoundationLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Request completed {Method} {Route} {StatusCode} {ElapsedMs} {TraceId}")]
    public static partial void RequestCompleted(ILogger logger, string method, string route, int statusCode, double elapsedMs, string traceId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Integrations configured: Authentication={AuthenticationEnabled} Jira={JiraEnabled} Email={EmailEnabled} FileStorage={FileStorageEnabled} BackgroundJobs={BackgroundJobsEnabled}")]
    public static partial void IntegrationsConfigured(ILogger logger, bool authenticationEnabled, bool jiraEnabled, bool emailEnabled, bool fileStorageEnabled, bool backgroundJobsEnabled);
}

internal static class ErrorExamples
{
    public static IResult Throw(int status) => throw status switch
    {
        400 => new ValidationException("Invalid request"),
        401 => new UnauthorizedAccessException(),
        403 => new SecurityException(),
        404 => new KeyNotFoundException(),
        409 => new DBConcurrencyException(),
        _ => new InvalidOperationException("Diagnostic failure")
    };
}
