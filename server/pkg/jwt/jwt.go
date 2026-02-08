package jwt

import (
	"crypto/hmac"
	"crypto/sha256"
	"encoding/base64"
	"encoding/json"
	"errors"
	"fmt"
	"strings"
	"time"
)

var (
	ErrInvalidSignature = errors.New("invalid signature")
	ErrExpired          = errors.New("jwt is expired")
)

type Claims map[string]any

func Verify(jwt string, key []byte) (Claims, error) {
	first := strings.Index(jwt, ".")
	last := strings.LastIndex(jwt, ".")

	headerPayload := jwt[:last]
	payload := jwt[first+1 : last]
	signature := jwt[last+1:]

	mac := hmac.New(sha256.New, key)
	mac.Write([]byte(headerPayload))
	expected := mac.Sum(nil)

	actual, err := base64.RawURLEncoding.DecodeString(signature)
	if err != nil {
		return nil, fmt.Errorf("decode signature: %w", err)
	}

	if !hmac.Equal(expected, actual) {
		return nil, ErrInvalidSignature
	}

	payloadJSON, err := base64.RawURLEncoding.DecodeString(payload)
	if err != nil {
		return nil, fmt.Errorf("decode payload: %w", err)
	}

	var claims Claims
	if err := json.Unmarshal(payloadJSON, &claims); err != nil {
		return nil, fmt.Errorf("unmarshal claims: %w", err)
	}

	exp, ok := claims["exp"].(float64)
	if !ok {
		return nil, fmt.Errorf("failed to get exp")
	}
	if int64(exp) < time.Now().Unix() {
		return nil, ErrExpired
	}

	return claims, nil
}

func Sign() (string, error) {
	return "mock", nil
}
