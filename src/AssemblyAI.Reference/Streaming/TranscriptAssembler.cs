using System.Text;

namespace AssemblyAI.Reference.Streaming;

/// <summary>
/// Builds the transcript from a session's messages the way the protocol intends:
/// <list type="number">
///   <item><description>Each <see cref="TurnMessage"/> replaces whatever is held for its <c>turn_order</c> (never append).</description></item>
///   <item><description>Each <see cref="SpeakerRevisionMessage"/> overwrites the turn and word speaker labels of the turns it lists.</description></item>
/// </list>
/// Keyed by <c>turn_order</c>, so the same code works whether revisions arrive once at the end or every few minutes.
/// </summary>
public sealed class TranscriptAssembler
{
    private const string Pending = "PENDING";

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

    /// <summary>
    /// The final transcript as speaker utterances, built from each word's <c>speaker</c> rather than the turn's label.
    /// When speakers hand over quickly one turn can hold both of them, and a long answer can arrive as several turns,
    /// so consecutive words from the same speaker form one utterance across turn boundaries. A word the model couldn't
    /// attribute (<c>PENDING</c>, or no <c>speaker</c>) stays with the speaker around it. With speaker labels off,
    /// each final turn is one utterance.
    /// </summary>
    public IReadOnlyList<SpeakerUtterance> Utterances()
    {
        var utterances = new List<SpeakerUtterance>();
        foreach (var segment in FinalTurns.SelectMany(SplitBySpeaker))
        {
            if (utterances.Count > 0 && IsAttributed(segment.Speaker) && utterances[^1].Speaker == segment.Speaker)
            {
                var previous = utterances[^1];
                utterances[^1] = new SpeakerUtterance(
                    previous.Speaker,
                    previous.Text + " " + segment.Text,
                    previous.Words.Concat(segment.Words).ToList());
            }
            else
            {
                utterances.Add(segment);
            }
        }

        return utterances;
    }

    /// <summary>The <see cref="Utterances"/> as <c>Speaker A: text</c> lines, or plain lines when speaker labels are off.</summary>
    public string ToText()
    {
        var sb = new StringBuilder();
        foreach (var utterance in Utterances())
        {
            sb.AppendLine(utterance.Speaker is null ? utterance.Text : $"Speaker {utterance.Speaker}: {utterance.Text}");
        }

        return sb.ToString();
    }

    private static IEnumerable<SpeakerUtterance> SplitBySpeaker(AssembledTurn turn)
    {
        var words = turn.Words;
        if (words.Count == 0)
        {
            yield return new SpeakerUtterance(turn.SpeakerLabel, turn.Text, words);
            yield break;
        }

        // Unattributed words join the speaker before them, or at the start of a turn the first one after them.
        // A turn with no attributed words keeps the turn's label.
        var speaker = words.Select(w => w.Speaker).FirstOrDefault(IsAttributed) ?? turn.SpeakerLabel;
        var start = 0;
        for (var i = 0; i < words.Count; i++)
        {
            if (IsAttributed(words[i].Speaker) && words[i].Speaker != speaker)
            {
                yield return Slice(speaker, words, start, i);
                speaker = words[i].Speaker;
                start = i;
            }
        }

        yield return Slice(speaker, words, start, words.Count);
    }

    private static SpeakerUtterance Slice(string? speaker, IReadOnlyList<StreamingWord> words, int from, int to)
    {
        var slice = words.Skip(from).Take(to - from).ToList();
        return new SpeakerUtterance(speaker, string.Join(" ", slice.Select(w => w.Text)), slice);
    }

    private static bool IsAttributed(string? speaker) => speaker is not null && speaker != Pending;
}

public sealed record AssembledTurn(
    int TurnOrder,
    string Text,
    string? SpeakerLabel,
    IReadOnlyList<StreamingWord> Words,
    bool IsFinal);

/// <summary>Consecutive words from one speaker. <see cref="Speaker"/> is null when speaker labels are off.</summary>
public sealed record SpeakerUtterance(string? Speaker, string Text, IReadOnlyList<StreamingWord> Words);
