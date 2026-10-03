package reef

import (
	"bytes"
	"context"
	"errors"
	"fmt"
	"os"
	"os/exec"
	"path/filepath"
	"reflect"
	"strings"
	"testing"
	"time"
)

func TestFailedWriteDoesNotCommitStateOrReceipt(t *testing.T) {
	service, repository, _ := testService(t)
	initial := mustGet(t, service, "player")
	path := repository.playerPath("player")
	before, err := os.ReadFile(path)
	if err != nil {
		t.Fatal(err)
	}
	realCommit := repository.commit
	repository.commit = func(string, []byte) error { return errors.New("injected disk failure before rename") }
	command := Command{RequestID: "purchase", ExpectedRevision: initial.Revision, Action: ActionAdopt, SpeciesID: "moon_jelly"}
	if _, err := service.Apply(t.Context(), "player", command); !errors.Is(err, ErrUnavailable) {
		t.Fatalf("got %v", err)
	}
	after, err := os.ReadFile(path)
	if err != nil {
		t.Fatal(err)
	}
	if !bytes.Equal(before, after) {
		t.Fatal("failed write changed previous record")
	}
	if got := mustGet(t, service, "player"); !reflect.DeepEqual(got, initial) {
		t.Fatal("failed write escaped in-memory")
	}
	repository.commit = realCommit
	result := mustApply(t, service, "player", command)
	if result.Replayed || !result.Success || result.State.Revision != 2 || result.State.Pearls != 0 {
		t.Fatalf("failed command receipt leaked: %+v", result)
	}
}

func TestUncertainPostRenameFailureIsResolvedByReplay(t *testing.T) {
	service, repository, _ := testService(t)
	initial := mustGet(t, service, "player")
	realCommit := repository.commit
	repository.commit = func(path string, data []byte) error {
		if err := realCommit(path, data); err != nil {
			return err
		}
		return errors.New("injected failure after rename")
	}
	command := Command{RequestID: "purchase", ExpectedRevision: initial.Revision, Action: ActionAdopt, SpeciesID: "moon_jelly"}
	if _, err := service.Apply(t.Context(), "player", command); !errors.Is(err, ErrUnavailable) {
		t.Fatalf("got %v", err)
	}
	repository.commit = realCommit
	result := mustApply(t, service, "player", command)
	if !result.Replayed || !result.Success || result.State.Revision != 2 || len(result.State.Creatures) != 2 {
		t.Fatalf("uncertain commit not resolved: %+v", result)
	}
}

func TestFailedGetAdvanceDoesNotConsumeClockGap(t *testing.T) {
	service, repository, now := testService(t)
	initial := mustGet(t, service, "player")
	*now = now.Add(time.Hour)
	realCommit := repository.commit
	repository.commit = func(string, []byte) error { return errors.New("disk full") }
	if _, err := service.Get(t.Context(), "player"); !errors.Is(err, ErrUnavailable) {
		t.Fatal(err)
	}
	repository.commit = realCommit
	got := mustGet(t, service, "player")
	if got.Revision != initial.Revision+1 {
		t.Fatal("failed advance consumed a revision")
	}
	near(t, got.Creatures[0].PendingPearls, 5.76)
}

func TestTransactionCallbackErrorAndCancellationAbort(t *testing.T) {
	service, repository, _ := testService(t)
	initial := mustGet(t, service, "player")
	abort := errors.New("abort transaction")
	if err := repository.Transact(t.Context(), "player", func(record *PlayerRecord) (*PlayerRecord, error) { record.State.Pearls = 0; return record, abort }); !errors.Is(err, abort) {
		t.Fatal(err)
	}
	ctx, cancel := context.WithCancel(t.Context())
	if err := repository.Transact(ctx, "player", func(record *PlayerRecord) (*PlayerRecord, error) {
		record.State.Pearls = 0
		cancel()
		return record, nil
	}); !errors.Is(err, context.Canceled) {
		t.Fatal(err)
	}
	if got := mustGet(t, service, "player"); !reflect.DeepEqual(got, initial) {
		t.Fatal("aborted transaction changed state")
	}
}

