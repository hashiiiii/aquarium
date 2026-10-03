using Aquarium.Online;
using System.Net;
using System.Text;
using System.Text.Json;
using static AssertEx;

if (args.Length != 2 || args[0] != "--server")
{
    Console.Error.WriteLine("Usage: dotnet run --project tools/Aquarium.Online.Tests -- --server /absolute/path/to/reef-local");
    return 2;
}

await using var server = new LiveServer(args[1]);
server.SeedReturningPlayer("collect-returning");
server.SeedReturningPlayer("collect-lost");
await server.StartAsync();
var tests = new List<(string Name, Func<Task> Run)>();
void Test(string name, Func<Task> run) => tests.Add((name, run));
TestClient Client(string player, string journal = null) => new(player, server.ClientPath(journal ?? player));

async Task Ready(TestClient client)
{
    var result = await client.Session.ConnectAsync();
    Equal(OnlineOperationKind.Ready, result.Kind, "Connect outcome");
    That(client.Session.IsConnected && client.Session.CanIssueCommands && !client.Session.IsBusy, "Connected session should allow commands");
}

void Accepted(OnlineOperationResult result, bool succeeded = true)
{
    Equal(succeeded ? OnlineOperationKind.Applied : OnlineOperationKind.GameplayRejected, result.Kind, "Command outcome");
    That(result.CommandAccepted, "Server accepted command should be represented as accepted");
    Equal(succeeded, result.GameplaySucceeded, "Gameplay success");
}

static string RequestId(ObservedRequest request)
{
    using var json = JsonDocument.Parse(request.Json);
    return json.RootElement.GetProperty("requestId").GetString();
}

static ulong RequestRevision(ObservedRequest request)
{
    using var json = JsonDocument.Parse(request.Json);
    return ulong.Parse(json.RootElement.GetProperty("expectedRevision").GetString());
}

Test("Live catalog and state use the shipped field DTOs and quoted 64-bit values", async () => {
    using var client = Client("catalog");
    await Ready(client);
    var state = client.Session.Snapshot;
    Equal(30L, Pearls(state), "Starter balance");
    Equal(1, state.creatures.Length, "Starter creature count");
    Equal("tide_sprite", state.creatures[0].speciesId, "Starter species");
    Equal(3, client.Session.Catalog.species.Length, "Catalog size");
    Equal("30", client.Session.Catalog.species.Single(value => value.id == "moon_jelly").price, "Server catalog price");
    Equal("9999", client.Session.Catalog.rules.walletCapacity, "Server wallet cap");
    Equal(28800.0, client.Session.Catalog.rules.offlineCapSeconds, "Server offline cap");
    var response = client.Transport.Requests.Single(value => value.Endpoint.AbsolutePath.EndsWith("/GetAquarium")).Response;
    using var json = JsonDocument.Parse(response.Body);
    var wire = json.RootElement.GetProperty("state");
    foreach (var field in new[] { "revision", "lastUpdatedAtUnixMs", "pearls" })
        Equal(JsonValueKind.String, wire.GetProperty(field).ValueKind, "Protobuf JSON encodes " + field + " as a decimal string");
    That(client.Transport.Requests.All(value => value.Player == "catalog"), "Player selected only by trusted transport header");
    client.AssertMonotonic();
});

