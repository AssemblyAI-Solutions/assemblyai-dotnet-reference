// Minimal webhook receiver for pre-recorded transcripts.
//
// AssemblyAI POSTs {"transcript_id", "status"} to your webhook_url when a job finishes. This app:
//   1. checks the auth header you set with webhook_auth_header_name/value (constant-time comparison),
//   2. acknowledges with 200 straight away (you have 10 seconds before it counts as a failure and is retried),
//   3. fetches, stores and deletes the transcript on a background worker.
//
//   ASSEMBLYAI_API_KEY=... Webhook__Secret=... dotnet run --project samples/WebhookReceiver
//
// Submit jobs with:
//   WebhookUrl = "https://your-host/webhooks/assemblyai",
//   WebhookAuthHeaderName = "X-Webhook-Secret", WebhookAuthHeaderValue = <same secret>
// AssemblyAI can't reach localhost, so test through a tunnel (for example ngrok) or a staging host.

using System.Threading.Channels;
using AssemblyAI.Reference;
using AssemblyAI.Reference.Webhooks;

var builder = WebApplication.CreateBuilder(args);

var headerName = builder.Configuration["Webhook:HeaderName"] ?? "X-Webhook-Secret";
var secret = builder.Configuration["Webhook:Secret"]
    ?? throw new InvalidOperationException("Set Webhook:Secret (for example the Webhook__Secret environment variable).");

builder.Services.AddSingleton(_ => new AssemblyAIClient(AssemblyAIOptions.FromEnvironment()));
builder.Services.AddSingleton(Channel.CreateUnbounded<WebhookNotification>());
builder.Services.AddHostedService<TranscriptWorker>();

var app = builder.Build();

app.MapPost("/webhooks/assemblyai", async (HttpRequest http, WebhookNotification notification, Channel<WebhookNotification> queue) =>
{
    if (!WebhookAuthentication.IsValid(http.Headers[headerName], secret))
    {
        return Results.Unauthorized();
    }

    await queue.Writer.WriteAsync(notification);
    return Results.Ok();
});

app.Run();

/// <summary>Fetches each finished transcript, hands it to storage, then deletes it from AssemblyAI.</summary>
internal sealed class TranscriptWorker(
    AssemblyAIClient client,
    Channel<WebhookNotification> queue,
    ILogger<TranscriptWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var notification in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                var transcript = await client.Transcripts.GetAsync(notification.TranscriptId, stoppingToken);
                if (transcript.IsError)
                {
                    // Log ids and error text only: transcripts are PHI, so they never go to logs.
                    logger.LogWarning("Transcript {Id} failed: {Error}", transcript.Id, transcript.Error);
                }
                else
                {
                    // Persist transcript.Utterances / transcript.Text to your own store here.
                    logger.LogInformation(
                        "Transcript {Id} completed: {Seconds}s of audio, {Utterances} utterances",
                        transcript.Id,
                        transcript.AudioDuration,
                        transcript.Utterances?.Count ?? 0);
                }

                await client.Transcripts.DeleteAsync(transcript.Id, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Couldn't process transcript {Id}; fetch it later with GET /v2/transcript/{{id}}.", notification.TranscriptId);
            }
        }
    }
}
