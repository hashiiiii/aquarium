package reef

import (
	"errors"
	"math"
	"reflect"
	"testing"
	"time"
)

var testStart = time.Date(2026, time.October, 2, 12, 0, 0, 0, time.UTC)

func near(t *testing.T, got, want float64) {
	t.Helper()
	if math.Abs(got-want) > 1e-9 {
		t.Fatalf("got %.15g, want %.15g", got, want)
	}
}

func TestNewAquariumAndCatalogMatchUnity(t *testing.T) {
	state := newState(testStart)
	if err := validateState(state); err != nil {
		t.Fatal(err)
	}
	if state.Pearls != 30 || state.Fullness != 75 || state.Cleanliness != 85 || state.Revision != 1 ||
		len(state.Creatures) != 1 || state.Creatures[0].SpeciesID != "tide_sprite" {
		t.Fatalf("unexpected new state: %+v", state)
	}
	catalog := Catalog()
	want := []SpeciesDefinition{{"tide_sprite", "Tide Sprite", 0, 8, 12}, {"moon_jelly", "Moon Jelly", 30, 10, 16}, {"coral_drake", "Coral Drake", 60, 14, 24}}
	if !reflect.DeepEqual(catalog, want) {
		t.Fatalf("unexpected catalog: %+v", catalog)
	}
	catalog[0].ID = "changed"
	if Catalog()[0].ID != "tide_sprite" {
		t.Fatal("caller mutated catalog")
	}
}

func TestElapsedPartitionDoesNotChangeOutcome(t *testing.T) {
	for _, care := range [][2]float64{{75, 85}, {90, 75}, {30, 35}, {20, 100}, {100, 20}, {0, 0}} {
		whole, split := newState(testStart), newState(testStart)
		whole.Fullness, split.Fullness = care[0], care[0]
		whole.Cleanliness, split.Cleanliness = care[1], care[1]
		advance(&whole, testStart.Add(8*time.Hour))
		for i := 1; i <= 480; i++ {
			advance(&split, testStart.Add(time.Duration(i)*time.Minute))
		}
		near(t, split.Fullness, whole.Fullness)
		near(t, split.Cleanliness, whole.Cleanliness)
		near(t, split.Creatures[0].GrowthHours, whole.Creatures[0].GrowthHours)
		near(t, split.Creatures[0].PendingPearls, whole.Creatures[0].PendingPearls)
	}
}

func TestOneHourExactProgress(t *testing.T) {
	state := newState(testStart)
	result := advance(&state, testStart.Add(time.Hour))
	near(t, state.Fullness, 69)
	near(t, state.Cleanliness, 82)
	near(t, state.Creatures[0].GrowthHours, .72)
	near(t, state.Creatures[0].PendingPearls, 5.76)
	if result.ElapsedSeconds != 3600 || result.SimulatedSeconds != 3600 || result.WasCapped {
		t.Fatalf("bad advance: %+v", result)
	}
}

func TestOfflineGapCapAndClockRollback(t *testing.T) {
	state := newState(testStart)
	future := testStart.Add(7 * 24 * time.Hour)
	result := advance(&state, future)
	if !result.WasCapped || result.SimulatedSeconds != OfflineCapSeconds || !state.LastUpdatedAt.Equal(future) {
		t.Fatalf("bad capped advance: %+v %+v", state, result)
	}
	earned := state.Creatures[0].PendingPearls
	if !advance(&state, testStart).ClockRolledBack || !state.LastUpdatedAt.Equal(future) {
		t.Fatal("clock rollback regressed high-water mark")
	}
	if advance(&state, future).SimulatedSeconds != 0 || state.Creatures[0].PendingPearls != earned {
		t.Fatal("elapsed gap replayed")
	}
}

func TestVeryLongGapStillConsumesEntireTimestamp(t *testing.T) {
	state := newState(testStart)
	future := time.Date(9999, 12, 31, 23, 59, 59, 0, time.UTC)
	result := advance(&state, future)
	if result.ElapsedSeconds < 200e9 || result.SimulatedSeconds != OfflineCapSeconds || !state.LastUpdatedAt.Equal(future) {
		t.Fatalf("large gap overflowed: %+v", result)
	}
}

func TestLowCarePausesAndFreeCareRecovers(t *testing.T) {
	state := newState(testStart)
	state.Pearls, state.Fullness, state.Cleanliness = 0, 0, 0
	advance(&state, testStart.Add(8*time.Hour))
	if state.Creatures[0].GrowthHours != 0 || len(state.Creatures) != 1 {
		t.Fatal("low-care progress or creature loss")
	}
	perform(&state, Command{Action: ActionFeed})
	perform(&state, Command{Action: ActionClean})
	advance(&state, testStart.Add(8*time.Hour+15*time.Minute))
	if state.Creatures[0].GrowthHours <= 0 || state.Pearls != 0 {
		t.Fatal("free care did not recover progress")
	}
}