Test("Feed, clean and adopt update only authoritative snapshots and exact accounting", async () => {
    using var client = Client("care-adopt");
    await Ready(client);
    var revision = Revision(client.Session.Snapshot);
    Accepted(await client.Session.FeedAsync());
    That(client.Session.Snapshot.fullness > 99.9 && client.Session.Snapshot.fullness <= 100, "Feed server result");
    Equal(revision + 1, Revision(client.Session.Snapshot), "One feed advances one revision");
    Accepted(await client.Session.CleanAsync());
    Equal(100.0, client.Session.Snapshot.cleanliness, "Clean caps at server maximum");
    Accepted(await client.Session.AdoptAsync("moon_jelly"));
    Equal(0L, Pearls(client.Session.Snapshot), "Adoption debits 30 exactly once");
    Equal(2, client.Session.Snapshot.creatures.Length, "Adoption adds one friend");
    var command = client.Transport.Commands.Last();
    using var json = JsonDocument.Parse(command.Json);
    Equal("ACTION_ADOPT", json.RootElement.GetProperty("action").GetString(), "Wire action");
    Equal("moon_jelly", json.RootElement.GetProperty("speciesId").GetString(), "Wire species");
    Equal(JsonValueKind.String, json.RootElement.GetProperty("expectedRevision").ValueKind, "Expected revision remains a string");
    That(!json.RootElement.TryGetProperty("pearls", out _) && !json.RootElement.TryGetProperty("playerId", out _) && !json.RootElement.TryGetProperty("lastUpdatedAtUnixMs", out _), "Only intent is sent");
    That(!client.Session.HasPendingCommand && !client.Store.TryRead(out _), "Acknowledged intent clears journal");
    client.AssertMonotonic();
});

Test("Gameplay rejection is accepted, clears pending, and never spends twice", async () => {
    using var client = Client("noops");
    await Ready(client);
    Accepted(await client.Session.AdoptAsync("coral_drake"), false);
    Equal(30L, Pearls(client.Session.Snapshot), "Insufficient funds leave balance intact");
    Accepted(await client.Session.AdoptAsync("moon_jelly"));
    var revision = Revision(client.Session.Snapshot);
    var duplicate = await client.Session.AdoptAsync("moon_jelly");
    Accepted(duplicate, false);
    Equal(revision + 1, Revision(client.Session.Snapshot), "Gameplay noop still consumes a revision");
    Equal(0L, Pearls(client.Session.Snapshot), "Duplicate adoption does not spend again");
    Equal(2, client.Session.Snapshot.creatures.Length, "Duplicate adoption does not duplicate creature");
    Accepted(await client.Session.CollectAsync(), false);
    That(!client.Session.HasPendingCommand && client.Session.CanIssueCommands, "Gameplay rejection is not a network outage");
    client.AssertMonotonic();
});

Test("Returning-player rewards are advanced by real server time and collect exactly once", async () => {
    using var client = Client("collect-returning");
    await Ready(client);
    var bank = client.Session.Snapshot.creatures[0].pendingPearls;
    That(bank > 5 && bank < 7, "One-hour historical fixture should accrue ordinary server rewards");
    var before = Pearls(client.Session.Snapshot);
    var result = await client.Session.CollectAsync();
    Accepted(result);
    Equal(5L, result.Amount, "Server collects the integer reward only");
    Equal(before + result.Amount, Pearls(client.Session.Snapshot), "Wallet changed by authoritative result");
    That(client.Session.Snapshot.creatures[0].pendingPearls >= 0 && client.Session.Snapshot.creatures[0].pendingPearls < 1, "Fraction remains banked");
    Accepted(await client.Session.CollectAsync(), false);
    Equal(before + result.Amount, Pearls(client.Session.Snapshot), "Repeated collection grants no duplicate reward");
});

