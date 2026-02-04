package handler

import (
	"context"
	"crypto/rand"
	"encoding/hex"
	"fmt"

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

// TODO: 直す
func (*SessionHandler) Login(ctx context.Context, req *sessionv1.LoginRequest) (*sessionv1.LoginResponse, error) {
	// TODO: 本来はここで以下を行う
	// 1. public_key で signed_device_id を検証
	// 2. DB からプレイヤーを取得または作成
	// 3. JWT トークンを生成

	// ダミーのセッショントークン生成
	token, err := generateToken()
	if err != nil {
		return nil, connect.NewError(
			connect.CodeInternal,
			fmt.Errorf("failed to generate session token"),
		)
	}

	return &sessionv1.LoginResponse{
		SessionToken: token,
		PlayerId:     "player_" + req.GetDeviceId(),
		IsNewPlayer:  true,
	}, nil
}

func generateToken() (string, error) {
	b := make([]byte, 32)
	if _, err := rand.Read(b); err != nil {
		return "", err
	}
	return hex.EncodeToString(b), nil
}
