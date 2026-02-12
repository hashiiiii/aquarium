package config

import "github.com/hashiiiii/aquarium/pkg/dotenv"

type remoteConfig struct {
	dotenv *dotenv.Dotenv
}

func (c *remoteConfig) JWTCommonKey() string {
	return "not implemented"
}

func (c *remoteConfig) ServerAddr() string {
	return "not implemented"
}

func newRemote(e *dotenv.Dotenv) (*remoteConfig, error) {
	return &remoteConfig{dotenv: e}, nil
}
