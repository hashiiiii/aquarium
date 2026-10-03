using System;
using System.Collections.Generic;

namespace Aquarium.Core
{
    /// <summary>Deterministic care simulation. All timestamps must explicitly be UTC.</summary>
    public static class AquariumSimulation
    {
        public const int SaveVersion = 1;
        public const int Capacity = 3;
        public const int WalletCapacity = 9999;
        public const double RewardCapacity = 100;
        public const double OfflineCapSeconds = 8 * 60 * 60;
        public const double FullnessLossPerHour = 6;
        public const double CleanlinessLossPerHour = 3;
        public const double CareThreshold = 20;

        public static AquariumState CreateNew(DateTime utcNow)
        {
            RequireUtc(utcNow);
            return new AquariumState
            {
                version = SaveVersion,
                lastUpdatedUtcTicks = utcNow.Ticks,
                pearls = 30,
                fullness = 75,
                cleanliness = 85,
                creatures = new List<CreatureState>
                {
                    new CreatureState { speciesId = "tide_sprite" }
                }
            };
        }

        /// <summary>
        /// Simulates up to eight hours since the last timestamp, then consumes the whole gap.
        /// A backwards clock does not move the high-water timestamp, preventing repeated rewards.
        /// For active play, call regularly; do not also advance elapsed time elsewhere.
        /// </summary>
        public static AdvanceResult AdvanceTo(AquariumState state, DateTime utcNow)
        {
            RequireValid(state);
            RequireUtc(utcNow);
            if (utcNow.Ticks < state.lastUpdatedUtcTicks)
                return new AdvanceResult { clockRolledBack = true };

            var elapsed = (utcNow.Ticks - state.lastUpdatedUtcTicks) / (double)TimeSpan.TicksPerSecond;
            var seconds = Math.Min(elapsed, OfflineCapSeconds);
            var hours = seconds / 3600.0;
            var careHours = IntegrateCare(state.fullness, state.cleanliness, hours);
            foreach (var creature in state.creatures)
            {
                var species = SpeciesCatalog.Get(creature.speciesId);
                creature.growthHours = Math.Min(species.matureAfterCareHours, creature.growthHours + careHours);
                creature.pendingPearls = Math.Min(RewardCapacity,
                    creature.pendingPearls + careHours * species.pearlsPerCareHour);
            }
            state.fullness = Math.Max(0, state.fullness - FullnessLossPerHour * hours);
            state.cleanliness = Math.Max(0, state.cleanliness - CleanlinessLossPerHour * hours);
            state.lastUpdatedUtcTicks = utcNow.Ticks;
            return new AdvanceResult
            {
                elapsedSeconds = elapsed, simulatedSeconds = seconds, wasCapped = elapsed > seconds
            };
        }

        // Exact integral of min(fullness, cleanliness)/100 until either reaches the rest threshold.
        // Splitting at the line intersection makes the result independent of update frequency.
        private static double IntegrateCare(double fullness, double cleanliness, double hours)
        {
            var end = Math.Min(hours, Math.Min(
                (fullness - CareThreshold) / FullnessLossPerHour,
                (cleanliness - CareThreshold) / CleanlinessLossPerHour));
            if (end <= 0) return 0;
            var crossing = (fullness - cleanliness) / (FullnessLossPerHour - CleanlinessLossPerHour);
            if (crossing > 0 && crossing < end)
                return IntegrateSegment(fullness, cleanliness, 0, crossing)
                    + IntegrateSegment(fullness, cleanliness, crossing, end);
            return IntegrateSegment(fullness, cleanliness, 0, end);
        }

        private static double IntegrateSegment(double fullness, double cleanliness, double start, double end)
        {
            var a = Math.Min(fullness - FullnessLossPerHour * start, cleanliness - CleanlinessLossPerHour * start);
            var b = Math.Min(fullness - FullnessLossPerHour * end, cleanliness - CleanlinessLossPerHour * end);
            return (a + b) * (end - start) / 200.0;
        }

