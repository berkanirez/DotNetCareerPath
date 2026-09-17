using FieldOps.Modules.Organizations;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// The host installs the module through its own extension method — it never
// names InMemoryOrganizationDirectory (that class is `internal` to the
// module and genuinely unreachable from here, not just hidden by convention).
builder.Services.AddOrganizationsModule();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
