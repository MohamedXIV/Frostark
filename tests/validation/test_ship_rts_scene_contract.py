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
        self.assertIn("private void Start()", script)
        self.assertIn("try { BuildDemo(); }", script)
        self.assertIn("Debug.LogException(ex, this)", script)

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


    def test_compile_blockers_from_first_real_unity_smoke_are_fixed(self):
        """Static regression only; Unity 6000.6.0f1 must recompile separately."""
        dispatch = (
            ROOT / "Assets/FrostArk/Tests/Editor/CrewTheaterMoveDispatchEditModeTests.cs"
        ).read_text(encoding="utf-8")
        imported = (
            ROOT / "Assets/FrostArk/Tests/Editor/ShipRtsDemoSceneEditModeTests.cs"
        ).read_text(encoding="utf-8")
        self.assertRegex(
            dispatch,
            r'private static readonly EntityId Engine\s*=\s*EntityId\.Parse\("room:engine"\)',
        )
        self.assertIn("using EntityId = FrostArk.Core.World.EntityId;", imported)

    def test_scene_imgui_overlay_consumes_world_pointer_input(self):
        runtime = SCRIPT.read_text(encoding="utf-8")
        tactical = (ROOT / "Assets/FrostArk/Unity/TacticalCommandController.cs").read_text(
            encoding="utf-8"
        )
        self.assertIn("_commander.SetPointerOverlayBlocker(IsPointerOverHud)", runtime)
        self.assertIn("Screen.height - screenPoint.y", runtime)
        self.assertIn("bool pointerOverUi = IsPointerBlocked(point)", tactical)
        self.assertIn("(_isPointerOverSceneOverlay?.Invoke(screenPoint) ?? false)", tactical)


    def test_editmode_selection_visuals_are_bound_without_awake(self):
        """Protect the real Unity smoke failure: 2 selected but proxy rings off."""
        tactical = (ROOT / "Assets/FrostArk/Unity/TacticalCommandController.cs").read_text(
            encoding="utf-8"
        )
        tests = (
            ROOT / "Assets/FrostArk/Tests/Editor/ShipRtsPointerBridgeEditModeTests.cs"
        ).read_text(encoding="utf-8")
        self.assertIn("private void EnsureSelectionBinding()", tactical)
        self.assertIn("_selectionController.SelectionChanged += OnSelectionChanged;", tactical)
        self.assertIn("public void RegisterShipActor(ShipCrewSelectionProxy actor)", tactical)
        self.assertIn(
            "public void EditModeSelectionNotificationsUpdateRegisteredShipVisualsWithoutAwake()",
            tests,
        )
        self.assertIn("scene.Commander.Selection.SelectMultiple(new[] { First, Second })", tests)


    def test_cooperative_movement_wiring_and_import_guids(self):
        """Static G0 wiring proof only; does not claim Unity G2/G3 PASS."""
        root = ROOT / "Assets/FrostArk/Unity"
        traffic = root / "ShipTrafficCoordinator.cs"
        traffic_meta = root / "ShipTrafficCoordinator.cs.meta"
        walker = (root / "ShipGridWalker.cs").read_text(encoding="utf-8")
        controller = (root / "TacticalCommandController.cs").read_text(encoding="utf-8")
        group = (root / "TacticalTheaterSelectionCommands.cs").read_text(
            encoding="utf-8"
        )
        spatial = (ROOT / "Assets/FrostArk/Core/Ship/ShipSpatialMap.cs").read_text(
            encoding="utf-8"
        )
        self.assertTrue(traffic.is_file())
        self.assertRegex(guid(traffic_meta.read_text(encoding="utf-8")), r"^[a-f0-9]{32}$")
        self.assertIn("class ShipTrafficCoordinator : MonoBehaviour", traffic.read_text(encoding="utf-8"))
        self.assertIn("CanEnter(this, _lastReachedCell, nextCell)", walker)
        self.assertIn("CanTraverseStraight", spatial)
        self.assertIn("new[] {", spatial)
        self.assertIn("MoveSelectedAssigned", controller)
        self.assertIn("TacticalGroupGoalPlanner.Allocate", controller)
        self.assertIn("public static class TacticalGroupGoalPlanner", group)


if __name__ == "__main__":
    unittest.main()
