package handler

import (
	"context"

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
	return &sessionv1.LoginResponse{}, nil
}
