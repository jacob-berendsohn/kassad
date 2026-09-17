namespace Kassad.Policies;

/// <summary>A policy set failed validation. Every problem is listed in <see cref="Errors"/>; fix them all at once.</summary>
public sealed class PolicyValidationException : Exception
{
    /// <summary>Each entry is one human-readable problem, prefixed with the policy id where applicable.</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>Create the exception from a list of problems.</summary>
    public PolicyValidationException(IReadOnlyList<string> errors)
        : base($"Policy set is invalid ({errors?.Count ?? 0} error(s)):{Environment.NewLine}  - {string.Join($"{Environment.NewLine}  - ", errors ?? [])}")
    {
        Errors = errors ?? [];
    }
}
