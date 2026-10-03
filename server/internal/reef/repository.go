package reef

import (
	"bytes"
	"context"
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"sync"
)

const maxRecordBytes = 1 << 20

// FileRepository stores one checksummed JSON transaction per player. It holds an
// exclusive advisory lock for the lifetime of the repository, and serializes
// transactions within the process. Use a local disk, not a network filesystem.
type FileRepository struct {
	mutex     sync.Mutex
	directory string
	lock      *os.File
	closed    bool
	// commit is injectable within this package for testing failures before and
	// after the atomic rename. There is deliberately no write-behind state cache.
	commit func(string, []byte) error
}

type diskEnvelope struct {
	FormatVersion int             `json:"format_version"`
	SHA256        string          `json:"sha256"`
	Payload       json.RawMessage `json:"payload"`
}

// NewFileRepository creates a private data directory and takes its exclusive
// process lock. Another open repository at that directory fails immediately.
func NewFileRepository(directory string) (*FileRepository, error) {
	if directory == "" {
		return nil, fmt.Errorf("%w: data directory is required", ErrInvalidArgument)
	}
	absolute, err := filepath.Abs(directory)
	if err != nil {
		return nil, unavailable("resolve data directory", err)
	}
	if err = os.MkdirAll(absolute, 0o700); err != nil {
		return nil, unavailable("create data directory", err)
	}
	absolute, err = filepath.EvalSymlinks(absolute)
	if err != nil {
		return nil, unavailable("resolve data directory", err)
	}
	lock, err := lockDirectory(absolute)
	if err != nil {
		return nil, unavailable("lock data directory", err)
	}
	return &FileRepository{directory: absolute, lock: lock, commit: atomicWrite}, nil
}

// Close waits for a current transaction and releases the process lock.
func (repository *FileRepository) Close() error {
	repository.mutex.Lock()
	defer repository.mutex.Unlock()
	if repository.closed {
		return nil
	}
	repository.closed = true
	if err := repository.lock.Close(); err != nil {
		return unavailable("close data directory lock", err)
	}
	return nil
}

// Transact atomically commits both state and idempotency receipts. Failed
// callbacks or failed writes before rename leave the previous file unchanged.
// A post-rename sync error is an uncertain outcome; retry the identical command
// to resolve it using the receipt. No corrupt record is silently reinitialized.
func (repository *FileRepository) Transact(ctx context.Context, playerID string,
	update func(*PlayerRecord) (*PlayerRecord, error)) error {
	if !validID(playerID) {
		return fmt.Errorf("%w: invalid player ID", ErrInvalidArgument)
	}
	if err := ctx.Err(); err != nil {
		return err
	}
	repository.mutex.Lock()
	defer repository.mutex.Unlock()
	if err := ctx.Err(); err != nil {
		return err
	}
	if repository.closed {
		return fmt.Errorf("%w: repository is closed", ErrUnavailable)
	}
	path := repository.playerPath(playerID)
	record, err := readRecord(path, playerID)
	if err != nil {
		return err
	}
	changed, err := update(record)
	if err != nil {
		return err
	}
	if changed == nil {
		return nil
	}
	if err = validateRecord(changed, playerID); err != nil {
		return err
	}
	data, err := encodeRecord(changed)
	if err != nil {
		return err
	}
	if err = ctx.Err(); err != nil {
		return err
	}
	if err = repository.commit(path, data); err != nil {
		return unavailable("commit player record", err)
	}
	return nil
}

func (repository *FileRepository) playerPath(playerID string) string {
	digest := sha256.Sum256([]byte(playerID))
	return filepath.Join(repository.directory, hex.EncodeToString(digest[:])+".json")
}

func unavailable(operation string, err error) error {
	return fmt.Errorf("%w: %s: %w", ErrUnavailable, operation, err)
}

func readRecord(path, playerID string) (*PlayerRecord, error) {
	info, err := os.Lstat(path)
	if errors.Is(err, os.ErrNotExist) {
		return nil, nil
	}
	if err != nil {
		return nil, unavailable("inspect player record", err)
	}
	if !info.Mode().IsRegular() || info.Size() > maxRecordBytes {
		return nil, fmt.Errorf("%w: player record is not a bounded regular file", ErrCorrupt)
	}
	file, err := os.Open(path)
	if err != nil {
		return nil, unavailable("open player record", err)
	}
	data, readErr := io.ReadAll(io.LimitReader(file, maxRecordBytes+1))
	closeErr := file.Close()
	if readErr != nil {
		return nil, unavailable("read player record", readErr)
	}
	if closeErr != nil {
		return nil, unavailable("close player record", closeErr)
	}
	if len(data) > maxRecordBytes {
		return nil, fmt.Errorf("%w: player record too large", ErrCorrupt)
	}
	var envelope diskEnvelope
	if err = strictJSON(data, &envelope); err != nil {
		return nil, fmt.Errorf("%w: invalid envelope: %v", ErrCorrupt, err)
	}
	digest := sha256.Sum256(envelope.Payload)
	if envelope.FormatVersion != 1 || envelope.SHA256 != hex.EncodeToString(digest[:]) {
		return nil, fmt.Errorf("%w: unsupported envelope or checksum mismatch", ErrCorrupt)
	}
	var record PlayerRecord
	if err = strictJSON(envelope.Payload, &record); err != nil {
		return nil, fmt.Errorf("%w: invalid payload: %v", ErrCorrupt, err)
	}
	if err = validateRecord(&record, playerID); err != nil {
		return nil, err
	}
	return &record, nil
}

