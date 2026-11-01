// Placeholder host. Later phases add DI wiring, middleware, ProblemDetails, health checks and CORS.
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();

app.Run();

/// <summary>Entry point. Public so integration tests can use WebApplicationFactory.</summary>
public partial class Program;