Test("Apply-then-drop adoption persists exact intent and replays after client and server restart", async () => {
    const string player = "lost-adopt";
    var path = server.ClientPath(player);
    string original;
    string persisted;
    using (var client = Client(player))
    {
        await Ready(client);
        client.Transport.DropNextApplyResponse = true;
        var result = await client.Session.AdoptAsync("moon_jelly");
        Equal(OnlineOperationKind.Pending, result.Kind, "Lost acknowledgement retains pending command");
        That(client.Session.HasPendingCommand && !client.Session.CanIssueCommands, "New actions blocked while outcome unknown");
        Equal(30L, Pearls(client.Session.Snapshot), "Lost response does not fabricate a local purchase");
        original = client.Transport.Commands.Single().Json;
        That(client.Store.TryRead(out persisted), "Pending command must be on disk before sending");
        using var journal = JsonDocument.Parse(persisted);
        Equal(original, journal.RootElement.GetProperty("requestJson").GetString(), "Durable journal records exact transport bytes");
        var before = client.Transport.Commands.Count;
        await client.Session.FeedAsync();
        Equal(before, client.Transport.Commands.Count, "Unresolved purchase forbids unrelated new intent");
    }
    Equal(persisted, File.ReadAllText(path), "Client disposal does not erase journal");
    await server.RestartAsync();
    using var reopened = Client(player);
    await reopened.Session.ConnectAsync();
    Equal(0, reopened.Transport.Commands.Count, "Reconnect only reads; pending retry must remain explicit");
    if (reopened.Session.HasPendingCommand) await reopened.Session.RetryPendingAsync();
    var retry = reopened.Transport.Commands.Single();
    Equal(original, retry.Json, "Retry is byte-identical after process and session restart");
    Equal(player, retry.Player, "Retry remains scoped to original player");
    Equal(200, retry.Response.StatusCode, "Replay goes through real HTTP");
    That(JsonDocument.Parse(retry.Response.Body).RootElement.GetProperty("replayed").GetBoolean(), "Server receipt survives restart");
    Equal(0L, Pearls(reopened.Session.Snapshot), "Server charged only once");
    Equal(2, reopened.Session.Snapshot.creatures.Length, "Server adopted only once");
    That(!reopened.Session.HasPendingCommand && !reopened.Store.TryRead(out _), "Replay resolves durable pending journal");
    reopened.AssertMonotonic();
});

Test("Lost positive collect response replays same amount without duplicate wallet credit", async () => {
    string original;
    long amount;
    using (var client = Client("collect-lost"))
    {
        await Ready(client);
        client.Transport.DropNextApplyResponse = true;
        Equal(OnlineOperationKind.Pending, (await client.Session.CollectAsync()).Kind, "Lost collect is uncertain");
        original = client.Transport.Commands.Single().Json;
        using var response = JsonDocument.Parse(client.Transport.Commands.Single().Response.Body);
        amount = long.Parse(response.RootElement.GetProperty("amount").GetString());
        That(amount > 0, "This test must lose a positive reward, not a noop");
    }
    await server.RestartAsync();
    using var reopened = Client("collect-lost");
    await reopened.Session.ConnectAsync();
    Equal(0, reopened.Transport.Commands.Count, "Reconnect only reads; pending retry must remain explicit");
    if (reopened.Session.HasPendingCommand) await reopened.Session.RetryPendingAsync();
    Equal(original, reopened.Transport.Commands.Single().Json, "Collect replay preserves full intent");
    Equal(30L + amount, Pearls(reopened.Session.Snapshot), "Replay credits original amount once");
    Accepted(await reopened.Session.CollectAsync(), false);
    Equal(30L + amount, Pearls(reopened.Session.Snapshot), "Fresh immediate collect cannot duplicate settled reward");
    reopened.AssertMonotonic();
});

Test("Two clients conflict; refresh never automatically reapplies a stale user action", async () => {
    using var first = Client("conflict", "conflict-first");
    using var second = Client("conflict", "conflict-second");
    await Ready(first);
    await Ready(second); // Real Get advances revision and makes first stale.
    Accepted(await second.Session.AdoptAsync("moon_jelly"));
    var conflict = await first.Session.FeedAsync();
    Equal(OnlineOperationKind.Conflict, conflict.Kind, "Stale expectedRevision is a conflict");
    Equal("aborted", conflict.ErrorCode, "Connect error is preserved");
    Equal(1, first.Transport.Commands.Count, "Conflict refresh must not resend intent automatically");
    That(!first.Session.HasPendingCommand, "Definite conflict clears rejected command");
    Equal(0L, Pearls(first.Session.Snapshot), "Conflict refresh sees other client's purchase");
    var conflicted = first.Transport.Commands.Single();
    Accepted(await first.Session.FeedAsync()); // Explicit fresh user action.
    Equal(2, first.Transport.Commands.Count, "Only explicit action produces fresh command");
    var fresh = first.Transport.Commands.Last();
    That(RequestId(conflicted) != RequestId(fresh), "New action has a new idempotency ID");
    That(RequestRevision(fresh) > RequestRevision(conflicted), "New action uses refreshed revision");
    Equal(0L, Pearls(first.Session.Snapshot), "Conflict resolution never recreates local funds");
    first.AssertMonotonic();
});

