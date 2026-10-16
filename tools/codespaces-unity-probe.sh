#!/usr/bin/env bash
# FrostArk Linux-only, opt-in Unity Editor smoke test. No automatic downloads.
set -Eeuo pipefail

action="${1:-check}"
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project_version="$(sed -n 's/^m_EditorVersion: //p' "$root/ProjectSettings/ProjectVersion.txt" | head -n 1)"
# Latest verified official 6000.6 patch as of 2026-10-16; override for future patches.
version="${UNITY_EDITOR_VERSION:-6000.6.6f1}"
if [[ "$version" != "${project_version%.*}."* ]]; then
  echo "Editor $version must stay within project's ${project_version%.*}.x line." >&2
  exit 2
fi
revision="$(sed -n 's/^m_EditorVersionWithRevision: .* (\(.*\))/\1/p' "$root/ProjectSettings/ProjectVersion.txt" | head -n 1)"
logs="$root/Logs"
mkdir -p "$logs"

help_text() {
  cat <<'HELP'
FrostArk Codespaces Unity probe

  bash tools/codespaces-unity-probe.sh check      # No downloads, no Unity needed
  bash tools/codespaces-unity-probe.sh editmode   # One real raycast EditMode suite
  bash tools/codespaces-unity-probe.sh playmode   # Four real combat PlayMode tests
  bash tools/codespaces-unity-probe.sh full       # All PlayMode regression suites

Prerequisites for tests: Unity Editor matching ProjectVersion.txt, an active
license, and compatible Linux libraries. See docs/engineering/codespaces-unity-probe.md.
Default cloud Editor: 6000.6.6f1 (latest published 6000.6 patch on 2026-10-16).
Set UNITY_EDITOR_VERSION to override to a newer 6000.6.x patch later.
Set UNITY_EDITOR_BIN to the Linux Editor executable if discovery fails.
Set UNITY_TIMEOUT_SECONDS to override the 900-second test timeout.
HELP
}

editor_path() {
  if [[ -n "${UNITY_EDITOR_BIN:-}" ]]; then
    [[ -x "$UNITY_EDITOR_BIN" ]] || { echo "UNITY_EDITOR_BIN is not executable: $UNITY_EDITOR_BIN" >&2; return 1; }
    printf '%s\n' "$UNITY_EDITOR_BIN"
    return
  fi
  local p=""
  if command -v unity >/dev/null 2>&1; then
    p="$(unity editors path "$version" 2>/dev/null || true)"
    p="$(printf '%s' "$p" | tr -d '\r' | head -n 1)"
    if [[ -x "$p" && -f "$p" ]]; then printf '%s\n' "$p"; return; fi
    for candidate in "$p/Unity" "$p/Editor/Unity"; do
      if [[ -x "$candidate" && -f "$candidate" ]]; then printf '%s\n' "$candidate"; return; fi
    done
  fi
  local candidate
  for candidate in \
    "$HOME/Unity/Hub/Editor/$version/Editor/Unity" \
    "$HOME/Unity/Hub/Editor/$version/Unity" \
    "$HOME/.local/share/unityhub/Editor/$version/Editor/Unity"; do
    if [[ -x "$candidate" && -f "$candidate" ]]; then printf '%s\n' "$candidate"; return; fi
  done
  echo "No Unity Editor $version found. Install it first, then export UNITY_EDITOR_BIN." >&2
  return 1
}

case "$action" in
  help|-h|--help) help_text; exit 0 ;;
  check)
    echo "FrostArk Unity cloud probe (read-only)"
    echo "Repository: $(git -C "$root" rev-parse --short HEAD 2>/dev/null || echo unknown)"
    echo "Project-pinned Unity: $project_version"
    echo "Cloud test Unity: $version"
    echo "Unity changeset: $revision"
    echo "OS: $(uname -sm)"
    if command -v free >/dev/null; then free -h | head -n 2; fi
    df -h "$root" | tail -n 1
    echo "Shared memory /dev/shm (Unity 6.6 requires >1 GiB):"
    df -h /dev/shm
    echo "If this is 64M, pull the branch and rebuild the devcontainer to apply runArgs --shm-size=2g."
    echo "Unity CLI: $(command -v unity || echo 'not installed')"
    if command -v unity >/dev/null; then
      unity --version || true
      unity auth status || true
      unity license status || true
    fi
    if bin="$(editor_path 2>/dev/null)"; then
      echo "Unity Editor: $bin"
    else
      echo "Unity Editor: not installed (expected before opt-in installation)"
    fi
    echo "No Unity installation, login, or project import was attempted."
    exit 0 ;;
  editmode|playmode|full) ;;
  *) help_text >&2; exit 2 ;;
