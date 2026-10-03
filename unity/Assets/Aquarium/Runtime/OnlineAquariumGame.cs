using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Aquarium.Online;
using Aquarium.Presentation;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Aquarium.Runtime
{
    /// <summary>Separate, development-only online composition root. No local aquarium simulation or save imports.</summary>
    public sealed class OnlineAquariumGame : MonoBehaviour
    {
        [Header("LOCAL DEVELOPMENT ONLY - never use real player identities")]
        [SerializeField, Tooltip("Native desktop loopback only. No public hosts, proxies, tunnels or browser/WebGL support.")]
        private string serverEndpoint = DevServerOptions.DefaultEndpoint;
        [SerializeField, Tooltip("Not authentication. Any local process can impersonate this development player.")]
        private string developmentPlayer = "unity-dev";
        [SerializeField, Min(5), Tooltip("Reads confirmed server progress while connected. Never advances gameplay locally.")]
        private float refreshIntervalSeconds = 15;

        private readonly List<RaycastResult> uiHits = new List<RaycastResult>();
        private readonly List<CreatureVisual> visuals = new List<CreatureVisual>();
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private AquariumOnlineSession session;
        private HttpClientOnlineTransport ownedTransport;
        private FilePendingCommandStore ownedJournal;
        private TankView tank;
        private OnlineAquariumHud hud;
        private bool operationRunning, initialized, destroyed, suspended, needsResumeRefresh;
        private volatile bool viewDirty;
        private bool connectOnStart = true;
        private float nextRefresh;
        private string selectedSpecies;
        private string startupError;
        public AquariumOnlineSession Session => session;

        /// <summary>Optional composition seam for a configured session or an in-memory test transport.</summary>
        public void Initialize(AquariumOnlineSession configuredSession, bool connect = true)
        {
            if (initialized || session != null) throw new InvalidOperationException("Online scene is already initialized.");
            session = configuredSession ?? throw new ArgumentNullException(nameof(configuredSession));
            connectOnStart = connect;
        }

        private void Start()
        {
            tank = gameObject.AddComponent<TankView>();
            tank.Initialize();
            tank.CreatureSelected += SelectCreature;
            hud = gameObject.AddComponent<OnlineAquariumHud>();
            hud.Initialize(Feed, Clean, Collect, Adopt, SelectCreature, Reconnect, RetryPending, developmentPlayer);
            initialized = true;
            nextRefresh = Time.unscaledTime + Mathf.Max(5, refreshIntervalSeconds);
            try
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                throw new PlatformNotSupportedException("Online First Reef supports native desktop loopback only. WebGL is not supported.");
#else
                if (session == null)
                {
                    var options = new DevServerOptions(serverEndpoint, developmentPlayer);
                    ownedTransport = new HttpClientOnlineTransport();
                    var api = new ReefApiClient(options, new UnityOnlineJsonCodec(), ownedTransport);
                    ownedJournal = new FilePendingCommandStore(PendingJournalPath(options));
                    session = new AquariumOnlineSession(api, ownedJournal);
                }
                session.Changed += MarkDirty;
                if (connectOnStart) Run(token => session.ConnectAsync(token));
#endif
            }
            catch (Exception exception)
            {
                // Fail visibly; an invalid online configuration must never launch the offline game.
                startupError = "Online setup failed: " + exception.Message + " Stop Play and check the Online First Reef Inspector.";
                ownedTransport?.Dispose();
                ownedTransport = null;
                ownedJournal?.Dispose();
                ownedJournal = null;
            }
            RefreshView();
        }

        private static string PendingJournalPath(DevServerOptions options)
        {
            // This is a command journal, not a gameplay save. Its envelope also validates endpoint/player binding.
            using (var hash = SHA256.Create())
            {
                var digest = hash.ComputeHash(Encoding.UTF8.GetBytes(options.Endpoint + "\n" + options.DevPlayer));
                var key = BitConverter.ToString(digest).Replace("-", "").ToLowerInvariant();
                return Path.Combine(Application.persistentDataPath, "online-command-" + key + ".json");
            }
        }

        private void Update()
        {
            if (!initialized || destroyed || suspended) return;
            if (viewDirty) RefreshView();
            if (Pointer.current != null && Pointer.current.press.wasPressedThisFrame)
            {
                var position = Pointer.current.position.ReadValue();
                uiHits.Clear();
                if (EventSystem.current != null)
                    EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = position }, uiHits);
                if (uiHits.Count == 0) tank.PickCreature(position);
            }
            if (session == null || operationRunning || session.IsBusy) return;
            if (needsResumeRefresh)
            {
                needsResumeRefresh = false;
                Reconnect();
            }
            else if (Time.unscaledTime >= nextRefresh && session.CanIssueCommands && !session.HasPendingCommand)
                Run(token => session.RefreshAsync(token));
        }

        private void Feed() => Submit(token => session.FeedAsync(token), true);
        private void Clean() => Submit(token => session.CleanAsync(token));
        private void Collect() => Submit(token => session.CollectAsync(token));
        private void Adopt(string speciesId) => Submit(token => session.AdoptAsync(speciesId, token));

        private void Submit(Func<CancellationToken, Task<OnlineOperationResult>> command, bool feedEffect = false)
        {
            if (session == null || !session.CanIssueCommands || session.HasPendingCommand) return;
            Run(command, feedEffect);
        }

        private void Reconnect()
        {
            if (session != null) Run(token => session.ConnectAsync(token));
        }

        private void RetryPending()
        {
            if (session != null && session.HasPendingCommand) Run(token => session.RetryPendingAsync(token));
        }

        private async void Run(Func<CancellationToken, Task<OnlineOperationResult>> operation, bool feedEffect = false)
        {
            // A synchronous scene-level gate also protects against repeated callbacks in the same frame.
            if (destroyed || !isActiveAndEnabled || operationRunning || session == null || session.IsBusy) return;
            operationRunning = true;
            try
            {
                var task = operation(lifetime.Token);
                RefreshView();
                var result = await task;
                if (!destroyed && isActiveAndEnabled && feedEffect && result.GameplaySucceeded) tank.PlayFeedEffect();
            }
            catch (OperationCanceledException) { /* The session preserves any uncertain command in its journal. */ }
            catch (Exception exception)
            {
                startupError = "Online client stopped: " + exception.Message + " Stop Play and inspect the configuration; no local progress was substituted.";
            }
            finally
            {
                operationRunning = false;
                if (destroyed) DisposeResources();
                else
                {
                    nextRefresh = Time.unscaledTime + Mathf.Max(5, refreshIntervalSeconds);
                    if (isActiveAndEnabled) RefreshView();
                    else viewDirty = true;
                }
            }
        }

        private void MarkDirty() => viewDirty = true;

        private void SelectCreature(string speciesId)
        {
            if (OnlineAquariumHud.FindCreature(session?.Snapshot, speciesId) == null) return;
            selectedSpecies = speciesId;
            RefreshView();
        }

        private void RefreshView()
        {
            viewDirty = false;
            if (!initialized || destroyed) return;
            var snapshot = session?.Snapshot;
            var catalog = session?.Catalog;
            if (OnlineAquariumHud.FindCreature(snapshot, selectedSpecies) == null)
                selectedSpecies = snapshot?.creatures != null && snapshot.creatures.Length > 0 ? snapshot.creatures[0].speciesId : null;
            visuals.Clear();
            if (snapshot?.creatures != null)
            {
                foreach (var creature in snapshot.creatures)
                {
                    var species = OnlineAquariumHud.FindSpecies(catalog, creature.speciesId);
                    var growth = species != null && species.matureAfterCareHours > 0 ? creature.growthHours / species.matureAfterCareHours : 0;
                    // Species ID chooses artwork only. All economics and maturity values come from the server.
                    var art = creature.speciesId == "moon_jelly" ? 1 : creature.speciesId == "coral_drake" ? 2 : 0;
                    visuals.Add(new CreatureVisual(creature.speciesId, art, (float)growth, creature.speciesId == selectedSpecies));
                }
            }
            tank.SyncCreatures(visuals);
            hud.Refresh(snapshot, catalog, selectedSpecies, startupError ?? session?.StatusMessage,
                operationRunning || (session?.IsBusy ?? false), startupError == null && (session?.CanIssueCommands ?? false),
                session?.HasPendingCommand ?? false, session != null && startupError == null);
        }

        private void OnApplicationPause(bool paused)
        {
            suspended = paused;
            if (!paused) needsResumeRefresh = true;
        }

        private void OnApplicationFocus(bool focused)
        {
            // Refresh on resume, including after an in-flight command settles. Never silently replay an uncertain command.
            if (initialized && focused) needsResumeRefresh = true;
        }

        private void OnEnable()
        {
            if (initialized) needsResumeRefresh = true;
        }

        private void OnDestroy()
        {
            destroyed = true;
            if (tank != null) tank.CreatureSelected -= SelectCreature;
            if (session != null) session.Changed -= MarkDirty;
            lifetime.Cancel();
            if (!operationRunning) DisposeResources();
        }

        private void DisposeResources()
        {
            ownedTransport?.Dispose();
            ownedTransport = null;
            ownedJournal?.Dispose();
            ownedJournal = null;
            lifetime.Dispose();
        }
    }
}
