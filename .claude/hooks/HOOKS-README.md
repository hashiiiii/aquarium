# HOOKS-README
contains all the details, scripts, and instructions for the hooks

## Hook Events Overview - [Official 15 Hooks](https://docs.claude.com/en/docs/claude-code/hooks-guide)
Claude Code provides several hook events that run at different points in the workflow:
1. SessionStart: Runs when Claude Code starts a new session or resumes one
2. SessionEnd: Runs when a Claude Code session ends
3. UserPromptSubmit: Runs when the user submits a prompt
4. PreToolUse: Runs before tool calls (can block them)
5. PostToolUse: Runs after tool calls complete
6. PostToolUseFailure: Runs after tool calls fail
7. PermissionRequest: Runs when Claude asks for a permission decision
8. Notification: Runs when Claude Code sends notifications
9. Stop: Runs when Claude Code finishes responding
10. SubagentStart: Runs when a subagent task begins
11. SubagentStop: Runs when a subagent task completes
12. PreCompact: Runs before Claude runs context compaction
13. Setup: Runs during setup/maintenance workflows
14. TeammateIdle: Runs when an Agent Team teammate goes idle
15. TaskCompleted: Runs when a tracked task reaches completion

## Prerequisites

The hook handler is a pre-compiled Go binary (`.claude/hooks/bin/hooks`). No runtime dependencies are required.

#### Building from source (optional)

If you need to rebuild the binary:
- **Go 1.21+**: `go version`
- Build: `cd .claude/hooks/scripts && go build -o ../bin/hooks .`

#### Audio Players (per platform)

- **macOS**: `afplay` (built-in, no installation needed)
- **Linux**: `sudo apt install pulseaudio-utils` (or aplay, mpg123, ffplay)
- **Windows**: Built-in PowerShell audio playback

## Configuring Hooks (Enable/Disable)

Hooks can be easily enabled or disabled at both the global and individual levels.

### Disable All Hooks at Once

Edit `.claude/settings.local.json` and set:
```json
{
  "disableAllHooks": true
}
```

**Note:** The `.claude/settings.local.json` file is git-ignored, so each user can configure their own hook preferences without affecting the team's shared settings in `.claude/settings.json`.

### Disable Individual Hooks

For granular control, you can disable specific hooks by editing the hooks configuration files.

#### Configuration Files

There are two configuration files for managing individual hooks:

1. **`.claude/hooks/config/hooks-config.json`** - The shared/default configuration that is committed to git
2. **`.claude/hooks/config/hooks-config.local.json`** - Your personal overrides (git-ignored)

The local config file (`.local.json`) takes precedence over the shared config, allowing each developer to customize their hook behavior without affecting the team.

#### Shared Configuration

Edit `.claude/hooks/config/hooks-config.json` for team-wide defaults:

```json
{
  "disableSessionStartHook": false,
  "disableSessionEndHook": false,
  "disableUserPromptSubmitHook": false,
  "disablePreToolUseHook": false,
  "disablePostToolUseHook": false,
  "disablePostToolUseFailureHook": false,
  "disablePermissionRequestHook": false,
  "disableNotificationHook": false,
  "disableSubagentStartHook": false,
  "disableStopHook": false,
  "disableSubagentStopHook": false,
  "disablePreCompactHook": false,
  "disableSetupHook": false,
  "disableTeammateIdleHook": false,
  "disableTaskCompletedHook": false,
  "disableLogging": true
}
```

#### Local Configuration (Personal Overrides)

Create or edit `.claude/hooks/config/hooks-config.local.json` for personal preferences:

```json
{
  "disablePostToolUseHook": true,
  "disableSessionStartHook": true
}
```

In this example, only the PostToolUse and SessionStart hooks are overridden locally. All other hooks will use the shared configuration values.

**Note:** Individual hook toggles are checked by the hook binary (`.claude/hooks/bin/hooks`). Local settings override shared settings, and if a hook is disabled, the binary exits silently without playing sounds or executing hook logic.

### Text to Speech (TTS)
website used to generate sounds: https://elevenlabs.io/
voice used: Samara X