esac

if [[ "$(uname -s)" != Linux ]]; then
  echo "This probe is only for Linux. Use tools/Validate-Unity.ps1 on Windows." >&2
  exit 2
fi

shm_bytes="$(df -B1 --output=size /dev/shm | tail -n 1 | tr -d '[:space:]')"
if [[ "$shm_bytes" =~ ^[0-9]+$ ]] && (( shm_bytes < 1074790400 )); then
  echo "BLOCKED: /dev/shm is $(df -h /dev/shm | tail -n 1), less than Unity 6.6's >1 GiB requirement." >&2
  echo "Pull .devcontainer/devcontainer.json with runArgs --shm-size=2g, rebuild the container, then verify df -h /dev/shm." >&2
  exit 3
fi

bin="$(editor_path)"
timeout_seconds="${UNITY_TIMEOUT_SECONDS:-900}"
[[ "$timeout_seconds" =~ ^[0-9]+$ ]] && ((timeout_seconds >= 30)) || {
  echo "UNITY_TIMEOUT_SECONDS must be an integer >= 30" >&2
  exit 2
}

settings="$root/ProjectSettings/EditorSettings.asset"
project_version_file="$root/ProjectSettings/ProjectVersion.txt"
tmp="$(mktemp -d)"
cp -p "$project_version_file" "$tmp/ProjectVersion.txt"
original_exists=0
if [[ -f "$settings" ]]; then
  cp -p "$settings" "$tmp/EditorSettings.asset"
  original_exists=1
fi
restore_settings() {
  # Unity can rewrite ProjectVersion.txt when opening with a newer patch.
  # This is a *cloud experiment*, so keep the user's pinned version unchanged.
  if [[ -f "$project_version_file" ]] && ! cmp -s "$project_version_file" "$tmp/ProjectVersion.txt"; then
    cp -p "$tmp/ProjectVersion.txt" "$project_version_file"
    echo "Restored pre-run ProjectVersion.txt bytes."
  fi
  # Never git checkout/reset; preserve the user's exact pre-run bytes.
  if ((original_exists)) && ! cmp -s "$settings" "$tmp/EditorSettings.asset"; then
    cp -p "$tmp/EditorSettings.asset" "$settings"
    echo "Restored pre-run EditorSettings.asset bytes."
  elif (( ! original_exists )) && [[ -e "$settings" ]]; then
    echo "EditorSettings.asset was created during the test; retaining for inspection." >&2
  fi
  rm -rf "$tmp"
}
trap restore_settings EXIT

output="$logs/codespaces-editmode.xml"
log="$logs/codespaces-editmode.log"
case "$action" in
  editmode)
    args=(-runTests -testPlatform EditMode -testFilter FrostArk.Unity.Tests.CombatRaycastEditModeTests -testResults "$output") ;;
  playmode)
    output="$logs/results-playmode.xml"
    log="$logs/codespaces-playmode.log"
    args=(-executeMethod FrostArk.Unity.Editor.InteractivePlayModeRunner.RunCombatPlayModeTests) ;;
  full)
    output="$logs/results-playmode.xml"
    log="$logs/codespaces-full-playmode.log"
    args=(-executeMethod FrostArk.Unity.Editor.InteractivePlayModeRunner.RunAllPlayModeTests) ;;
esac

rm -f "$output"  # Stale results must not count as proof.
echo "Running $action on $version with $bin"
echo "This is the real Linux Unity Editor in -batchmode -nographics."
echo "Log: $log"
echo "Result: $output"

status=0
timeout --kill-after=15s "${timeout_seconds}s" \
  "$bin" -batchmode -nographics -projectPath "$root" \
  -logFile "$log" "${args[@]}" || status=$?
if ((status != 0)); then
  echo "Unity returned $status. Inspect: tail -n 100 '$log'" >&2
  exit "$status"
fi
if [[ ! -s "$output" ]]; then
  echo "Unity returned 0 without a test result file. Treating this as a failure." >&2
  exit 1
fi

python3 - "$output" <<'PY'
import sys
from xml.etree import ElementTree
xml_path = sys.argv[1]
root = ElementTree.parse(xml_path).getroot()
if root.tag != "test-run":
    sys.exit(f"Unexpected result format: {root.tag}")
total = int(root.get("total", "-1"))
passed = int(root.get("passed", "-1"))
failed = int(root.get("failed", "-1"))
skipped = int(root.get("skipped", "-1"))
print(f"Unity result: total={total} passed={passed} failed={failed} skipped={skipped}")
if total <= 0 or failed > 0 or passed + failed + skipped != total:
    sys.exit(1)
PY
