package reef

import (
	"context"
	"errors"
	"fmt"
	"math"
	"reflect"
	"sync"
	"testing"
	"time"
)

func testRepository(t *testing.T) *FileRepository {
	t.Helper()
	repository, err := NewFileRepository(t.TempDir())
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() {
		if err := repository.Close(); err != nil {
			t.Error(err)
		}
	})
	return repository
}

func testService(t *testing.T) (*Service, *FileRepository, *time.Time) {
	t.Helper()
	repository := testRepository(t)
	now := testStart
	return NewService(repository, func() time.Time { return now }), repository, &now
}

func mustGet(t *testing.T, service *Service, player string) State {
	t.Helper()
	state, err := service.Get(t.Context(), player)
	if err != nil {
		t.Fatal(err)
	}
	return state
}

func mustApply(t *testing.T, service *Service, player string, command Command) Result {
	t.Helper()
	result, err := service.Apply(t.Context(), player, command)
	if err != nil {
		t.Fatal(err)
	}
	return result
}

func TestGetPersistsOfflineProgressOnceAcrossRestart(t *testing.T) {
	service, repository, now := testService(t)
	first := mustGet(t, service, "player")
	*now = now.Add(24 * time.Hour)
	advanced := mustGet(t, service, "player")
	if advanced.Revision != first.Revision+1 || advanced.Fullness != 27 || advanced.Cleanliness != 61 {
		t.Fatalf("wrong offline state: %+v", advanced)
	}
	if err := repository.Close(); err != nil {
		t.Fatal(err)
	}
	reopened, err := NewFileRepository(repository.directory)
	if err != nil {
		t.Fatal(err)
	}
	defer reopened.Close()
	again := mustGet(t, NewService(reopened, func() time.Time { return *now }), "player")
	if !reflect.DeepEqual(advanced, again) {
		t.Fatalf("progress reapplied on restart: %+v vs %+v", advanced, again)
	}
}

func TestApplyAdvancesClockThenActionAndReplaysOriginal(t *testing.T) {
	service, _, now := testService(t)
	state := mustGet(t, service, "player")
	*now = now.Add(time.Hour)
	command := Command{RequestID: "collect-1", ExpectedRevision: state.Revision, Action: ActionCollect}
	result := mustApply(t, service, "player", command)
	if result.Amount != 5 || result.State.Pearls != 35 || result.State.Revision != 2 || result.Advance.ElapsedSeconds != 3600 {
		t.Fatalf("bad result: %+v", result)
	}
	near(t, result.State.Creatures[0].PendingPearls, .76)
	*now = now.Add(time.Hour)
	later := mustGet(t, service, "player")
	replay := mustApply(t, service, "player", command)
	if !replay.Replayed {
		t.Fatal("missing replay marker")
	}
	replay.Replayed = false
	if !reflect.DeepEqual(result, replay) {
		t.Fatalf("replay changed original outcome: %+v vs %+v", result, replay)
	}
	if got := mustGet(t, service, "player"); !reflect.DeepEqual(later, got) {
		t.Fatal("replay mutated current state")
	}
}

func TestReplaySurvivesRestartAndResponseAliasing(t *testing.T) {
	service, repository, now := testService(t)
	state := mustGet(t, service, "player")
	command := Command{RequestID: "adopt-1", ExpectedRevision: state.Revision, Action: ActionAdopt, SpeciesID: "moon_jelly"}
	result := mustApply(t, service, "player", command)
	result.State.Creatures[0].SpeciesID = "mutated-client-copy"
	if err := repository.Close(); err != nil {
		t.Fatal(err)
	}
	reopened, err := NewFileRepository(repository.directory)
	if err != nil {
		t.Fatal(err)
	}
	defer reopened.Close()
	after := NewService(reopened, func() time.Time { return *now })
	replay := mustApply(t, after, "player", command)
	if !replay.Replayed || replay.State.Pearls != 0 || len(replay.State.Creatures) != 2 || replay.State.Creatures[0].SpeciesID != "tide_sprite" {
		t.Fatalf("bad restart replay: %+v", replay)
	}
	if state := mustGet(t, after, "player"); state.Revision != 2 || len(state.Creatures) != 2 {
		t.Fatal("adoption reapplied")
	}
}

