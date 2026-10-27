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
// AiService:Mode=Simulated returns made-up predictions: Development only, same rule.
AgriSage.Infrastructure.Ai.AiServiceMode.EnsureAllowed(
    AgriSage.Infrastructure.Ai.AiServiceMode.IsSimulated(builder.Configuration), builder.Environment.IsDevelopment());
// MessageDelivery:SmsProvider=Simulated sends no SMS and shows the codes in a development-only page: same rule.
var simulatedSms = AgriSage.Infrastructure.Messaging.SimulatedSmsMode.IsSimulated(builder.Configuration);
AgriSage.Infrastructure.Messaging.SimulatedSmsMode.EnsureAllowed(simulatedSms, builder.Environment.IsDevelopment());
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.Configure<AgriSage.Application.Features.Diagnosis.AiDiagnosisOptions>(
    options => options.AllowStubResults = builder.Environment.IsDevelopment());

builder.Services.AddControllers(options =>
{
    options.Filters.Add<AgriSage.Api.Authorization.PermissionFilter>();
    options.Filters.Add<ValidationFilter>();
});
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddApiAuthentication();
builder.Services.AddAuthorization();
builder.Services.AddSignalR(options =>
{
    options.MaximumReceiveMessageSize = 4096;
    options.EnableDetailedErrors = false;
});
builder.Services.AddSingleton<Microsoft.AspNetCore.SignalR.IUserIdProvider, AgriSage.Api.Realtime.SubjectUserIdProvider>();
builder.Services.AddScoped<AgriSage.Application.Features.Notifications.IRealtimePublisher, AgriSage.Api.Realtime.SignalRPublisher>();
builder.Services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, AgriSage.Api.Authorization.DelegatedPermissionHandler>();
builder.Services.AddApiRateLimiting();
builder.Services.AddApiCors(builder.Configuration);
builder.Services.AddApiSwagger();

var app = builder.Build();

if (simulatedPayments)
{
    app.Logger.LogWarning("PayOS:Mode=Simulated - online payments are simulated, no money moves and payOS is not called (development only).");
}

if (simulatedSms)
{
    app.Logger.LogWarning("MessageDelivery:SmsProvider=Simulated - no SMS is sent; codes show at /api/dev/sms-outbox/view (development only).");
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
app.MapHub<AgriSage.Api.Realtime.EventsHub>("/api/realtime", options => options.CloseOnAuthenticationExpiration = true);

app.Run();
