using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using Aquarium.Online;
using Aquarium.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Aquarium.Tests
{
    public sealed class AquariumOnlinePresentationTests
    {
        private GameObject root;
        private OnlineAquariumHud hud;
        private int fed, cleaned, collected, adopted, selected, refreshed, retried;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (root != null) UnityEngine.Object.Destroy(root);
            yield return null;
        }

        [SetUp]
        public void ResetCounters() => fed = cleaned = collected = adopted = selected = refreshed = retried = 0;

        private void CreateHud()
        {
            root = new GameObject("Online HUD test");
            hud = root.AddComponent<OnlineAquariumHud>();
            hud.Initialize(() => fed++, () => cleaned++, () => collected++, _ => adopted++, _ => selected++,
                () => refreshed++, () => retried++, "test-player");
        }

        private static ServerAquariumState Snapshot() => new ServerAquariumState
        {
            version = 1, revision = "9007199254740993", lastUpdatedAtUnixMs = "1790985600000", pearls = "41",
            fullness = 50, cleanliness = 60,
            creatures = new[] { new ServerCreature { speciesId = "tide_sprite", growthHours = 9, pendingPearls = 3.7 } }
        };

        private static ServerCatalog Catalog() => new ServerCatalog
        {
            // Deliberately differs from the offline Core catalog. UI must use these server values.
            species = new[]
            {
                new ServerSpeciesDefinition { id = "tide_sprite", displayName = "Server Sprite", price = "0", pearlsPerCareHour = 17, matureAfterCareHours = 36 },
                new ServerSpeciesDefinition { id = "moon_jelly", displayName = "Server Jelly", price = "41", pearlsPerCareHour = 21, matureAfterCareHours = 44 }
            },
            rules = new ServerSimulationRules { careThreshold = 70, walletCapacity = "12000", capacity = 4, replayCapacity = 256, rewardCapacity = 100, offlineCapSeconds = 28800 }
        };

        [UnityTest]
        public IEnumerator CatalogueAndSnapshotDriveOnlineHudWithoutMutatingThem()
        {
            CreateHud();
            var snapshot = Snapshot();
            var catalog = Catalog();
            hud.Refresh(snapshot, catalog, "tide_sprite", "Server ready", false, true, false);
            yield return null;
            Assert.That(FindText("Online Pearl balance").text, Is.EqualTo("41  PEARLS"));
            Assert.That(FindText("Online Selected creature").text, Is.EqualTo("Server Sprite"));
            Assert.That(FindText("Online Growth details").text, Does.Contain("25%"));
            Assert.That(FindText("Online Growth details").text, Does.Contain("17 pearls"));
            Assert.That(FindText("Online Growth details").text, Does.Contain("Resting"));
            Assert.That(FindText("Online Connection").text, Does.Contain("9007199254740993"));
            Assert.That(FindButton("Online Species moon_jelly").GetComponentInChildren<Text>().text, Is.EqualTo("Server Jelly  /  41"));
            FindButton("Online Feed").onClick.Invoke();
            FindButton("Online Clean").onClick.Invoke();
            FindButton("Online Collect").onClick.Invoke();
            FindButton("Online Species moon_jelly").onClick.Invoke();
            FindButton("Online Species tide_sprite").onClick.Invoke();
            Assert.That(new[] { fed, cleaned, collected, adopted, selected }, Is.EqualTo(new[] { 1, 1, 1, 1, 1 }));
            Assert.That(snapshot.pearls, Is.EqualTo("41"));
            Assert.That(snapshot.creatures.Length, Is.EqualTo(1));
            Assert.That(snapshot.creatures[0].growthHours, Is.EqualTo(9));
        }

        [UnityTest]
        public IEnumerator BusyPendingAndDisconnectedControlsDoNotAcceptGameplayClicks()
        {
            CreateHud();
            var snapshot = Snapshot();
            var catalog = Catalog();
            hud.Refresh(snapshot, catalog, "tide_sprite", "Sending...", true, false, false);
            ClickEveryControl();
            Assert.That(new[] { fed, cleaned, collected, adopted, refreshed, retried }, Is.EqualTo(new[] { 0, 0, 0, 0, 0, 0 }));
            Assert.That(selected, Is.EqualTo(1), "Inspection remains available while networking.");
            hud.Refresh(snapshot, catalog, "tide_sprite", "Result unknown. Retry the same command.", false, false, true);
            ClickEveryControl();
            Assert.That(new[] { fed, cleaned, collected, adopted }, Is.EqualTo(new[] { 0, 0, 0, 0 }));
            Assert.That(refreshed, Is.EqualTo(1));
            Assert.That(retried, Is.EqualTo(1));
            Assert.That(FindText("Online Connection").text, Does.Contain("COMMAND RESULT UNKNOWN"));
            hud.Refresh(snapshot, catalog, "tide_sprite", "Server unavailable", false, false, false);
            ClickEveryControl();
            Assert.That(new[] { fed, cleaned, collected, adopted }, Is.EqualTo(new[] { 0, 0, 0, 0 }));
            Assert.That(refreshed, Is.EqualTo(2));
            Assert.That(retried, Is.EqualTo(1));
            Assert.That(FindText("Online Pearl balance").text, Is.EqualTo("41  PEARLS"), "Disconnected HUD keeps the confirmed snapshot.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator RepeatedCatalogueRefreshDoesNotDuplicateControlsAndOwnedButtonOnlyInspects()
        {
            CreateHud();
            var snapshot = Snapshot();
            var catalog = Catalog();
            hud.Refresh(snapshot, catalog, "tide_sprite", "Ready", false, true, false);
            hud.Refresh(snapshot, catalog, "tide_sprite", "Ready", false, true, false);
            snapshot.creatures = new[] { snapshot.creatures[0], new ServerCreature { speciesId = "moon_jelly" } };
            hud.Refresh(snapshot, catalog, "moon_jelly", "Adopted", false, true, false);
            FindButton("Online Species moon_jelly").onClick.Invoke();
            FindButton("Online Species moon_jelly").onClick.Invoke();
            Assert.That(adopted, Is.Zero);
            Assert.That(selected, Is.EqualTo(2));
            Assert.That(root.GetComponentsInChildren<Button>().Length, Is.EqualTo(7));
            Assert.That(FindText("Online Selected creature").text, Is.EqualTo("Server Jelly"));
            yield return null;
        }

        [UnityTest]
        public IEnumerator InvalidSetupKeepsOnlineControlsBlockedWithoutInventingAReef()
        {
            CreateHud();
            hud.Refresh(null, null, null, "Invalid endpoint. Stop Play and configure loopback.", false, false, false, false);
            FindButton("Online Feed").onClick.Invoke();
            FindButton("Online Refresh").onClick.Invoke();
            FindButton("Online Retry").onClick.Invoke();
            Assert.That(fed + refreshed + retried, Is.Zero);
            Assert.That(FindText("Online Pearl balance").text, Is.EqualTo("--  PEARLS"));
            Assert.That(FindText("Online Connection").text, Does.Contain("ONLINE SETUP BLOCKED"));
            yield return null;
        }

        [UnityTest]
        public IEnumerator SceneWiringSerializesClicksAndOnlyDisplaysConfirmedServerChanges()
        {
            var codec = new UnityOnlineJsonCodec();
            var transport = new DeferredTransport(codec);
            var session = new AquariumOnlineSession(new ReefApiClient(new DevServerOptions(DevServerOptions.DefaultEndpoint, "scene-test"), codec, transport), new MemoryJournal());
            // This fake's reads finish synchronously; no socket or persistent game save is involved.
            Assert.That(session.ConnectAsync().GetAwaiter().GetResult().Kind, Is.EqualTo(OnlineOperationKind.Ready));
            root = new GameObject("Online scene test");
            var game = root.AddComponent<OnlineAquariumGame>();
            game.Initialize(session, false);
            yield return null;
            Assert.That(root.GetComponent<AquariumGame>(), Is.Null);
            Assert.That(root.transform.Find("Creature tide_sprite"), Is.Not.Null);
            var feedButton = FindButton("Online Feed");
            feedButton.onClick.Invoke();
            feedButton.onClick.Invoke();
            Assert.That(transport.CommandCount, Is.EqualTo(1));
            Assert.That(feedButton.interactable, Is.False);
            Assert.That(session.Snapshot.fullness, Is.EqualTo(50));
            Assert.That(FindText("Online Fullness").text, Is.EqualTo("FULLNESS  50%"));
            transport.CompleteFeed();
            yield return new WaitUntil(() => !session.IsBusy);
            yield return null;
            Assert.That(session.Snapshot.fullness, Is.EqualTo(75));
            Assert.That(FindText("Online Fullness").text, Is.EqualTo("FULLNESS  75%"));
            Assert.That(session.HasPendingCommand, Is.False);
        }

        private sealed class MemoryJournal : IPendingCommandStore
        {
            private string contents;
            public bool TryRead(out string value) { value = contents; return !string.IsNullOrEmpty(value); }
            public void Write(string value) => contents = value;
            public void Clear() => contents = null;
        }

        private sealed class DeferredTransport : IOnlineTransport
        {
            private readonly UnityOnlineJsonCodec codec;
            private TaskCompletionSource<OnlineHttpResponse> pending;
            public int CommandCount { get; private set; }
            public DeferredTransport(UnityOnlineJsonCodec codec) { this.codec = codec; }
            public Task<OnlineHttpResponse> PostAsync(Uri endpoint, string player, string json, CancellationToken token)
            {
                if (endpoint.AbsolutePath.EndsWith("GetCatalog", StringComparison.Ordinal))
                    return Task.FromResult(new OnlineHttpResponse(200, codec.Serialize(Catalog())));
                if (endpoint.AbsolutePath.EndsWith("GetAquarium", StringComparison.Ordinal))
                    return Task.FromResult(new OnlineHttpResponse(200, codec.Serialize(new ServerGetAquariumResponse { state = Snapshot() })));
                CommandCount++;
                pending = new TaskCompletionSource<OnlineHttpResponse>();
                token.Register(() => pending.TrySetCanceled());
                return pending.Task;
            }
            public void CompleteFeed()
            {
                var state = Snapshot();
                state.revision = "9007199254740994";
                state.fullness = 75;
                pending.SetResult(new OnlineHttpResponse(200, codec.Serialize(new ServerApplyCommandResponse
                {
                    state = state, success = true, amount = "0", message = "Fed by server.", advance = new ServerAdvanceResult()
                })));
            }
        }

        [Test]
        public void JsonUtilityPreservesProtobufStringIntegersAndCommandIntent()
        {
            var codec = new UnityOnlineJsonCodec();
            var snapshot = codec.Deserialize<ServerGetAquariumResponse>(
                "{\"state\":{\"version\":1,\"revision\":\"18446744073709551614\",\"lastUpdatedAtUnixMs\":\"1790985600000\",\"pearls\":\"41\",\"fullness\":50,\"cleanliness\":60,\"creatures\":[{\"speciesId\":\"tide_sprite\",\"growthHours\":9,\"pendingPearls\":3.7}]}}").state;
            Assert.That(snapshot.revision, Is.EqualTo("18446744073709551614"));
            Assert.That(snapshot.creatures[0].growthHours, Is.EqualTo(9));
            var command = new ServerApplyCommandRequest { requestId = "intent-001", expectedRevision = snapshot.revision, action = "ACTION_ADOPT", speciesId = "moon_jelly" };
            var json = codec.Serialize(command);
            Assert.That(json, Does.Contain("\"expectedRevision\":\"18446744073709551614\""));
            Assert.That(json, Does.Not.Contain("pearls"));
            Assert.That(json, Does.Not.Contain("growthHours"));
        }

        [Test]
        public void ProductionCodecAcceptsOmittedProto3ZeroFields()
        {
            var codec = new UnityOnlineJsonCodec();
            var transport = new ImmediateTransport(codec, "{\"state\":{\"version\":1,\"revision\":\"1\",\"lastUpdatedAtUnixMs\":\"1790985600000\"}}", "{}");
            var api = new ReefApiClient(new DevServerOptions(DevServerOptions.DefaultEndpoint, "zero-fields"), codec, transport);
            var state = api.GetAquariumAsync().GetAwaiter().GetResult();
            Assert.That(state.pearls, Is.EqualTo("0"));
            Assert.That(state.fullness, Is.Zero);
            Assert.That(state.cleanliness, Is.Zero);
            Assert.That(state.creatures, Is.Empty);
        }

        [TestCase("{\"success\":true,\"state\":{}}")]
        [TestCase("{\"success\":true,\"state\":{\"version\":\"invalid\",\"revision\":\"9007199254740994\",\"lastUpdatedAtUnixMs\":\"1790985600000\"},\"advance\":{}}")]
        public void ProductionCodecKeepsIntentPendingWhenSuccessBodyIsMalformed(string malformedBody)
        {
            var codec = new UnityOnlineJsonCodec();
            var transport = new ImmediateTransport(codec, codec.Serialize(new ServerGetAquariumResponse { state = Snapshot() }), malformedBody);
            var session = new AquariumOnlineSession(new ReefApiClient(new DevServerOptions(DevServerOptions.DefaultEndpoint, "bad-success"), codec, transport), new MemoryJournal());
            Assert.That(session.ConnectAsync().GetAwaiter().GetResult().Kind, Is.EqualTo(OnlineOperationKind.Ready));
            var result = session.FeedAsync().GetAwaiter().GetResult();
            Assert.That(result.Kind, Is.EqualTo(OnlineOperationKind.Pending));
            Assert.That(session.HasPendingCommand, Is.True);
            Assert.That(session.CanIssueCommands, Is.False);
            Assert.That(session.Snapshot.revision, Is.EqualTo("9007199254740993"));
            Assert.That(session.Snapshot.fullness, Is.EqualTo(50));
        }

        private sealed class ImmediateTransport : IOnlineTransport
        {
            private readonly UnityOnlineJsonCodec codec;
            private readonly string stateJson, commandJson;
            public ImmediateTransport(UnityOnlineJsonCodec codec, string stateJson, string commandJson)
            { this.codec = codec; this.stateJson = stateJson; this.commandJson = commandJson; }
            public Task<OnlineHttpResponse> PostAsync(Uri endpoint, string player, string json, CancellationToken token)
            {
                var body = endpoint.AbsolutePath.EndsWith("GetCatalog", StringComparison.Ordinal) ? codec.Serialize(Catalog()) :
                    endpoint.AbsolutePath.EndsWith("GetAquarium", StringComparison.Ordinal) ? stateJson : commandJson;
                return Task.FromResult(new OnlineHttpResponse(200, body));
            }
        }

        private void ClickEveryControl()
        {
            foreach (var button in root.GetComponentsInChildren<Button>()) button.onClick.Invoke();
        }

        private Button FindButton(string name)
        {
            foreach (var button in root.GetComponentsInChildren<Button>()) if (button.name == name) return button;
            Assert.Fail("Missing online button: " + name);
            return null;
        }

        private Text FindText(string name)
        {
            foreach (var text in root.GetComponentsInChildren<Text>()) if (text.name == name) return text;
            Assert.Fail("Missing online label: " + name);
            return null;
        }
    }
}
