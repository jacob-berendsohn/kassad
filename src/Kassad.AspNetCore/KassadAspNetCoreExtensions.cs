using Kassad.Engine;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kassad.AspNetCore;

/// <summary>Service registration for the ASP.NET Core integration.</summary>
public static class KassadServiceCollectionAspNetCoreExtensions
{
    /// <summary>
    /// Register <see cref="KassadOptions"/> and the <see cref="KassadDelegatingHandler"/>.
    /// Requires <see cref="IGuardrailEngine"/> (via <c>AddKassad</c>) and an <see cref="IDecisionModel"/> (via <c>AddTypeSafe</c>) to be registered as well.
    /// </summary>
    public static IServiceCollection AddKassadAspNetCore(this IServiceCollection services, Action<KassadOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = services.AddOptions<KassadOptions>();
        if (configure is not null)
        {
            options.Configure(configure);
        }

        options.Validate(o => o.RejectionStatusCode is >= 400 and <= 599, "RejectionStatusCode must be a 4xx or 5xx status.")
               .Validate(o => o.MaxBodyBytes > 0, "MaxBodyBytes must be positive.")
               .Validate(o => o.RejectAt != VerdictAction.Allow, "RejectAt cannot be Allow; that would reject everything.")
               .ValidateOnStart();

        services.TryAddTransient<KassadDelegatingHandler>();
        return services;
    }
}

/// <summary>Pipeline registration.</summary>
public static class KassadApplicationBuilderExtensions
{
    /// <summary>
    /// Add <see cref="KassadInboundMiddleware"/>. Place it after authentication (so verdicts can be tied to a principal in logs)
    /// and before the endpoints that accept LLM input. Consider <c>MapWhen</c>/<c>UseWhen</c> to scope it to those routes.
    /// </summary>
    public static IApplicationBuilder UseKassadInbound(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<KassadInboundMiddleware>();
    }
}

/// <summary>Attach Kassad to an <see cref="HttpClient"/> registration.</summary>
public static class KassadHttpClientBuilderExtensions
{
    /// <summary>
    /// Insert <see cref="KassadDelegatingHandler"/> into the client's handler chain.
    /// <code>services.AddHttpClient("openai", c => ...).AddKassadHandler();</code>
    /// </summary>
    public static IHttpClientBuilder AddKassadHandler(this IHttpClientBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddHttpMessageHandler<KassadDelegatingHandler>();
    }
}
