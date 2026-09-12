using Microsoft.EntityFrameworkCore;
using RoadmapOS.Web.Data;
using RoadmapOS.Web.Domain;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddDbContext<RoadmapOSDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("RoadmapOSDb")));
builder.Services.AddScoped<ISkillCatalog, EfSkillCatalog>();
builder.Services.AddSingleton<ProgressCalculator>();

var app = builder.Build();

// Development-only: create starter data on a fresh database.
// See Data/DbSeeder.cs for why this stays a runtime seeder instead of EF Core's HasData().
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<RoadmapOSDbContext>();
    DbSeeder.Seed(context);
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