func TestStaleCASAndRequestIDConflictsDoNotAdvance(t *testing.T) {
	service, _, now := testService(t)
	state := mustGet(t, service, "player")
	command := Command{RequestID: "first", ExpectedRevision: state.Revision, Action: ActionFeed}
	committed := mustApply(t, service, "player", command)
	*now = now.Add(48 * time.Hour)
	for _, entry := range []struct {
		command Command
		want    error
	}{
		{Command{RequestID: "second", ExpectedRevision: 1, Action: ActionClean}, ErrRevisionConflict},
		{Command{RequestID: "first", ExpectedRevision: 2, Action: ActionFeed}, ErrRequestIDConflict},
		{Command{RequestID: "first", ExpectedRevision: 1, Action: ActionClean}, ErrRequestIDConflict},
	} {
		if _, err := service.Apply(t.Context(), "player", entry.command); !errors.Is(err, entry.want) {
			t.Fatalf("got %v, want %v", err, entry.want)
		}
	}
	*now = testStart
	if got := mustGet(t, service, "player"); !reflect.DeepEqual(got, committed.State) {
		t.Fatal("rejected command mutated state")
	}
}

func TestGameplayNoOpIsPersistedAndReplayable(t *testing.T) {
	service, _, _ := testService(t)
	state := mustGet(t, service, "player")
	command := Command{RequestID: "nothing", ExpectedRevision: state.Revision, Action: ActionCollect}
	result := mustApply(t, service, "player", command)
	if result.Success || result.Amount != 0 || result.State.Revision != 2 {
		t.Fatalf("bad no-op: %+v", result)
	}
	if replay := mustApply(t, service, "player", command); !replay.Replayed || replay.Success {
		t.Fatal("no-op not replayable")
	}
}

func TestClockRollbackPreservesHighWatermark(t *testing.T) {
	service, _, now := testService(t)
	mustGet(t, service, "player")
	*now = now.Add(time.Hour)
	state := mustGet(t, service, "player")
	*now = testStart
	result := mustApply(t, service, "player", Command{RequestID: "rollback", ExpectedRevision: state.Revision, Action: ActionClean})
	if !result.Advance.ClockRolledBack || !result.State.LastUpdatedAt.Equal(state.LastUpdatedAt) {
		t.Fatal("rollback moved timestamp")
	}
	near(t, result.State.Creatures[0].PendingPearls, state.Creatures[0].PendingPearls)
	*now = testStart.Add(time.Hour)
	if got := mustGet(t, service, "player"); got.Revision != result.State.Revision {
		t.Fatal("old interval replayed")
	}
}

func TestConcurrentDuplicateCommandsCommitExactlyOnce(t *testing.T) {
	service, _, _ := testService(t)
	state := mustGet(t, service, "player")
	command := Command{RequestID: "one-adoption", ExpectedRevision: state.Revision, Action: ActionAdopt, SpeciesID: "moon_jelly"}
	const workers = 32
	results := make(chan Result, workers)
	errs := make(chan error, workers)
	var group sync.WaitGroup
	for range workers {
		group.Go(func() {
			result, err := service.Apply(context.Background(), "player", command)
			results <- result
			errs <- err
		})
	}
	group.Wait()
	close(results)
	close(errs)
	for err := range errs {
		if err != nil {
			t.Fatal(err)
		}
	}
	originals := 0
	for result := range results {
		if !result.Replayed {
			originals++
		}
		if result.State.Revision != 2 || result.State.Pearls != 0 {
			t.Fatal("duplicate mutation")
		}
	}
	if originals != 1 {
		t.Fatalf("got %d original commits", originals)
	}
}

func TestConcurrentDifferentCommandsWithSameRevisionOneWins(t *testing.T) {
	service, _, _ := testService(t)
	state := mustGet(t, service, "player")
	const workers = 32
	errs := make(chan error, workers)
	var group sync.WaitGroup
	for i := range workers {
		group.Go(func() {
			_, err := service.Apply(context.Background(), "player", Command{RequestID: fmt.Sprintf("request-%d", i), ExpectedRevision: state.Revision, Action: ActionFeed})
			errs <- err
		})
	}
	group.Wait()
	close(errs)
	committed, stale := 0, 0
	for err := range errs {
		if err == nil {
			committed++
		} else if errors.Is(err, ErrRevisionConflict) {
			stale++
		} else {
			t.Fatal(err)
		}
	}
	if committed != 1 || stale != workers-1 {
		t.Fatalf("committed=%d stale=%d", committed, stale)
	}
}

