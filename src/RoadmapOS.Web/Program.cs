using RoadmapOS.Web.Domain;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    // TEMPORARY — Day 2 domain verification only. Will be removed on Day 3
    // once a real controller/view vertical slice replaces this console check.
    var sqlServerSkill = new Skill("SQL Server", "Database", SkillLevel.NotStudied, SkillLevel.CanImplementWithGuidance);

    var skills = new List<Skill>
    {
        sqlServerSkill,
        new("C#", "Language", SkillLevel.CanImplementWithGuidance, SkillLevel.CanExplainProduction),
        new("ASP.NET Core", "Framework", SkillLevel.CanExplainPurpose, SkillLevel.CanImplementIndependently),
        new("EF Core", "Framework", SkillLevel.NotStudied, SkillLevel.CanImplementIndependently)
        {
            Notes = "Not started yet"
        }
    };

    skills.Sort();

    Console.WriteLine("--- Day 2 domain verification ---");
    foreach (var skill in skills)
    {
        Console.WriteLine($"{skill.Name} ({skill.Category}): {skill.CurrentLevel} -> {skill.TargetLevel}, AtTarget={skill.IsAtTarget}");
    }

    Console.WriteLine($"Notes length (null-safe): {skills[0].Notes?.Length ?? 0}");
    Console.WriteLine($"SQL Server Notes length (null-safe): {sqlServerSkill.Notes?.Length ?? 0}");

    var snapshotToday = new SkillSnapshot("C#", SkillLevel.CanImplementWithGuidance, DateOnly.FromDateTime(DateTime.Today));
    var snapshotNextWeek = snapshotToday with
    {
        Level = SkillLevel.CanImplementIndependently,
        RecordedOn = snapshotToday.RecordedOn.AddDays(7)
    };
    var sameSnapshotAgain = snapshotToday with { };

    Console.WriteLine($"Original snapshot: {snapshotToday}");
    Console.WriteLine($"Updated snapshot:  {snapshotNextWeek}");
    Console.WriteLine($"Original == Updated? {snapshotToday == snapshotNextWeek}");
    Console.WriteLine($"Original == re-created copy? {snapshotToday == sameSnapshotAgain}");
    Console.WriteLine("--- end verification ---");
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
