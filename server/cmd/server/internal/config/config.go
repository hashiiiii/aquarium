package config

import (
	"fmt"

	"github.com/hashiiiii/aquarium/pkg/application"
	"github.com/hashiiiii/aquarium/pkg/dotenv"
)

type Config interface {
	JWTCommonKey() string
	ServerAddr() string
}

func New(app *application.Application, e *dotenv.Dotenv) (Config, error) {
	if app.IsLocal() {
		c, err := newLocal(e)
		if err != nil {
			return nil, fmt.Errorf("failed to create local config: %w", err)
		}
		return c, nil
	}

	c, err := newRemote(e)
	if err != nil {
		return nil, fmt.Errorf("failed to create remote config: %w", err)
	}
	return c, nil
}
