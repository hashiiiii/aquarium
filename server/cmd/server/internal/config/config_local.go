package config

import "github.com/hashiiiii/aquarium/pkg/dotenv"

type localConfig struct {
	dotenv *dotenv.Dotenv
}

func (c *localConfig) JWTCommonKey() string {
	return c.dotenv.Require("AQUA_JWT_COMMON_KEY")
}

func (c *localConfig) ServerAddr() string {
	return c.dotenv.Require("AQUA_SERVER_ADDR")
}

func newLocal(e *dotenv.Dotenv) (*localConfig, error) {
	return &localConfig{dotenv: e}, nil
}
