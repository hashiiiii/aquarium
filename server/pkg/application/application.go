package application

import "errors"

// cmd/server/internal のなかに入れてしまう方がいいかも
// 基本はどの環境でも同じコードが動くようにしたい。
// そのためにはできるだけ環境を意識しなくても済むような作りにすべき

type Environment string

const (
	Production  Environment = "production"
	Development Environment = "development"
	Test        Environment = "test"
)

var environments = map[string]Environment{
	string(Production):  Production,
	string(Development): Development,
	string(Test):        Test,
}

func (e Environment) String() string {
	return string(e)
}

type Deployment string

const (
	Remote Deployment = "remote"
	Local  Deployment = "local"
)

var deployments = map[string]Deployment{
	string(Remote): Remote,
	string(Local):  Local,
}

func (d Deployment) String() string {
	return string(d)
}

type Application struct {
	environment Environment
	deployment  Deployment
}

var (
	ErrInvalidEnvironment = errors.New("invalid environment")
	ErrInvalidDeployment  = errors.New("invalid deployment")
)

func New(environment string, deployment string) (*Application, error) {
	e, ok := environments[environment]
	if !ok {
		return nil, ErrInvalidEnvironment
	}

	d, ok := deployments[deployment]
	if !ok {
		return nil, ErrInvalidDeployment
	}

	return &Application{environment: e, deployment: d}, nil
}

func (e *Application) IsProduction() bool {
	return e.environment == Production
}

func (e *Application) IsDevelopment() bool {
	return e.environment == Development
}

func (e *Application) IsTest() bool {
	return e.environment == Test
}

func (e *Application) IsRemote() bool {
	return e.deployment == Remote
}

func (e *Application) IsLocal() bool {
	return e.deployment == Local
}