func strictJSON(data []byte, target any) error {
	decoder := json.NewDecoder(bytes.NewReader(data))
	decoder.DisallowUnknownFields()
	if err := decoder.Decode(target); err != nil {
		return err
	}
	var extra any
	if err := decoder.Decode(&extra); !errors.Is(err, io.EOF) {
		if err == nil {
			return errors.New("multiple JSON values")
		}
		return err
	}
	return nil
}

func encodeRecord(record *PlayerRecord) ([]byte, error) {
	payload, err := json.Marshal(record)
	if err != nil {
		return nil, fmt.Errorf("%w: encode payload: %v", ErrCorrupt, err)
	}
	digest := sha256.Sum256(payload)
	data, err := json.Marshal(diskEnvelope{FormatVersion: 1, SHA256: hex.EncodeToString(digest[:]), Payload: payload})
	if err != nil {
		return nil, fmt.Errorf("%w: encode envelope: %v", ErrCorrupt, err)
	}
	if len(data) > maxRecordBytes {
		return nil, fmt.Errorf("%w: player record too large", ErrUnavailable)
	}
	return data, nil
}

func validateRecord(record *PlayerRecord, playerID string) error {
	if record.SchemaVersion != 1 || record.PlayerID != playerID || !validID(record.PlayerID) {
		return fmt.Errorf("%w: unsupported schema or wrong player ID", ErrCorrupt)
	}
	if err := validateState(record.State); err != nil {
		return err
	}
	if len(record.Receipts) > ReplayCapacity {
		return fmt.Errorf("%w: too many receipts", ErrCorrupt)
	}
	seen := make(map[string]bool, len(record.Receipts))
	var previousRevision uint64
	for _, receipt := range record.Receipts {
		if err := validateCommand(receipt.Command); err != nil {
			return fmt.Errorf("%w: invalid receipt command", ErrCorrupt)
		}
		result := receipt.Result
		if err := validateState(result.State); err != nil {
			return err
		}
		if seen[receipt.Command.RequestID] || result.Replayed ||
			receipt.Command.ExpectedRevision != result.State.Revision-1 ||
			result.State.Revision <= previousRevision || result.State.Revision > record.State.Revision ||
			result.State.LastUpdatedAt.After(record.State.LastUpdatedAt) ||
			result.Amount < 0 || result.Amount > WalletCapacity || len(result.Message) > 1024 ||
			!validNumber(result.Advance.ElapsedSeconds, 0, 315537897600) ||
			!validNumber(result.Advance.SimulatedSeconds, 0, OfflineCapSeconds) ||
			result.Advance.SimulatedSeconds > result.Advance.ElapsedSeconds {
			return fmt.Errorf("%w: invalid receipt", ErrCorrupt)
		}
		seen[receipt.Command.RequestID] = true
		previousRevision = result.State.Revision
	}
	return nil
}

func atomicWrite(path string, data []byte) (returnedError error) {
	directory, err := os.Open(filepath.Dir(path))
	if err != nil {
		return err
	}
	defer func() { returnedError = errors.Join(returnedError, directory.Close()) }()
	temp, err := os.CreateTemp(filepath.Dir(path), ".player-*.tmp")
	if err != nil {
		return err
	}
	temporaryPath := temp.Name()
	defer func() {
		if err := os.Remove(temporaryPath); err != nil && !errors.Is(err, os.ErrNotExist) {
			returnedError = errors.Join(returnedError, err)
		}
	}()
	if _, err = temp.Write(data); err != nil {
		return errors.Join(err, temp.Close())
	}
	if err = temp.Sync(); err != nil {
		return errors.Join(err, temp.Close())
	}
	if err = temp.Close(); err != nil {
		return err
	}
	// Same-directory rename is the commit point. The old file or complete new
	// file is visible after a crash; directory fsync makes the rename durable.
	if err = os.Rename(temporaryPath, path); err != nil {
		return err
	}
	if err = directory.Sync(); err != nil {
		return fmt.Errorf("commit outcome uncertain after rename: %w", err)
	}
	return nil
}
