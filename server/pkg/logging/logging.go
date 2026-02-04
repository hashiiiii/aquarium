package logging

import (
	"context"
	"log"

	"connectrpc.com/connect"
)

// TODO: slog + Loki スタック向けに修正する
func NewInterceptor() connect.UnaryInterceptorFunc {
	return func(next connect.UnaryFunc) connect.UnaryFunc {
		return func(ctx context.Context, req connect.AnyRequest) (connect.AnyResponse, error) {
			res, err := next(ctx, req)
			if err != nil {
				log.Printf("[ERROR] %s: %v", req.Spec().Procedure, err)
			}

			return res, err
		}
	}
}
