package reef

import (
	"fmt"
	"math"
	"time"
)

func newState(now time.Time) State {
	return State{
		Version: SaveVersion, Revision: 1, LastUpdatedAt: now.UTC(),
		Pearls: 30, Fullness: 75, Cleanliness: 85,
		Creatures: []Creature{{SpeciesID: "tide_sprite"}},
	}
}

func cloneState(state State) State {
	state.Creatures = append([]Creature(nil), state.Creatures...)
	return state
}

func cloneResult(result Result) Result {
	result.State = cloneState(result.State)
	return result
}

// advance consumes the entire elapsed gap but simulates at most eight hours.
// Backwards clocks preserve the previous high-water timestamp and grant nothing.
func advance(state *State, now time.Time) AdvanceResult {
	now = now.UTC()
	if now.Before(state.LastUpdatedAt) {
		return AdvanceResult{ClockRolledBack: true}
	}
	// Unix seconds avoid time.Duration saturation for gaps longer than 292 years.
	elapsed := float64(now.Unix()-state.LastUpdatedAt.Unix()) +
		float64(now.Nanosecond()-state.LastUpdatedAt.Nanosecond())/1e9
	seconds := math.Min(elapsed, OfflineCapSeconds)
	hours := seconds / 3600
	careHours := integrateCare(state.Fullness, state.Cleanliness, hours)
	for i := range state.Creatures {
		creature := &state.Creatures[i]
		definition, _ := species(creature.SpeciesID)
		creature.GrowthHours = math.Min(definition.MatureAfterCareHours, creature.GrowthHours+careHours)
		creature.PendingPearls = math.Min(RewardCapacity, creature.PendingPearls+careHours*definition.PearlsPerCareHour)
	}
	state.Fullness = math.Max(0, state.Fullness-FullnessLossPerHour*hours)
	state.Cleanliness = math.Max(0, state.Cleanliness-CleanlinessLossPerHour*hours)
	state.LastUpdatedAt = now
	return AdvanceResult{ElapsedSeconds: elapsed, SimulatedSeconds: seconds, WasCapped: elapsed > seconds}
}

func integrateCare(fullness, cleanliness, hours float64) float64 {
	end := math.Min(hours, math.Min(
		(fullness-CareThreshold)/FullnessLossPerHour,
		(cleanliness-CareThreshold)/CleanlinessLossPerHour))
	if end <= 0 {
		return 0
	}
	crossing := (fullness - cleanliness) / (FullnessLossPerHour - CleanlinessLossPerHour)
	if crossing > 0 && crossing < end {
		return integrateSegment(fullness, cleanliness, 0, crossing) + integrateSegment(fullness, cleanliness, crossing, end)
	}
	return integrateSegment(fullness, cleanliness, 0, end)
}

func integrateSegment(fullness, cleanliness, start, end float64) float64 {
	a := math.Min(fullness-FullnessLossPerHour*start, cleanliness-CleanlinessLossPerHour*start)
	b := math.Min(fullness-FullnessLossPerHour*end, cleanliness-CleanlinessLossPerHour*end)
	return (a + b) * (end - start) / 200
}

func perform(state *State, command Command) (bool, string, int) {
	switch command.Action {
	case ActionFeed:
		if state.Fullness >= 100 {
			return false, "Everyone is already well fed.", 0
		}
		state.Fullness = math.Min(100, state.Fullness+25)
		return true, "A little sea-magic snack. Fullness restored.", 0
	case ActionClean:
		if state.Cleanliness >= 100 {
			return false, "The water is already sparkling.", 0
		}
		state.Cleanliness = math.Min(100, state.Cleanliness+30)
		return true, "The water is sparkling again.", 0
	case ActionCollect:
		collected := 0
		for i := range state.Creatures {
			creature := &state.Creatures[i]
			amount := min(int(math.Floor(creature.PendingPearls)), WalletCapacity-state.Pearls)
			creature.PendingPearls -= float64(amount)
			state.Pearls += amount
			collected += amount
		}
		if collected > 0 {
			return true, fmt.Sprintf("Collected %d pearls.", collected), collected
		}
		if state.Pearls == WalletCapacity {
			return false, "Your pearl pouch is full.", 0
		}
		return false, "No whole pearls ready yet.", 0
	case ActionAdopt:
		definition, ok := species(command.SpeciesID)
		if !ok {
			return false, "Unknown creature.", 0
		}
		for _, creature := range state.Creatures {
			if creature.SpeciesID == command.SpeciesID {
				return false, "This friend already lives here.", 0
			}
		}
		if len(state.Creatures) >= Capacity {
			return false, "Your sanctuary is full.", 0
		}
		if state.Pearls < definition.Price {
			return false, "Not enough pearls yet.", 0
		}
		state.Pearls -= definition.Price
		state.Creatures = append(state.Creatures, Creature{SpeciesID: definition.ID})
		return true, "Welcome, " + definition.DisplayName + "!", definition.Price
	default:
		// The service validates commands before entering a transaction.
		return false, "Unknown action.", 0
	}
}

func validNumber(value, low, high float64) bool {
	return !math.IsNaN(value) && !math.IsInf(value, 0) && value >= low && value <= high
}

func validTime(value time.Time) bool {
	return !value.IsZero() && value.Year() >= 1 && value.Year() <= 9999
}

func validateState(state State) error {
	if state.Version != SaveVersion || state.Revision == 0 || !validTime(state.LastUpdatedAt) {
		return fmt.Errorf("%w: unsupported state version, revision or timestamp", ErrCorrupt)
	}
	if state.Pearls < 0 || state.Pearls > WalletCapacity ||
		!validNumber(state.Fullness, 0, 100) || !validNumber(state.Cleanliness, 0, 100) {
		return fmt.Errorf("%w: invalid wallet or care levels", ErrCorrupt)
	}
	if len(state.Creatures) < 1 || len(state.Creatures) > Capacity {
		return fmt.Errorf("%w: invalid creature count", ErrCorrupt)
	}
	seen := make(map[string]bool, Capacity)
	for _, creature := range state.Creatures {
		definition, ok := species(creature.SpeciesID)
		if !ok || seen[creature.SpeciesID] ||
			!validNumber(creature.GrowthHours, 0, definition.MatureAfterCareHours) ||
			!validNumber(creature.PendingPearls, 0, RewardCapacity) {
			return fmt.Errorf("%w: invalid creature data", ErrCorrupt)
		}
		seen[creature.SpeciesID] = true
	}
	return nil
}
