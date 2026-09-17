using Kassad.Engine;
using Kassad.Policies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kassad;

/// <summary>DI registration for the guardrail engine. Register an <see cref="IDecisionModel"/> separately (e.g. <c>AddTypeSafe()</c>).</summary>
public static class KassadServiceCollectionExtensions
{
    /// <summary>
    /// Register <see cref="IGuardrailEngine"/> over an already-validated <see cref="PolicySet"/> with default
    /// <see cref="EvaluationOptions"/> (no budget). <see cref="EvaluationOptions"/> is registered through the
    /// options pattern and validated at startup, so it can also be bound from configuration.
    /// </summary>
    public static IServiceCollection AddKassad(this IServiceCollection services, PolicySet policies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(policies);

        services.TryAddSingleton(policies);
        services.AddOptions<EvaluationOptions>()
                .Validate(o => EvaluationOptions.IsValidBudget(o.Budget), EvaluationOptions.BudgetRule)
                .ValidateOnStart();
        services.TryAddSingleton<IGuardrailEngine, GuardrailEngine>();
        return services;
    }

    /// <summary>Register <see cref="IGuardrailEngine"/> over an already-validated <see cref="PolicySet"/> and configure its <see cref="EvaluationOptions"/>.</summary>
    /// <example>
    /// <code>
    /// services.AddKassad(policies, o => o.Budget = TimeSpan.FromMilliseconds(800));
    /// </code>
    /// </example>
    public static IServiceCollection AddKassad(this IServiceCollection services, PolicySet policies, Action<EvaluationOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        AddKassad(services, policies);
        services.Configure(configure);
        return services;
    }

    /// <summary>Register <see cref="IGuardrailEngine"/> from a policy JSON file. Fails fast at startup if the file is invalid.</summary>
    public static IServiceCollection AddKassad(this IServiceCollection services, string policyFilePath) =>
        AddKassad(services, PolicySet.FromFile(policyFilePath));

    /// <summary>Register <see cref="IGuardrailEngine"/> from a policy JSON file and configure its <see cref="EvaluationOptions"/>. Fails fast at startup if the file is invalid.</summary>
    public static IServiceCollection AddKassad(this IServiceCollection services, string policyFilePath, Action<EvaluationOptions> configure) =>
        AddKassad(services, PolicySet.FromFile(policyFilePath), configure);
}
