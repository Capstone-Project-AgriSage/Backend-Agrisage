using AgriSage.Application.Features.Auth.Dtos.Requests;
using AgriSage.Application.Features.Auth.Interfaces;
using AgriSage.Infrastructure.Persistence.Seed;
using FluentValidation;

namespace AgriSage.Api.Extensions;

public enum MaintenanceCommand
{
    Seed,
    CreateAdmin
}

// One-shot operator commands that run against the configured database and exit before HTTP starts:
//   --seed          reference data (roles, units, diseases, the store)
//   --create-admin  the first Admin account (AdminBootstrap:Email / :Password / :FullName / :PhoneNumber)
// Both together run in that order. Output is limited to counts and safe messages: provider exceptions can contain
// connection details and the admin password must never be printed, so nothing else is logged.
public static class MaintenanceCommands
{
    private const string SeedFlag = "--seed";
    private const string CreateAdminFlag = "--create-admin";

    public static IReadOnlyList<MaintenanceCommand> Detect(string[] args)
    {
        var commands = new List<MaintenanceCommand>();
        if (args.Contains(SeedFlag, StringComparer.Ordinal))
        {
            commands.Add(MaintenanceCommand.Seed);
        }

        if (args.Contains(CreateAdminFlag, StringComparer.Ordinal))
        {
            commands.Add(MaintenanceCommand.CreateAdmin);
        }

        return commands;
    }

    public static string[] WithoutFlags(string[] args) =>
        args.Where(arg => arg is not (SeedFlag or CreateAdminFlag)).ToArray();

    public static void SuppressProviderLogging(WebApplicationBuilder builder)
    {
        builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.None);
        builder.Logging.AddFilter("Npgsql", LogLevel.None);
    }

    // Returns the process exit code: 0 on success, 1 on failure or cancellation.
    public static async Task<int> RunAsync(WebApplication app, IReadOnlyList<MaintenanceCommand> commands)
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
            foreach (var command in commands)
            {
                switch (command)
                {
                    case MaintenanceCommand.Seed:
                        await SeedAsync(scope.ServiceProvider, app.Logger, cancellation.Token);
                        break;
                    case MaintenanceCommand.CreateAdmin:
                        await CreateAdminAsync(scope.ServiceProvider, app.Configuration, app.Logger, cancellation.Token);
                        break;
                }
            }

            return 0;
        }
        catch (MaintenanceCommandException exception)
        {
            app.Logger.LogError("{CommandError}", exception.Message);
            return 1;
        }
        catch (ReferenceSeedException exception)
        {
            app.Logger.LogError("{SeedError}", exception.Message);
            return 1;
        }
        catch (OperationCanceledException)
        {
            app.Logger.LogWarning("Command cancelled.");
            return 1;
        }
        catch (Exception)
        {
            // Provider exceptions can contain connection details: never log them here.
            app.Logger.LogError("Command failed. Check database access and the reference data / account constraints.");
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
            await app.DisposeAsync();
        }
    }

    private static async Task SeedAsync(IServiceProvider services, ILogger logger, CancellationToken cancellationToken)
    {
        var result = await services.GetRequiredService<DatabaseSeeder>().SeedAsync(cancellationToken);
        logger.LogInformation("Reference seed added roles={Roles}, units={Units}, diseases={Diseases}, stores={Stores}",
            result.Roles, result.Units, result.Diseases, result.Stores);
    }

    private static async Task CreateAdminAsync(
        IServiceProvider services,
        IConfiguration configuration,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var section = configuration.GetSection("AdminBootstrap");
        var request = new CreateAdminRequest(
            section["FullName"] ?? "Administrator",
            section["Email"] ?? string.Empty,
            section["PhoneNumber"],
            section["Password"] ?? string.Empty);

        var validation = await services.GetRequiredService<IValidator<CreateAdminRequest>>()
            .ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            // Field names only: values (the password) are never printed.
            var fields = string.Join(", ", validation.Errors.Select(error => error.PropertyName).Distinct());
            throw new MaintenanceCommandException(
                $"Invalid AdminBootstrap settings: {fields}. Set AdminBootstrap:Email and AdminBootstrap:Password (User Secrets / environment).");
        }

        var result = await services.GetRequiredService<IAdminBootstrapService>().CreateFirstAdminAsync(request, cancellationToken);
        logger.LogInformation(result == AdminBootstrapResult.Created
            ? "Admin account created."
            : "An Admin account already exists; nothing changed.");
    }

    // Only these deliberately safe messages may be displayed by the commands.
    private sealed class MaintenanceCommandException(string message) : InvalidOperationException(message);
}