func TestFreeCareClampsAndNoOps(t *testing.T) {
	state := newState(testStart)
	state.Fullness, state.Cleanliness = 99, 99
	for _, action := range []Action{ActionFeed, ActionClean} {
		if success, _, _ := perform(&state, Command{Action: action}); !success {
			t.Fatal("care should succeed")
		}
		if success, _, _ := perform(&state, Command{Action: action}); success {
			t.Fatal("full care should no-op")
		}
	}
	if state.Fullness != 100 || state.Cleanliness != 100 || state.Pearls != 30 {
		t.Fatalf("care not clamped: %+v", state)
	}
}

func TestAdoptionPricesDuplicatesAndUnknown(t *testing.T) {
	state := newState(testStart)
	state.Pearls = 10
	for _, id := range []string{"moon_jelly", "missing", "tide_sprite"} {
		if success, _, _ := perform(&state, Command{Action: ActionAdopt, SpeciesID: id}); success {
			t.Fatalf("unexpected adoption %s", id)
		}
	}
	if state.Pearls != 10 || len(state.Creatures) != 1 {
		t.Fatal("rejected adoption mutated state")
	}
	state.Pearls = 90
	for _, entry := range []struct {
		id    string
		price int
	}{{"moon_jelly", 30}, {"coral_drake", 60}} {
		if success, _, amount := perform(&state, Command{Action: ActionAdopt, SpeciesID: entry.id}); !success || amount != entry.price {
			t.Fatal("wrong adoption price")
		}
		if success, _, _ := perform(&state, Command{Action: ActionAdopt, SpeciesID: entry.id}); success {
			t.Fatal("duplicate adoption")
		}
	}
	if state.Pearls != 0 || len(state.Creatures) != Capacity {
		t.Fatal("wrong final adoption state")
	}
}

func TestCollectRetainsFractionsAndWalletOverflow(t *testing.T) {
	state := newState(testStart)
	state.Pearls = WalletCapacity - 2
	state.Creatures[0].PendingPearls = 5.75
	if success, _, amount := perform(&state, Command{Action: ActionCollect}); !success || amount != 2 {
		t.Fatal("wrong capped collection")
	}
	near(t, state.Creatures[0].PendingPearls, 3.75)
	if success, _, _ := perform(&state, Command{Action: ActionCollect}); success {
		t.Fatal("full wallet should no-op")
	}
	state.Pearls = 0
	if _, _, amount := perform(&state, Command{Action: ActionCollect}); amount != 3 {
		t.Fatal("wrong whole-pearl collection")
	}
	near(t, state.Creatures[0].PendingPearls, .75)
}

func TestLongPlayRemainsBounded(t *testing.T) {
	state := newState(testStart)
	for i := 1; i <= 100; i++ {
		state.Fullness, state.Cleanliness = 100, 100
		advance(&state, testStart.Add(time.Duration(i)*8*time.Hour))
	}
	if state.Creatures[0].GrowthHours != 12 || state.Creatures[0].PendingPearls != RewardCapacity {
		t.Fatalf("growth or rewards unbounded: %+v", state)
	}
	if err := validateState(state); err != nil {
		t.Fatal(err)
	}
}

func TestStateValidationRejectsInvalidData(t *testing.T) {
	cases := map[string]func(*State){
		"version":         func(s *State) { s.Version++ },
		"revision":        func(s *State) { s.Revision = 0 },
		"timestamp":       func(s *State) { s.LastUpdatedAt = time.Time{} },
		"wallet negative": func(s *State) { s.Pearls = -1 },
		"wallet overflow": func(s *State) { s.Pearls = 10000 },
		"nan":             func(s *State) { s.Fullness = math.NaN() },
		"infinity":        func(s *State) { s.Cleanliness = math.Inf(1) },
		"care negative":   func(s *State) { s.Fullness = -1 },
		"care overflow":   func(s *State) { s.Cleanliness = 101 },
		"missing":         func(s *State) { s.Creatures = nil },
		"duplicate":       func(s *State) { s.Creatures = append(s.Creatures, s.Creatures[0]) },
		"unknown":         func(s *State) { s.Creatures[0].SpeciesID = "missing" },
		"growth":          func(s *State) { s.Creatures[0].GrowthHours = 13 },
		"rewards":         func(s *State) { s.Creatures[0].PendingPearls = 101 },
	}
	for name, change := range cases {
		t.Run(name, func(t *testing.T) {
			s := newState(testStart)
			change(&s)
			if err := validateState(s); !errors.Is(err, ErrCorrupt) {
				t.Fatalf("got %v", err)
			}
		})
	}
}