func TestCorruptRecordsAreRejectedWithoutOverwrite(t *testing.T) {
	cases := map[string]func([]byte) []byte{
		"empty":            func([]byte) []byte { return nil },
		"garbage":          func([]byte) []byte { return []byte("not json") },
		"incomplete":       func(data []byte) []byte { return data[:len(data)/2] },
		"changed checksum": func(data []byte) []byte { return bytes.Replace(data, []byte(`"pearls":30`), []byte(`"pearls":31`), 1) },
		"extra JSON":       func(data []byte) []byte { return append(data, []byte(`{}`)...) },
		"unknown field": func(data []byte) []byte {
			return bytes.Replace(data, []byte(`"format_version":1`), []byte(`"unexpected":true,"format_version":1`), 1)
		},
		"unsupported envelope": func(data []byte) []byte {
			return bytes.Replace(data, []byte(`"format_version":1`), []byte(`"format_version":99`), 1)
		},
		"too large": func([]byte) []byte { return bytes.Repeat([]byte("x"), maxRecordBytes+1) },
	}
	for name, damage := range cases {
		t.Run(name, func(t *testing.T) {
			service, repository, _ := testService(t)
			mustGet(t, service, "player")
			path := repository.playerPath("player")
			data, err := os.ReadFile(path)
			if err != nil {
				t.Fatal(err)
			}
			corrupt := damage(data)
			if err := os.WriteFile(path, corrupt, 0o600); err != nil {
				t.Fatal(err)
			}
			if _, err := service.Get(t.Context(), "player"); !errors.Is(err, ErrCorrupt) {
				t.Fatalf("Get got %v", err)
			}
			if _, err := service.Apply(t.Context(), "player", Command{RequestID: "x", ExpectedRevision: 1, Action: ActionFeed}); !errors.Is(err, ErrCorrupt) {
				t.Fatalf("Apply got %v", err)
			}
			after, err := os.ReadFile(path)
			if err != nil {
				t.Fatal(err)
			}
			if !bytes.Equal(corrupt, after) {
				t.Fatal("corrupt original was overwritten")
			}
		})
	}
}

func TestValidChecksumDoesNotBypassStateValidation(t *testing.T) {
	for name, change := range map[string]func(*PlayerRecord){
		"wrong player":      func(r *PlayerRecord) { r.PlayerID = "somebody-else" },
		"bad schema":        func(r *PlayerRecord) { r.SchemaVersion = 2 },
		"bad balance":       func(r *PlayerRecord) { r.State.Pearls = -100 },
		"bad revision":      func(r *PlayerRecord) { r.State.Revision = 0 },
		"duplicate species": func(r *PlayerRecord) { r.State.Creatures = append(r.State.Creatures, r.State.Creatures[0]) },
		"bad receipt": func(r *PlayerRecord) {
			r.Receipts = []Receipt{{Command: Command{RequestID: "old", ExpectedRevision: 1, Action: ActionFeed}, Result: Result{State: cloneState(r.State)}}}
		},
	} {
		t.Run(name, func(t *testing.T) {
			service, repository, _ := testService(t)
			mustGet(t, service, "player")
			path := repository.playerPath("player")
			record, err := readRecord(path, "player")
			if err != nil {
				t.Fatal(err)
			}
			change(record)
			data, err := encodeRecord(record)
			if err != nil {
				t.Fatal(err)
			}
			if err := os.WriteFile(path, data, 0o600); err != nil {
				t.Fatal(err)
			}
			if _, err := service.Get(t.Context(), "player"); !errors.Is(err, ErrCorrupt) {
				t.Fatalf("got %v", err)
			}
		})
	}
}

func TestSymlinkPlayerRecordAndLockAreRejected(t *testing.T) {
	service, repository, _ := testService(t)
	target := filepath.Join(t.TempDir(), "target")
	if err := os.WriteFile(target, []byte("do not touch"), 0o600); err != nil {
		t.Fatal(err)
	}
	if err := os.Symlink(target, repository.playerPath("player")); err != nil {
		t.Fatal(err)
	}
	if _, err := service.Get(t.Context(), "player"); !errors.Is(err, ErrCorrupt) {
		t.Fatalf("symlink record got %v", err)
	}
	directory := t.TempDir()
	if err := os.Symlink(target, filepath.Join(directory, ".reef.lock")); err != nil {
		t.Fatal(err)
	}
	if _, err := NewFileRepository(directory); !errors.Is(err, ErrUnavailable) {
		t.Fatalf("symlink lock got %v", err)
	}
}

