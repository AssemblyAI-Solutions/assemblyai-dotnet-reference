using System.Buffers;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Serialization;
using AssemblyAI.Reference.Http;

namespace AssemblyAI.Reference.Streaming;

/// <summary>
/// One Streaming session over <see cref="ClientWebSocket"/>.
/// <list type="number">
///   <item><description>Connect. Authentication happens after the WebSocket upgrade, so the session isn't live until <c>Begin</c> arrives; <see cref="StreamingClient.ConnectAsync"/> waits for it.</description></item>
///   <item><description>Send audio as binary frames of about 50 ms (1,600 bytes at 16 kHz mono PCM). Frames must be 20-1000 ms long.</description></item>
///   <item><description>Read messages with <see cref="ReadMessagesAsync"/> on a separate task.</description></item>
///   <item><description>Call <see cref="TerminateAsync"/> when the audio ends, then keep reading until <see cref="TerminationMessage"/>, which comes after the last turn and any speaker revision.</description></item>
/// </list>
/// Streaming bills on how long the session is open, so always terminate rather than leaving an idle socket.
/// </summary>
public sealed class StreamingSession : IAsyncDisposable
{
    private readonly ClientWebSocket _socket;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private StreamingSession(ClientWebSocket socket, BeginMessage begin)
    {
        _socket = socket;
        Begin = begin;
    }

    /// <summary>The session id, expiry and the configuration the server applied.</summary>
    public BeginMessage Begin { get; }

    public WebSocketCloseStatus? CloseStatus => _socket.CloseStatus;

    public string? CloseStatusDescription => _socket.CloseStatusDescription;

    internal static async Task<StreamingSession> ConnectAsync(Uri uri, string? apiKey, CancellationToken cancellationToken)
    {
        var socket = new ClientWebSocket();
        if (apiKey is not null)
        {
            socket.Options.SetRequestHeader("Authorization", apiKey);
        }

        try
        {
            await socket.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);
            var first = await ReceiveAsync(socket, cancellationToken).ConfigureAwait(false);
            return first switch
            {
                BeginMessage begin => new StreamingSession(socket, begin),
                ErrorMessage error => throw new StreamingException(error.Error ?? "The server rejected the session.", error.ErrorCode, socket.CloseStatus, socket.CloseStatusDescription),
                null => throw new StreamingException("The server closed the connection before the session began.", null, socket.CloseStatus, socket.CloseStatusDescription),
                _ => throw new StreamingException($"Expected Begin, got {first.Type}.", null, socket.CloseStatus, socket.CloseStatusDescription),
            };
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>Sends one frame of audio in the encoding and sample rate you connected with.</summary>
    public Task SendAudioAsync(ReadOnlyMemory<byte> audio, CancellationToken cancellationToken = default) =>
        SendAsync(audio, WebSocketMessageType.Binary, cancellationToken);

    /// <summary>
    /// Changes settings mid-session, for example raising <c>min_turn_silence</c> before the clinician reads out a
    /// medication list. The server doesn't acknowledge it.
    /// </summary>
    public Task UpdateConfigurationAsync(StreamingConfigurationUpdate update, CancellationToken cancellationToken = default) =>
        SendJsonAsync(update, cancellationToken);

    /// <summary>Ends the current turn immediately, for example when your own UI decides the speaker is done.</summary>
    public Task ForceEndpointAsync(CancellationToken cancellationToken = default) =>
        SendJsonAsync(new ControlMessage("ForceEndpoint"), cancellationToken);

    /// <summary>Asks the server to finish. Keep reading: the final turn, any speaker revision and <c>Termination</c> follow.</summary>
    public Task TerminateAsync(CancellationToken cancellationToken = default) =>
        SendJsonAsync(new ControlMessage("Terminate"), cancellationToken);

    /// <summary>
    /// Yields every server message until <see cref="TerminationMessage"/> (included) or until the server closes the
    /// socket. If the session ends with an error, the last item is an <see cref="ErrorMessage"/> and
    /// <see cref="CloseStatus"/> holds the close code.
    /// </summary>
    public async IAsyncEnumerable<StreamingMessage> ReadMessagesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (_socket.State is WebSocketState.Open or WebSocketState.CloseSent)
        {
            var message = await ReceiveAsync(_socket, cancellationToken).ConfigureAwait(false);
            if (message is null)
            {
                yield break;
            }

            yield return message;
            if (message is TerminationMessage)
            {
                yield break;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_socket.State == WebSocketState.Open)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "client closed", timeout.Token).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException)
        {
            // The socket is going away regardless.
        }
        finally
        {
            _socket.Dispose();
            _sendLock.Dispose();
        }
    }

    private Task SendJsonAsync<T>(T message, CancellationToken cancellationToken) =>
        SendAsync(Encoding.UTF8.GetBytes(AssemblyAIJson.Serialize(message)), WebSocketMessageType.Text, cancellationToken);

    // ClientWebSocket allows only one send at a time, and audio and control messages often come from different tasks.
    private async Task SendAsync(ReadOnlyMemory<byte> payload, WebSocketMessageType type, CancellationToken cancellationToken)
    {
        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _socket.SendAsync(payload, type, endOfMessage: true, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private static async Task<StreamingMessage?> ReceiveAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            using var message = new MemoryStream();
            while (true)
            {
                var result = await socket.ReceiveAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    if (socket.State == WebSocketState.CloseReceived)
                    {
                        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None).ConfigureAwait(false);
                    }

                    return null;
                }

                message.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage)
                {
                    continue;
                }

                if (result.MessageType != WebSocketMessageType.Text)
                {
                    message.SetLength(0); // the server only sends JSON text; ignore anything else
                    continue;
                }

                return StreamingMessage.Parse(Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private sealed record ControlMessage([property: JsonPropertyName("type")] string Type);
}

