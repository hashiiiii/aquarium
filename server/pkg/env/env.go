package env

import (
	"fmt"
	"os"
	"strings"
)

type Env struct {
	missing []string
}

func (e *Env) Require(key string) (string, error) {
	if key == "" {
		return "", fmt.Errorf("key is empty")
	}

	v := os.Getenv(key)
	if v == "" {
		e.missing = append(e.missing, key)
	}

	return v, nil
}

func (e *Env) Optional(key string, fallback string) (string, error) {
	if key == "" {
		return "", fmt.Errorf("key is empty")
	}
	if fallback == "" {
		return "", fmt.Errorf("fallback is empty")
	}

	v := os.Getenv(key)
	if v == "" {
		v = fallback
	}

	return v, nil
}

func (e *Env) Err() error {
	if len(e.missing) > 0 {
		return fmt.Errorf("missing required environment variables: %s", strings.Join(e.missing, ","))
	}

	return nil
}
