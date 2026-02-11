package dotenv

import (
	"fmt"
	"os"
	"strings"
)

type Dotenv struct {
	missing []string
}

func New() *Dotenv {
	return &Dotenv{}
}

func (e *Dotenv) Require(key string) string {
	if key == "" {
		panic("key is empty")
	}

	v := os.Getenv(key)
	if v == "" {
		e.missing = append(e.missing, key)
	}

	return v
}

func (e *Dotenv) Optional(key string, fallback string) string {
	if key == "" {
		panic("key is empty")
	}
	if fallback == "" {
		panic("fallback is empty")
	}

	v := os.Getenv(key)
	if v == "" {
		v = fallback
	}

	return v
}

func (e *Dotenv) Err() error {
	if len(e.missing) > 0 {
		return fmt.Errorf("missing required environment variables: %s", strings.Join(e.missing, ","))
	}

	return nil
}
