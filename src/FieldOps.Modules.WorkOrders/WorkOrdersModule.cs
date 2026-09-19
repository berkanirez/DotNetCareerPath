using Microsoft.Extensions.DependencyInjection;

namespace FieldOps.Modules.WorkOrders;

public static class WorkOrdersModule
{
    public static IServiceCollection AddWorkOrdersModule(this IServiceCollection services) =>
        services.AddSingleton<IWorkOrderDirectory, InMemoryWorkOrderDirectory>();
}
