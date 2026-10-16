using System.Net;
using System.Text;
using AssemblyAI.Reference.Audio;
using AssemblyAI.Reference.Http;

namespace AssemblyAI.Reference.Tests;

public class RetryHandlerTests
{
    private static HttpClient Client(FakeHandler fake, int maxRetries = 2) =>
        new(new RetryHandler(maxRetries, baseDelay: TimeSpan.FromMilliseconds(1)) { InnerHandler = fake });

    [Fact]
    public async Task Get_is_retried_on_503_then_succeeds()
    {
        var fake = new FakeHandler()
            .Respond(HttpStatusCode.ServiceUnavailable)
            .Respond(HttpStatusCode.OK, "{}");

        var response = await Client(fake).GetAsync("https://api.assemblyai.com/v2/transcript/abc");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, fake.Requests.Count);
    }

    [Fact]
    public async Task Post_is_not_retried_on_500_because_it_could_create_a_second_transcript()
    {
        var fake = new FakeHandler().Respond(HttpStatusCode.InternalServerError);

        var response = await Client(fake).PostAsync("https://api.assemblyai.com/v2/transcript", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Single(fake.Requests);
    }

    [Fact]
    public async Task Post_is_retried_on_429_and_honors_retry_after()
    {
        var fake = new FakeHandler()
            .Respond(HttpStatusCode.TooManyRequests, configure: r => r.Headers.RetryAfter = new(TimeSpan.Zero))
            .Respond(HttpStatusCode.OK, "{}");

        var response = await Client(fake).PostAsync("https://api.assemblyai.com/v2/transcript", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, fake.Requests.Count);
        Assert.Equal("{}", fake.Requests[1].BodyText);
    }

    [Fact]
    public async Task Polling_get_is_retried_on_403_because_that_is_the_http_rate_limit_response()
    {
        var fake = new FakeHandler()
            .Respond(HttpStatusCode.Forbidden)
            .Respond(HttpStatusCode.OK, "{}");

        var response = await Client(fake).GetAsync("https://api.assemblyai.com/v2/transcript/abc");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, fake.Requests.Count);
    }

    [Fact]
    public async Task Post_is_not_retried_on_403_because_it_can_be_a_real_permission_error()
    {
        var fake = new FakeHandler().Respond(HttpStatusCode.Forbidden, "{\"error\":\"Cannot access uploaded file\"}");

        var response = await Client(fake).PostAsync("https://api.assemblyai.com/v2/transcript", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Single(fake.Requests);
    }

    [Fact]
    public async Task Streamed_bodies_are_never_retried()
    {
        var fake = new FakeHandler().Respond(HttpStatusCode.TooManyRequests);

        var response = await Client(fake).PostAsync(
            "https://api.assemblyai.com/v2/upload",
            new StreamContent(new MemoryStream(new byte[] { 1, 2, 3 })));

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Single(fake.Requests);
    }

    [Fact]
    public async Task Gives_up_after_max_retries()
    {
        var fake = new FakeHandler()
            .Respond(HttpStatusCode.BadGateway)
            .Respond(HttpStatusCode.BadGateway)
            .Respond(HttpStatusCode.BadGateway);

        var response = await Client(fake, maxRetries: 2).GetAsync("https://api.assemblyai.com/v2/transcript/abc");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(3, fake.Requests.Count);
    }
}

public class ApiErrorsTests
{
    [Theory]
    [InlineData("{\"error\":\"Invalid audio_url\"}", "Invalid audio_url")]
    [InlineData("{\"error\":{\"message\":\"Unknown model\"}}", "Unknown model")]
    [InlineData("{\"status\":415,\"title\":\"Unsupported Media Type\",\"detail\":\"Send WAV or PCM\"}", "Unsupported Media Type: Send WAV or PCM")]
    [InlineData("{\"detail\":\"Too many requests\"}", "Too many requests")]
    [InlineData("<html>bad gateway</html>", null)]
    [InlineData("", null)]
    public void Extracts_the_message_from_each_error_shape(string body, string? expected) =>
        Assert.Equal(expected, ApiErrors.ExtractMessage(body));

    [Fact]
    public async Task Throws_with_status_and_message()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("{\"error\":\"Invalid audio_url\"}", Encoding.UTF8, "application/json"),
        };

        var ex = await Assert.ThrowsAsync<AssemblyAIApiException>(() => ApiErrors.EnsureSuccessAsync(response, default));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Equal("Invalid audio_url", ex.Message);
        Assert.False(ex.IsTransient);
    }
}

public class AudioTests
{
    private static byte[] Wav(int sampleRate, short channels, int samples)
    {
        var dataBytes = samples * channels * 2;
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(Encoding.ASCII.GetBytes("RIFF"));
        w.Write(36 + dataBytes);
        w.Write(Encoding.ASCII.GetBytes("WAVE"));
        w.Write(Encoding.ASCII.GetBytes("fmt "));
        w.Write(16);
        w.Write((short)1);
        w.Write(channels);
        w.Write(sampleRate);
        w.Write(sampleRate * channels * 2);
        w.Write((short)(channels * 2));
        w.Write((short)16);
        w.Write(Encoding.ASCII.GetBytes("data"));
        w.Write(dataBytes);
        w.Write(new byte[dataBytes]);
        return ms.ToArray();
    }

    [Fact]
    public void Parses_header_and_strips_it_from_the_pcm()
    {
        var wav = WavFile.Parse(Wav(16000, 1, 16000));

        Assert.Equal(16000, wav.SampleRate);
        Assert.Equal(1, wav.Channels);
        Assert.Equal(32000, wav.PcmData.Length);
        Assert.Equal(TimeSpan.FromSeconds(1), wav.Duration);
    }

    [Fact]
    public void Splits_pcm_into_50ms_frames()
    {
        var wav = WavFile.Parse(Wav(16000, 1, 16000));

        var frames = wav.Frames(TimeSpan.FromMilliseconds(50)).ToList();

        Assert.Equal(20, frames.Count);
        Assert.All(frames, f => Assert.Equal(1600, f.Length));
    }

    [Fact]
    public void Rejects_non_wav_input() =>
        Assert.Throws<InvalidDataException>(() => WavFile.Parse(Encoding.ASCII.GetBytes("not a wav file at all")));

    [Fact]
    public async Task Producer_stream_yields_frames_in_order_then_ends()
    {
        var stream = new ProducerStream();
        var writer = Task.Run(async () =>
        {
            await stream.WriteAsync(new byte[] { 1, 2, 3 });
            await stream.WriteAsync(new byte[] { 4, 5 });
            stream.Complete();
        });

        using var collected = new MemoryStream();
        await stream.CopyToAsync(collected);
        await writer;

        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, collected.ToArray());
    }
}