Test("An older receipt cannot roll a newer snapshot backwards during reconnect", async () => {
    string oldJson;
    ulong oldRevision;
    using (var client = Client("stale-replay", "stale-original"))
    {
        await Ready(client);
        client.Transport.DropNextApplyResponse = true;
        await client.Session.AdoptAsync("moon_jelly");
        var command = client.Transport.Commands.Single();
        oldJson = command.Json;
        using var body = JsonDocument.Parse(command.Response.Body);
        oldRevision = ulong.Parse(body.RootElement.GetProperty("state").GetProperty("revision").GetString());
    }
    ulong newerRevision;
    using (var other = Client("stale-replay", "stale-other"))
    {
        await Ready(other);
        Accepted(await other.Session.CleanAsync());
        newerRevision = Revision(other.Session.Snapshot);
        That(newerRevision > oldRevision, "Prepare a genuinely newer snapshot");
    }
    using var reopened = Client("stale-replay", "stale-original");
    await reopened.Session.ConnectAsync();
    Equal(0, reopened.Transport.Commands.Count, "Reconnect only reads; pending retry must remain explicit");
    if (reopened.Session.HasPendingCommand) await reopened.Session.RetryPendingAsync();
    Equal(oldJson, reopened.Transport.Commands.Single().Json, "Old command is retried unchanged");
    using var replay = JsonDocument.Parse(reopened.Transport.Commands.Single().Response.Body);
    Equal(oldRevision, ulong.Parse(replay.RootElement.GetProperty("state").GetProperty("revision").GetString()), "Go returns original receipt snapshot");
    That(Revision(reopened.Session.Snapshot) >= newerRevision, "Session ends at current state, never original receipt state");
    Equal(0L, Pearls(reopened.Session.Snapshot), "Receipt did not reset balance");
    reopened.AssertMonotonic();
});

Test("Disconnect keeps snapshot unchanged, blocks offline rewards, and reconnects", async () => {
    using var client = Client("disconnect");
    await Ready(client);
    var before = new TestJsonCodec().Serialize(client.Session.Snapshot);
    await server.StopAsync();
    try
    {
        var failed = await client.Session.RefreshAsync();
        Equal(OnlineOperationKind.Failed, failed.Kind, "Unavailable Get is a connection failure");
        That(!client.Session.IsConnected && !client.Session.CanIssueCommands, "Disconnected session disables commands");
        Equal(before, new TestJsonCodec().Serialize(client.Session.Snapshot), "No local time/reward mutation while disconnected");
    }
    finally { await server.StartAsync(); }
    await Ready(client);
    Equal(30L, Pearls(client.Session.Snapshot), "Reconnect preserves server wallet");
    Accepted(await client.Session.FeedAsync());
    client.AssertMonotonic();
});

Test("Cancellation after server commit leaves durable intent and replay resolves it", async () => {
    using var client = Client("cancel-applied");
    await Ready(client);
    using var cancel = new CancellationTokenSource();
    var entered = client.Transport.HoldNextApply(HoldPoint.AfterResponse);
    var running = client.Session.AdoptAsync("moon_jelly", cancel.Token);
    await entered.WaitAsync(TimeSpan.FromSeconds(5));
    That(client.Store.TryRead(out _), "Pending persisted while committed response is withheld");
    cancel.Cancel();
    Equal(OnlineOperationKind.Pending, (await running).Kind, "Cancellation after send is an uncertain command outcome");
    That(client.Session.HasPendingCommand && !client.Session.CanIssueCommands, "Cancel does not discard possibly applied intent");
    var original = client.Transport.Commands.Single().Json;
    var result = await client.Session.RetryPendingAsync();
    Accepted(result);
    That(result.Replayed, "Canceled committed command is replayed");
    Equal(original, client.Transport.Commands.Last().Json, "Canceled retry preserves ID, revision and action");
    Equal(0L, Pearls(client.Session.Snapshot), "Cancellation and retry debit once");
});

