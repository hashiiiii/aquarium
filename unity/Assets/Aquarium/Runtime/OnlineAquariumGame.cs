using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Aquarium.Online;
using Aquarium.Presentation;
using Aquarium.InGame;
using Aquarium.Outgame.Client;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using VContainer;

namespace Aquarium.Runtime
{
    /// <summary>Separate, development-only online composition root. No local aquarium simulation or save imports.</summary>
    public sealed class OnlineAquariumGame : MonoBehaviour
    {
        [Header("LOCAL DEVELOPMENT ONLY - never use real player identities")]
        [SerializeField] private AquariumConnectionSettings connectionSettings;
        [SerializeField, Min(5), Tooltip("Reads confirmed server progress while connected. Never advances gameplay locally.")]
        private float refreshIntervalSeconds = 15;

        private readonly List<RaycastResult> uiHits = new List<RaycastResult>();
        private readonly List<CreatureVisual> visuals = new List<CreatureVisual>();
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private AquariumOnlineSession session;
        private IAquariumOnlineSessionFactory sessionFactory;
        private TankView tank;
        private OnlineAquariumHud hud;
        private AquariumStateMachine stateMachine;
        private bool operationRunning, initialized, destroyed, suspended, needsResumeRefresh;
        private volatile bool viewDirty;
        private bool connectOnStart = true;
        private bool lastFocused;
        private float nextRefresh;
        private string selectedSpecies;
        private string startupError;
        public AquariumOnlineSession Session => session;
        public AquariumConnectionSettings ConnectionSettings => connectionSettings;

        [Inject]
        private void Construct(IAquariumOnlineSessionFactory factory)
        {
            sessionFactory = factory;
        }

        public void Configure(AquariumConnectionSettings settings)
        {
            connectionSettings = settings;
        }

        /// <summary>Optional composition seam for a configured session or an in-memory test transport.</summary>
        public void Initialize(AquariumOnlineSession configuredSession, bool connect = true)
        {
            if (initialized || session != null) throw new InvalidOperationException("Online scene is already initialized.");
            session = configuredSession ?? throw new ArgumentNullException(nameof(configuredSession));
            connectOnStart = connect;
        }

        private void Start()
        {
            stateMachine = new AquariumStateMachine(() => SceneManager.LoadScene("Home"));
            lastFocused = Application.isFocused;
            tank = gameObject.AddComponent<TankView>();
            tank.Initialize();
            tank.CreatureSelected += SelectCreature;
            hud = gameObject.AddComponent<OnlineAquariumHud>();
            hud.Initialize(Feed, Clean, Collect, Adopt, SelectCreature, Reconnect, RetryPending,
                connectionSettings == null ? "unity-dev" : connectionSettings.DevelopmentPlayer);
            initialized = true;
            nextRefresh = Time.unscaledTime + Mathf.Max(5, refreshIntervalSeconds);
            try
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                throw new PlatformNotSupportedException("Online First Reef supports native desktop loopback only. WebGL is not supported.");
#else
                if (session == null)
                {
                    if (sessionFactory == null)
                        throw new InvalidOperationException("Aquarium scene scope did not inject an online session factory.");
                    session = sessionFactory.Create();
                }
                session.Changed += MarkDirty;
                if (connectOnStart) Run(token => session.ConnectAsync(token));
#endif
            }
            catch (Exception exception)
            {
                // Fail visibly; an invalid online configuration must never launch the offline game.
                startupError = "Online setup failed: " + exception.Message + " Stop Play and check the Online First Reef Inspector.";
            }
            RefreshView();
        }

        private void Update()
        {
            if (!initialized || destroyed) return;
            var focused = Application.isFocused;
            if (focused && !lastFocused) needsResumeRefresh = true;
            suspended = !focused;
            lastFocused = focused;
            stateMachine.Update(session?.Snapshot != null,
                Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame);
            if (stateMachine.Mode == AquariumMode.Leaving || stateMachine.Mode == AquariumMode.Closed) return;
            UpdatePresentation();
            UpdatePointerInput();
            UpdateConnection();
        }

        private void UpdatePresentation()
        {
            if (viewDirty) RefreshView();
            tank?.Tick();
            hud?.Tick();
        }

        private void UpdatePointerInput()
        {
            if (Pointer.current != null && Pointer.current.press.wasPressedThisFrame)
            {
                var position = Pointer.current.position.ReadValue();
                uiHits.Clear();
                if (EventSystem.current != null)
                    EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = position }, uiHits);
                if (uiHits.Count == 0) tank.PickCreature(position);
            }
        }

        private void UpdateConnection()
        {
            if (suspended) return;
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
                sessionFactory?.Track(task);
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
            lifetime.Dispose();
        }
    }
}
