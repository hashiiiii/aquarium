package config

type localConfig struct{}

func (c *localConfig) JWTCommonKey() string {
	return ""
}

func (c *localConfig) ServerAddr() string {
	return ""
}

func newLocal() (*localConfig, error) {
	return &localConfig{}, nil
}
