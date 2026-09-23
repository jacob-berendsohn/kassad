namespace Kassad.Eval;

/// <summary>Process exit codes of <c>kassad-eval</c>. Documented in <c>eval/README.md</c>.</summary>
internal static class ExitCodes
{
    /// <summary>run: every row was evaluated and the file was written. report: the report was printed.</summary>
    public const int Ok = 0;

    /// <summary>
    /// The run could not start or could not go on: bad arguments, an unreadable dataset or policy file, no API key, a
    /// key or request the API rejects. Nothing was written. For report: a missing directory, no results file in it, or a
    /// file the report cannot read. (Parse errors also exit 1, through System.CommandLine.)
    /// </summary>
    public const int Fatal = 1;

    /// <summary>The run completed and the file was written, but some rows carry a model error instead of an answer.</summary>
    public const int CompletedWithErrors = 3;

    /// <summary>compare: the comparison was printed and at least one number moved more than the tolerance.</summary>
    public const int Drifted = 4;

    /// <summary>The run was cancelled. The file holds the rows evaluated so far and says it was interrupted.</summary>
    public const int Interrupted = 130;
}
