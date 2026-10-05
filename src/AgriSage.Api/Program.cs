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
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddControllers(options => options.Filters.Add<ValidationFilter>());
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddApiAuthentication();
builder.Services.AddAuthorization();
builder.Services.AddApiRateLimiting();
builder.Services.AddApiCors(builder.Configuration);
builder.Services.AddApiSwagger();

var app = builder.Build();

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
