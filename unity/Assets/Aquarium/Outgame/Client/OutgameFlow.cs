using System;
using System.Threading;
using System.Threading.Tasks;
using Aquarium.Outgame.Application;
using Aquarium.Outgame.Domain;
using R3;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using VContainer;
using VContainer.Unity;

namespace Aquarium.Outgame.Client
{
    public enum OutgameAction
    {
        Start,
        Continue,
        BackToTitle,
        Retry
    }

    public readonly struct OutgameInputDto
    {
        public OutgameAction Action { get; }
        public OutgameInputDto(OutgameAction action) => Action = action;
    }

    public readonly struct OutgameViewDto
    {
        public string Title { get; }
        public string Status { get; }
        public string AquariumName { get; }
        public int Pearls { get; }
        public double Fullness { get; }
        public double Cleanliness { get; }
        public int Creatures { get; }
        public bool Busy { get; }

        public OutgameViewDto(string title, string status, string aquariumName,
            int pearls, double fullness, double cleanliness, int creatures, bool busy)
        {
            Title = title;
            Status = status;
            AquariumName = aquariumName;
            Pearls = pearls;
            Fullness = fullness;
            Cleanliness = cleanliness;
            Creatures = creatures;
            Busy = busy;
        }
    }

    public interface IPresenter
    {
        Observable<OutgameViewDto> State { get; }
        void Begin(string sceneName, CancellationToken cancellationToken);
        void Handle(OutgameInputDto input);
    }

    public sealed class OutgamePresenter : IPresenter, IDisposable
    {
        private readonly IGetAquariumUseCase getAquarium;
        private readonly Subject<OutgameViewDto> state = new();
        private CancellationToken cancellationToken;
        private OutgameViewDto dto;
        private bool hasStarted;

        public Observable<OutgameViewDto> State => state;

        public OutgamePresenter(IGetAquariumUseCase getAquarium)
        {
            this.getAquarium = getAquarium;
        }

        public void Begin(string sceneName, CancellationToken token)
        {
            if (hasStarted) return;
            hasStarted = true;
            cancellationToken = token;
            dto = new OutgameViewDto(sceneName == "Title" ? "FIRST REEF" : "MY REEF",
                sceneName == "Title" ? "A quiet place is waiting for you." : "",
                "", 0, 0, 0, 0, false);
            state.OnNext(dto);
            if (sceneName == "Home") _ = LoadAquariumAsync();
        }

        public void Handle(OutgameInputDto input)
        {
            if (cancellationToken.IsCancellationRequested) return;
            switch (input.Action)
            {
                case OutgameAction.Start:
                    Navigate("Home");
                    break;
                case OutgameAction.Continue:
                    if (!dto.Busy) Navigate("AquariumOnline");
                    break;
                case OutgameAction.BackToTitle:
                    Navigate("Title");
                    break;
                case OutgameAction.Retry:
                    if (!dto.Busy) _ = LoadAquariumAsync();
                    break;
            }
        }

        private void Navigate(string sceneName)
        {
            if (dto.Busy) return;
            dto = new OutgameViewDto(dto.Title, "Opening your reef…", dto.AquariumName,
                dto.Pearls, dto.Fullness, dto.Cleanliness, dto.Creatures, true);
            state.OnNext(dto);
            SceneManager.LoadScene(sceneName);
        }

        private async Task LoadAquariumAsync()
        {
            if (dto.Busy || cancellationToken.IsCancellationRequested) return;
            dto = new OutgameViewDto(dto.Title, "Connecting to your reef…", "", 0, 0, 0, 0, true);
            state.OnNext(dto);
            try
            {
                var model = await getAquarium.GetAquariumAsync(cancellationToken);
                if (cancellationToken.IsCancellationRequested) return;
                dto = new OutgameViewDto(dto.Title, "Your reef is ready",
                    "Your Aquarium", model.PearlBalance, model.Fullness, model.Cleanliness,
                    model.CreatureCount, false);
                state.OnNext(dto);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (Exception)
            {
                if (cancellationToken.IsCancellationRequested) return;
                dto = new OutgameViewDto(dto.Title,
                    "Couldn't reach the reef. Check the local server and retry.", "", 0, 0, 0, 0, false);
                state.OnNext(dto);
            }
        }

        public void Dispose() => state.Dispose();
    }

}