        public static ActionResult Feed(AquariumState state)
        {
            RequireValid(state);
            if (state.fullness >= 100) return new ActionResult(false, "Everyone is already well fed.");
            state.fullness = Math.Min(100, state.fullness + 25);
            return new ActionResult(true, "A little sea-magic snack. Fullness restored.");
        }

        public static ActionResult Clean(AquariumState state)
        {
            RequireValid(state);
            if (state.cleanliness >= 100) return new ActionResult(false, "The water is already sparkling.");
            state.cleanliness = Math.Min(100, state.cleanliness + 30);
            return new ActionResult(true, "The water is sparkling again.");
        }

        public static ActionResult Collect(AquariumState state)
        {
            RequireValid(state);
            var collected = 0;
            foreach (var creature in state.creatures)
            {
                var available = (int)Math.Floor(creature.pendingPearls);
                var amount = Math.Min(available, WalletCapacity - state.pearls);
                creature.pendingPearls -= amount;
                state.pearls += amount;
                collected += amount;
            }
            return collected > 0
                ? new ActionResult(true, "Collected " + collected + " pearls.", collected)
                : new ActionResult(false, state.pearls == WalletCapacity ? "Your pearl pouch is full." : "No whole pearls ready yet.");
        }

        public static ActionResult Acquire(AquariumState state, string speciesId)
        {
            RequireValid(state);
            var species = SpeciesCatalog.Get(speciesId);
            if (species == null) return new ActionResult(false, "Unknown creature.");
            foreach (var creature in state.creatures)
                if (creature.speciesId == speciesId) return new ActionResult(false, "This friend already lives here.");
            if (state.creatures.Count >= Capacity) return new ActionResult(false, "Your sanctuary is full.");
            if (state.pearls < species.price) return new ActionResult(false, "Not enough pearls yet.");
            state.pearls -= species.price;
            state.creatures.Add(new CreatureState { speciesId = speciesId });
            return new ActionResult(true, "Welcome, " + species.displayName + "!", species.price);
        }

        public static bool Validate(AquariumState state, out string error)
        {
            error = null;
            if (state == null) error = "Save is empty.";
            else if (state.version != SaveVersion) error = "Unsupported save version.";
            else if (state.lastUpdatedUtcTicks <= DateTime.MinValue.Ticks || state.lastUpdatedUtcTicks > DateTime.MaxValue.Ticks)
                error = "Invalid UTC timestamp.";
            else if (state.pearls < 0 || state.pearls > WalletCapacity) error = "Invalid pearl balance.";
            else if (!InRange(state.fullness, 0, 100) || !InRange(state.cleanliness, 0, 100)) error = "Invalid care levels.";
            else if (state.creatures == null || state.creatures.Count < 1 || state.creatures.Count > Capacity) error = "Invalid creature count.";
            else
            {
                var ids = new HashSet<string>();
                foreach (var creature in state.creatures)
                {
                    var species = creature == null ? null : SpeciesCatalog.Get(creature.speciesId);
                    if (species == null || !ids.Add(creature.speciesId)
                        || !InRange(creature.growthHours, 0, species.matureAfterCareHours)
                        || !InRange(creature.pendingPearls, 0, RewardCapacity))
                    {
                        error = "Invalid creature data.";
                        break;
                    }
                }
            }
            return error == null;
        }

        private static bool InRange(double value, double min, double max)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value >= min && value <= max;
        }

        private static void RequireValid(AquariumState state)
        {
            if (!Validate(state, out var error)) throw new ArgumentException(error, nameof(state));
        }

        internal static void RequireUtc(DateTime time)
        {
            if (time.Kind != DateTimeKind.Utc || time.Ticks == 0)
                throw new ArgumentException("A nonzero, explicitly UTC timestamp is required.", nameof(time));
        }
    }
}
