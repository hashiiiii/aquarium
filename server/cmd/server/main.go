package main

import (
	"database/sql"
	"fmt"
	"log"
	"net/http"
	"os"
	"time"

	_ "github.com/go-sql-driver/mysql"
	"github.com/hashiiiii/aquarium/cmd/server/internal"
	"github.com/hashiiiii/aquarium/pkg/graceful"
	"golang.org/x/net/http2"
	"golang.org/x/net/http2/h2c"
)

func main() {
	server, err := newServer()
	if err != nil {
		log.Fatalf("server error: %v", err)
	}

	go func() {
		if err := server.ListenAndServe(); err != http.ErrServerClosed {
			log.Fatalf("listen and serve error: %v", err)
		}
	}()

	if err := graceful.WaitTerminateSignal(server); err != nil {
		log.Printf("shutdown error: %v", err)
		os.Exit(1)
	}

	log.Print("server stopped gracefully")
}

func newServer() (*http.Server, error) {
	mux := http.NewServeMux()

	internalMux, err := internal.NewMux()
	if err != nil {
		return nil, fmt.Errorf("failed to new internal mux: %w", err)
	}

	mux.Handle("/", internalMux)

	// ========= mock =========
	db, err := sql.Open("mysql", "root@tcp(localhost:3306)/")
	if err != nil {
		log.Fatalf("failed to open db: %v", err)
	}
	defer db.Close()
	mux.HandleFunc("/ping", func(w http.ResponseWriter, r *http.Request) {
		err = db.Ping()
		if err != nil {
			http.Error(w, "DB connection error", http.StatusInternalServerError)
			return
		}
	})
	// ========================

	return &http.Server{
		Addr:              ":8080",
		Handler:           h2c.NewHandler(mux, &http2.Server{}),
		ReadTimeout:       10 * time.Second,
		WriteTimeout:      10 * time.Second,
		ReadHeaderTimeout: 10 * time.Second,
	}, nil
}
