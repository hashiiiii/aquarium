package handler

import (
	"context"
	"encoding/base64"
	"errors"
	"strings"

	"connectrpc.com/connect"
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
	// DB から select して公開鍵があるかチェックする
	// あればその鍵で、なければリクエストの鍵で signedDeviceId を復号する
	// そして検証する
	rawPublicKey, err := base64.StdEncoding.DecodeString(req.PublicKey)
	if err != nil || len(rawPublicKey) != 65 || rawPublicKey[0] != 0x04 {
		return nil, connect.NewError(connect.CodeInvalidArgument, errors.New("invalid public key"))
	}
	// transfer_id での select
	// platform_id での select
	// device_id での select
	return &sessionv1.LoginResponse{
		SessionToken: "dummy_token",
		PlayerId:     strings.Join([]string{"player_", req.DeviceId}, ","),
		IsNewPlayer:  true,
	}, nil
}
