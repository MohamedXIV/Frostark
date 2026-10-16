# FrostArk — Codespaces Unity headless feasibility probe

**Experiment only.** Nothing in this PR claims a successful Codespaces Unity run or changes the game's production runtime. The primary objective is to answer whether the **real Linux Unity Editor on the newest verified 6000.6.x patch** can run a representative FrostArk physics test inside a GitHub Codespace.

## Why no desktop-lite?

This is a `-batchmode -nographics` experiment. Neither a remote desktop, Xfce nor a browser-displayed Editor UI is needed to execute a headless Unity Editor test. Desktop-lite may help with GUI diagnostics, but it doesn't fix headless license activation, CPU/RAM/storage limits, or Linux package compatibility.

## Start from GitHub Codespaces (user action)

1. Open `https://github.com/MohamedXIV/Frostark` → **Code** → **Codespaces** → **New codespace**.
2. Select branch **`experiment/codespaces-unity-headless`** so the new `.devcontainer/devcontainer.json` is used. The environment is Ubuntu 24.04; it installs **no Unity Editor or Unity Hub by default**.
3. Start with the smallest available machine for the **read-only probe**. For actually importing this Unity project, a machine with more RAM and storage may be needed. Check your Codespaces quotas/costs before upgrading.
4. In the Codespaces terminal:

   ```bash
   bash tools/codespaces-unity-probe.sh check
   ```

   This prints available RAM/storage, Unity version pinned in the repo, installed CLI/Editor and license state. It does **not** install, sign in, or run Unity.

## Optional next phase: install official Unity CLI (not Hub)

Only proceed if there is adequate storage and you are willing to spend cloud storage/compute quota. The standalone Unity CLI is **experimental**. Follow Unity's official installation instructions and verify the installer URL against Unity documentation before running a download script:

```bash
curl -fsSL https://unity.com/install.sh | bash
export PATH="$HOME/.local/bin:$PATH"
unity --version
unity editors --installed
unity auth status
```

The Unity CLI added `unity auth login`, `unity license status` and `unity license activate`. Check what your **installed** version supports:

```bash
unity auth login
unity license status
unity license activate --help
```

**Licensing limitation:** Unity's Editor documentation says Unity Personal normally requires a signed-in Unity Hub; the newer experimental Unity CLI advertises Personal activation. Do not assume either route works on a Codespaces container. If CLI sign-in or Personal activation fails, record the precise non-secret error and **stop before large downloads**. You may need to revisit authentication on a supported desktop/hub environment. Never paste account passwords, OAuth tokens, license files or service-account secrets into logs, git, or chat.

After confirming licensing and enough disk space, prefer the latest stable patch in the SAME 6000.6 line; on October 16, 2026 the official archive lists **6000.6.6f1 (released October 15)**. First perform a non-downloading dry run (verify available disk):

```bash
unity editors info 6000.6.6f1
unity install 6000.6.6f1 --dry-run
```

If capacity and license are sufficient, opt in to actual Editor installation (no extra build-platform modules):

```bash
unity install 6000.6.6f1
unity editors --installed
unity editors path 6000.6.6f1
```

If installation needs additional Linux shared libraries, install only documented missing dependencies; don't blindly install large build-support modules (Android, iOS, WebGL). A brand-new Codespace can have 2 CPU cores / 8 GB RAM / 32 GB storage; that might be insufficient for the Editor plus its first import/cache. The initial import may be substantially slower than on the local Windows machine.

## Execute the actual Unity tests (only after successful license + install)

```bash
bash tools/codespaces-unity-probe.sh editmode
```

This launches the real Editor `-batchmode -nographics` and runs **`FrostArk.Unity.Tests.CombatRaycastEditModeTests`** first. Requires a genuine NUnit XML report and 0 test failures.

If the EditMode smoke is green:

```bash
bash tools/codespaces-unity-probe.sh playmode
```

This calls the project's existing `FrostArk.Unity.Editor.InteractivePlayModeRunner.RunCombatPlayModeTests` instead of the default Unity PlayMode runner, preserving the fix for the Unity 6 PlayMode scene-reload lifecycle. It tests real `NavMeshAgent`, physics raycasting and combat execution. Once that passes, optionally run all existing PlayMode tests:

```bash
bash tools/codespaces-unity-probe.sh full
```

The cloud probe now defaults to Unity **6000.6.6f1** and keeps the original `ProjectVersion.txt` bytes intact after any test. This does **not** update the repository's project-pinned `6000.6.0f1` on main; only adopt a newer patch for all developers after the smoke tests pass and a separate upgrade decision. Set `UNITY_EDITOR_VERSION` to a future 6000.6.x patch when released; other minor versions are rejected. If Unity is installed in a nonstandard location, set `UNITY_EDITOR_BIN` to its **Linux executable** before the smoke command.

