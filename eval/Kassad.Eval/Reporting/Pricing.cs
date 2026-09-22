namespace Kassad.Eval.Reporting;

/// <summary>
/// The rate the cost column is computed at. TypeSafe publishes no price list (its docs index had no pricing page on
/// 2026-09-22), so this is the publicly quoted rate, labeled "quoted, verify" wherever it is printed, as the roadmap's
/// prerequisite says. Replace all four values together when official pricing appears.
/// </summary>
internal static class Pricing
{
    /// <summary>US dollars per million input tokens.</summary>
    public const double UsdPerMillionInputTokens = 0.042;

    /// <summary>Output tokens are not billed at the quoted rate.</summary>
    public const double UsdPerMillionOutputTokens = 0;

    /// <summary>Who quoted the rate, and when.</summary>
    public const string SourceName = "MarkTechPost, 2026-09-19";

    /// <summary>Where the quote can be checked.</summary>
    public const string SourceUrl = "https://www.marktechpost.com/2026/09/19/typesafe-ai-releases-jev/";
}
