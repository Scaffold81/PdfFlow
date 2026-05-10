using ApiGateway.Data;
using ApiGateway.Messaging;
using ApiGateway.Middleware;
using ApiGateway.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// -- Database ------------------------------------------------------------------
// PostgreSQL via Npgsql. Connection string is set per-environment
// in appsettings.json or as Docker environment variables.
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        npgsql => npgsql.CommandTimeout(30)
    )
);

// -- Messaging ----------------------------------------------------------------
// Singleton: one RabbitMQ connection shared for the lifetime of the application.
builder.Services.AddSingleton<IMessagePublisher, RabbitMQPublisher>();

// -- Application Services -----------------------------------------------------
// Scoped: a new DocumentService instance per HTTP request.
builder.Services.AddScoped<IDocumentService, DocumentService>();

// -- ASP.NET Core -------------------------------------------------------------
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "PdfFlow API", Version = "v1" });
    c.EnableAnnotations();
});

// Raise the request body size limit to allow PDF uploads up to 50 MB.
builder.Services.Configure<IISServerOptions>(o => o.MaxRequestBodySize = 52_428_800);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 52_428_800);

var app = builder.Build();

// -- Middleware Pipeline -------------------------------------------------------
// Global exception handler must be first so it catches errors from all middleware.
app.UseMiddleware<ExceptionHandlingMiddleware>();

// Swagger is enabled in Development and Docker environments.
if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Docker"))
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "PdfFlow API v1"));
}

app.MapControllers();

// -- Database Initialization --------------------------------------------------
// Wait for PostgreSQL to become available before accepting traffic.
// In production, consider using EF migrations instead of EnsureCreated.
await WaitForDatabaseAsync(app);

app.Run();

/// <summary>
/// Retries the database connection on startup to handle cases where
/// the PostgreSQL container is still initializing when the API starts.
/// </summary>
static async Task WaitForDatabaseAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    var retries = 5;
    while (retries-- > 0)
    {
        try
        {
            await db.Database.EnsureCreatedAsync();
            logger.LogInformation("Database connection established.");
            return;
        }
        catch (Exception ex)
        {
            logger.LogWarning("DB not ready ({Retries} retries left): {Message}", retries, ex.Message);
            await Task.Delay(3000);
        }
    }

    throw new Exception("Could not connect to the database after multiple retries.");
}
