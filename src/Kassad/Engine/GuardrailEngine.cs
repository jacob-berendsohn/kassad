using System.Diagnostics;
using Kassad.Policies;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kassad.Engine;

/// <summary>
/// Default <see cref="IGuardrailEngine"/>. Batches every policy for a stage into a single
/// <see cref="DecisionRequest"/> (questions are evaluated independently and in parallel upstream, so
/// adding policies does not add round trips), then resolves each answer with <see cref="VerdictResolver"/>.
/// </summary>
public sealed class GuardrailEngine : IGuardrailEngine
{
    private readonly IDecisionModel _model;
    private readonly PolicySet _policies;
    private readonly ILogger<GuardrailEngine> _logger;

    /// <summary>Create an engine over a decision model and a validated policy set.</summary>
    public GuardrailEngine(IDecisionModel model, PolicySet policies, ILogger<GuardrailEngine>? logger = null)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _policies = policies ?? throw new ArgumentNullException(nameof(policies));
        _logger = logger ?? NullLogger<GuardrailEngine>.Instance;
    }

    /// <inheritdoc />
    public async Task<StageResult> EvaluateAsync(Stage stage, object state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        var policies = _policies.ForStage(stage);
        if (policies.IsEmpty)
        {
            return new StageResult
            {
                Stage = stage,
                Verdicts = [],
                Outcome = VerdictAction.Allow,
                ModelLatency = TimeSpan.Zero,
            };
        }

        var questions = policies.ToDictionary(p => p.Id, p => p.Question, StringComparer.Ordinal);
        var request = new DecisionRequest(state, questions);

        var stopwatch = Stopwatch.StartNew();
        DecisionResponse? response = null;
        Exception? failure = null;

        try
        {
            response = await _model.EvaluateAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        stopwatch.Stop();

        var verdicts = new Verdict[policies.Length];
        for (var i = 0; i < policies.Length; i++)
        {
            var policy = policies[i];
            verdicts[i] = failure is not null
                ? VerdictResolver.FromError(policy, failure)
                : response!.Answers.TryGetValue(policy.Id, out var answer)
                    ? VerdictResolver.Resolve(policy, answer)
                    : VerdictResolver.FromError(policy, new DecisionModelException($"No answer returned for policy '{policy.Id}'."));
        }

        var outcome = verdicts.Max(v => v.Action);
        var result = new StageResult
        {
            Stage = stage,
            Verdicts = verdicts,
            Outcome = outcome,
            ModelLatency = stopwatch.Elapsed,
            Usage = response?.Usage,
        };

        Log(result, failure);
        return result;
    }

    private void Log(StageResult result, Exception? failure)
    {
        if (failure is not null)
        {
            _logger.LogWarning(
                failure,
                "Kassad {Stage}: decision model {Model} failed after {LatencyMs}ms; error policies applied, outcome {Outcome}",
                result.Stage, _model.Name, result.ModelLatency.TotalMilliseconds, result.Outcome);
        }

        foreach (var v in result.Verdicts)
        {
            var level = v.Action switch
            {
                VerdictAction.Allow => LogLevel.Debug,
                VerdictAction.Flag => LogLevel.Information,
                _ => LogLevel.Warning,
            };

            if (_logger.IsEnabled(level))
            {
                _logger.Log(
                    level,
                    "Kassad {Stage} {PolicyId} → {Action} (value={Value} confidence={Confidence} fromError={FromError}): {Reason}",
                    v.Stage, v.PolicyId, v.Action, v.Value, v.Confidence, v.FromError, v.Reason);
            }
        }

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "Kassad {Stage} outcome {Outcome} in {LatencyMs}ms ({PolicyCount} policies, {InputTokens} in / {OutputTokens} out)",
                result.Stage, result.Outcome, result.ModelLatency.TotalMilliseconds, result.Verdicts.Count,
                result.Usage?.InputTokens, result.Usage?.OutputTokens);
        }
    }
}
