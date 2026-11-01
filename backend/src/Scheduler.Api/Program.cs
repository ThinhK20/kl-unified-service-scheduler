using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Scheduler.Api.Http;
using Scheduler.Api.Middleware;
using Scheduler.Api.Swagger;
using Scheduler.Application;
using Scheduler.Infrastructure;
using Scheduler.Infrastructure.Persistence;
using InfrastructureDependencyInjection = Scheduler.Infrastructure.DependencyInjection;

const string CorsPolicy = "web";
const string ReadyTag = "ready";

var builder = WebApplication.CreateBuilder(args);

// ---- Services (dependency injection; Application and Infrastructure register their own parts) ----
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services
    .AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    // Model binding errors → 400 INVALID_REQUEST ProblemDetails with a code.
    options.InvalidModelStateResponseFactory = ModelStateProblems.Create;

    // Leave bare 404/405/415 bodies empty so StatusCodeProblems writes them with a code.
    options.SuppressMapClientErrors = true;
});

builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddPolicy(CorsPolicy, policy => policy
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .WithExposedHeaders("Location", "Retry-After")));

builder.Services
    .AddHealthChecks()
    .AddSqlServer(
        connectionStringFactory: services => services.GetRequiredService<IConfiguration>().GetConnectionString(InfrastructureDependencyInjection.ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{InfrastructureDependencyInjection.ConnectionStringName}' is not configured."),
        name: "sqlserver",
        tags: [ReadyTag],
        timeout: TimeSpan.FromSeconds(5));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new() { Title = "Unified Service Scheduler API", Version = "v1" });
    var xmlFile = Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
    if (File.Exists(xmlFile))
    {
        options.IncludeXmlComments(xmlFile);
    }

    options.OperationFilter<IdempotencyKeyHeaderFilter>();
});

var app = builder.Build();

// ---- Pipeline (order matters) ----
// CORS first, so its headers are also on error responses written below.
app.UseCors(CorsPolicy);

// Chain of Responsibility: exceptions → ProblemDetails, then empty error statuses → ProblemDetails.
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseStatusCodePages(StatusCodeProblems.WriteAsync);

if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Swagger:Enabled"))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false, // the process is up; no dependencies checked
    ResponseWriter = WriteHealthAsync,
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains(ReadyTag), // Unhealthy → 503
    ResponseWriter = WriteHealthAsync,
});

app.MapControllers();

await app.Services.MigrateDatabaseIfEnabledAsync();

await app.RunAsync();

static Task WriteHealthAsync(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json";
    var body = new
    {
        status = report.Status.ToString(),
        checks = report.Entries.Select(entry => new { name = entry.Key, status = entry.Value.Status.ToString() }),
    };
    return context.Response.WriteAsync(JsonSerializer.Serialize(body, HealthJson));
}

/// <summary>Entry point. Public so integration tests can use WebApplicationFactory.</summary>
public partial class Program
{
    private static readonly JsonSerializerOptions HealthJson = new(JsonSerializerDefaults.Web);
}
