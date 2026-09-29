using AgriSage.Api.Extensions;
using AgriSage.Application;
using AgriSage.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddControllers();
builder.Services.AddApiAuthentication();
builder.Services.AddAuthorization();
builder.Services.AddApiSwagger();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseApiSwagger();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