**Evidence to retain:** project `main`/branch SHA, exact Editor revision, mode, exit code, `Logs/codespaces-*.log`, `Logs/codespaces-editmode.xml` or `Logs/results-playmode.xml`, and the Codespace machine specs. Don't claim `G2/G3 PASS` without valid XML and a successful process exit. Don't merge this setup PR merely because GitHub G0/G1 passes; Codespaces runtime remains unverified until a real Codespaces execution.

## Unity 6.6 DataStore/UDS startup: container shared memory

A real user smoke on `6000.6.6f1` under Codespaces exited `134` before tests:

```text
Aborting batchmode due to fatal error:
Failed to initialise UDS client in the Editor
```

**Probable cause (confirm locally):** Unity 6.6 requires over 1 GiB of container shared memory; stock Docker `/dev/shm` is only 64 MiB. GameCI documents this exact new Unity 6.6 resource requirement and defaults to `1025m`. For headroom the Codespaces devcontainer in this branch now configures `"runArgs": ["--shm-size=2g"]`. This is shared-memory capacity, **not** a reserved 2 GiB block. Do not conflate Unity's DataStore (UDS) subsystem with Linux D-Bus/keyring auth, which has already been separately resolved.

Before rebuilding, confirm the probable cause:

```bash
df -h /dev/shm
tail -n 50 Logs/codespaces-editmode.log
```

If `/dev/shm` is 64 MiB:
1. Commit/stash any local code changes; no existing code edits are required.
2. `git pull --ff-only` this experiment branch.
3. Use **Command Palette → Codespaces: Rebuild Container** (or the UI rebuild control) to apply `runArgs`. Just restarting the terminal is insufficient. **Warning:** `/tmp` and keyring/session state may not survive a container rebuild, so verify before reinstalling the Editor.
4. `df -h /dev/shm` must now show about **2.0 GiB**. If Codespaces refuses the runtime flag or still shows 64 MiB, stop and report the result instead of repeatedly crashing Unity.
5. `unity license status` and `unity editors list` should confirm the previous activation and installation. If the binary in `/tmp` vanished, reinstall only after verifying storage and license state.
6. Re-run `bash tools/codespaces-unity-probe.sh editmode`.

Sources:
- https://game.ci/docs/github/builder/ (Unity 6.6 shm requirement)
- https://github.com/microsoft/vscode-dev-containers/blob/main/script-library/docs/desktop-lite.md (documented `--shm-size` devcontainer flag)
- https://docs.github.com/en/codespaces/setting-up-your-project-for-codespaces/adding-a-dev-container-configuration/introduction-to-dev-containers

## Verified shared-memory fix; next Linux runtime-library hurdle (2026-10-16)

The live Codespace log confirmed this was a real startup blocker: `Requested 1073741824 bytes, but only 67108864 bytes available`. After applying `--shm-size=2g` through a devcontainer rebuild, `df -h /dev/shm` now correctly displays **2.0G**. The Linux Editor binary installed under `/tmp/frostark-unity-editors/6000.6.6f1/Editor/Unity` survived the rebuild, although the Unity CLI shell command did not.

On the next EditMode smoke, the Editor failed **before project import and before NUnit** with exit 127, reporting:

```text
error while loading shared libraries: libGL.so.1: cannot open shared object file
```

This is a missing Linux runtime library, **not** evidence that `-nographics` needs desktop-lite, nor a failing raycast test. No new Editor download or devcontainer rebuild is needed for this step. In the same Codespaces terminal:

```bash
sudo apt-get update
sudo apt-get install -y libgl1 libglu1-mesa
export UNITY_EDITOR_BIN=/tmp/frostark-unity-editors/6000.6.6f1/Editor/Unity
ldd "$UNITY_EDITOR_BIN" | grep "not found" || echo "No missing direct libraries"
bash tools/codespaces-unity-probe.sh editmode
```

The probe now detects directly missing ELF libraries and reports an actionable error instead of running the Editor blindly. If another library is missing, install its matching package based on the exact `ldd` result; do not install a full desktop environment or broad graphics-driver stack speculatively. The project's first actual Unity Import, licensing state after rebuilding, G2/G3 NUnit XML and any remaining runtime libraries are **not yet verified**.

## Subsequent Linux ELF dependencies (GTK 3 and X11)

After installing the initial `libgl1`/`libglu1-mesa` packages, the real user `ldd "$UNITY_EDITOR_BIN"` still reported missing `libXcursor.so.1`, `libXrandr.so.2`, `libgtk-3.so.0`, GLib/GIO/GObject, GDK Pixbuf, Cairo, Pango, Fontconfig, ATK and HarfBuzz. These are **runtime libraries** linked by Unity Editor, even when the Editor itself is run headlessly; this does not justify desktop-lite or a display server.

