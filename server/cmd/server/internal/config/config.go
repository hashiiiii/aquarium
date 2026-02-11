package config

import (
	"fmt"

	"github.com/hashiiiii/aquarium/pkg/application"
)

type Config interface {
	JWTCommonKey() string
	ServerAddr() string
}

func New(app *application.Application) (Config, error) {
	if app.IsLocal() {
		c, err := newLocal()
		if err != nil {
			return nil, fmt.Errorf("failed to new local config: %w", err)
		}
		return c, nil
	}

	c, err := newRemote()
	if err != nil {
		return nil, fmt.Errorf("failed to new remote config: %w", err)
	}
	return c, nil
}
