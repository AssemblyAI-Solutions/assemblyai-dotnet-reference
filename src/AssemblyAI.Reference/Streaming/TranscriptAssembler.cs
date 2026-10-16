using System.Text;

namespace AssemblyAI.Reference.Streaming;

/// <summary>
/// Builds the transcript from a session's messages the way the protocol intends:
/// <list type="number">
///   <item><description>Each <see cref="TurnMessage"/> replaces whatever is held for its <c>turn_order</c> (never append).</description></item>
///   <item><description>Each <see cref="SpeakerRevisionMessage"/> overwrites the speaker labels of the turns it lists.</description></item>
/// </list>
/// Keyed by <c>turn_order</c>, so the same code works whether revisions arrive once at the end or every few minutes.
/// </summary>
public sealed class TranscriptAssembler
{
    private readonly SortedDictionary<int, AssembledTurn> _turns = new();

    public IReadOnlyCollection<AssembledTurn> Turns => _turns.Values;

    public IEnumerable<AssembledTurn> FinalTurns => _turns.Values.Where(t => t.IsFinal);

    public void Apply(StreamingMessage message)
    {
        switch (message)
        {
            case TurnMessage turn:
                _turns[turn.TurnOrder] = new AssembledTurn(
                    turn.TurnOrder,
                    turn.Transcript,
                    turn.SpeakerLabel,
                    turn.Words,
                    IsFinal: turn.EndOfTurn);
                break;

            case SpeakerRevisionMessage revision:
                foreach (var item in revision.Revisions)
                {
                    if (_turns.TryGetValue(item.TurnOrder, out var existing))
                    {
                        _turns[item.TurnOrder] = existing with
                        {
                            SpeakerLabel = item.SpeakerLabel,
                            Words = item.Words.Count > 0 ? item.Words : existing.Words,
                        };
                    }
                }

                break;
        }
    }

    /// <summary>The final turns as <c>Speaker A: text</c> lines, or plain lines when speaker labels are off.</summary>
    public string ToText()
    {
        var sb = new StringBuilder();
        foreach (var turn in FinalTurns)
        {
            sb.AppendLine(turn.SpeakerLabel is null ? turn.Text : $"Speaker {turn.SpeakerLabel}: {turn.Text}");
        }

        return sb.ToString();
    }
}

public sealed record AssembledTurn(
    int TurnOrder,
    string Text,
    string? SpeakerLabel,
    IReadOnlyList<StreamingWord> Words,
    bool IsFinal);
