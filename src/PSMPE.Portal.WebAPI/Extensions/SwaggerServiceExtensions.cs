using Microsoft.OpenApi.Models;

namespace PSMPE.Portal.WebAPI.Extensions;

public static class SwaggerServiceExtensions
{
    public static IServiceCollection AddPortalSwagger(this IServiceCollection services)
    {
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "PSMPE Portal API",
                Version = "v1",
                Description = "Portal/CMS starter API — see /openspecs in the repo for feature contracts."
            });

            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "Bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Enter a JWT obtained from POST /api/auth/login, e.g. \"eyJhbGci...\" (no need to type \"Bearer \" — Swagger adds it)."
            });

            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
                    },
                    Array.Empty<string>()
                }
            });
        });

        return services;
    }
}
