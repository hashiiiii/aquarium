package main

import (
	"encoding/json"
	"fmt"
	"io"
	"os"
	"os/exec"
	"path/filepath"
	"regexp"
	"runtime"
	"strings"
)

// hookSoundMap maps each hook event to its corresponding sound folder.
var hookSoundMap = map[string]string{
	"SessionStart":       "sessionstart",
	"SessionEnd":         "sessionend",
	"UserPromptSubmit":   "userpromptsubmit",
	"PreToolUse":         "pretooluse",
	"PostToolUse":        "posttooluse",
	"PostToolUseFailure": "posttoolusefailure",
	"PermissionRequest":  "permissionrequest",
	"Notification":       "notification",
	"Stop":               "stop",
	"SubagentStart":      "subagentstart",
	"SubagentStop":       "subagentstop",
	"PreCompact":         "precompact",
	"Setup":              "setup",
	"TeammateIdle":       "teammateidle",
	"TaskCompleted":      "taskcompleted",
}

// bashPattern holds a compiled regex and the sound name to play when matched.
type bashPattern struct {
	re        *regexp.Regexp
	soundName string
}

var bashPatterns = []bashPattern{
	{regexp.MustCompile(`git commit`), "pretooluse-git-committing"},
}

// hookData represents the JSON payload from Claude Code's stdin.
type hookData struct {
	HookEventName string                 `json:"hook_event_name"`
	ToolName      string                 `json:"tool_name"`
	ToolInput     map[string]interface{} `json:"tool_input"`
	// Capture all remaining fields for logging.
	raw json.RawMessage
}

// hooksDir returns the absolute path to .claude/hooks/ based on the binary's location.
func hooksDir() string {
	exe, err := os.Executable()
	if err != nil {
		// Fallback: use working directory
		return filepath.Join(".", ".claude", "hooks")
	}
	exe, _ = filepath.EvalSymlinks(exe)
	// Binary is at .claude/hooks/bin/hooks → parent is bin/, grandparent is hooks/
	return filepath.Dir(filepath.Dir(exe))
}

// loadJSON reads a JSON file into a map. Returns nil if the file doesn't exist or is invalid.
func loadJSON(path string) map[string]interface{} {
	data, err := os.ReadFile(path)
	if err != nil {
		return nil
	}
	var m map[string]interface{}
	if err := json.Unmarshal(data, &m); err != nil {
		return nil
	}
	return m
}

// isHookDisabled checks hooks-config.local.json then hooks-config.json
// to determine if a specific hook is disabled.
func isHookDisabled(eventName string) bool {
	configDir := filepath.Join(hooksDir(), "config")
	key := "disable" + eventName + "Hook"

	// Local config takes priority
	if local := loadJSON(filepath.Join(configDir, "hooks-config.local.json")); local != nil {
		if v, ok := local[key]; ok {
			if b, ok := v.(bool); ok {
				return b
			}
		}
	}

	// Fallback to shared config
	if shared := loadJSON(filepath.Join(configDir, "hooks-config.json")); shared != nil {
		if v, ok := shared[key]; ok {
			if b, ok := v.(bool); ok {
				return b
			}
		}
	}

	return false
}

// isLoggingDisabled checks if logging is disabled in config.
func isLoggingDisabled() bool {
	configDir := filepath.Join(hooksDir(), "config")
	key := "disableLogging"

	if local := loadJSON(filepath.Join(configDir, "hooks-config.local.json")); local != nil {
		if v, ok := local[key]; ok {
			if b, ok := v.(bool); ok {
				return b
			}
		}
	}

	if shared := loadJSON(filepath.Join(configDir, "hooks-config.json")); shared != nil {
		if v, ok := shared[key]; ok {
			if b, ok := v.(bool); ok {
				return b
			}
		}
	}

	return false
}

