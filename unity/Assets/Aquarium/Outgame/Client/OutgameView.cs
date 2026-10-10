using System;
using R3;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Aquarium.Outgame.Client
{
    /// <summary>Passive UI adapter: it forwards an input DTO and renders presenter DTOs.</summary>
    public sealed class OutgameView : MonoBehaviour
    {
        private Text title;
        private Text status;
        private Text aquarium;
        private Text details;
        private Button[] buttons;

        public void Bind(IPresenter presenter, Text titleLabel, Text statusLabel,
            Text aquariumLabel, Text detailsLabel, Button[] actionButtons,
            OutgameAction[] actionMappings, CompositeDisposable subscriptions)
        {
            title = titleLabel;
            status = statusLabel;
            aquarium = aquariumLabel;
            details = detailsLabel;
            buttons = actionButtons;
            for (var index = 0; index < buttons.Length; index++)
            {
                var action = actionMappings[index];
                UnityAction listener = () => presenter.Handle(new OutgameInputDto(action));
                buttons[index].onClick.AddListener(listener);
                subscriptions.Add(new ButtonInputSubscription(buttons[index], listener));
            }
            presenter.State.Subscribe(Render).AddTo(subscriptions);
        }

        private void Render(OutgameViewDto dto)
        {
            title.text = dto.Title;
            status.text = dto.Status;
            aquarium.text = dto.AquariumName;
            details.text = dto.Creatures > 0
                ? $"{dto.Pearls} pearls   ·   {dto.Creatures} companions\nCARE   {dto.Fullness:0}% / {dto.Cleanliness:0}%"
                : string.Empty;
            foreach (var button in buttons) button.interactable = !dto.Busy;
        }

        private sealed class ButtonInputSubscription : IDisposable
        {
            private readonly Button button;
            private readonly UnityAction listener;

            public ButtonInputSubscription(Button button, UnityAction listener)
            {
                this.button = button;
                this.listener = listener;
            }

            public void Dispose() => button.onClick.RemoveListener(listener);
        }
    }
}
