using Scheduler.Infrastructure;
using Scheduler.Infrastructure.Persistence;

// Placeholder host. Later phases add DI wiring, middleware, ProblemDetails, health checks and CORS.
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

await app.Services.MigrateDatabaseIfEnabledAsync();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();

app.Run();

/// <summary>Entry point. Public so integration tests can use WebApplicationFactory.</summary>
public partial class Program;
