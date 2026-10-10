using System;
using Aquarium.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Aquarium.Runtime
{
    /// <summary>Small, mouse/touch/keyboard-friendly prototype HUD. All controls are real uGUI buttons.</summary>
    public sealed class AquariumHud : MonoBehaviour
    {
        private static readonly Color Ink = new Color(0.025f, 0.075f, 0.11f, 0.94f);
        private static readonly Color Muted = new Color(0.59f, 0.76f, 0.77f);
        private static readonly Color Pearl = new Color(0.95f, 0.86f, 0.65f);
        private static readonly Color Aqua = new Color(0.34f, 0.83f, 0.77f);
        private Font font;
        private RectTransform safeRoot;
        private GameObject canvasObject;
        private GameObject eventSystemObject;
        private Text balance;
        private Text status;
        private Text creatureTitle;
        private Text creatureDetail;
        private Text fullness;
        private Text cleanliness;
        private Text collectLabel;
        private Image fullnessBar;
        private Image cleanlinessBar;
        private Image growthBar;
        private Button feed;
        private Button clean;
        private Button collect;
        private readonly Button[] speciesButtons = new Button[3];
        private readonly Text[] speciesLabels = new Text[3];
        private Rect lastSafeArea;
        private int lastWidth;
        private int lastHeight;

        public void Initialize(Action onFeed, Action onClean, Action onCollect, Action<string> onAdopt, Action<string> onSelect)
        {
            if (canvasObject != null) return;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            canvasObject = new GameObject("Aquarium HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
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
                eventSystemObject = new GameObject("Aquarium Event System", typeof(EventSystem), typeof(InputSystemUIInputModule));
                eventSystemObject.transform.SetParent(transform, false);
            }

            var header = Panel("Header", safeRoot, new Vector2(0, 1), Vector2.one, new Vector2(24, -94), new Vector2(-24, -20), Ink);
            Label("Eyebrow", header, "A Q U A R I U M   /   OFFLINE FIRST REEF", 13, Aqua, 20, 9, 670, 21);
            Label("Title", header, "A little world, growing with you.", 26, Color.white, 20, 30, 760, 37);
            balance = Label("Pearl balance", header, "30  PEARLS", 22, Pearl, -270, 20, 245, 36, true);
            balance.alignment = TextAnchor.MiddleRight;

            var journal = Panel("Creature journal", safeRoot, Vector2.one, Vector2.one, new Vector2(-310, -345), new Vector2(-24, -110), Ink);
            Label("Collection title", journal, "REEF COMPANIONS", 14, Aqua, 18, 10, 250, 24);
            Label("Collection hint", journal, "Adopt once. Care for them together.", 13, Muted, 18, 34, 250, 24);
            for (var i = 0; i < SpeciesCatalog.All.Count; i++)
            {
                var id = SpeciesCatalog.All[i].id;
                var button = MakeButton("Species " + id, journal, 16, 63 + i * 52, 254, 44, "", () => onAdopt(id));
                speciesButtons[i] = button;
                speciesLabels[i] = button.GetComponentInChildren<Text>();
                // Listener switches between inspection and adoption without rebuilding the UI.
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() =>
                {
                    if (button.gameObject.name.EndsWith(" Owned", StringComparison.Ordinal)) onSelect(id);
                    else onAdopt(id);
                });
            }

            var dock = Panel("Care dock", safeRoot, Vector2.zero, new Vector2(1, 0), new Vector2(24, 24), new Vector2(-24, 226), Ink);
            creatureTitle = Label("Selected creature", dock, "Tide Sprite", 25, Pearl, 22, 12, 320, 34);
            creatureDetail = Label("Growth details", dock, "A young companion", 14, Muted, 22, 52, 338, 45);
            creatureDetail.verticalOverflow = VerticalWrapMode.Overflow;
            growthBar = Meter("Growth", dock, 22, 106, 320, new Color(0.64f, 0.63f, 0.92f));
            Label("Gentle care hint", dock, "No deaths. Care lets your reef grow.\nProgress continues offline, up to 8 hours.", 13, Muted, 22, 129, 350, 48);

            var controls = Rect("Care controls", dock, new Vector2(0.32f, 0), Vector2.one, new Vector2(10, 0), new Vector2(-18, 0));
            fullness = Label("Fullness", controls, "FULLNESS  100%", 14, Aqua, 8, 15, 260, 26);
            cleanliness = Label("Cleanliness", controls, "WATER  100%", 14, Aqua, 8, 53, 260, 26);
            fullnessBar = Meter("Fullness meter", controls, 270, 23, 320, Aqua);
            cleanlinessBar = Meter("Water meter", controls, 270, 61, 320, Aqua);
            feed = FractionButton("Feed", controls, 0, "FEED  +25", onFeed);
            clean = FractionButton("Clean", controls, 1, "CLEAN  +30", onClean);
            collect = FractionButton("Collect", controls, 2, "COLLECT  0", onCollect);
            collectLabel = collect.GetComponentInChildren<Text>();
            status = Label("Status", controls, "", 14, Pearl, 8, 145, 735, 49);
            status.horizontalOverflow = HorizontalWrapMode.Wrap;
            status.verticalOverflow = VerticalWrapMode.Truncate;
            var statusRect = status.rectTransform;
            statusRect.anchorMax = new Vector2(1, 1);
            statusRect.offsetMax = new Vector2(-8, -145);
            statusRect.offsetMin = new Vector2(8, -194);
            UpdateSafeArea();
            if (eventSystemObject != null) EventSystem.current.SetSelectedGameObject(feed.gameObject);
        }

        public void Refresh(AquariumState state, string selectedSpecies, string message)
        {
            balance.text = state.pearls + "  PEARLS";
            fullness.text = "FULLNESS  " + Mathf.RoundToInt((float)state.fullness) + "%";
            cleanliness.text = "WATER  " + Mathf.RoundToInt((float)state.cleanliness) + "%";
            SetMeter(fullnessBar, state.fullness / 100);
            SetMeter(cleanlinessBar, state.cleanliness / 100);
            feed.interactable = state.fullness < 99.999;
            clean.interactable = state.cleanliness < 99.999;
            var collectable = 0;
            foreach (var creature in state.creatures)
            {
                collectable += (int)Math.Floor(creature.pendingPearls);
                if (creature.speciesId != selectedSpecies) continue;
                var species = SpeciesCatalog.Get(creature.speciesId);
                var progress = Math.Min(1, creature.growthHours / species.matureAfterCareHours);
                creatureTitle.text = species.displayName;
                creatureDetail.text = (progress >= 1 ? "Fully grown" : "Growing  " + Math.Floor(progress * 100) + "%") +
                    "  /  " + species.pearlsPerCareHour + " pearls per care hour\n" +
                    (state.fullness > AquariumSimulation.CareThreshold && state.cleanliness > AquariumSimulation.CareThreshold ? "Comfortable and exploring" : "Resting. Feed and clean to resume growth.");
                SetMeter(growthBar, progress);
            }
            collectLabel.text = "COLLECT  " + collectable;
            collect.interactable = collectable > 0 && state.pearls < AquariumSimulation.WalletCapacity;
            for (var i = 0; i < speciesButtons.Length; i++)
            {
                var species = SpeciesCatalog.All[i];
                var owned = false;
                foreach (var creature in state.creatures) if (creature.speciesId == species.id) owned = true;
                speciesLabels[i].text = owned ? species.displayName + "   /   VIEW" : species.displayName + "   /   " + species.price;
                speciesButtons[i].gameObject.name = "Species " + species.id + (owned ? " Owned" : "");
                speciesButtons[i].interactable = owned || state.pearls >= species.price;
            }
            status.text = message;
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
            image.color = new Color(0.10f, 0.27f, 0.30f);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.25f, 1.30f, 1.20f);
            colors.selectedColor = new Color(1.25f, 1.30f, 1.20f);
            colors.pressedColor = new Color(0.65f, 0.95f, 0.9f);
            colors.disabledColor = new Color(0.55f, 0.60f, 0.65f, 0.6f);
            button.colors = colors;
            button.onClick.AddListener(() => action());
            var label = Label("Label", rect, text, 15, Color.white, 0, 0, width, height);
            label.alignment = TextAnchor.MiddleCenter;
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = Vector2.zero;
            return button;
        }

        private Image Meter(string name, Transform parent, float x, float y, float width, Color color)
        {
            var rect = BoxRect(name, parent, x, y, width, 7);
            rect.gameObject.AddComponent<Image>().color = new Color(0.13f, 0.24f, 0.27f);
            var fill = Rect("Fill", rect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).gameObject.AddComponent<Image>();
            fill.color = color;
            fill.raycastTarget = false;
            return fill;
        }

        private static void SetMeter(Image fill, double amount)
        {
            fill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01((float)amount), 1);
        }

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
            label.alignment = TextAnchor.UpperLeft;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            return label;
        }

        private static RectTransform BoxRect(string name, Transform parent, float x, float y, float width, float height)
        {
            return Rect(name, parent, new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, -y - height), new Vector2(x + width, -y));
        }

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