func TestDifferentPlayersAreIsolated(t *testing.T) {
	service, _, _ := testService(t)
	alice, bob := mustGet(t, service, "alice"), mustGet(t, service, "bob")
	mustApply(t, service, "alice", Command{RequestID: "same-id", ExpectedRevision: alice.Revision, Action: ActionAdopt, SpeciesID: "moon_jelly"})
	if got := mustGet(t, service, "bob"); !reflect.DeepEqual(got, bob) {
		t.Fatal("cross-player mutation")
	}
	result := mustApply(t, service, "bob", Command{RequestID: "same-id", ExpectedRevision: bob.Revision, Action: ActionFeed})
	if result.Replayed {
		t.Fatal("cross-player replay")
	}
}

func TestReplayWindowIsBoundedAndExpiredRetryStaysStale(t *testing.T) {
	service, repository, _ := testService(t)
	state := mustGet(t, service, "player")
	first := Command{RequestID: "request-0", ExpectedRevision: state.Revision, Action: ActionFeed}
	for i := 0; i <= ReplayCapacity; i++ {
		result := mustApply(t, service, "player", Command{RequestID: fmt.Sprintf("request-%d", i), ExpectedRevision: state.Revision, Action: ActionFeed})
		state = result.State
	}
	if _, err := service.Apply(t.Context(), "player", first); !errors.Is(err, ErrRevisionConflict) {
		t.Fatalf("expired retry got %v", err)
	}
	record, err := readRecord(repository.playerPath("player"), "player")
	if err != nil {
		t.Fatal(err)
	}
	if len(record.Receipts) != ReplayCapacity {
		t.Fatalf("unbounded receipts: %d", len(record.Receipts))
	}
	if state.Revision != ReplayCapacity+2 {
		t.Fatal("wrong revision")
	}
}

func TestValidationAndCancellationLeaveNoRecord(t *testing.T) {
	service, repository, _ := testService(t)
	for _, id := range []string{"", "../player", "player/other", "a b", "日本語"} {
		if _, err := service.Get(t.Context(), id); !errors.Is(err, ErrInvalidArgument) {
			t.Fatalf("invalid ID %q: %v", id, err)
		}
	}
	for _, command := range []Command{
		{RequestID: "x", ExpectedRevision: 1, Action: "upload_state"},
		{RequestID: "", ExpectedRevision: 1, Action: ActionFeed},
		{RequestID: "x", Action: ActionFeed},
		{RequestID: "x", ExpectedRevision: 1, Action: ActionAdopt},
		{RequestID: "x", ExpectedRevision: 1, Action: ActionFeed, SpeciesID: "moon_jelly"},
	} {
		if _, err := service.Apply(t.Context(), "player", command); !errors.Is(err, ErrInvalidArgument) {
			t.Fatalf("invalid command: %v", err)
		}
	}
	ctx, cancel := context.WithCancel(t.Context())
	cancel()
	if _, err := service.Get(ctx, "player"); !errors.Is(err, context.Canceled) {
		t.Fatal(err)
	}
	if record, err := readRecord(repository.playerPath("player"), "player"); record != nil || err != nil {
		t.Fatal("invalid operation created state")
	}
	if _, err := service.Apply(t.Context(), "player", Command{RequestID: "valid", ExpectedRevision: 1, Action: ActionFeed}); !errors.Is(err, ErrRevisionConflict) {
		t.Fatal("Apply without Get should conflict")
	}
}

func TestInvalidClockAndRevisionExhaustion(t *testing.T) {
	service, repository, now := testService(t)
	*now = time.Time{}
	if _, err := service.Get(t.Context(), "player"); !errors.Is(err, ErrUnavailable) {
		t.Fatal(err)
	}
	*now = testStart
	mustGet(t, service, "player")
	if err := repository.Transact(t.Context(), "player", func(record *PlayerRecord) (*PlayerRecord, error) {
		record.State.Revision = math.MaxUint64
		return record, nil
	}); err != nil {
		t.Fatal(err)
	}
	*now = now.Add(time.Hour)
	if _, err := service.Get(t.Context(), "player"); !errors.Is(err, ErrUnavailable) {
		t.Fatal("revision overflow on Get")
	}
	if _, err := service.Apply(t.Context(), "player", Command{RequestID: "overflow", ExpectedRevision: math.MaxUint64, Action: ActionFeed}); !errors.Is(err, ErrUnavailable) {
		t.Fatal("revision overflow on Apply")
	}
}
