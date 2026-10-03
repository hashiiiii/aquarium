// Package reef implements the server-authoritative aquarium simulation and its
// local, single-process persistence boundary.
package reef

import (
	"context"
	"errors"
	"time"
)

const (
	SaveVersion            = 1
	Capacity               = 3
	WalletCapacity         = 9999
	RewardCapacity         = 100.0
	OfflineCapSeconds      = 8 * 60 * 60
	FullnessLossPerHour    = 6.0
	CleanlinessLossPerHour = 3.0
	CareThreshold          = 20.0
	// ReplayCapacity is the number of recent command responses retained per player.
	// Older retries are rejected by their stale expected revision, never reapplied.
	ReplayCapacity = 256
)

var (
	ErrInvalidArgument   = errors.New("invalid aquarium argument")
	ErrRevisionConflict  = errors.New("aquarium revision conflict")
	ErrRequestIDConflict = errors.New("request ID already used for another command")
	ErrCorrupt           = errors.New("aquarium storage is corrupt")
	ErrUnavailable       = errors.New("aquarium storage unavailable")
)

// SpeciesDefinition is an immutable catalog entry matching the Unity prototype.
type SpeciesDefinition struct {
	ID                   string  `json:"id"`
	DisplayName          string  `json:"display_name"`
	Price                int     `json:"price"`
	PearlsPerCareHour    float64 `json:"pearls_per_care_hour"`
	MatureAfterCareHours float64 `json:"mature_after_care_hours"`
}

var speciesCatalog = [...]SpeciesDefinition{
	{"tide_sprite", "Tide Sprite", 0, 8, 12},
	{"moon_jelly", "Moon Jelly", 30, 10, 16},
	{"coral_drake", "Coral Drake", 60, 14, 24},
}

// Catalog returns an independent copy; callers cannot change simulation rules.
func Catalog() []SpeciesDefinition {
	return append([]SpeciesDefinition(nil), speciesCatalog[:]...)
}

func species(id string) (SpeciesDefinition, bool) {
	for _, item := range speciesCatalog {
		if item.ID == id {
			return item, true
		}
	}
	return SpeciesDefinition{}, false
}

// Creature is the persisted progress of one owned catalog species.
type Creature struct {
	SpeciesID     string  `json:"species_id"`
	GrowthHours   float64 `json:"growth_hours"`
	PendingPearls float64 `json:"pending_pearls"`
}

// State is a server-created snapshot. Revision increases on every persisted
// time advance or accepted command, including a gameplay no-op.
type State struct {
	Version       int        `json:"version"`
	Revision      uint64     `json:"revision"`
	LastUpdatedAt time.Time  `json:"last_updated_at"`
	Pearls        int        `json:"pearls"`
	Fullness      float64    `json:"fullness"`
	Cleanliness   float64    `json:"cleanliness"`
	Creatures     []Creature `json:"creatures"`
}

// Action identifies the only mutations accepted from a client.
type Action string

const (
	ActionFeed    Action = "feed"
	ActionClean   Action = "clean"
	ActionCollect Action = "collect"
	ActionAdopt   Action = "adopt"
)

// Command contains intent, never a client-owned state or clock. RequestID must
// be unique per logical command. Retries repeat all four fields unchanged.
type Command struct {
	RequestID        string `json:"request_id"`
	ExpectedRevision uint64 `json:"expected_revision"`
	Action           Action `json:"action"`
	SpeciesID        string `json:"species_id,omitempty"`
}

// AdvanceResult reports the server-clock interval consumed before a command.
type AdvanceResult struct {
	ElapsedSeconds   float64 `json:"elapsed_seconds"`
	SimulatedSeconds float64 `json:"simulated_seconds"`
	ClockRolledBack  bool    `json:"clock_rolled_back"`
	WasCapped        bool    `json:"was_capped"`
}

// Result is persisted with its command, so a replay returns the original
// snapshot and outcome, even when a later command has changed current state.
type Result struct {
	State    State         `json:"state"`
	Success  bool          `json:"success"`
	Message  string        `json:"message"`
	Amount   int           `json:"amount"`
	Advance  AdvanceResult `json:"advance"`
	Replayed bool          `json:"replayed"`
}

// Receipt stores a committed request and its original result.
type Receipt struct {
	Command Command `json:"command"`
	Result  Result  `json:"result"`
}

// PlayerRecord is the repository transaction unit. State and receipts must
// commit together; repositories must not publish a partially written record.
type PlayerRecord struct {
	SchemaVersion int       `json:"schema_version"`
	PlayerID      string    `json:"player_id"`
	State         State     `json:"state"`
	Receipts      []Receipt `json:"receipts"`
}

// Repository serializes read-modify-write transactions. The callback receives
// nil for a missing player. Returning nil means no write. An error aborts the
// transaction; the callback must never be retried by an implementation.
type Repository interface {
	Transact(context.Context, string, func(*PlayerRecord) (*PlayerRecord, error)) error
}
