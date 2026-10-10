using System;
using System.Threading;
using Aquarium.Outgame.Application;
using R3;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using VContainer;
using VContainer.Unity;

namespace Aquarium.Outgame.Client
{
    /// <summary>One scene composition root for each outgame scene.</summary>
    public sealed class OutgameSceneRoot : LifetimeScope
    {
        [SerializeField] private OutgameView view;
        [SerializeField] private Text titleText;
        [SerializeField] private Text statusText;
        [SerializeField] private Text aquariumText;
        [SerializeField] private Text detailsText;
        [SerializeField] private RectTransform buttonRoot;
        private Button[] actionButtons;

        private readonly CompositeDisposable subscriptions = new();
        private CancellationTokenSource lifetime;

        public void UseGlobalParent()
        {
            parentReference = ParentReference.Create<GlobalLifetimeScope>();
        }

        protected override void Awake()
        {
            if (GlobalLifetimeScope.Instance != null)
                parentReference.Object = GlobalLifetimeScope.Instance;
            base.Awake();
        }

        protected override void Configure(IContainerBuilder builder)
        {
            builder.Register<OutgamePresenter>(Lifetime.Scoped).As<IPresenter>();
            builder.Register<GetAquariumUseCase>(Lifetime.Scoped).As<IGetAquariumUseCase>();
        }

        private void Start()
        {
            lifetime = new CancellationTokenSource();
            var presenter = Container.Resolve<IPresenter>();
            EnsureView();
            var actions = SceneManager.GetActiveScene().name == "Title"
                ? new[] { OutgameAction.Start }
                : new[] { OutgameAction.Continue, OutgameAction.Retry, OutgameAction.BackToTitle };
            view.Bind(presenter, titleText, statusText, aquariumText, detailsText,
                actionButtons, actions, subscriptions);
            presenter.Begin(SceneManager.GetActiveScene().name, lifetime.Token);
        }

        private void EnsureView()
        {
            if (view != null) return;
            var canvasObject = new GameObject("Outgame Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            if (FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
                new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem),
                    typeof(InputSystemUIInputModule));

            titleText = CreateLabel(canvasObject.transform, "Title", new Vector2(0, 160), 38);
            statusText = CreateLabel(canvasObject.transform, "Status", new Vector2(0, 75), 18);
            aquariumText = CreateLabel(canvasObject.transform, "Aquarium", new Vector2(0, 30), 24);
            detailsText = CreateLabel(canvasObject.transform, "Details", new Vector2(0, -8), 18);
            buttonRoot = canvasObject.GetComponent<RectTransform>();
            view = gameObject.AddComponent<OutgameView>();

            var sceneName = SceneManager.GetActiveScene().name;
            if (sceneName == "Title")
                actionButtons = new[] { CreateButton(buttonRoot, "ENTER", new Vector2(0, -100)) };
            else
            {
                actionButtons = new[]
                {
                    CreateButton(buttonRoot, "ENTER AQUARIUM", new Vector2(0, -100)),
                    CreateButton(buttonRoot, "RETRY", new Vector2(0, -154)),
                    CreateButton(buttonRoot, "TITLE", new Vector2(0, -208))
                };
            }
        }

        private static Text CreateLabel(Transform parent, string objectName, Vector2 position, int fontSize)
        {
            var labelObject = new GameObject(objectName, typeof(RectTransform), typeof(Text));
            labelObject.transform.SetParent(parent, false);
            var rect = (RectTransform)labelObject.transform;
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(700, 54);
            var text = labelObject.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            return text;
        }

        private Button CreateButton(Transform parent, string label, Vector2 position)
        {
            var buttonObject = new GameObject(label + " Button", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            var rect = (RectTransform)buttonObject.transform;
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(260, 48);
            buttonObject.GetComponent<Image>().color = new Color(.05f, .38f, .55f, .92f);
            var buttonText = CreateLabel(buttonObject.transform, "Label", Vector2.zero, 16);
            buttonText.text = label;
            buttonText.rectTransform.sizeDelta = rect.sizeDelta;
            return buttonObject.GetComponent<Button>();
        }

        protected override void OnDestroy()
        {
            lifetime?.Cancel();
            subscriptions.Dispose();
            lifetime?.Dispose();
            base.OnDestroy();
        }
    }
}
