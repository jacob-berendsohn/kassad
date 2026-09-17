using Kassad.Engine;
using Kassad.Policies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Kassad.Tests;

/// <summary>
/// Registration-level checks. This test project does not reference the container implementation, so these
/// inspect the descriptors <c>AddKassad</c> adds instead of resolving from a built provider.
/// </summary>
public class KassadServiceCollectionExtensionsTests
{
    private static PolicySet Set() => PolicySet.FromPolicies([TestPolicies.Injection()]);

    [Fact]
    public void AddKassad_registers_the_engine_and_the_policies_as_singletons()
    {
        var services = new ServiceCollection();

        services.AddKassad(Set());

        Assert.Contains(services, d => d.ServiceType == typeof(PolicySet) && d.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(services, d => d.ServiceType == typeof(IGuardrailEngine) && d.ImplementationType == typeof(GuardrailEngine) && d.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddKassad_validates_the_budget()
    {
        var services = new ServiceCollection();

        services.AddKassad(Set());

        var validators = Instances<IValidateOptions<EvaluationOptions>>(services);
        Assert.NotEmpty(validators);
        Assert.All(validators, v => Assert.True(v.Validate(Options.DefaultName, new EvaluationOptions()).Succeeded));
        Assert.All(validators, v => Assert.True(v.Validate(Options.DefaultName, new EvaluationOptions { Budget = TimeSpan.FromMilliseconds(800) }).Succeeded));
        Assert.Contains(validators, v => v.Validate(Options.DefaultName, new EvaluationOptions { Budget = TimeSpan.Zero }).Failed);
        Assert.Contains(validators, v => v.Validate(Options.DefaultName, new EvaluationOptions { Budget = TimeSpan.FromMilliseconds(-1) }).Failed);
    }

    [Fact]
    public void AddKassad_with_a_delegate_configures_the_options()
    {
        var services = new ServiceCollection();

        services.AddKassad(Set(), o => o.Budget = TimeSpan.FromMilliseconds(250));

        var options = new EvaluationOptions();
        foreach (var configure in Instances<IConfigureOptions<EvaluationOptions>>(services))
        {
            configure.Configure(options);
        }

        Assert.Equal(TimeSpan.FromMilliseconds(250), options.Budget);
    }

    [Fact]
    public void AddKassad_guards_its_arguments()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddKassad(Set()));
        Assert.Throws<ArgumentNullException>(() => services.AddKassad((PolicySet)null!));
        Assert.Throws<ArgumentNullException>(() => services.AddKassad(Set(), null!));
    }

    private static List<T> Instances<T>(IServiceCollection services) where T : class =>
        services.Where(d => d.ServiceType == typeof(T)).Select(d => d.ImplementationInstance).OfType<T>().ToList();
}
