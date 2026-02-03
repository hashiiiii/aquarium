package internal

import (
	"net/http"

	"github.com/hashiiiii/aquarium/gen/session/v1/sessionv1connect"
	"github.com/hashiiiii/aquarium/internal/handler"
)

func NewMux() (*http.ServeMux, error) {
	mux := http.NewServeMux()
	mux.Handle(sessionv1connect.NewSessionServiceHandler(handler.NewSessionHandler()))
	return mux, nil
}
