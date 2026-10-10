package main

import (
	"fmt"
	"io"
	"os"
	"os/exec"
	"path/filepath"
)

func main() {
	if err := generate(); err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}
}

func generate() error {
	serverDir, err := os.Getwd()
	if err != nil {
		return fmt.Errorf("get server working directory: %w", err)
	}

	schemaDir, err := os.MkdirTemp("", "aquarium-proto-")
	if err != nil {
		return fmt.Errorf("create isolated schema directory: %w", err)
	}
	defer os.RemoveAll(schemaDir)

	schemaPath := filepath.Join(schemaDir, "aquarium", "v1", "aquarium.proto")
	if err := os.MkdirAll(filepath.Dir(schemaPath), 0o755); err != nil {
		return fmt.Errorf("create schema directory: %w", err)
	}
	if err := copyFile(filepath.Join(serverDir, "..", "api", "proto", "aquarium", "v1", "aquarium.proto"), schemaPath); err != nil {
		return err
	}

	if err := run(serverDir, "buf", "lint", schemaDir, "--config", "buf.reef.yaml"); err != nil {
		return err
	}
	if err := run(serverDir, "buf", "format", schemaDir, "--diff", "--exit-code"); err != nil {
		return err
	}
	return run(serverDir, "buf", "generate", schemaDir, "--config", "buf.reef.yaml", "--template", "buf.reef.gen.yaml")
}

func copyFile(source, destination string) error {
	input, err := os.Open(source)
	if err != nil {
		return fmt.Errorf("open schema %s: %w", source, err)
	}
	defer input.Close()

	output, err := os.Create(destination)
	if err != nil {
		return fmt.Errorf("create schema copy %s: %w", destination, err)
	}
	if _, err := io.Copy(output, input); err != nil {
		output.Close()
		return fmt.Errorf("copy schema to %s: %w", destination, err)
	}
	if err := output.Close(); err != nil {
		return fmt.Errorf("close schema copy %s: %w", destination, err)
	}
	return nil
}

func run(directory, executable string, arguments ...string) error {
	command := exec.Command(executable, arguments...)
	command.Dir = directory
	command.Stdout = os.Stdout
	command.Stderr = os.Stderr
	if err := command.Run(); err != nil {
		return fmt.Errorf("run %s %v: %w", executable, arguments, err)
	}
	return nil
}
