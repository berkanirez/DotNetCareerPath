using Microsoft.EntityFrameworkCore;
using RoadmapOS.Web.Data;
using RoadmapOS.Web.Domain;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddDbContext<RoadmapOSDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("RoadmapOSDb")));
builder.Services.AddScoped<ISkillCatalog, EfSkillCatalog>();

var app = builder.Build();

// TEMPORARY — placeholder seeding until Day 9 introduces a real seed data strategy.
// Only runs in Development, and only if the Skills table is empty.
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<RoadmapOSDbContext>();
    if (!context.Skills.Any())
    {
        context.Skills.AddRange(
            new Skill("C#", "Language", SkillLevel.CanImplementWithGuidance, SkillLevel.CanExplainProduction),
            new Skill("ASP.NET Core", "Framework", SkillLevel.CanExplainPurpose, SkillLevel.CanImplementIndependently),
            new Skill("EF Core", "Framework", SkillLevel.NotStudied, SkillLevel.CanImplementIndependently)
            {
                Notes = "Not started yet"
            },
            new Skill("SQL Server", "Database", SkillLevel.NotStudied, SkillLevel.CanImplementWithGuidance)
        );
        context.SaveChanges();
    }
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
