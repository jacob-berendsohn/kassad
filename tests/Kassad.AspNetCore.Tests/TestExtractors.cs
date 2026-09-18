namespace Kassad.AspNetCore.Tests;

/// <summary>
/// An <see cref="IStateExtractor"/> that records every call and answers with fixed states: an <see cref="InboundState"/>
/// for requests and a reply string for responses, or <c>null</c> for both when built with <see cref="Returning(bool)"/>.
/// </summary>
internal sealed class RecordingExtractor : IStateExtractor
{
    private readonly bool _recognise;

    private RecordingExtractor(bool recognise) => _recognise = recognise;

    /// <summary>The inbound state a recognising extractor returns for every request body.</summary>
    public static InboundState Prompt { get; } = new("custom user message", "custom system prompt");

    /// <summary>The reply a recognising extractor returns for every response body.</summary>
    public const string Reply = "custom reply";

    /// <summary>Every call, in order.</summary>
    public List<(string Body, string? ContentType, Stage Stage)> Calls { get; } = [];

    /// <summary>An extractor that recognises everything (<c>true</c>) or nothing (<c>false</c>).</summary>
    public static RecordingExtractor Returning(bool recognise) => new(recognise);

    public object? Extract(string body, string? contentType, Stage stage)
    {
        Calls.Add((body, contentType, stage));
        if (!_recognise)
        {
            return null;
        }

        return stage switch
        {
            Stage.Inbound => Prompt,
            Stage.Outbound => Reply,
            _ => null,
        };
    }
}
