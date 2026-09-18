using System.Diagnostics;
using Kassad.Policies;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Kassad.Engine;

/// <summary>
/// Default <see cref="IGuardrailEngine"/>. Batches every policy for a stage into a single
/// <see cref="DecisionRequest"/> (questions are evaluated independently and in parallel upstream, so
/// adding policies does not add round trips), then resolves each answer with <see cref="VerdictResolver"/>.
/// An <see cref="EvaluationOptions.Budget"/>, when set, caps how long the model call may take. Every call is one
/// <c>Kassad.Evaluate</c> activity and updates the <c>Kassad</c> meter; <see cref="KassadTelemetry"/> has the names.
/// </summary>
public sealed class GuardrailEngine : IGuardrailEngine
{
    private readonly IDecisionModel _model;
    private readonly PolicySet _policies;
    private readonly TimeSpan? _budget;
    private readonly ILogger<GuardrailEngine> _logger;

    /// <summary>Create an engine over a decision model and a validated policy set.</summary>
    /// <param name="model">The decision model every stage is evaluated against.</param>
    /// <param name="policies">The validated policies; the engine picks the ones for the requested stage on each call.</param>
    /// <param name="options">
    /// Evaluation settings. <c>null</c> means the defaults: no budget. Outside DI, pass
    /// <c>Options.Create(new EvaluationOptions { Budget = ... })</c>.
    /// </param>
    /// <param name="logger">Receives one line per verdict and one per outcome; <c>null</c> disables logging.</param>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="EvaluationOptions.Budget"/> is set but not positive.</exception>
    public GuardrailEngine(IDecisionModel model, PolicySet policies, IOptions<EvaluationOptions>? options = null, ILogger<GuardrailEngine>? logger = null)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _policies = policies ?? throw new ArgumentNullException(nameof(policies));
        _budget = options?.Value.Budget;
        if (!EvaluationOptions.IsValidBudget(_budget))
        {
            throw new ArgumentOutOfRangeException(nameof(options), _budget, EvaluationOptions.BudgetRule);
        }

        _logger = logger ?? NullLogger<GuardrailEngine>.Instance;
    }

    /// <inheritdoc />
    public async Task<StageResult> EvaluateAsync(Stage stage, object state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        // One activity per call, however it ends. Null, and nothing allocated, when no listener is attached.
        using var activity = KassadTelemetry.StartEvaluation(stage, _model.Name);

        var policies = _policies.ForStage(stage);
        if (policies.IsEmpty)
        {
            var empty = new StageResult
            {
                Stage = stage,
                Verdicts = [],
                Outcome = VerdictAction.Allow,
                ModelLatency = TimeSpan.Zero,
            };

            KassadTelemetry.RecordEvaluation(activity, empty, _model.Name, modelCalled: false);
            return empty;
        }

        var questions = policies.ToDictionary(p => p.Id, p => p.Question, StringComparer.Ordinal);
        var request = new DecisionRequest(state, questions);

        // The budget rides on a token linked to the caller's, so the model sees a single token while the
        // engine can still tell afterwards which one fired: the caller's cancellation propagates, the
        // budget's becomes error verdicts.
        using var budgetCts = _budget is { } budget ? StartBudget(budget, cancellationToken) : null;

        var stopwatch = Stopwatch.StartNew();
        DecisionResponse? response = null;
        Exception? failure = null;
        var budgetExceeded = false;

        try
        {
            response = await _model.EvaluateAsync(request, budgetCts?.Token ?? cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            KassadTelemetry.RecordCancellation(activity);
            throw;
        }
        catch (Exception) when (budgetCts is { IsCancellationRequested: true } && !cancellationToken.IsCancellationRequested)
        {
            // Once the budget has fired, whatever the model threw (its OperationCanceledException, or a wrapper
            // around it) is the budget's doing.
            budgetExceeded = true;
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        stopwatch.Stop();

        var verdicts = new Verdict[policies.Length];
        for (var i = 0; i < policies.Length; i++)
        {
            verdicts[i] = Resolve(policies[i], response, failure, budgetExceeded);
        }

        var outcome = verdicts.Max(v => v.Action);
        var result = new StageResult
        {
            Stage = stage,
            Verdicts = verdicts,
            Outcome = outcome,
            ModelLatency = stopwatch.Elapsed,
            Usage = response?.Usage,
            BudgetExceeded = budgetExceeded,
        };

        // Before the activity stops, so metric exemplars can point at it.
        KassadTelemetry.RecordEvaluation(activity, result, _model.Name, modelCalled: true);
        Log(result, failure);
        return result;
    }

    private static CancellationTokenSource StartBudget(TimeSpan budget, CancellationToken callerToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        cts.CancelAfter(budget);
        return cts;
    }

    private Verdict Resolve(Policy policy, DecisionResponse? response, Exception? failure, bool budgetExceeded)
    {
        if (budgetExceeded)
        {
            return VerdictResolver.FromBudgetExceeded(policy, _budget.GetValueOrDefault());
        }

        if (failure is not null)
        {
            return VerdictResolver.FromError(policy, failure);
        }

        return response!.Answers.TryGetValue(policy.Id, out var answer)
            ? VerdictResolver.Resolve(policy, answer)
            : VerdictResolver.FromError(policy, new DecisionModelException($"No answer returned for policy '{policy.Id}'."));
    }

    private void Log(StageResult result, Exception? failure)
    {
        if (result.BudgetExceeded)
        {
            if (_logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning(
                    "Kassad {Stage}: decision model {Model} did not answer within the {BudgetMs}ms budget (gave up after {LatencyMs}ms); error policies applied, outcome {Outcome}",
                    result.Stage, _model.Name, _budget.GetValueOrDefault().TotalMilliseconds, result.ModelLatency.TotalMilliseconds, result.Outcome);
            }
        }
        else if (failure is not null)
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
