using Microsoft.Extensions.DependencyInjection;

namespace FieldOps.Modules.Customers;

public static class CustomersModule
{
    public static IServiceCollection AddCustomersModule(this IServiceCollection services) =>
        services.AddSingleton<ICustomerDirectory, InMemoryCustomerDirectory>();
}