Test("Double click during an in-flight action cannot queue a second command", async () => {
    using var client = Client("double-click");
    await Ready(client);
    var entered = client.Transport.HoldNextApply(HoldPoint.BeforeSend);
    var running = client.Session.AdoptAsync("moon_jelly");
    await entered.WaitAsync(TimeSpan.FromSeconds(5));
    That(client.Session.IsBusy && !client.Session.CanIssueCommands, "UI can disable while command in flight");
    var second = await client.Session.AdoptAsync("moon_jelly");
    Equal(OnlineOperationKind.Busy, second.Kind, "Second click is rejected immediately");
    Equal(1, client.Transport.Commands.Count, "No duplicate queued transport operation");
    client.Transport.Release();
    Accepted(await running);
    Equal(0L, Pearls(client.Session.Snapshot), "Only one debit");
    Equal(2, client.Session.Snapshot.creatures.Length, "Only one addition");
});

Test("Cancellation before network send remains recoverable without hidden command loss", async () => {
    using var client = Client("cancel-before-send");
    await Ready(client);
    using var cancel = new CancellationTokenSource();
    var entered = client.Transport.HoldNextApply(HoldPoint.BeforeSend);
    var running = client.Session.FeedAsync(cancel.Token);
    await entered.WaitAsync(TimeSpan.FromSeconds(5));
    cancel.Cancel();
    Equal(OnlineOperationKind.Pending, (await running).Kind, "Persisted intent remains pending after cancel");
    That(client.Transport.Commands.Single().Response == null, "No network command reached server before cancel");
    var original = client.Transport.Commands.Single().Json;
    var retry = await client.Session.RetryPendingAsync();
    Accepted(retry);
    That(!retry.Replayed, "The first server application occurs only on retry");
    Equal(original, client.Transport.Commands.Last().Json, "Retry of unsent intent is still exact");
});

Test("Corrupt pending journal fails closed and preserves original bytes", async () => {
    foreach (var contents in new[] { "", "not-json", "{}", "{\"version\":999}", new string('x', FilePendingCommandStore.MaximumJournalBytes + 1) })
    {
        var id = "corrupt-" + Guid.NewGuid().ToString("N");
        var path = server.ClientPath(id);
        File.WriteAllText(path, contents);
        using var client = Client(id);
        await client.Session.ConnectAsync();
        That(client.Session.HasStorageError && !client.Session.CanIssueCommands, "Corrupted storage prevents new commands");
        await client.Session.AdoptAsync("moon_jelly");
        Equal(0, client.Transport.Commands.Count, "Corrupt journal never permits an untracked purchase");
        Equal(contents, File.ReadAllText(path), "Corrupted journal is not auto-reset");
    }
});

Test("Journal write failure prevents sending and never mutates server state", async () => {
    using var client = Client("journal-write-failure");
    await Ready(client);
    Directory.CreateDirectory(client.Store.Path + ".tmp");
    var result = await client.Session.AdoptAsync("moon_jelly");
    Equal(OnlineOperationKind.StorageError, result.Kind, "Durable write must precede mutation");
    That(client.Session.HasStorageError && !client.Session.CanIssueCommands, "Persistence failure blocks new commands");
    Equal(0, client.Transport.Commands.Count, "No command sent when journal cannot be written");
    Equal(30L, Pearls(await client.Api.GetAquariumAsync()), "Server balance is untouched");
});

Test("Acknowledgement clear failure retains journal and restart safely replays it", async () => {
    string original;
    using (var client = Client("journal-clear-failure"))
    {
        await Ready(client);
        client.Transport.AfterNextApplyResponse = _ => Directory.CreateDirectory(client.Store.Path + ".tmp");
        var result = await client.Session.AdoptAsync("moon_jelly");
        Equal(OnlineOperationKind.StorageError, result.Kind, "Failed acknowledgement persistence is surfaced");
        That(client.Store.TryRead(out _) && client.Session.HasStorageError, "Original journal is preserved after clear failure");
        original = client.Transport.Commands.Single().Json;
        var sent = client.Transport.Commands.Count;
        await client.Session.FeedAsync();
        Equal(sent, client.Transport.Commands.Count, "Storage error blocks fresh command");
        Directory.Delete(client.Store.Path + ".tmp");
    }
    using var reopened = Client("journal-clear-failure");
    await reopened.Session.ConnectAsync();
    Equal(0, reopened.Transport.Commands.Count, "Reconnect only reads; pending retry must remain explicit");
    if (reopened.Session.HasPendingCommand) await reopened.Session.RetryPendingAsync();
    Equal(original, reopened.Transport.Commands.Single().Json, "Preserved receipt query is exact");
    Equal(0L, Pearls(reopened.Session.Snapshot), "Successful purchase is not charged again");
    That(!reopened.Session.HasPendingCommand && !reopened.Session.HasStorageError, "Recovered persistence permits safe continuation");
});