func TestExclusiveDirectoryLockAndClose(t *testing.T) {
	repository := testRepository(t)
	if another, err := NewFileRepository(repository.directory); !errors.Is(err, ErrUnavailable) {
		if another != nil {
			another.Close()
		}
		t.Fatalf("second open got %v", err)
	}
	alias := filepath.Join(t.TempDir(), "alias")
	if err := os.Symlink(repository.directory, alias); err != nil {
		t.Fatal(err)
	}
	if another, err := NewFileRepository(alias); !errors.Is(err, ErrUnavailable) {
		if another != nil {
			another.Close()
		}
		t.Fatalf("symlink bypassed lock: %v", err)
	}
	if err := repository.Close(); err != nil {
		t.Fatal(err)
	}
	if err := repository.Close(); err != nil {
		t.Fatal(err)
	}
	if err := repository.Transact(t.Context(), "player", func(*PlayerRecord) (*PlayerRecord, error) { t.Fatal("called closed repository"); return nil, nil }); !errors.Is(err, ErrUnavailable) {
		t.Fatal(err)
	}
	reopened, err := NewFileRepository(repository.directory)
	if err != nil {
		t.Fatal(err)
	}
	if err := reopened.Close(); err != nil {
		t.Fatal(err)
	}
}

func TestDirectoryLockAcrossProcesses(t *testing.T) {
	repository := testRepository(t)
	run := func(want string) {
		t.Helper()
		executable, err := os.Executable()
		if err != nil {
			t.Fatal(err)
		}
		command := exec.Command(executable, "-test.run=^TestRepositorySubprocessLockHelper$")
		command.Env = append(os.Environ(), "REEF_TEST_LOCK_DIRECTORY="+repository.directory, "REEF_TEST_LOCK_EXPECT="+want)
		output, err := command.CombinedOutput()
		if err != nil {
			t.Fatalf("subprocess: %v\n%s", err, output)
		}
		if !strings.Contains(string(output), "LOCK_CHECK_OK") {
			t.Fatalf("subprocess did not check lock: %s", output)
		}
	}
	run("locked")
	if err := repository.Close(); err != nil {
		t.Fatal(err)
	}
	run("open")
	// The helper exits without calling Close, simulating an abrupt process exit.
	// The kernel must release the lock without needing a stale-lock reset.
	reopened, err := NewFileRepository(repository.directory)
	if err != nil {
		t.Fatal(err)
	}
	if err := reopened.Close(); err != nil {
		t.Fatal(err)
	}
}

func TestRepositorySubprocessLockHelper(t *testing.T) {
	directory := os.Getenv("REEF_TEST_LOCK_DIRECTORY")
	if directory == "" {
		t.Skip("subprocess helper")
	}
	repository, err := NewFileRepository(directory)
	switch os.Getenv("REEF_TEST_LOCK_EXPECT") {
	case "locked":
		if !errors.Is(err, ErrUnavailable) {
			t.Fatalf("expected lock error, got %v", err)
		}
	case "open":
		if err != nil {
			t.Fatal(err)
		}
		if repository == nil {
			t.Fatal("missing repository")
		}
	default:
		t.Fatal("unknown subprocess expectation")
	}
	fmt.Println("LOCK_CHECK_OK")
	os.Exit(0)
}

func TestOrphanedTemporaryFileIsNeverLoaded(t *testing.T) {
	service, repository, _ := testService(t)
	initial := mustGet(t, service, "player")
	if err := os.WriteFile(filepath.Join(repository.directory, ".player-orphan.tmp"), []byte("incomplete crash write"), 0o600); err != nil {
		t.Fatal(err)
	}
	if got := mustGet(t, service, "player"); !reflect.DeepEqual(got, initial) {
		t.Fatal("orphaned temp changed state")
	}
	files, err := filepath.Glob(filepath.Join(repository.directory, ".player-*.tmp"))
	if err != nil {
		t.Fatal(err)
	}
	if len(files) != 1 {
		t.Fatalf("unexpected temporary files: %v", files)
	}
}

func TestAtomicWriteFailureKeepsTargetAndCleansTemporaryFiles(t *testing.T) {
	directory := t.TempDir()
	target := filepath.Join(directory, "is-a-directory")
	if err := os.Mkdir(target, 0o700); err != nil {
		t.Fatal(err)
	}
	if err := atomicWrite(target, []byte("new data")); err == nil {
		t.Fatal("expected rename failure")
	}
	info, err := os.Stat(target)
	if err != nil {
		t.Fatal(err)
	}
	if !info.IsDir() {
		t.Fatal("failed rename changed destination")
	}
	files, err := filepath.Glob(filepath.Join(directory, ".player-*.tmp"))
	if err != nil {
		t.Fatal(err)
	}
	if len(files) != 0 {
		t.Fatalf("left temporary files after failure: %v", files)
	}
}
