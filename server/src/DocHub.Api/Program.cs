using System.Text.Json;
using System.Text.Json.Serialization;
using DocHub.Api.Infrastructure;
using DocHub.DataAccess;
using DocHub.Integrations;
using DocHub.Services;
using DocHub.Services.Ingestion;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

// Layers register themselves; the host only composes them.
builder.Services.AddDataAccess(builder.Configuration);
builder.Services.AddIntegrations(builder.Configuration);
builder.Services.AddServices(builder.Configuration);

// Background ingestion. Hangfire shares the application's Postgres, so a queued
// job survives a restart and there is no second store to operate.
var databaseConnection = builder.Configuration["Database:ConnectionString"]
    ?? throw new InvalidOperationException("Database:ConnectionString must be configured.");

builder.Services.AddHangfire(hangfire => hangfire
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    // Hangfire creates and migrates its own `hangfire` schema on first run.
    // That is the one exception to this project's "provisioning is explicit"
    // rule, and a deliberate one: those tables are the job runner's private
    // bookkeeping, versioned with the library rather than with our migrations.
    // Application data is still only ever created by `dotnet ef database update`.
    .UsePostgreSqlStorage(postgres => postgres.UseNpgsqlConnection(databaseConnection)));

builder.Services.AddHangfireServer(options =>
{
    // Embedding is the bottleneck and a local model serves one request at a
    // time; more workers would just queue inside Ollama instead of here.
    options.WorkerCount = 2;
    options.Queues = ["default"];
});

builder.Services.AddScoped<IIngestionQueue, HangfireIngestionQueue>();

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.Converters.Add(
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    });

// Domain exceptions become RFC 7807 responses; services stay HTTP-agnostic.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ServiceExceptionHandler>();

// Cap uploads at the same 25 MB the service enforces, so an oversized file is
// rejected by the server before it is buffered rather than after.
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 25 * 1024 * 1024;
});

builder.Services.AddOpenApi();

// The Angular dev server runs on its own origin during local development.
const string DevCorsPolicy = "dochub-dev-client";
builder.Services.AddCors(options => options.AddPolicy(DevCorsPolicy, policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

// One-shot setup command: `dotnet run -- init-storage`.
//
// Provisioning is deliberately never done at startup. Creating databases or
// containers as a side effect of booting hides real configuration problems,
// races when more than one instance starts, and makes it unclear who owns the
// resource. Setup is an explicit step the operator runs — see the README.
if (args.Contains("init-storage"))
{
    await app.Services.InitializeIntegrationsAsync();
    return;
}

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseCors(DevCorsPolicy);

    // Development only: the dashboard is unauthenticated and exposes job
    // arguments — including document ids. It gets real authorisation when auth
    // lands in phase 5, and only then can it be exposed anywhere else.
    app.UseHangfireDashboard("/jobs");
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