/// <summary>
/// Mid-session settings for <c>UpdateConfiguration</c>. Only set what you want to change.
/// Avoid sending <c>mode</c> here unless you mean it: it re-applies that mode's whole profile and overwrites
/// earlier silence settings (and, with speaker labels on, the diarization profile).
/// </summary>
public sealed class StreamingConfigurationUpdate
{
    [JsonPropertyName("type")]
    public string Type => "UpdateConfiguration";

    /// <summary>Replaces the session's key terms. Send an empty list to clear them.</summary>
    [JsonPropertyName("keyterms_prompt")]
    public IReadOnlyList<string>? KeytermsPrompt { get; init; }

    /// <summary>Replaces the prompt. Send <c>""</c> to clear it.</summary>
    [JsonPropertyName("prompt")]
    public string? Prompt { get; init; }

    [JsonPropertyName("min_turn_silence")]
    public int? MinTurnSilence { get; init; }

    [JsonPropertyName("max_turn_silence")]
    public int? MaxTurnSilence { get; init; }

    [JsonPropertyName("mode")]
    public string? Mode { get; init; }

    [JsonExtensionData]
    public Dictionary<string, object?>? AdditionalProperties { get; init; }
}

/// <summary>The session couldn't start, or ended with an error. <see cref="ErrorCode"/> and <see cref="CloseStatus"/> say why.</summary>
public sealed class StreamingException : Exception
{
    public StreamingException(string message, int? errorCode, WebSocketCloseStatus? closeStatus, string? closeDescription)
        : base(closeDescription is null ? message : $"{message} (close: {(int?)closeStatus} {closeDescription})")
    {
        ErrorCode = errorCode;
        CloseStatus = closeStatus;
    }

    /// <summary>The <c>error_code</c> from the server's Error message, when it sent one.</summary>
    public int? ErrorCode { get; }

    /// <summary>
    /// 1008 = authentication failed or too many new sessions per minute; 3006 = invalid parameter or message;
    /// 3007 = audio frame too short or too long; 3008 = session reached its maximum duration.
    /// </summary>
    public WebSocketCloseStatus? CloseStatus { get; }
}
