# Bootstrap tools

Run in Windows PowerShell from the repository root, with Unity 6000.6.0f1 installed and a valid local license:

```powershell
./tools/Validate-Unity.ps1 -Mode Import
./tools/Validate-Unity.ps1 -Mode EditMode
```

Save and close the Editor for this project first. The runner refuses an already-open project, caps execution time, records output under ignored `Logs/`, and validates exit codes and NUnit results. It never builds Android or downloads modules. The default Editor path is the verified owner's installation; a different runner must supply its installed 6000.6.0f1 path via `-EditorPath`.

Import audits exact required package versions. EditMode runs the two package smoke tests; this proves no player-build or device behavior. A fresh clone's fixture/settings/meta files are committed; no Library cache is required.

GitHub relationship plan (authenticated GitHub CLI required):

```powershell
./tools/Sync-IssueRelationships.ps1
```

`-Apply` adds and verifies missing native relationships for Issues #13–#43. It is an external mutation requiring authorization; the bootstrap run was explicitly authorized. It preserves issue bodies and board fields.

See [verified handoff](../docs/engineering/local-bootstrap-handoff.md) for evidence, platform gaps and the ShellWarden blocker. These scripts do not expose a remote endpoint or weaken ShellWarden permissions.
