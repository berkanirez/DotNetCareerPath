using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
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
// Singleton (not Scoped): a refresh token must survive across separate
// requests (login now, refresh later) — the same reasoning RoadmapOS's
// InMemorySkillCatalog used on Day 3, before EF Core needed Scoped instead.
builder.Services.AddSingleton<IRefreshTokenStore, InMemoryRefreshTokenStore>();

var jwtSection = builder.Configuration.GetSection("Jwt");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSection["Issuer"],
            ValidateAudience = true,
            ValidAudience = jwtSection["Audience"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSection["Key"]!)),
            ValidateLifetime = true,
            // Default is 5 minutes — a token can otherwise still be accepted
            // up to 5 minutes after its own `exp` claim says it expired
            // (meant to tolerate clock drift between servers). Set to zero
            // so our short-lived access tokens expire exactly when they say.
            ClockSkew = TimeSpan.Zero
        };
    });
builder.Services.AddAuthorization();

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

// Must come before UseAuthorization(): this is what actually reads the
// Authorization: Bearer <token> header and populates HttpContext.User.
// UseAuthorization() only checks whether that User is already authenticated —
// it never validates tokens itself.
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
