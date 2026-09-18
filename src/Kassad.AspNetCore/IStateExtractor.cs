namespace Kassad.AspNetCore;

/// <summary>
/// Turns an HTTP body into the state the policies of a stage judge. <see cref="KassadInboundMiddleware"/> and
/// <see cref="KassadDelegatingHandler"/> call <see cref="KassadOptions.StateExtractor"/> once per textual body they read:
/// with <see cref="Stage.Inbound"/> for a request body, the prompt on its way to the model, and with
/// <see cref="Stage.Outbound"/> for a response body, the completion on its way back.
/// </summary>
/// <remarks>
/// <para>
/// The built-in <see cref="DefaultStateExtractor"/> reduces an OpenAI chat-completions or Anthropic messages request to
/// an <see cref="InboundState"/> (the user's latest message and the system prompt) and a response to the assistant's
/// reply as a <see cref="string"/>; <c>Docs/specs/state-extraction.md</c> is the contract. Implement this interface for
/// another shape, and return those two types where you can: the handler folds an <see cref="InboundState"/> and a
/// <see cref="string"/> into the <see cref="KassadDelegatingHandler.OutboundState"/> it hands to outbound policies. Any
/// other object is evaluated by the inbound stage as returned, but the outbound state then carries the raw request.
/// </para>
/// <para>
/// One instance serves every request, so implementations must be thread-safe, and they must not throw for any body:
/// what reaches them is untrusted input, and an exception here fails the request rather than the check.
/// </para>
/// </remarks>
public interface IStateExtractor
{
    /// <summary>Extract the state for <paramref name="stage"/> from <paramref name="body"/>.</summary>
    /// <param name="body">The body, decoded as UTF-8. Never <c>null</c>; may be empty.</param>
    /// <param name="contentType">The body's <c>Content-Type</c> header value, parameters included, or <c>null</c> when it had none.</param>
    /// <param name="stage"><see cref="Stage.Inbound"/> for a request body, <see cref="Stage.Outbound"/> for a response body. Kassad's HTTP components pass no other value.</param>
    /// <returns>
    /// The state to evaluate, or <c>null</c> when the body has no shape this extractor recognises. On <c>null</c>, Kassad
    /// evaluates the raw body: the whole request as a string for <see cref="Stage.Inbound"/>, the whole response as
    /// <see cref="KassadDelegatingHandler.OutboundState.Response"/> for <see cref="Stage.Outbound"/>.
    /// </returns>
    object? Extract(string body, string? contentType, Stage stage);
}
