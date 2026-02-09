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

// **
// generation protocol for JWT
// **
// header (JSON) -> base64url -> AAA
// payload (JSON) -> base64url -> BBB
// AAA.BBB -> sha256 -> base64url -> CCC
// AAA.BBB.CCC
// **

var (
	ErrInvalidSignature = errors.New("invalid signature")
	ErrJWTExpired       = errors.New("jwt is expired")
	ErrInvalidJWTFormat = errors.New("invalid jwt format")
)

type Claims map[string]any

func Verify(jwt string, key []byte) (Claims, error) {
	parts := strings.Split(jwt, ".")
	if len(parts) != 3 {
		return nil, ErrInvalidJWTFormat
	}

	header, payload, signature := parts[0], parts[1], parts[2]
	headerAndPayload := strings.Join([]string{header, payload}, ".")

	// encrypt the header and payload with a shared key
	mac := hmac.New(sha256.New, key)
	mac.Write([]byte(headerAndPayload))
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

	expVal, ok := claims["exp"]
	if !ok {
		return nil, ErrInvalidJWTFormat
	}

	// when unmarshaling a JSON number into Any type, it is treated as float64
	// even if the original value is integer
	exp, ok := expVal.(float64)
	if !ok {
		return nil, ErrInvalidJWTFormat
	}
	if int64(exp) < time.Now().Unix() {
		return nil, ErrJWTExpired
	}

	return claims, nil
}

func Sign(claims Claims, key []byte) (string, error) {
	// HS256 is the name defined in the JWT standard for HMAC-SHA256
	// SHA256 is the hash function used within HS256
	header := base64.RawURLEncoding.EncodeToString([]byte(`{"alg":"HS256","typ":"JWT"}`))

	payloadJSON, err := json.Marshal(claims)
	if err != nil {
		return "", fmt.Errorf("marshal claims: %w", err)
	}
	payload := base64.RawURLEncoding.EncodeToString(payloadJSON)

	headerAndPayload := strings.Join([]string{header, payload}, ".")

	mac := hmac.New(sha256.New, key)
	mac.Write([]byte(headerAndPayload))
	signature := base64.RawURLEncoding.EncodeToString(mac.Sum(nil))

	return strings.Join([]string{headerAndPayload, signature}, "."), nil
}