Test("Pending intent is scoped to endpoint and player, never sent for another identity", async () => {
    using (var first = Client("journal-owner", "bound-journal"))
    {
        await Ready(first);
        first.Transport.DropNextApplyResponse = true;
        await first.Session.AdoptAsync("moon_jelly");
    }
    var path = server.ClientPath("bound-journal");
    var original = File.ReadAllText(path);
    using (var wrongPlayer = new TestClient("different-owner", path))
    {
        await wrongPlayer.Session.ConnectAsync();
        That(wrongPlayer.Session.HasStorageError && !wrongPlayer.Session.CanIssueCommands, "Player mismatch fails closed");
        Equal(0, wrongPlayer.Transport.Commands.Count, "Never retransmit another player's intent");
    }
    using (var wrongEndpoint = new TestClient("journal-owner", path, "http://127.0.0.1:1"))
    {
        await wrongEndpoint.Session.ConnectAsync();
        That(wrongEndpoint.Session.HasStorageError && !wrongEndpoint.Session.CanIssueCommands, "Endpoint mismatch fails closed");
        Equal(0, wrongEndpoint.Transport.Commands.Count, "Never retransmit intent to another endpoint");
    }
    Equal(original, File.ReadAllText(path), "Scope mismatch preserves journal for rightful recovery");
});

Test("Unsafe endpoint and invalid development identity are rejected before transmission", async () => {
    foreach (var endpoint in new[] { "https://127.0.0.1:8081", "http://localhost:8081", "http://192.0.2.1:8081", "http://[::1]:8081", "http://name@127.0.0.1:8081", "http://127.0.0.1:8081/path", "http://127.0.0.1:8081/?secret=value", "http://127.0.0.1:8081/#fragment" })
        Throws<ArgumentException>(() => new DevServerOptions(endpoint, "valid-player"), "Unsafe endpoint accepted: " + endpoint);
    foreach (var player in new[] { "", "space name", "bad/name", "line\r\nbreak", "日本語", new string('a', 65) })
        Throws<ArgumentException>(() => new DevServerOptions(LiveServer.Endpoint, player), "Invalid player accepted");
    using var transport = new HttpClientOnlineTransport();
    try
    {
        await transport.PostAsync(new Uri("https://example.invalid/"), "valid-player", "{}", CancellationToken.None);
        throw new InvalidOperationException("Direct transport must also enforce local endpoint restriction.");
    }
    catch (ArgumentException) { }
});

Test("Actual network rejects missing or invalid identity and browser-origin requests", async () => {
    using var client = new HttpClient(new HttpClientHandler { UseProxy = false });
    foreach (var identity in new[] { null, "bad identity" })
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, LiveServer.Endpoint + "/aquarium.v1.AquariumService/GetAquarium");
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        request.Headers.Add("Connect-Protocol-Version", "1");
        if (identity != null) request.Headers.Add("X-Aquarium-Dev-Player", identity);
        using var result = await client.SendAsync(request);
        Equal(HttpStatusCode.Unauthorized, result.StatusCode, "Server identity guard");
        using var body = JsonDocument.Parse(await result.Content.ReadAsStringAsync());
        Equal("unauthenticated", body.RootElement.GetProperty("code").GetString(), "Connect identity error");
    }
    using var browserRequest = new HttpRequestMessage(HttpMethod.Post, LiveServer.Endpoint + "/aquarium.v1.AquariumService/GetCatalog");
    browserRequest.Content = new StringContent("{}", Encoding.UTF8, "application/json");
    browserRequest.Headers.Add("Origin", "http://example.invalid");
    browserRequest.Headers.Add("X-Aquarium-Dev-Player", "valid-player");
    using var browserResult = await client.SendAsync(browserRequest);
    Equal(HttpStatusCode.Forbidden, browserResult.StatusCode, "Browser origin is rejected");
});

