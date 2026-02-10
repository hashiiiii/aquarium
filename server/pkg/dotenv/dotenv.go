package dotenv

import (
	"fmt"
	"os"
	"strings"
)

type Dotenv struct {
	missing []string
}

func (e *Dotenv) Require(key string) (string, error) {
	if key == "" {
		return "", fmt.Errorf("key is empty")
	}

	v := os.Getenv(key)
	if v == "" {
		e.missing = append(e.missing, key)
	}

	return v, nil
}

func (e *Dotenv) Optional(key string, fallback string) (string, error) {
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

func (e *Dotenv) Err() error {
	if len(e.missing) > 0 {
		return fmt.Errorf("missing required environment variables: %s", strings.Join(e.missing, ","))
	}

	return nil
}
