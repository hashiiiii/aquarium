using System;
using System.Collections.Generic;
using System.IO;
using Aquarium.Core;
using Aquarium.Presentation;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Aquarium.Runtime
{
    /// <summary>Scene-local composition root. No server, account or global startup hooks.</summary>
    public sealed class AquariumGame : MonoBehaviour
    {
        private readonly List<RaycastResult> uiHits = new List<RaycastResult>();
        private readonly List<CreatureVisual> visuals = new List<CreatureVisual>(3);
        private AquariumState state;
        private AquariumSaveService saves;
        private TankView tank;
        private AquariumHud hud;
        private float nextRefresh;
        private float nextSave;
        private bool savingAllowed = true;
        private bool initialized;
        private string selectedSpecies = "tide_sprite";
        private string persistenceWarning;
        private string notice;

        public AquariumState State => state;

        private sealed class JsonSerializer : IAquariumSerializer
        {
            public string Serialize(AquariumState value) => JsonUtility.ToJson(value, true);
            public AquariumState Deserialize(string json) => JsonUtility.FromJson<AquariumState>(json);
        }

        private void Awake()
        {
            var now = DateTime.UtcNow;
            var store = new AquariumFileStore(Path.Combine(Application.persistentDataPath, "aquarium-v1.json"));
            var serializer = new JsonSerializer();
            saves = new AquariumSaveService(store, serializer);
            var recovery = AquariumRecovery.Load(store, serializer, now);
            var loaded = recovery.load;
            savingAllowed = recovery.savingAllowed;
            persistenceWarning = recovery.warning;
            notice = recovery.notice;
            state = loaded.state;
            if (string.IsNullOrEmpty(notice)) notice = WelcomeMessage(loaded);
            tank = gameObject.AddComponent<TankView>();
            tank.Initialize();
            tank.CreatureSelected += SelectCreature;
            hud = gameObject.AddComponent<AquariumHud>();
            hud.Initialize(Feed, Clean, Collect, Adopt, SelectCreature);
            initialized = true;
            Refresh();
            Save();
        }

        private static string WelcomeMessage(LoadResult loaded)
        {
            if (!loaded.restored) return "Welcome home. Feed your Tide Sprite, then adopt a Moon Jelly with your 30 pearls.";
            if (loaded.advance.clockRolledBack) return "Clock moved backwards. Progress resumes when the saved time catches up.";
            if (loaded.advance.simulatedSeconds >= 60)
                return "Welcome back! Your reef grew for " + TimeSpan.FromSeconds(loaded.advance.simulatedSeconds).ToString(@"h\h\ mm\m") +
                    (loaded.advance.wasCapped ? " (8-hour offline limit)." : ".");
            return "Welcome back to your little reef.";
        }

        private void Update()
        {
            if (!initialized) return;
            tank.Tick();
            if (Pointer.current != null && Pointer.current.press.wasPressedThisFrame)
            {
                var position = Pointer.current.position.ReadValue();
                uiHits.Clear();
                if (EventSystem.current != null)
                    EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = position }, uiHits);
                if (uiHits.Count == 0) tank.PickCreature(position);
            }
            if (Time.unscaledTime >= nextRefresh)
            {
                AquariumSimulation.AdvanceTo(state, DateTime.UtcNow);
                Refresh();
                nextRefresh = Time.unscaledTime + 1f;
            }
            if (Time.unscaledTime >= nextSave) Save();
        }

        private void Feed() => Act(() => AquariumSimulation.Feed(state), true);
        private void Clean() => Act(() => AquariumSimulation.Clean(state));
        private void Collect() => Act(() => AquariumSimulation.Collect(state));
        private void Adopt(string speciesId) => Act(() => AquariumSimulation.Acquire(state, speciesId));

        private void Act(Func<ActionResult> action, bool feeding = false)
        {
            AquariumSimulation.AdvanceTo(state, DateTime.UtcNow);
            var result = action();
            notice = result.message;
            if (result.success && feeding) tank.PlayFeedEffect();
            if (result.success) Save();
            Refresh();
        }

        private void SelectCreature(string speciesId)
        {
            foreach (var creature in state.creatures)
            {
                if (creature.speciesId != speciesId) continue;
                selectedSpecies = speciesId;
                Refresh();
                break;
            }
        }

        private void Refresh()
        {
            visuals.Clear();
            foreach (var creature in state.creatures)
            {
                var species = SpeciesCatalog.Get(creature.speciesId);
                var index = 0;
                for (var i = 0; i < SpeciesCatalog.All.Count; i++)
                    if (SpeciesCatalog.All[i].id == creature.speciesId) index = i;
                visuals.Add(new CreatureVisual(creature.speciesId, index,
                    (float)(creature.growthHours / species.matureAfterCareHours), creature.speciesId == selectedSpecies));
            }
            tank.SyncCreatures(visuals);
            hud.Refresh(state, selectedSpecies, string.IsNullOrEmpty(persistenceWarning) ? notice : persistenceWarning);
        }

        private void Save()
        {
            nextSave = Time.unscaledTime + 30f;
            if (!savingAllowed || state == null) return;
            var result = saves.Save(state, DateTime.UtcNow);
            persistenceWarning = result.success ? null : result.message + " Progress is only in memory until saving succeeds.";
        }

        private void OnApplicationPause(bool paused)
        {
            if (!initialized) return;
            if (paused) Save();
            else Resume();
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!initialized) return;
            if (focused) Resume();
            else Save();
        }

        private void Resume()
        {
            var elapsed = AquariumSimulation.AdvanceTo(state, DateTime.UtcNow);
            if (elapsed.simulatedSeconds >= 60)
                notice = WelcomeMessage(new LoadResult { restored = true, advance = elapsed });
            Save();
            Refresh();
        }

        private void OnDestroy()
        {
            if (!initialized) return;
            if (tank != null) tank.CreatureSelected -= SelectCreature;
            Save();
        }

        private void OnApplicationQuit() => Save();
    }
}
