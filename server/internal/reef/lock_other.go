//go:build !linux && !darwin && !freebsd && !openbsd && !netbsd && !dragonfly

package reef

import (
	"errors"
	"os"
)

func lockDirectory(_ string) (*os.File, error) {
	return nil, errors.New("local reef persistence requires a Unix platform with advisory flock")
}
