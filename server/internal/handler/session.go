package handler

import (
	"context"
	"strings"

	sessionv1 "github.com/hashiiiii/aquarium/gen/session/v1"
	"github.com/hashiiiii/aquarium/gen/session/v1/sessionv1connect"
)

var _ sessionv1connect.SessionServiceHandler = (*SessionHandler)(nil)

type SessionHandler struct {
}

func NewSessionHandler() *SessionHandler {
	return &SessionHandler{}
}

func (*SessionHandler) Login(ctx context.Context, req *sessionv1.LoginRequest) (*sessionv1.LoginResponse, error) {
	// TODO: server/docs/login.md を見つつ実装をする

	return &sessionv1.LoginResponse{
		SessionToken: "dummy_token",
		PlayerId:     strings.Join([]string{"player_", req.DeviceId}, ","),
		IsNewPlayer:  true,
	}, nil
}
