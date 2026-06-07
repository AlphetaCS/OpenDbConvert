using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace OpenDbConvert.Infrastructure;

public static class ServiceRegistrar
{
    public static void RegisterServices(ServiceCollection services)
    {
        var serviceTypes = Assembly.GetExecutingAssembly().GetTypes()
            .Where(t => t.Namespace == "OpenDbConvert.Services" && t.IsClass && !t.IsAbstract);

        foreach (var serviceType in serviceTypes)
            services.AddSingleton(serviceType);
    }
}
