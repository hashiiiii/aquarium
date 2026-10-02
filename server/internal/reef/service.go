package reef

import (
	"context"
	"fmt"
	"math"
	"time"
)

// Service applies client intent to persisted server state using its own clock.
type Service struct {
	repository Repository
	clock      func() time.Time
}

// NewService creates the simulation boundary. A nil clock uses time.Now.
func NewService(repository Repository, clock func() time.Time) *Service {
	if clock == nil {
		clock = time.Now
	}
	return &Service{repository: repository, clock: clock}
}

// Get creates a new sanctuary on first access, or advances and persists the
// current state. Reads that advance time also advance its CAS revision.
func (service *Service) Get(ctx context.Context, playerID string) (State, error) {
	if !validID(playerID) {
		return State{}, fmt.Errorf("%w: invalid player ID", ErrInvalidArgument)
	}
	var result State
	err := service.repository.Transact(ctx, playerID, func(record *PlayerRecord) (*PlayerRecord, error) {
		now := service.clock().UTC()
		if !validTime(now) {
			return nil, fmt.Errorf("%w: invalid server clock", ErrUnavailable)
		}
		if record == nil {
			record = &PlayerRecord{SchemaVersion: 1, PlayerID: playerID, State: newState(now), Receipts: []Receipt{}}
			result = cloneState(record.State)
			return record, nil
		}
		if !now.After(record.State.LastUpdatedAt) {
			result = cloneState(record.State)
			return nil, nil
		}
		if record.State.Revision == math.MaxUint64 {
			return nil, fmt.Errorf("%w: revision exhausted", ErrUnavailable)
		}
		advance(&record.State, now)
		record.State.Revision++
		result = cloneState(record.State)
		return record, nil
	})
	if err != nil {
		return State{}, err
	}
	return result, nil
}

// Apply checks a replay before CAS or the server clock. Every newly accepted
// command consumes exactly one revision, even when gameplay returns Success=false.
// Validation, stale revisions and reused IDs leave storage completely unchanged.
func (service *Service) Apply(ctx context.Context, playerID string, command Command) (Result, error) {
	if !validID(playerID) {
		return Result{}, fmt.Errorf("%w: invalid player ID", ErrInvalidArgument)
	}
	if err := validateCommand(command); err != nil {
		return Result{}, err
	}
	var result Result
	err := service.repository.Transact(ctx, playerID, func(record *PlayerRecord) (*PlayerRecord, error) {
		if record == nil {
			return nil, fmt.Errorf("%w: call Get before Apply", ErrRevisionConflict)
		}
		for _, receipt := range record.Receipts {
			if receipt.Command.RequestID != command.RequestID {
				continue
			}
			if receipt.Command != command {
				return nil, ErrRequestIDConflict
			}
			result = cloneResult(receipt.Result)
			result.Replayed = true
			return nil, nil
		}
		if record.State.Revision != command.ExpectedRevision {
			return nil, ErrRevisionConflict
		}
		if record.State.Revision == math.MaxUint64 {
			return nil, fmt.Errorf("%w: revision exhausted", ErrUnavailable)
		}
		now := service.clock().UTC()
		if !validTime(now) {
			return nil, fmt.Errorf("%w: invalid server clock", ErrUnavailable)
		}
		elapsed := advance(&record.State, now)
		success, message, amount := perform(&record.State, command)
		record.State.Revision++
		result = Result{State: cloneState(record.State), Success: success, Message: message, Amount: amount, Advance: elapsed}
		if len(record.Receipts) >= ReplayCapacity {
			record.Receipts = append([]Receipt(nil), record.Receipts[len(record.Receipts)-ReplayCapacity+1:]...)
		}
		record.Receipts = append(record.Receipts, Receipt{Command: command, Result: cloneResult(result)})
		return record, nil
	})
	if err != nil {
		return Result{}, err
	}
	return result, nil
}

func validID(id string) bool {
	if len(id) < 1 || len(id) > 128 {
		return false
	}
	for _, character := range id {
		if (character >= 'a' && character <= 'z') || (character >= 'A' && character <= 'Z') ||
			(character >= '0' && character <= '9') || character == '-' || character == '_' {
			continue
		}
		return false
	}
	return true
}

func validateCommand(command Command) error {
	if !validID(command.RequestID) || command.ExpectedRevision == 0 {
		return fmt.Errorf("%w: request ID and a positive expected revision are required", ErrInvalidArgument)
	}
	switch command.Action {
	case ActionFeed, ActionClean, ActionCollect:
		if command.SpeciesID != "" {
			return fmt.Errorf("%w: species ID is only valid for adopt", ErrInvalidArgument)
		}
	case ActionAdopt:
		if !validID(command.SpeciesID) {
			return fmt.Errorf("%w: adopt requires a valid species ID", ErrInvalidArgument)
		}
	default:
		return fmt.Errorf("%w: unknown action", ErrInvalidArgument)
	}
	return nil
}
