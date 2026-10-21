"""Structural evidence for the tracked RTS demo scene, NOT a Unity runtime test."""
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCENE = ROOT / "Assets/FrostArk/Scenes/ShipRtsDemo.unity"
SCENE_META = ROOT / "Assets/FrostArk/Scenes/ShipRtsDemo.unity.meta"
SCRIPT_META = ROOT / "Assets/FrostArk/Unity/ShipRtsDemoRuntime.cs.meta"
SCRIPT = ROOT / "Assets/FrostArk/Unity/ShipRtsDemoRuntime.cs"
BUILD = ROOT / "ProjectSettings/EditorBuildSettings.asset"


def guid(text):
    match = re.search(r"(?m)^guid: ([0-9a-f]{32})$", text)
    if not match:
        raise AssertionError("Missing Unity GUID")
    return match.group(1)


class ShipRtsDemoSceneContractTests(unittest.TestCase):
    def test_scene_asset_links_the_actual_runtime_script_guid(self):
        scene = SCENE.read_text(encoding="utf-8")
        script = SCRIPT.read_text(encoding="utf-8")
        script_id = guid(SCRIPT_META.read_text(encoding="utf-8"))
        self.assertIn("%YAML 1.1", scene)
        self.assertIn("m_Script: {fileID: 11500000, guid: " + script_id + ", type: 3}", scene)
        self.assertIn("FrostArk.Unity.ShipRtsDemoRuntime", scene)
        self.assertIn("public sealed class ShipRtsDemoRuntime : MonoBehaviour", script)
        self.assertIn("private void Start() => BuildDemo()", script)

    def test_scene_is_registered_without_overwriting_other_launchers(self):
        build = BUILD.read_text(encoding="utf-8")
        scene_id = guid(SCENE_META.read_text(encoding="utf-8"))
        self.assertIn(
            "path: Assets/FrostArk/Scenes/ShipRtsDemo.unity\n    guid: " + scene_id,
            build,
        )
        for path in [
            "Assets/Scenes/SampleScene.unity",
            "Assets/FrostArk/Scenes/TacticalVerticalSlice.unity",
            "Assets/FrostArk/Scenes/ShipShoreTransitLauncher.unity",
        ]:
            self.assertIn("path: " + path, build)
        self.assertTrue(
            (ROOT / "Assets/FrostArk/Tests/Editor/ShipRtsDemoSceneEditModeTests.cs").is_file()
        )
        self.assertTrue(
            (ROOT / "Assets/FrostArk/Tests/PlayMode/ShipRtsDemoScenePlayModeTests.cs").is_file()
        )


if __name__ == "__main__":
    unittest.main()
