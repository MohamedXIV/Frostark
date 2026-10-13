# Bootstrap & Validation Tools

Run in Windows PowerShell from the repository root, with Unity 6000.6.0f1 installed and an active license:

```powershell
# Verify package resolution, compiler readiness, and engine module availability
./tools/Validate-Unity.ps1 -Mode Import

# Run all EditMode tests in the project (outputs structured NUnit JSON)
./tools/Validate-Unity.ps1 -Mode EditMode

# Run targeted EditMode test filter
./tools/Validate-Unity.ps1 -Mode EditMode -TestFilter FrostArk.Unity.Tests.TacticalSelectionBridgeTests

# Run PlayMode integration tests
./tools/Validate-Unity.ps1 -Mode PlayMode
```

### Safety and Execution Rules

- **Editor Lock Detection:** The runner detects if Unity Editor is already open for this project and cleanly aborts with a structured `EDITOR_LOCKED` error instead of causing lock corruption or multiple instances.
- **Strict Parameter Validation:** `-Mode` is constrained to `Import`, `EditMode`, or `PlayMode`. `-TestFilter` is validated against `^[a-zA-Z0-9_\.]*$` to prevent shell argument injection.
- **Process Cleanup & Timeout:** Capped by `-TimeoutSeconds` (default: 600s). In case of timeout or failure, the process is safely terminated.
- **Structured JSON Output:** Returns structured JSON containing execution status, exit code, log file path, test count breakdown (`total`, `passed`, `failed`, `skipped`, `duration`), and error messages for any failing tests.
- **Log Hygiene:** Detailed engine output is recorded under git-ignored `Logs/`.

---

### GitHub Relationship Sync

Authenticated GitHub CLI required:

```powershell
./tools/Sync-IssueRelationships.ps1
```

`-Apply` adds and verifies missing native relationships for Issues #13–#43. It is an external mutation requiring authorization; the bootstrap run was explicitly authorized. It preserves issue bodies and board fields.

See [verified handoff](../docs/engineering/local-bootstrap-handoff.md) for evidence, platform gaps and the ShellWarden broker allowlist policy.
