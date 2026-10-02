using System;
using System.Collections.Generic;

namespace Aquarium.Core
{
    [Serializable]
    public sealed class AquariumState
    {
        public int version;
        public long lastUpdatedUtcTicks;
        public int pearls;
        public double fullness;
        public double cleanliness;
        public List<CreatureState> creatures;
    }

    [Serializable]
    public sealed class CreatureState
    {
        public string speciesId;
        public double growthHours;
        public double pendingPearls;
    }

    public sealed class SpeciesDefinition
    {
        public readonly string id;
        public readonly string displayName;
        public readonly int price;
        public readonly double pearlsPerCareHour;
        public readonly double matureAfterCareHours;

        public SpeciesDefinition(string id, string displayName, int price,
            double pearlsPerCareHour, double matureAfterCareHours)
        {
            this.id = id;
            this.displayName = displayName;
            this.price = price;
            this.pearlsPerCareHour = pearlsPerCareHour;
            this.matureAfterCareHours = matureAfterCareHours;
        }
    }

    public static class SpeciesCatalog
    {
        private static readonly SpeciesDefinition[] Species =
        {
            new SpeciesDefinition("tide_sprite", "Tide Sprite", 0, 8, 12),
            new SpeciesDefinition("moon_jelly", "Moon Jelly", 30, 10, 16),
            new SpeciesDefinition("coral_drake", "Coral Drake", 60, 14, 24)
        };

        public static IReadOnlyList<SpeciesDefinition> All { get; } = Array.AsReadOnly(Species);

        public static SpeciesDefinition Get(string id)
        {
            foreach (var species in Species)
                if (species.id == id) return species;
            return null;
        }
    }

    public struct ActionResult
    {
        public bool success;
        public string message;
        public int amount;

        internal ActionResult(bool success, string message, int amount = 0)
        {
            this.success = success;
            this.message = message;
            this.amount = amount;
        }
    }

    public struct AdvanceResult
    {
        public double elapsedSeconds;
        public double simulatedSeconds;
        public bool clockRolledBack;
        public bool wasCapped;
    }
}
