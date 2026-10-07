using AgriSage.Api.Extensions;
using AgriSage.Api.Filters;
using AgriSage.Api.Middleware;
using AgriSage.Application;
using AgriSage.Infrastructure;

var commands = MaintenanceCommands.Detect(args);
var builder = WebApplication.CreateBuilder(MaintenanceCommands.WithoutFlags(args));

if (commands.Count > 0)
{
    MaintenanceCommands.SuppressProviderLogging(builder);
}

// Optional per-developer settings file (gitignored, never published): see appsettings.Local.example.json.
builder.AddLocalSettingsFile();

builder.Services.AddApplication();
builder.Services.Configure<AgriSage.Application.Features.Credit.CreditPolicy>(builder.Configuration.GetSection("Credit"));
// PayOS:Mode=Simulated confirms payments without money: Development only, checked before anything else is registered.
var simulatedPayments = AgriSage.Infrastructure.Payments.PayOsMode.IsSimulated(builder.Configuration);
AgriSage.Infrastructure.Payments.PayOsMode.EnsureAllowed(simulatedPayments, builder.Environment.IsDevelopment());
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddControllers(options => options.Filters.Add<ValidationFilter>());
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddApiAuthentication();
builder.Services.AddAuthorization();
builder.Services.AddApiRateLimiting();
builder.Services.AddApiCors(builder.Configuration);
builder.Services.AddApiSwagger();

var app = builder.Build();

if (simulatedPayments)
{
    app.Logger.LogWarning("PayOS:Mode=Simulated - online payments are simulated, no money moves and payOS is not called (development only).");
}

if (commands.Count > 0)
{
    Environment.ExitCode = await MaintenanceCommands.RunAsync(app, commands);
    return;
}

// First, so that preflight requests and error responses (401, 422, 500...) carry the CORS headers too.
app.UseCors(CorsExtensions.PolicyName);

app.UseExceptionHandler(_ => { });

if (app.Environment.IsDevelopment())
{
    app.UseApiSwagger();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers();

app.Run();
