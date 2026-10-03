//go:build linux || darwin || freebsd || openbsd || netbsd || dragonfly

package reef

import (
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"syscall"
)

func lockDirectory(directory string) (*os.File, error) {
	path := filepath.Join(directory, ".reef.lock")
	if info, err := os.Lstat(path); err == nil && !info.Mode().IsRegular() {
		return nil, fmt.Errorf("lock path is not a regular file")
	} else if err != nil && !errors.Is(err, os.ErrNotExist) {
		return nil, err
	}
	file, err := os.OpenFile(path, os.O_CREATE|os.O_RDWR, 0o600)
	if err != nil {
		return nil, err
	}
	if err = syscall.Flock(int(file.Fd()), syscall.LOCK_EX|syscall.LOCK_NB); err != nil {
		return nil, errors.Join(fmt.Errorf("another process owns the data directory: %w", err), file.Close())
	}
	// Never unlink the lock file: doing so can leave two processes holding locks
	// on different inodes. Closing this descriptor releases the advisory lock.
	return file, nil
}
