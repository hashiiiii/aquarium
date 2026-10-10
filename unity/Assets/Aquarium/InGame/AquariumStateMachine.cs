using System;

namespace Aquarium.InGame
{
    public enum AquariumMode
    {
        Entering,
        Swimming,
        Leaving,
        Closed
    }

    /// <summary>Plain state machine; OnlineAquariumGame is its only Unity Update entry point.</summary>
    public sealed class AquariumStateMachine
    {
        private readonly Action leaveAquarium;
        private AquariumMode mode = AquariumMode.Entering;

        public AquariumMode Mode => mode;

        public AquariumStateMachine(Action leaveAquarium)
        {
            this.leaveAquarium = leaveAquarium ?? throw new ArgumentNullException(nameof(leaveAquarium));
        }

        public void Update(bool hasServerSnapshot, bool escapePressed)
        {
            switch (mode)
            {
                case AquariumMode.Entering:
                    Enter(hasServerSnapshot);
                    break;
                case AquariumMode.Swimming:
                    Swim(escapePressed);
                    break;
                case AquariumMode.Leaving:
                    Leave();
                    break;
            }
        }

        private void Enter(bool hasServerSnapshot)
        {
            if (hasServerSnapshot) mode = AquariumMode.Swimming;
        }

        private void Swim(bool escapePressed)
        {
            if (escapePressed) mode = AquariumMode.Leaving;
        }

        private void Leave()
        {
            mode = AquariumMode.Closed;
            leaveAquarium();
        }
    }
}
