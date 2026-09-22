namespace Kassad.Eval;

/// <summary>
/// A problem the operator can fix: a dataset that has not been downloaded, a policy file that does not validate, no
/// API key, a key the API rejects. The command line prints the message and exits <see cref="ExitCodes.Fatal"/>
/// without writing a results file.
/// </summary>
internal sealed class EvalUsageException : Exception
{
    public EvalUsageException(string message) : base(message)
    {
    }

    public EvalUsageException(string message, Exception? innerException) : base(message, innerException)
    {
    }
}
