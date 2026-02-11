package config

type remoteConfig struct{}

func (c *remoteConfig) JWTCommonKey() string {
	return ""
}

func (c *remoteConfig) ServerAddr() string {
	return ""
}

func newRemote() (*remoteConfig, error) {
	return &remoteConfig{}, nil
}
