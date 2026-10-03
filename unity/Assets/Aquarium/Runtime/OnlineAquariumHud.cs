using System;
using System.Collections.Generic;
using Aquarium.Online;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Aquarium.Runtime
{
    /// <summary>Read-only server projection. This HUD never computes gameplay progress or changes a snapshot.</summary>
    public sealed class OnlineAquariumHud : MonoBehaviour
    {
        private static readonly Color Ink = new Color(.025f, .075f, .11f, .96f);
        private static readonly Color Muted = new Color(.59f, .76f, .77f);
        private static readonly Color Pearl = new Color(.95f, .86f, .65f);
        private static readonly Color Aqua = new Color(.34f, .83f, .77f);
        private readonly List<SpeciesRow> rows = new List<SpeciesRow>();
        private Font font;
        private RectTransform safeRoot, speciesContent;
        private GameObject canvasObject;
        private Text balance, connection, status, creatureTitle, creatureDetail, fullness, cleanliness, collectLabel;
        private Image growthBar, fullnessBar, cleanlinessBar;
        private Button feed, clean, collect, refresh, retry;
        private Action<string> onAdopt, onSelect;
        private Rect lastSafeArea;
        private int lastWidth, lastHeight;

        private sealed class SpeciesRow
        {
            public string id;
            public Button button;
            public Text label;
            public bool owned;
        }

        public void Initialize(Action onFeed, Action onClean, Action onCollect, Action<string> adopt,
            Action<string> select, Action onRefresh, Action onRetry, string identity)
        {
            if (canvasObject != null) return;
            onAdopt = adopt;
            onSelect = select;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            canvasObject = new GameObject("Online Aquarium HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            safeRoot = Rect("Safe Area", canvasObject.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            if (EventSystem.current == null)
            {
                var events = new GameObject("Online Aquarium Event System", typeof(EventSystem), typeof(InputSystemUIInputModule));
                events.transform.SetParent(transform, false);
            }
            var header = Panel("Header", safeRoot, new Vector2(0, 1), Vector2.one, new Vector2(24, -94), new Vector2(-24, -20), Ink);
            Label("Mode", header, "A Q U A R I U M   /   ONLINE FIRST REEF   /   LOCAL DEV", 13, Aqua, 20, 9, 760, 21);
            Label("Title", header, "A reef kept by your local server.", 26, Color.white, 20, 30, 760, 37);
            balance = Label("Online Pearl balance", header, "--  PEARLS", 22, Pearl, -270, 20, 245, 36, true);
            balance.alignment = TextAnchor.MiddleRight;

            var network = Panel("Connection", safeRoot, new Vector2(0, 1), Vector2.one, new Vector2(24, -165), new Vector2(-24, -106), Ink);
            connection = Label("Online Connection", network, "CONNECTING", 14, Aqua, 18, 8, 790, 22);
            Label("Online Identity", network, "Development player: " + identity + "  |  Server-authoritative progress", 12, Muted, 18, 32, 790, 20);
            refresh = MakeButton("Online Refresh", network, -386, 9, 178, 41, "RECONNECT / REFRESH", onRefresh);
            retry = MakeButton("Online Retry", network, -198, 9, 180, 41, "RETRY SAME COMMAND", onRetry);
            ((RectTransform)refresh.transform).anchorMin = ((RectTransform)refresh.transform).anchorMax = Vector2.one;
            ((RectTransform)retry.transform).anchorMin = ((RectTransform)retry.transform).anchorMax = Vector2.one;

            var journal = Panel("Server catalogue", safeRoot, Vector2.one, Vector2.one, new Vector2(-310, -408), new Vector2(-24, -181), Ink);
            Label("Collection title", journal, "SERVER COMPANIONS", 14, Aqua, 18, 10, 250, 24);
            Label("Collection hint", journal, "Prices and growth come from the server.", 12, Muted, 18, 34, 252, 22);
            var viewport = Rect("Species viewport", journal, Vector2.zero, Vector2.one, new Vector2(12, 10), new Vector2(-12, -62));
            viewport.gameObject.AddComponent<RectMask2D>();
            speciesContent = Rect("Species content", viewport, new Vector2(0, 1), Vector2.one, Vector2.zero, Vector2.zero);
            speciesContent.pivot = new Vector2(.5f, 1);
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = speciesContent;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 28;

            var dock = Panel("Server care dock", safeRoot, Vector2.zero, new Vector2(1, 0), new Vector2(24, 24), new Vector2(-24, 244), Ink);
            creatureTitle = Label("Online Selected creature", dock, "Waiting for your reef", 24, Pearl, 22, 12, 330, 34);
            creatureDetail = Label("Online Growth details", dock, "Connect to load a server snapshot.", 14, Muted, 22, 52, 335, 58);
            growthBar = Meter("Online Growth", dock, 22, 115, 320, new Color(.64f, .63f, .92f));
            Label("Authority hint", dock, "Showing the last confirmed snapshot.\nNo local progress or offline-save import.\nReconnect after returning to this window.", 12, Muted, 22, 135, 340, 65);
            var controls = Rect("Online Care controls", dock, new Vector2(.32f, 0), Vector2.one, new Vector2(10, 0), new Vector2(-18, 0));
            fullness = Label("Online Fullness", controls, "FULLNESS  --", 14, Aqua, 8, 15, 260, 26);
            cleanliness = Label("Online Cleanliness", controls, "WATER  --", 14, Aqua, 8, 53, 260, 26);
            fullnessBar = Meter("Online Fullness meter", controls, 270, 23, 320, Aqua);
            cleanlinessBar = Meter("Online Water meter", controls, 270, 61, 320, Aqua);
            feed = FractionButton("Online Feed", controls, 0, "FEED", onFeed);
            clean = FractionButton("Online Clean", controls, 1, "CLEAN", onClean);
            collect = FractionButton("Online Collect", controls, 2, "COLLECT", onCollect);
            collectLabel = collect.GetComponentInChildren<Text>();
            status = Label("Online Status", controls, "", 14, Pearl, 8, 145, 735, 66);
            status.rectTransform.anchorMax = new Vector2(1, 1);
            status.rectTransform.offsetMax = new Vector2(-8, -145);
            status.rectTransform.offsetMin = new Vector2(8, -211);
            UpdateSafeArea();
            Refresh(null, null, null, "Connecting to the development server...", true, false, false);
        }

        public void Refresh(ServerAquariumState snapshot, ServerCatalog catalog, string selectedSpecies,
            string message, bool busy, bool canIssueCommands, bool pending, bool configurationValid = true)
        {
            var ready = snapshot != null && catalog != null;
            var canAct = ready && canIssueCommands && !busy && !pending;
            connection.text = (!configurationValid ? "ONLINE SETUP BLOCKED" : busy ? "CONTACTING SERVER" : pending ? "COMMAND RESULT UNKNOWN" : canIssueCommands ? "SERVER CONNECTED" : "RECONNECT REQUIRED") +
                (snapshot == null ? "" : "   |   Last confirmed revision " + snapshot.revision);
            connection.color = canIssueCommands && !pending ? Aqua : Pearl;
            status.text = message ?? "";
            refresh.interactable = configurationValid && !busy;
            retry.interactable = configurationValid && pending && !busy;
            feed.interactable = clean.interactable = collect.interactable = canAct;
            balance.text = snapshot == null ? "--  PEARLS" : snapshot.pearls + "  PEARLS";
            fullness.text = "FULLNESS  " + (snapshot == null ? "--" : Mathf.RoundToInt((float)snapshot.fullness) + "%");
            cleanliness.text = "WATER  " + (snapshot == null ? "--" : Mathf.RoundToInt((float)snapshot.cleanliness) + "%");
            SetMeter(fullnessBar, snapshot == null ? 0 : snapshot.fullness / 100);
            SetMeter(cleanlinessBar, snapshot == null ? 0 : snapshot.cleanliness / 100);
            var collectable = 0d;
            if (snapshot?.creatures != null)
                foreach (var creature in snapshot.creatures) collectable += Math.Floor(creature.pendingPearls);
            collectLabel.text = "COLLECT  " + collectable.ToString("0");
            // These are presentation hints only; the server validates every command.
            if (snapshot != null)
            {
                feed.interactable &= snapshot.fullness < 100;
                clean.interactable &= snapshot.cleanliness < 100;
                collect.interactable &= collectable > 0;
            }
            SyncSpecies(catalog, snapshot, canAct);
            var selected = FindCreature(snapshot, selectedSpecies);
            var definition = FindSpecies(catalog, selectedSpecies);
            if (selected == null || definition == null)
            {
                creatureTitle.text = ready ? "Select a companion" : "Waiting for your reef";
                creatureDetail.text = ready ? "Select an owned companion to inspect it." : "Connect to load a server snapshot.";
                SetMeter(growthBar, 0);
                return;
            }
            var progress = definition.matureAfterCareHours > 0 ? selected.growthHours / definition.matureAfterCareHours : 0;
            creatureTitle.text = definition.displayName;
            creatureDetail.text = (progress >= 1 ? "Fully grown" : "Growing  " + Math.Floor(Math.Min(1, progress) * 100) + "%") +
                "  /  " + definition.pearlsPerCareHour + " pearls per care hour\n" +
                (catalog.rules != null && snapshot.fullness > catalog.rules.careThreshold && snapshot.cleanliness > catalog.rules.careThreshold
                    ? "Comfortable in the last server snapshot." : "Resting. Feed and clean to resume growth.");
            SetMeter(growthBar, progress);
        }

        public static ServerCreature FindCreature(ServerAquariumState snapshot, string id)
        {
            if (snapshot?.creatures != null)
                foreach (var creature in snapshot.creatures) if (creature.speciesId == id) return creature;
            return null;
        }

        public static ServerSpeciesDefinition FindSpecies(ServerCatalog catalog, string id)
        {
            if (catalog?.species != null)
                foreach (var species in catalog.species) if (species.id == id) return species;
            return null;
        }

        private void SyncSpecies(ServerCatalog catalog, ServerAquariumState snapshot, bool canAct)
        {
            var species = catalog?.species;
            var count = species?.Length ?? 0;
            var rebuild = rows.Count != count;
            for (var i = 0; !rebuild && i < count; i++) rebuild = rows[i].id != species[i].id;
            if (rebuild)
            {
                foreach (var row in rows)
                {
                    row.button.gameObject.SetActive(false);
                    Destroy(row.button.gameObject);
                }
                rows.Clear();
                for (var i = 0; i < count; i++)
                {
                    var row = new SpeciesRow { id = species[i].id };
                    row.button = MakeButton("Online Species " + row.id, speciesContent, 4, i * 52, 254, 44, "", () =>
                    {
                        if (row.owned) onSelect(row.id);
                        else onAdopt(row.id);
                    });
                    row.label = row.button.GetComponentInChildren<Text>();
                    rows.Add(row);
                }
                speciesContent.sizeDelta = new Vector2(0, count * 52);
            }
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                row.owned = FindCreature(snapshot, row.id) != null;
                row.label.text = species[i].displayName + (row.owned ? "  /  VIEW" : "  /  " + species[i].price);
                row.button.interactable = row.owned || canAct;
            }
        }

        private void Update() => UpdateSafeArea();

        private void UpdateSafeArea()
        {
            if (safeRoot == null || Screen.width <= 0 || Screen.height <= 0) return;
            var area = Screen.safeArea;
            if (area == lastSafeArea && Screen.width == lastWidth && Screen.height == lastHeight) return;
            lastSafeArea = area;
            lastWidth = Screen.width;
            lastHeight = Screen.height;
            safeRoot.anchorMin = new Vector2(area.xMin / Screen.width, area.yMin / Screen.height);
            safeRoot.anchorMax = new Vector2(area.xMax / Screen.width, area.yMax / Screen.height);
        }

        private Button FractionButton(string name, Transform parent, int index, string text, Action action)
        {
            var button = MakeButton(name, parent, 0, 93, 0, 45, text, action);
            var rect = (RectTransform)button.transform;
            rect.anchorMin = new Vector2(index / 3f, 1);
            rect.anchorMax = new Vector2((index + 1) / 3f, 1);
            rect.offsetMin = new Vector2(8, -138);
            rect.offsetMax = new Vector2(-8, -93);
            return button;
        }

        private Button MakeButton(string name, Transform parent, float x, float y, float width, float height, string text, Action action)
        {
            var rect = BoxRect(name, parent, x, y, width, height);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(.10f, .27f, .30f);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.highlightedColor = colors.selectedColor = new Color(1.25f, 1.30f, 1.20f);
            colors.pressedColor = new Color(.65f, .95f, .9f);
            colors.disabledColor = new Color(.55f, .60f, .65f, .6f);
            button.colors = colors;
            button.onClick.AddListener(() => { if (button.interactable) action(); });
            var label = Label("Label", rect, text, 14, Color.white, 0, 0, width, height);
            label.alignment = TextAnchor.MiddleCenter;
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
            return button;
        }

        private Image Meter(string name, Transform parent, float x, float y, float width, Color color)
        {
            var rect = BoxRect(name, parent, x, y, width, 7);
            rect.gameObject.AddComponent<Image>().color = new Color(.13f, .24f, .27f);
            var fill = Rect("Fill", rect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).gameObject.AddComponent<Image>();
            fill.color = color;
            fill.raycastTarget = false;
            return fill;
        }

        private static void SetMeter(Image fill, double amount) => fill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01((float)amount), 1);

        private Text Label(string name, Transform parent, string text, int size, Color color, float x, float y, float width, float height, bool right = false)
        {
            var rect = BoxRect(name, parent, x, y, width, height);
            if (right) rect.anchorMin = rect.anchorMax = Vector2.one;
            var label = rect.gameObject.AddComponent<Text>();
            label.font = font;
            label.text = text;
            label.fontSize = size;
            label.color = color;
            label.raycastTarget = false;
            label.supportRichText = false;
            label.alignment = TextAnchor.UpperLeft;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            return label;
        }

        private static RectTransform BoxRect(string name, Transform parent, float x, float y, float width, float height) =>
            Rect(name, parent, new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, -y - height), new Vector2(x + width, -y));

        private static RectTransform Panel(string name, Transform parent, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax, Color color)
        {
            var rect = Rect(name, parent, min, max, offsetMin, offsetMax);
            rect.gameObject.AddComponent<Image>().color = color;
            return rect;
        }

        private static RectTransform Rect(string name, Transform parent, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            return rect;
        }
    }
}
