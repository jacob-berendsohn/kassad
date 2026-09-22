using System.Collections.Concurrent;

namespace Kassad.Eval.Running;

/// <summary>
/// Forwards to the real model and remembers two things a <see cref="StageResult"/> does not carry: the release that
/// answered (<see cref="DecisionResponse.Model"/>, <c>jev-1.13.0</c> behind the <c>jev-latest</c> alias, which the API
/// notes say to log when comparing eval runs) and the most recent exception, so the runner can tell a failure no
/// retry will fix from a transient one after the first row.
/// </summary>
internal sealed class RecordingDecisionModel : IDecisionModel
{
    private readonly IDecisionModel _inner;
    private readonly ConcurrentDictionary<string, byte> _resolved = new(StringComparer.Ordinal);
    private volatile Exception? _lastFailure;

    public RecordingDecisionModel(IDecisionModel inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public string Name => _inner.Name;

    /// <summary>Every distinct <see cref="DecisionResponse.Model"/> seen so far, sorted. Usually one.</summary>
    public IReadOnlyList<string> ResolvedModels => _resolved.Keys.Order(StringComparer.Ordinal).ToArray();

    /// <summary>The most recent exception the model threw, or <c>null</c>. Cancellation does not count.</summary>
    public Exception? LastFailure => _lastFailure;

    public async Task<DecisionResponse> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _inner.EvaluateAsync(request, cancellationToken).ConfigureAwait(false);
            _resolved.TryAdd(response.Model, 0);
            return response;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _lastFailure = ex;
            throw;
        }
    }
}