On Ubuntu 24.04 (Noble), the GTK 3 runtime uses the `t64` package name and pulls the remaining GLib, Cairo, Pango and GDK components transitively:

```bash
sudo apt-get update
sudo apt-get install -y libgtk-3-0t64 libxcursor1 libxrandr2 libfontconfig1
ldd "$UNITY_EDITOR_BIN" | grep 'not found' || echo 'All direct libraries resolved'
```

If the linker is clean, retry `bash tools/codespaces-unity-probe.sh editmode`. A clean `ldd` proves only the direct linker prerequisites; still collect the Editor log and authentic NUnit XML. **No successful Unity project import or G2/G3 result has yet been reported.**

Ubuntu 24.04 package dependency reference: https://packages.ubuntu.com/noble/libgtk-3-0t64

## Exit 198 after container rebuild: Unity Editor licensing

The latest real Codespaces smoke got beyond shared memory and Linux runtime libraries but terminated with **Editor exit 198**, before project import or NUnit. The `Logs/codespaces-editmode.log` explicitly states:

```text
[Licensing::Module] Error: Access token is unavailable; failed to update
[Licensing::Module] Error: 'com.unity.editor.headless' was not found.
No valid Unity Editor license found. Please activate your license.
```

Rebuilding the devcontainer removed the separately installed standalone CLI (`unity: command not found`) and its previous authenticated session. The Editor binary **survived** under `/tmp/frostark-unity-editors/6000.6.6f1/Editor/Unity`, so do not download it again. The editor also reported that `~/.local/share/unity3d` could not be created: inspect the home-directory ownership and create the user-owned directory if possible before retrying.

Restore the CLI and personal licensing (interactive authentication through the user's own Unity account):

```bash
mkdir -p "$HOME/.local/share/unity3d"
curl -fsSL https://public-cdn.cloud.unity3d.com/hub/prod/cli/install.sh -o /tmp/unity-cli-install.sh
UNITY_CLI_CHANNEL=beta bash /tmp/unity-cli-install.sh
export PATH="$HOME/.local/bin:$PATH"
unity --version
sudo apt-get install -y dbus-x11 gnome-keyring libsecret-tools
dbus-run-session -- bash
# The lines below execute in the new D-Bus-enabled bash shell.
export PATH="$HOME/.local/bin:$PATH"
unity auth status
unity auth login
unity license activate --personal --accept-eula
unity license status
```

If Unity CLI reports `KEYRING_WRITE_FAILED`, ensure that the gnome-keyring-daemon secret-service component has started and the login keyring is unlocked in the *same* D-Bus session. Never paste a keyring password, Unity OAuth redirect/token, or entitlement file into logs or chat. An active Unity Personal license reported by CLI is necessary but not sufficient evidence that the independently launched batch-mode Editor can use it. Only retry the EditMode smoke after sign-in and license activation; if it still exits 198, investigate the CLI-vs-Editor identity bridge rather than repeating installation.

From that same authenticated shell:

```bash
export UNITY_EDITOR_BIN=/tmp/frostark-unity-editors/6000.6.6f1/Editor/Unity
bash tools/codespaces-unity-probe.sh editmode
```

The original sample runner launches the Editor directly. If the Editor still cannot see an otherwise active license, compare launching from the official Unity CLI (`unity test --help`, `unity editors add --help`, `unity open --help`) to distinguish an auth-broker/identity-server issue from an actual entitlement limit. Do not claim a successful test based only on `unity license status` or a successful CLI command.

Sources:
- https://docs.unity.com/en-us/unity-cli/use-unity-cli
- https://github.com/Unity-Technologies/skills/blob/main/skills/unity-cli/references/auth-license-cloud.md

## Safety and cost boundaries

- Unity install/download and tests are **opt-in** and cannot start from `postCreateCommand`.
- The probe stores no credentials; use Unity's secure sign-in.
- Script protects the exact pre-test bytes of `ProjectSettings/EditorSettings.asset` and **never** calls `git checkout` or `git reset`.
- All editor logs and test outputs are kept under ignored `Logs/`; don't commit large imported `Library/` caches.
- Stop your Codespace immediately after the smoke experiment to avoid burning quota; delete it when no longer needed.
- Avoid spending GitHub Actions minutes on Unity; this is a manually triggered Codespace experiment and does not change existing CI.

References:
- https://unity.com/releases/editor/archive
- https://docs.unity.com/en-us/unity-cli/use-unity-cli
- https://docs.unity.com/en-us/unity-cli/release-notes
- https://docs.unity.com/en-us/engine/6000.6/manual/get-started/install-and-upgrade/licenses-and-activation/managing-your-unity-license
- https://docs.github.com/en/codespaces/about-codespaces/what-are-codespaces
