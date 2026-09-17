using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Kassad.TypeSafe;

/// <summary>DI registration for <see cref="TypeSafeClient"/>.</summary>
public static class TypeSafeServiceCollectionExtensions
{
    /// <summary>
    /// Register <see cref="TypeSafeClient"/> as both itself and <see cref="IDecisionModel"/>, backed by a
    /// pooled <see cref="HttpClient"/> configured from <see cref="TypeSafeClientOptions"/>.
    /// </summary>
    /// <example>
    /// <code>
    /// services.AddTypeSafe(o => o.ApiKey = builder.Configuration["TypeSafe:ApiKey"]);
    /// </code>
    /// </example>
    public static IServiceCollection AddTypeSafe(this IServiceCollection services, Action<TypeSafeClientOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = services.AddOptions<TypeSafeClientOptions>();
        if (configure is not null)
        {
            options.Configure(configure);
        }

        options.Validate(o => o.MaxRetries >= 0, "MaxRetries must be >= 0.")
               .Validate(o => o.BaseAddress.IsAbsoluteUri, "BaseAddress must be absolute.")
               .Validate(o => !string.IsNullOrWhiteSpace(o.Model), "Model is required.")
               .ValidateOnStart();

        services.AddHttpClient(TypeSafeClient.HttpClientName, (provider, http) =>
        {
            var o = provider.GetRequiredService<IOptions<TypeSafeClientOptions>>().Value;
            http.BaseAddress = o.BaseAddress;
            http.Timeout = o.Timeout;
        });

        services.TryAddSingleton<TypeSafeClient>();
        services.TryAddSingleton<IDecisionModel>(provider => provider.GetRequiredService<TypeSafeClient>());
        return services;
    }
}
