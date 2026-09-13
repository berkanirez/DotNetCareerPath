using Microsoft.EntityFrameworkCore;
using StockPilot.Api.Data;
using StockPilot.Api.Models;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddDbContext<StockPilotDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("StockPilotDb")));
builder.Services.AddScoped<IProductStore, EfProductStore>();

var app = builder.Build();

// Development-only: create starter data on a fresh database.
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<StockPilotDbContext>();
    DbSeeder.Seed(context);
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseExceptionHandler();

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
