using Kassad.TypeSafe;

namespace Kassad.Eval.Running;

/// <summary>What <c>kassad-eval run</c> was asked to do. The defaults here are the command line's defaults.</summary>
internal sealed record RunSettings
{
    /// <summary>Where the download scripts put raw data, relative to the working directory: run from the repository root.</summary>
    public const string DefaultDataDir = "eval/data";

    /// <summary>Rows in flight at once. Rate limits are unknown (roadmap prerequisites), so the default is conservative.</summary>
    public const int DefaultConcurrency = 4;

    /// <summary>Seed for <see cref="Sample"/>.</summary>
    public const int DefaultSeed = 0;

    /// <summary>
    /// Retries per request, above the client's default of 3, and the longest wait between them, above the client's 5 s:
    /// nobody is waiting on an eval run, so a 429 should be waited out per <c>Retry-After</c> rather than recorded as
    /// error rows. Both go straight into <see cref="TypeSafeClientOptions"/>; the retry itself is the client's.
    /// </summary>
    public const int MaxRetries = 5;

    /// <inheritdoc cref="MaxRetries"/>
    public static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);

    /// <summary>The <c>--dataset</c> name.</summary>
    public required string DatasetName { get; init; }

    /// <summary>The policy file, as given on the command line.</summary>
    public required string PoliciesPath { get; init; }

    /// <summary>Where the results file goes. Overwritten if present.</summary>
    public required string OutPath { get; init; }

    /// <summary>The directory the download scripts fill.</summary>
    public string DataDir { get; init; } = DefaultDataDir;

    /// <summary>Rows evaluated at the same time.</summary>
    public int Concurrency { get; init; } = DefaultConcurrency;

    /// <summary>Evaluate a deterministic stratified sample of this many rows instead of the full set; <c>null</c> for the full set.</summary>
    public int? Sample { get; init; }

    /// <summary>Seed for <see cref="Sample"/>.</summary>
    public int Seed { get; init; } = DefaultSeed;

    /// <summary>The TypeSafe model alias to send.</summary>
    public string Model { get; init; } = TypeSafeClientOptions.DefaultModel;
}
