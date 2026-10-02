using AgriSage.Api.Extensions;
using AgriSage.Application;
using AgriSage.Infrastructure;
using AgriSage.Infrastructure.Persistence.Seed;

var seedRequested = args.Contains("--seed", StringComparer.Ordinal);
var builder = WebApplication.CreateBuilder(args.Where(arg => arg != "--seed").ToArray());

if (seedRequested)
{
    // The command reports counts or safe messages only, including when a provider operation fails.
    builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.None);
    builder.Logging.AddFilter("Npgsql", LogLevel.None);
}

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddControllers();
builder.Services.AddApiAuthentication();
builder.Services.AddAuthorization();
builder.Services.AddApiSwagger();

var app = builder.Build();

if (seedRequested)
{
    using var cancellation = new CancellationTokenSource();
    ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        cancellation.Cancel();
    };
    Console.CancelKeyPress += cancelHandler;
    try
    {
        await using var scope = app.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync(cancellation.Token);
        app.Logger.LogInformation("Reference seed added roles={Roles}, units={Units}, diseases={Diseases}, stores={Stores}",
            result.Roles, result.Units, result.Diseases, result.Stores);
    }
    catch (ReferenceSeedException exception)
    {
        app.Logger.LogError("{SeedError}", exception.Message);
        Environment.ExitCode = 1;
    }
    catch (OperationCanceledException)
    {
        app.Logger.LogWarning("Reference seed cancelled.");
        Environment.ExitCode = 1;
    }
    catch (Exception)
    {
        // Provider exceptions can contain connection details: never log them here.
        app.Logger.LogError("Reference seed failed. Check database access and reference data constraints.");
        Environment.ExitCode = 1;
    }
    finally
    {
        Console.CancelKeyPress -= cancelHandler;
        await app.DisposeAsync();
    }

    return;
}

if (app.Environment.IsDevelopment())
{
    app.UseApiSwagger();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
