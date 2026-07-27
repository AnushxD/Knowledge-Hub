using System.Text.Json;
using System.Text.Json.Serialization;
using DocHub.DataAccess;
using DocHub.Integrations;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

// Layers register themselves; the host only composes them.
builder.Services.AddDataAccess(builder.Configuration);
builder.Services.AddIntegrations(builder.Configuration);

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddOpenApi();

// The Angular dev server runs on its own origin during local development.
const string DevCorsPolicy = "dochub-dev-client";
builder.Services.AddCors(options => options.AddPolicy(DevCorsPolicy, policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseCors(DevCorsPolicy);
}
else
{
    app.UseHttpsRedirection();
}

app.MapControllers();

// Liveness: is the process up at all. Deliberately checks nothing external, so
// an orchestrator does not restart the app just because Postgres blipped.
app.MapHealthChecks("/healthz/live", new HealthCheckOptions
{
    Predicate = _ => false,
});

// Readiness: can this instance actually serve traffic (DB + blob reachable).
app.MapHealthChecks("/healthz", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = WriteHealthResponse,
});

app.Run();

static Task WriteHealthResponse(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json";

    return context.Response.WriteAsync(JsonSerializer.Serialize(new
    {
        status = report.Status.ToString(),
        totalDurationMs = report.TotalDuration.TotalMilliseconds,
        checks = report.Entries.Select(entry => new
        {
            name = entry.Key,
            status = entry.Value.Status.ToString(),
            description = entry.Value.Description,
            error = entry.Value.Exception?.Message,
            data = entry.Value.Data,
        }),
    }, new JsonSerializerOptions { WriteIndented = true }));
}

/// <summary>Exposed so integration tests can reference the host.</summary>
public partial class Program;