// logHookData appends the raw JSON to hooks-log.jsonl.
func logHookData(rawJSON []byte) {
	if isLoggingDisabled() {
		return
	}

	logsDir := filepath.Join(hooksDir(), "logs")
	_ = os.MkdirAll(logsDir, 0755)

	logPath := filepath.Join(logsDir, "hooks-log.jsonl")
	f, err := os.OpenFile(logPath, os.O_APPEND|os.O_CREATE|os.O_WRONLY, 0644)
	if err != nil {
		return
	}
	defer f.Close()

	// Pretty-print for readability (matching Python's indent=2 behavior)
	var pretty json.RawMessage
	if err := json.Unmarshal(rawJSON, &pretty); err == nil {
		formatted, err := json.MarshalIndent(pretty, "", "  ")
		if err == nil {
			f.Write(formatted)
			f.WriteString("\n")
			return
		}
	}
	f.Write(rawJSON)
	f.WriteString("\n")
}

// getAudioPlayer returns the command and args for the current platform's audio player.
func getAudioPlayer() []string {
	switch runtime.GOOS {
	case "darwin":
		return []string{"afplay"}
	case "linux":
		players := [][]string{
			{"paplay"},
			{"aplay"},
			{"ffplay", "-nodisp", "-autoexit"},
			{"mpg123", "-q"},
		}
		for _, p := range players {
			if _, err := exec.LookPath(p[0]); err == nil {
				return p
			}
		}
		return nil
	case "windows":
		return []string{"WINDOWS"}
	default:
		return nil
	}
}

// playSound plays the sound file for the given sound name.
func playSound(soundName string) bool {
	if strings.Contains(soundName, "/") || strings.Contains(soundName, "\\") || strings.Contains(soundName, "..") {
		fmt.Fprintf(os.Stderr, "Invalid sound name: %s\n", soundName)
		return false
	}

	player := getAudioPlayer()
	if player == nil {
		return false
	}

	folderName := strings.SplitN(soundName, "-", 2)[0]
	soundsDir := filepath.Join(hooksDir(), "sounds", folderName)

	isWindows := player[0] == "WINDOWS"

	extensions := []string{".wav", ".mp3"}
	if isWindows {
		extensions = []string{".wav"}
	}

	for _, ext := range extensions {
		filePath := filepath.Join(soundsDir, soundName+ext)
		if _, err := os.Stat(filePath); err != nil {
			continue
		}

		if isWindows {
			// On Windows, use PowerShell to play WAV
			cmd := exec.Command("powershell", "-c",
				fmt.Sprintf(`(New-Object Media.SoundPlayer '%s').PlaySync()`, filePath))
			cmd.Stdout = nil
			cmd.Stderr = nil
			return cmd.Start() == nil
		}

		// Unix: fire and forget
		cmd := exec.Command(player[0], append(player[1:], filePath)...)
		cmd.Stdout = nil
		cmd.Stderr = nil
		cmd.SysProcAttr = nil
		return cmd.Start() == nil
	}

	return false
}

// detectBashCommandSound checks if the command matches any special bash patterns.
func detectBashCommandSound(command string) string {
	command = strings.TrimSpace(command)
	if command == "" {
		return ""
	}
	for _, bp := range bashPatterns {
		if bp.re.MatchString(command) {
			return bp.soundName
		}
	}
	return ""
}

// getSoundName determines which sound to play based on the hook event and context.
func getSoundName(data hookData) string {
	if data.HookEventName == "PreToolUse" && data.ToolName == "Bash" {
		if cmd, ok := data.ToolInput["command"].(string); ok {
			if special := detectBashCommandSound(cmd); special != "" {
				return special
			}
		}
	}
	return hookSoundMap[data.HookEventName]
}

func main() {
	rawJSON, err := io.ReadAll(os.Stdin)
	if err != nil || len(strings.TrimSpace(string(rawJSON))) == 0 {
		os.Exit(0)
	}

	logHookData(rawJSON)

	var data hookData
	if err := json.Unmarshal(rawJSON, &data); err != nil {
		fmt.Fprintf(os.Stderr, "Error parsing JSON input: %v\n", err)
		os.Exit(0)
	}

	if isHookDisabled(data.HookEventName) {
		os.Exit(0)
	}

	if soundName := getSoundName(data); soundName != "" {
		playSound(soundName)
	}
}
