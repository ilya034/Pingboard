using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Pingboard.Api.Infrastructure;

/// <summary>
///     Объявляет в OpenAPI схему безопасности Bearer и ставит требование к тем операциям,
///     которые помечены RequireAuthorization().
/// </summary>
/// <remarks>
///     Раньше здесь был <c>WithOpenApi</c> на группе маршрутов, но он признан устаревшим
///     (ASPDEPR002), а требование выводится прямо из метаданных endpoint'а: одна точка
///     правды — RequireAuthorization() в MonitorEndpoints, без дублирования списка путей.
/// </remarks>
public static class OpenApiSecuritySetup
{
    public const string SchemeId = "Bearer";

    public static OpenApiOptions AddBearerSecurity(this OpenApiOptions options)
    {
        options.AddDocumentTransformer((document, _, _) =>
        {
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes[SchemeId] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Access token issued by POST /api/auth/login (HS256)."
            };

            return Task.CompletedTask;
        });

        options.AddOperationTransformer((operation, context, _) =>
        {
            // Endpoint — источник правды: если маршрут требует авторизации, спека обязана это сказать.
            if (context.Description.ActionDescriptor.EndpointMetadata.OfType<IAuthorizeData>().Any())
            {
                operation.Security ??= [];
                operation.Security.Add(new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference(SchemeId, context.Document)] = []
                });
            }

            return Task.CompletedTask;
        });

        return options;
    }
}
