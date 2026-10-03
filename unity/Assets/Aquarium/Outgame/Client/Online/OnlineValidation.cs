using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Aquarium.Online
{
    internal static class OnlineValidation
    {
        internal static bool IsIdentifier(string value, int maximum = 128)
        {
            if (string.IsNullOrEmpty(value) || value.Length > maximum) return false;
            foreach (var character in value)
                if (!(character >= 'a' && character <= 'z') && !(character >= 'A' && character <= 'Z') &&
                    !(character >= '0' && character <= '9') && character != '_' && character != '-') return false;
            return true;
        }

        internal static ulong Revision(string value)
        {
            if (!ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var revision) || revision == 0)
                throw new InvalidDataException("Invalid server revision.");
            return revision;
        }

        internal static string NonnegativeInteger(string value, string field)
        {
            // Proto3 JSON legitimately omits zero fields.
            value = value ?? "0";
            if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number < 0)
                throw new InvalidDataException("Invalid server integer: " + field);
            return number.ToString(CultureInfo.InvariantCulture);
        }

        internal static void Nonnegative(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
                throw new InvalidDataException("Invalid server number.");
        }

        internal static void ValidateState(ServerAquariumState state)
        {
            if (state == null || state.version != 1) throw new InvalidDataException("Unsupported aquarium state version.");
            Revision(state.revision);
            state.pearls = NonnegativeInteger(state.pearls, "pearls");
            if (!long.TryParse(state.lastUpdatedAtUnixMs, NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture, out var timestamp) || timestamp < -62135596800000L || timestamp > 253402300799999L)
                throw new InvalidDataException("Invalid server timestamp.");
            Nonnegative(state.fullness);
            Nonnegative(state.cleanliness);
            if (state.fullness > 100 || state.cleanliness > 100) throw new InvalidDataException("Invalid care values.");
            state.creatures = state.creatures ?? Array.Empty<ServerCreature>();
            if (state.creatures.Length > 1024) throw new InvalidDataException("Too many creatures.");
            var identities = new HashSet<string>(StringComparer.Ordinal);
            foreach (var creature in state.creatures)
            {
                if (creature == null || !IsIdentifier(creature.speciesId) || !identities.Add(creature.speciesId))
                    throw new InvalidDataException("Invalid creature identity.");
                Nonnegative(creature.growthHours);
                Nonnegative(creature.pendingPearls);
            }
        }

        internal static void ValidateCatalog(ServerCatalog catalog)
        {
            if (catalog?.rules == null || catalog.species == null || catalog.species.Length == 0 || catalog.species.Length > 1024)
                throw new InvalidDataException("Missing server catalog.");
            var identities = new HashSet<string>(StringComparer.Ordinal);
            foreach (var species in catalog.species)
            {
                if (species == null || !IsIdentifier(species.id) || !identities.Add(species.id) ||
                    string.IsNullOrWhiteSpace(species.displayName) || species.displayName.Length > 256)
                    throw new InvalidDataException("Invalid catalog species.");
                species.price = NonnegativeInteger(species.price, "price");
                Nonnegative(species.pearlsPerCareHour);
                Nonnegative(species.matureAfterCareHours);
                if (species.matureAfterCareHours == 0) throw new InvalidDataException("Invalid maturity duration.");
            }
            var rules = catalog.rules;
            if (rules.capacity == 0 || rules.capacity > 1024 || rules.replayCapacity == 0)
                throw new InvalidDataException("Invalid catalog capacity.");
            rules.walletCapacity = NonnegativeInteger(rules.walletCapacity, "walletCapacity");
            Nonnegative(rules.rewardCapacity);
            Nonnegative(rules.offlineCapSeconds);
            Nonnegative(rules.fullnessLossPerHour);
            Nonnegative(rules.cleanlinessLossPerHour);
            Nonnegative(rules.careThreshold);
            if (rules.careThreshold > 100) throw new InvalidDataException("Invalid care threshold.");
        }

        internal static void ValidateAgainstCatalog(ServerAquariumState state, ServerCatalog catalog)
        {
            if (catalog == null) throw new InvalidDataException("Catalog is required before accepting state.");
            if (state.creatures.Length > catalog.rules.capacity ||
                long.Parse(state.pearls, CultureInfo.InvariantCulture) > long.Parse(catalog.rules.walletCapacity, CultureInfo.InvariantCulture))
                throw new InvalidDataException("State exceeds catalog capacity.");
            foreach (var creature in state.creatures)
            {
                bool found = false;
                foreach (var species in catalog.species) if (species.id == creature.speciesId) { found = true; break; }
                if (!found || creature.pendingPearls > catalog.rules.rewardCapacity)
                    throw new InvalidDataException("State does not match server catalog.");
            }
        }

        internal static void ValidateCommand(ServerApplyCommandRequest command)
        {
            if (command == null || !IsIdentifier(command.requestId)) throw new InvalidDataException("Invalid pending command ID.");
            Revision(command.expectedRevision);
            switch (command.action)
            {
                case "ACTION_FEED": case "ACTION_CLEAN": case "ACTION_COLLECT":
                    if (!string.IsNullOrEmpty(command.speciesId)) throw new InvalidDataException("Unexpected pending species.");
                    break;
                case "ACTION_ADOPT":
                    if (!IsIdentifier(command.speciesId)) throw new InvalidDataException("Invalid pending species.");
                    break;
                default: throw new InvalidDataException("Invalid pending action.");
            }
        }
    }
}
