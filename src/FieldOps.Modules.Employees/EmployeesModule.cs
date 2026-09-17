using Microsoft.Extensions.DependencyInjection;

namespace FieldOps.Modules.Employees;

public static class EmployeesModule
{
    public static IServiceCollection AddEmployeesModule(this IServiceCollection services)
    {
        return services.AddSingleton<IEmployeeDirectory, InMemoryEmployeeDirectory>();
    }
}
