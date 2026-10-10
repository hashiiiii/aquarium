using System.Globalization;
using System;

namespace Aquarium.Outgame.Domain
{
    /// <summary>A validated projection of the authoritative aquarium snapshot.</summary>
    public sealed class AquariumModel
    {
        public string Revision { get; }
        public int PearlBalance { get; }
        public double Fullness { get; }
        public double Cleanliness { get; }
        public int CreatureCount { get; }

        public AquariumModel(string revision, string pearls, double fullness, double cleanliness, int creatureCount)
        {
            if (string.IsNullOrEmpty(revision)) throw new ArgumentException("Revision is required.", nameof(revision));
            if (!int.TryParse(pearls, NumberStyles.None, CultureInfo.InvariantCulture, out var pearlBalance) || pearlBalance < 0)
                throw new ArgumentException("Pearl balance must be a non-negative integer string.", nameof(pearls));
            if (!IsPercent(fullness)) throw new ArgumentOutOfRangeException(nameof(fullness), "Fullness must be between 0 and 100.");
            if (!IsPercent(cleanliness)) throw new ArgumentOutOfRangeException(nameof(cleanliness), "Cleanliness must be between 0 and 100.");
            if (creatureCount < 0) throw new ArgumentOutOfRangeException(nameof(creatureCount));

            Revision = revision;
            PearlBalance = pearlBalance;
            Fullness = fullness;
            Cleanliness = cleanliness;
            CreatureCount = creatureCount;
        }

        private static bool IsPercent(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0 && value <= 100;
        }
    }
}
