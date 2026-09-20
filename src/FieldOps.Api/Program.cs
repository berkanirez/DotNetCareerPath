using FieldOps.Api.Application;
using FieldOps.Modules.Customers;
using FieldOps.Modules.Employees;
using FieldOps.Modules.Organizations;
using FieldOps.Modules.WorkOrders;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// The host installs each module through its own extension method — it never
// names either module's internal concrete implementation class directly.
builder.Services.AddOrganizationsModule();
builder.Services.AddEmployeesModule();
builder.Services.AddWorkOrdersModule();
builder.Services.AddCustomersModule();

// Application-layer service: cross-module orchestration that belongs to the
// host (Day 34), not inside either module or directly inside a controller.
builder.Services.AddScoped<EmployeeApplicationService>();
builder.Services.AddScoped<WorkOrderAssignmentService>();

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
