package internal

import (
	"net/http"

	"connectrpc.com/connect"
	"connectrpc.com/validate"
	"github.com/hashiiiii/aquarium/gen/session/v1/sessionv1connect"
	"github.com/hashiiiii/aquarium/internal/handler"
	"github.com/hashiiiii/aquarium/internal/handler/interceptor/authorization"
	"github.com/hashiiiii/aquarium/pkg/logging"
)

func NewMux() (*http.ServeMux, error) {
	// req: 上から, res: 下から
	opts := connect.WithInterceptors(
		logging.NewInterceptor(),
		validate.NewInterceptor(),
		authorization.NewInterceptor(),
	)

	mux := http.NewServeMux()
	mux.Handle(sessionv1connect.NewSessionServiceHandler(handler.NewSessionHandler(), opts))
	return mux, nil
}