Test("Journal lease excludes simultaneous writers and is reusable after disposal", async () => {
    var path = server.ClientPath("exclusive-store");
    using (var store = new FilePendingCommandStore(path))
    {
        store.Write("preserve me");
        Throws<IOException>(() => { using var overlapping = new FilePendingCommandStore(path); },
            "Two live writers opened the same pending journal.");
        Equal("preserve me", File.ReadAllText(path), "Rejected writer leaves journal intact");
    }
    using (var reopened = new FilePendingCommandStore(path))
    {
        That(reopened.TryRead(out var content), "Reopened store reads durable journal");
        Equal("preserve me", content, "Lease survives only for original writer lifetime");
    }
    await Task.CompletedTask;
});

Test("Malformed success after real server apply retains intent until exact receipt replay", async () => {
    foreach (var brokenBody in new[] { "{\"state\":", "{}", "{\"state\":null}" })
    {
        using var client = Client("malformed-" + Guid.NewGuid().ToString("N"));
        await Ready(client);
        client.Transport.TransformNextApplyResponse = _ => new OnlineHttpResponse(200, brokenBody);
        var result = await client.Session.AdoptAsync("moon_jelly");
        Equal(OnlineOperationKind.Pending, result.Kind, "Malformed successful response is ambiguous");
        That(client.Session.HasPendingCommand && !client.Session.CanIssueCommands, "Ambiguous response cannot release pending intent");
        var original = client.Transport.Commands.Single().Json;
        var retry = await client.Session.RetryPendingAsync();
        Accepted(retry);
        That(retry.Replayed, "Real server already committed malformed-response command");
        Equal(original, client.Transport.Commands.Last().Json, "Malformed response uses same exact retry");
        Equal(0L, Pearls(client.Session.Snapshot), "Malformed response does not duplicate spend");
    }
});

Test("Fresh lower server revision after an offline reset fails closed instead of reporting ready", async () => {
    const string player = "regressed-server";
    using var client = Client(player);
    await Ready(client);
    Accepted(await client.Session.FeedAsync());
    Accepted(await client.Session.CleanAsync());
    var before = new TestJsonCodec().Serialize(client.Session.Snapshot);
    var revision = Revision(client.Session.Snapshot);
    await server.StopAsync();
    server.ArchivePlayerToSimulateReset(player);
    await server.StartAsync();
    var result = await client.Session.RefreshAsync();
    Equal(OnlineOperationKind.Failed, result.Kind, "Server revision reset is not a ready connection");
    That(!client.Session.IsConnected && !client.Session.CanIssueCommands, "Stale commands disabled after server history regresses");
    Equal(before, new TestJsonCodec().Serialize(client.Session.Snapshot), "Reset never replaces known newer snapshot");
    var freshGet = client.Transport.Requests.Last(value => value.Endpoint.AbsolutePath.EndsWith("/GetAquarium"));
    using var body = JsonDocument.Parse(freshGet.Response.Body);
    That(ulong.Parse(body.RootElement.GetProperty("state").GetProperty("revision").GetString()) < revision,
        "Test observed a genuine lower revision from the real server");
    client.AssertMonotonic();
});

var failures = 0;
foreach (var test in tests)
{
    try
    {
        await test.Run().WaitAsync(TimeSpan.FromSeconds(30));
        Console.WriteLine("PASS " + test.Name);
    }
    catch (Exception error)
    {
        failures++;
        Console.Error.WriteLine("FAIL " + test.Name + "\n" + error);
    }
}
Console.WriteLine($"{tests.Count - failures}/{tests.Count} live online integration checks passed.");
if (failures != 0) Console.Error.WriteLine("Server output:\n" + server.Logs);
return failures == 0 ? 0 : 1;
