// reef-local is a DEVELOPMENT-ONLY aquarium server. It trusts a caller-selected
// player header and is deliberately restricted to this machine's loopback.
// It is not an authentication system and must not be exposed through a proxy.
package main

import (
	"context"
	"errors"
	"flag"
	"fmt"
	"io"
	"log"
	"net"
	"net/http"
	"os"
	"os/signal"
	"syscall"
	"time"

	"connectrpc.com/connect"
	"github.com/hashiiiii/aquarium/gen/aquarium/v1/aquariumv1connect"
	"github.com/hashiiiii/aquarium/internal/handler"
	"github.com/hashiiiii/aquarium/internal/reef"
)

const localAddress = "127.0.0.1:8081"

func main() {
	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()
	if err := run(ctx, os.Args[1:], os.Stderr); err != nil {
		log.Print(err)
		os.Exit(1)
	}
}

func run(ctx context.Context, args []string, output io.Writer) (runErr error) {
	flags := flag.NewFlagSet("reef-local", flag.ContinueOnError)
	flags.SetOutput(output)
	dev := flags.Bool("dev", false, "required: enable insecure DEVELOPMENT-ONLY player identity")
	dataDir := flags.String("data-dir", ".reef-data", "directory for private local development saves")
	if err := flags.Parse(args); err != nil {
		return err
	}
	if !*dev {
		return errors.New("refusing to start: --dev is required; this host has no production authentication")
	}
	if flags.NArg() != 0 {
		return errors.New("unexpected positional arguments")
	}
	if *dataDir == "" {
		return errors.New("--data-dir must not be empty")
	}
	repo, err := reef.NewFileRepository(*dataDir)
	if err != nil {
		return fmt.Errorf("open local repository: %w", err)
	}
	defer func() { runErr = errors.Join(runErr, repo.Close()) }()

	listener, err := net.Listen("tcp4", localAddress)
	if err != nil {
		return fmt.Errorf("listen on %s: %w", localAddress, err)
	}
	defer func() { _ = listener.Close() }()
	server := newLocalServer(reef.NewService(repo, time.Now), localAddress)
	logger := log.New(output, "", log.LstdFlags)
	logger.Printf("DEVELOPMENT ONLY: listening on http://%s; %s selects any local player. Never proxy or expose this port.", localAddress, handler.DevelopmentPlayerHeader)

	serveErr := make(chan error, 1)
	go func() { serveErr <- server.Serve(listener) }()
	select {
	case err := <-serveErr:
		if !errors.Is(err, http.ErrServerClosed) {
			return fmt.Errorf("serve: %w", err)
		}
		return nil
	case <-ctx.Done():
		shutdownCtx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
		defer cancel()
		if err := server.Shutdown(shutdownCtx); err != nil {
			_ = server.Close()
			return fmt.Errorf("shutdown: %w", err)
		}
		if err := <-serveErr; err != nil && !errors.Is(err, http.ErrServerClosed) {
			return fmt.Errorf("serve: %w", err)
		}
		return nil
	}
}

func newLocalServer(service *reef.Service, authority string) *http.Server {
	mux := http.NewServeMux()
	mux.Handle(aquariumv1connect.NewAquariumServiceHandler(handler.NewReefHandler(service),
		connect.WithInterceptors(handler.NewDevelopmentIdentityInterceptor()),
		connect.WithReadMaxBytes(8<<10),
		connect.WithSendMaxBytes(1<<20),
	))
	mux.HandleFunc("GET /healthz", func(w http.ResponseWriter, _ *http.Request) {
		w.Header().Set("Content-Type", "application/json")
		_, _ = io.WriteString(w, "{\"status\":\"ok\",\"mode\":\"development-only\"}\n")
	})
	protocols := new(http.Protocols)
	protocols.SetHTTP1(true)
	protocols.SetUnencryptedHTTP2(true)
	return &http.Server{
		Addr:              authority,
		Handler:           localRequestsOnly(authority, http.MaxBytesHandler(mux, 64<<10)),
		Protocols:         protocols,
		ReadHeaderTimeout: 5 * time.Second,
		ReadTimeout:       10 * time.Second,
		WriteTimeout:      15 * time.Second,
		IdleTimeout:       60 * time.Second,
		MaxHeaderBytes:    16 << 10,
		HTTP2: &http.HTTP2Config{
			MaxConcurrentStreams:          32,
			MaxReadFrameSize:              16 << 10,
			MaxReceiveBufferPerConnection: 1 << 20,
			MaxReceiveBufferPerStream:     64 << 10,
		},
	}
}

// These checks reduce accidental exposure and browser DNS-rebinding attacks.
// They do not authenticate local programs or protect against a trusted proxy.
func localRequestsOnly(authority string, next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Cache-Control", "no-store")
		w.Header().Set("X-Content-Type-Options", "nosniff")
		if r.Host != authority {
			http.Error(w, "development host requires its numeric loopback address", http.StatusForbidden)
			return
		}
		if len(r.Header.Values("Origin")) != 0 || r.Header.Get("Sec-Fetch-Site") == "cross-site" {
			http.Error(w, "browser-origin requests are disabled on this development host", http.StatusForbidden)
			return
		}
		next.ServeHTTP(w, r)
	})
}
