using System.Diagnostics;
using Microsoft.Extensions.Options;
using TempoDesk.Api;
using TempoDesk.Infrastructure.Configuration;
using TempoDesk.Infrastructure.Health;
using TempoDesk.Infrastructure.Identity;

if (args.Contains("--health-check", StringComparer.Ordinal))
{
    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
    try { return (await client.GetAsync("http://localhost:8080/health/ready")).IsSuccessStatusCode ? 0 : 1; }
    catch (HttpRequestException) { return 1; }
    catch (TaskCanceledException) { return 1; }
}
Activity.DefaultIdFormat = ActivityIdFormat.W3C;
Activity.ForceDefaultIdFormat = true;
var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.None);
builder.Services.AddFoundationConfiguration(builder.Configuration);
builder.Services.AddTempoDeskIdentity();
builder.Services.AddSingleton<DatabaseReadiness>();
builder.Services.AddControllers();
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    context.ProblemDetails.Type = $"https://httpstatuses.com/{context.ProblemDetails.Status}";
    context.ProblemDetails.Detail = "The request could not be completed.";
    context.ProblemDetails.Extensions["traceId"] = Activity.Current?.TraceId.ToString() ?? context.HttpContext.TraceIdentifier;
});
builder.Services.AddCors();
var app = builder.Build();
app.UseMiddleware<RequestBoundaryMiddleware>();
app.UseRouting();
var cors = app.Services.GetRequiredService<IOptions<CorsOptions>>().Value;
app.UseCors(policy => policy.WithOrigins(cors.AllowedOrigins).AllowAnyHeader().AllowAnyMethod());
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/health/live", () => Results.Json(new { status = "Healthy", elapsedMs = 0d }));
app.MapGet("/health/ready", async (DatabaseReadiness database, CancellationToken cancellationToken) =>
{
    var timer = Stopwatch.StartNew();
    var ready = await database.IsReadyAsync(cancellationToken);
    return Results.Json(new { status = ready ? "Healthy" : "Unhealthy", elapsedMs = timer.Elapsed.TotalMilliseconds }, statusCode: ready ? 200 : 503);
});
if (app.Environment.IsDevelopment())
    app.MapGet("/dev/errors/{status:int}", (int status) => ErrorExamples.Throw(status));
app.MapControllers();
var authentication = app.Services.GetRequiredService<IOptions<AuthenticationOptions>>().Value;
var jira = app.Services.GetRequiredService<IOptions<JiraOptions>>().Value;
var email = app.Services.GetRequiredService<IOptions<EmailOptions>>().Value;
var storage = app.Services.GetRequiredService<IOptions<FileStorageOptions>>().Value;
var jobs = app.Services.GetRequiredService<IOptions<BackgroundJobsOptions>>().Value;
FoundationLog.IntegrationsConfigured(app.Logger, authentication.Enabled, jira.Enabled, email.Enabled, storage.Enabled, jobs.Enabled);
await app.RunAsync();
return 0;

public partial class Program;
