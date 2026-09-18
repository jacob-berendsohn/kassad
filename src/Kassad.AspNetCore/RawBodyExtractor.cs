namespace Kassad.AspNetCore;

/// <summary>
/// The whole body is the state, whatever its shape: how Kassad evaluated every body before structured extraction
/// existed. <see cref="DefaultStateExtractor"/> falls back to it for bodies it does not recognise; set it as
/// <see cref="KassadOptions.StateExtractor"/> to evaluate every body whole, envelope included.
/// </summary>
/// <remarks>
/// For <see cref="Stage.Inbound"/> the state is the body string. For <see cref="Stage.Outbound"/> it returns
/// <c>null</c>: the raw response is already the <c>response</c> of the <see cref="KassadDelegatingHandler.OutboundState"/>,
/// and a <see cref="string"/> from an outbound extraction means an extracted reply.
/// </remarks>
public sealed class RawBodyExtractor : IStateExtractor
{
    private RawBodyExtractor()
    {
    }

    /// <summary>The shared instance; the extractor holds no state.</summary>
    public static RawBodyExtractor Instance { get; } = new();

    /// <inheritdoc />
    public object? Extract(string body, string? contentType, Stage stage)
    {
        ArgumentNullException.ThrowIfNull(body);
        return stage == Stage.Inbound ? body : null;
    }
}
