using Microsoft.EntityFrameworkCore;
using Worker.Data;
using Worker.Messaging;
using Worker.Services;

var builder = Host.CreateApplicationBuilder(args);

// -- Database ------------------------------------------------------------------
// Scoped lifetime: a fresh DbContext is created per message (per DI scope).
// This avoids stale state between consecutive message processings.
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        npgsql => npgsql.CommandTimeout(60)  // Allow more time for large PDFs
    )
);

// -- PDF Extraction ------------------------------------------------------------
// Scoped: instantiated fresh for each message processing scope.
builder.Services.AddScoped<IPdfExtractor, PdfPigExtractor>();

// -- RabbitMQ Consumer --------------------------------------------------------
// Singleton BackgroundService - runs for the entire lifetime of the Worker.
builder.Services.AddHostedService<RabbitMQConsumerService>();

var host = builder.Build();

// -- Database Readiness Check -------------------------------------------------
// Wait for PostgreSQL to be available before starting the consumer.
// Prevents "connection refused" errors during Docker startup sequencing.
await WaitForDatabaseAsync(host);

await host.RunAsync();

/// <summary>
/// Retries database connection on startup to tolerate slow PostgreSQL initialization.
/// </summary>
static async Task WaitForDatabaseAsync(IHost host)
{
    using var scope = host.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    var retries = 10;
    while (retries-- > 0)
    {
        try
        {
            await db.Database.EnsureCreatedAsync();
            logger.LogInformation("Database is ready.");
            return;
        }
        catch (Exception ex)
        {
            logger.LogWarning("DB not ready ({Retries} retries left): {Message}", retries, ex.Message);
            await Task.Delay(3000);
        }
    }

    throw new Exception("Database unavailable after multiple retries.");
}
