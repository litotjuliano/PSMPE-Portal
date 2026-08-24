using Microsoft.Extensions.DependencyInjection;
using PSMPE.Portal.Application.Content;
using PSMPE.Portal.Application.Events;
using PSMPE.Portal.Application.Layouts;
using PSMPE.Portal.Application.Members;
using PSMPE.Portal.Application.Payments;

namespace PSMPE.Portal.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IContentService, ContentService>();
        services.AddScoped<ILayoutService, LayoutService>();
        services.AddScoped<IMemberService, MemberService>();
        services.AddScoped<IMemberUploadService, MemberUploadService>();
        services.AddScoped<IMemberCertificateService, MemberCertificateService>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<IEventService, EventService>();
        return services;
    }
}
