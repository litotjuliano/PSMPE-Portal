using Microsoft.Extensions.DependencyInjection;
using PSMPE.Portal.Application.Content;
using PSMPE.Portal.Application.Layouts;

namespace PSMPE.Portal.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IContentService, ContentService>();
        services.AddScoped<ILayoutService, LayoutService>();
        return services;
    }
}
