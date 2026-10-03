using Aquarium.Online;
using System.Globalization;
using System.Text.Json;

internal static class AssertEx
{
    public static void That(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message}: expected {expected}, received {actual}.");
    }

    public static void Near(double expected, double actual, double tolerance, string message)
    {
        That(double.IsFinite(actual) && Math.Abs(expected - actual) <= tolerance,
            $"{message}: expected {expected} ± {tolerance}, received {actual}.");
    }

    public static void Throws<T>(Action action, string message) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException(message);
    }

    public static ulong Revision(ServerAquariumState state) => ulong.Parse(state.revision, CultureInfo.InvariantCulture);
    public static long Pearls(ServerAquariumState state) => long.Parse(state.pearls, CultureInfo.InvariantCulture);
}

// This is the only substitute codec. Actual production DTO fields, API client,
// HttpClient transport, journal and session sources are linked by the csproj.
// Unity JsonUtility itself still needs the separately documented Editor tests.
internal sealed class TestJsonCodec : IOnlineJsonCodec
{
    private readonly JsonSerializerOptions options = new() { IncludeFields = true };
    public string Serialize<T>(T value) => JsonSerializer.Serialize(value, options);
    public T Deserialize<T>(string value) => JsonSerializer.Deserialize<T>(value, options);
}

internal sealed record ObservedRequest(Uri Endpoint, string Player, string Json)
{
    public OnlineHttpResponse Response { get; set; }
    public bool IsCommand => Endpoint.AbsolutePath.EndsWith("/ApplyCommand", StringComparison.Ordinal);
}

internal enum HoldPoint { None, BeforeSend, AfterResponse }

// Fault injection wraps the REAL HTTP transport. AfterResponse faults are
// triggered only after the real Go process has returned a successful response.
internal sealed class FaultTransport : IOnlineTransport
{
    private readonly IOnlineTransport inner;
    private readonly List<ObservedRequest> requests = new();
    private TaskCompletionSource<bool> holdEntered;
    private TaskCompletionSource<bool> holdRelease;
    private HoldPoint holdPoint;
    public bool DropNextApplyResponse { get; set; }
    public Action<ObservedRequest> AfterNextApplyResponse { get; set; }
    public Func<OnlineHttpResponse, OnlineHttpResponse> TransformNextApplyResponse { get; set; }
    public IReadOnlyList<ObservedRequest> Requests { get { lock (requests) return requests.ToArray(); } }
    public IReadOnlyList<ObservedRequest> Commands => Requests.Where(value => value.IsCommand).ToArray();

    public FaultTransport(IOnlineTransport inner) { this.inner = inner; }

    public Task HoldNextApply(HoldPoint point)
    {
        holdPoint = point;
        holdEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        holdRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        return holdEntered.Task;
    }

    public void Release() => holdRelease?.TrySetResult(true);

    public async Task<OnlineHttpResponse> PostAsync(Uri endpoint, string devPlayer, string json, CancellationToken cancellationToken)
    {
        var request = new ObservedRequest(endpoint, devPlayer, json);
        lock (requests) requests.Add(request);
        var currentHold = request.IsCommand ? holdPoint : HoldPoint.None;
        if (request.IsCommand) holdPoint = HoldPoint.None;
        if (currentHold == HoldPoint.BeforeSend) await WaitAtHold(cancellationToken);
        request.Response = await inner.PostAsync(endpoint, devPlayer, json, cancellationToken);
        if (request.IsCommand)
        {
            var callback = AfterNextApplyResponse;
            AfterNextApplyResponse = null;
            callback?.Invoke(request);
            if (currentHold == HoldPoint.AfterResponse) await WaitAtHold(cancellationToken);
            if (DropNextApplyResponse)
            {
                DropNextApplyResponse = false;
                AssertEx.Equal(200, request.Response.StatusCode, "Only drop a server-accepted response");
                throw new HttpRequestException("Integration fixture: server applied command but response was lost.");
            }
        }
        if (request.IsCommand && TransformNextApplyResponse != null)
        {
            var transform = TransformNextApplyResponse;
            TransformNextApplyResponse = null;
            return transform(request.Response);
        }
        return request.Response;
    }

    private async Task WaitAtHold(CancellationToken cancellationToken)
    {
        holdEntered.TrySetResult(true);
        await holdRelease.Task.WaitAsync(cancellationToken);
    }
}

internal sealed class TestClient : IDisposable
{
    public HttpClientOnlineTransport Http { get; }
    public FaultTransport Transport { get; }
    public ReefApiClient Api { get; }
    public FilePendingCommandStore Store { get; }
    public AquariumOnlineSession Session { get; }
    public List<ulong> ObservedRevisions { get; } = new();

    public TestClient(string player, string journalPath, string endpoint = LiveServer.Endpoint)
    {
        Http = new HttpClientOnlineTransport(TimeSpan.FromSeconds(3));
        Transport = new FaultTransport(Http);
        Api = new ReefApiClient(new DevServerOptions(endpoint, player), new TestJsonCodec(), Transport);
        Store = new FilePendingCommandStore(journalPath);
        Session = new AquariumOnlineSession(Api, Store);
        Session.Changed += () => {
            if (Session.Snapshot != null) ObservedRevisions.Add(AssertEx.Revision(Session.Snapshot));
        };
    }

    public void AssertMonotonic()
    {
        for (var index = 1; index < ObservedRevisions.Count; index++)
            AssertEx.That(ObservedRevisions[index] >= ObservedRevisions[index - 1],
                $"Snapshot regressed from {ObservedRevisions[index - 1]} to {ObservedRevisions[index]}.");
    }

    public void Dispose() { Http.Dispose(); Store.Dispose(); }
}
