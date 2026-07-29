using System.Text.Json;
using System.Text.Json.Serialization;
using DocHub.Api.Infrastructure;
using DocHub.Api.Infrastructure.Auth;
using DocHub.DataAccess;
using DocHub.Integrations;
using DocHub.Services;
using DocHub.Services.Ingestion;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Swashbuckle.AspNetCore.SwaggerUI;

var builder = WebApplication.CreateBuilder(args);

// Layers register themselves; the host only composes them.
builder.Services.AddDataAccess(builder.Configuration);
builder.Services.AddIntegrations(builder.Configuration);
builder.Services.AddServices(builder.Configuration);

// Identity, the session cookie, and the binding of ICurrentUser to the
// authenticated principal.
builder.Services.AddDocHubAuthentication(builder.Configuration);
builder.Services.AddDocHubRateLimiting(builder.Configuration);

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

builder.Services.AddOpenApi(options =>
{
    // Describes the document itself, so the Swagger UI header says what this
    // API is rather than just echoing the assembly name.
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info = new()
        {
            Title = "DocHub API",
            Version = "v1",
            Description =
                "Documentation & Knowledge Hub — folders, documents, ingestion and "
                + "hybrid search. Errors are RFC 7807 problem details: 400 for a "
                + "rejected business rule, 404 for a missing entity.",
        };

        return Task.CompletedTask;
    });
});

// The Angular dev server runs on its own origin during local development.
const string DevCorsPolicy = "dochub-dev-client";
builder.Services.AddCors(options => options.AddPolicy(DevCorsPolicy, policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
    .AllowAnyHeader()
    .AllowAnyMethod()
    // The session lives in a cookie, so a cross-origin caller has to be
    // allowed to send it. Safe only because the origins are an explicit list —
    // AllowCredentials with AllowAnyOrigin is rejected by the framework, and
    // rightly.
    .AllowCredentials()));

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

// `dotnet run -- seed-admin` — sets the seeded administrator's password from
// configuration. Separate from startup for the same reason: a credential is
// provisioned deliberately, not as a side effect of the app booting.
if (args.Contains("seed-admin"))
{
    Environment.ExitCode = await AdminSeeder.RunAsync(app.Services, app.Configuration);
    return;
}

app.UseExceptionHandler();

// Order matters: who you are, then what you may do, then the endpoint.
app.UseAuthentication();
app.UseAuthorization();

// After authentication, so the chat limiter can partition by user id
// rather than lumping everyone into one anonymous bucket.
app.UseRateLimiter();

// Both dashboards are now gated on the Admin role rather than on the
// environment. Dev-only registration was a stand-in for authorisation — it kept
// them off production, but it also meant every developer's machine served an
// open jobs dashboard to anything that could reach the port.
app.UseAdminOnly("/swagger", "/openapi");

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.UseCors(DevCorsPolicy);

    // Interactive API browser at /swagger, reading the document MapOpenApi
    // serves. Registered in development only — an API explorer is a
    // development tool, not something to ship and then defend — and behind the
    // admin gate above even there.
    app.UseSwaggerUI(swagger =>
    {
        swagger.SwaggerEndpoint("/openapi/v1.json", "DocHub API v1");
        swagger.DocumentTitle = "DocHub API";
        // Collapsed by default; the endpoint list is long enough that expanded
        // models bury it.
        swagger.DocExpansion(DocExpansion.List);
        swagger.DisplayRequestDuration();
    });

    // Landing on the API root in a browser is nearly always someone looking
    // for the docs.
    app.MapGet("/", () => Results.Redirect("/swagger"))
        .AllowAnonymous()
        .ExcludeFromDescription();
}
else
{
    app.UseHttpsRedirection();
}

// The jobs dashboard exposes job arguments, which include document ids, so it
// is administrators only. Registered in every environment now that it is
// actually protected — a queue you can only inspect on a developer's laptop is
// not much use when a production job is stuck.
app.UseHangfireDashboard("/jobs", new DashboardOptions
{
    Authorization = [new AdminDashboardFilter()],
});

app.MapControllers();

// Liveness: is the process up at all. Deliberately checks nothing external, so
// an orchestrator does not restart the app just because Postgres blipped.
// Anonymous, deliberately: the thing asking is a load balancer or an
// orchestrator, which has no session and never will. Neither endpoint reveals
// anything but whether dependencies answer.
app.MapHealthChecks("/healthz/live", new HealthCheckOptions
{
    Predicate = _ => false,
}).AllowAnonymous();

// Readiness: can this instance actually serve traffic (DB + blob reachable).
app.MapHealthChecks("/healthz", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = WriteHealthResponse,
}).AllowAnonymous();

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
