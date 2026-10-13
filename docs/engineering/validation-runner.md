# Headless Unity Validation Runner & ShellWarden Protocol (#14 / Phase 4) — 2026-10-13

## 1. Context and Problem Statement

Autonomous cloud coding agents (such as GPT-6) can run:
- **G0 checks**: Python-based structural, meta-file, and contract validators.
- **G1 tests**: Pure C# .NET Core NUnit tests via `dotnet test`.

However, cloud agents cannot currently execute **G2 (Unity EditMode/Import)** or **G3 (PlayMode)** tests directly without a local bridge.
When ShellWarden was tested locally, `shellwarden-broker.exe` rejected Unity execution because the installed broker enforces a hardcoded allowlist: `allowedCommands: ["git"]`.

## 2. Implemented Tooling: `tools/Validate-Unity.ps1`

We enhanced `tools/Validate-Unity.ps1` to provide a locked-down, robust execution harness with strict safeguards:

### Supported Modes & Arguments
- `-Mode <Import | EditMode | PlayMode>`: Validated via `ValidateSet`.
- `-TestFilter <string>`: Validated with regex `^[a-zA-Z0-9_\.]*$` to prevent shell argument injection.
- `-TimeoutSeconds <int>`: Constrained to range `[30, 1800]` (default 600s).
- `-EditorPath <string>`: Validates exact Unity 6000.6.0f1 path.

### Safety Guards
1. **Editor Lock Detection:** Queries running `Unity.exe` processes for matching project path. If the GUI Editor is open, immediately returns structured `EDITOR_LOCKED` error instead of causing lock corruption.
2. **Safe Timeout & Process Termination:** Automatically invokes `Stop-Process -Force` if execution exceeds timeout.
3. **Structured JSON Output:** Returns standardized JSON containing:
   - `ok`: boolean overall pass status.
   - `mode`: executed mode.
   - `exitCode`: Unity process exit code.
   - `log`: Project-relative log file.
   - `tests`: Detailed breakdown (`result`, `total`, `passed`, `failed`, `skipped`, `duration`, `failures` list, and NUnit XML path).

### Verification Evidence
- `./tools/Validate-Unity.ps1 -Mode Import`: exit `0`, `ok: true`.
- `./tools/Validate-Unity.ps1 -Mode EditMode`: exit `0`, `total: 7`, `passed: 7`, `failed: 0`, `duration: 0.60s`.
- `./tools/Validate-Unity.ps1 -Mode EditMode -TestFilter FrostArk.Unity.Tests.TacticalSelectionBridgeTests`: exit `0`, `total: 3`, `passed: 3`, `failed: 0`, `duration: 0.20s`.

## 3. ShellWarden Integration Barrier & Actionable Next Steps

Empirical verification against `F:\Development\Tools\ShellWarden\execution\shellwarden-broker.exe`:
```json
{"type":"health"} -> {"type":"ready","ok":true,"coreVersion":"1.1.12","allowedCommands":["git"]}
{"type":"execute","id":"test-1","command":["powershell",...]} -> {"ok":false,"error":"bootstrap policy does not allow executable: powershell"}
```

### Root Cause
`shellwarden-broker.exe` version `1.1.12` does not have a configurable allowlist file or SQLite grant exposed; its binary allows only `git`.

### Minimum Secure Policy Change Required in ShellWarden
To safely enable remote agent Unity validation without granting generic shell or arbitrary command execution:
1. **Targeted Executable Policy:** Allow either:
   - `F:\Program Files\Unity\6000.6.0f1\Editor\Unity.exe` with forced arguments `-batchmode -nographics -projectPath F:\UnityProjects\Frostark`, OR
   - `powershell.exe` constrained strictly to the fixed script path `F:\UnityProjects\Frostark\tools\Validate-Unity.ps1` with allowed flags.
2. **Standard Output Parsing:** Return the JSON stdout produced by `Validate-Unity.ps1` to the remote MCP client.
