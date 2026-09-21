using FieldOps.Modules.Customers.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FieldOps.Modules.Customers;

public static class CustomersModule
{
    public static IServiceCollection AddCustomersModule(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<CustomersDbContext>(options => options.UseSqlServer(connectionString));
        return services.AddScoped<ICustomerDirectory, EfCustomerDirectory>();
    }
}
