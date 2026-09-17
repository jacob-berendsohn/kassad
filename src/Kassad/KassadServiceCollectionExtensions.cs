using Kassad.Engine;
using Kassad.Policies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kassad;

/// <summary>DI registration for the guardrail engine. Register an <see cref="IDecisionModel"/> separately (e.g. <c>AddTypeSafe()</c>).</summary>
public static class KassadServiceCollectionExtensions
{
    /// <summary>Register <see cref="IGuardrailEngine"/> over an already-validated <see cref="PolicySet"/>.</summary>
    public static IServiceCollection AddKassad(this IServiceCollection services, PolicySet policies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(policies);

        services.TryAddSingleton(policies);
        services.TryAddSingleton<IGuardrailEngine, GuardrailEngine>();
        return services;
    }

    /// <summary>Register <see cref="IGuardrailEngine"/> from a policy JSON file. Fails fast at startup if the file is invalid.</summary>
    public static IServiceCollection AddKassad(this IServiceCollection services, string policyFilePath) =>
        AddKassad(services, PolicySet.FromFile(policyFilePath));
}
