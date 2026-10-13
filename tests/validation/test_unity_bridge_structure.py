"""G0-only static checks for Unity session assembly wiring; not a Unity compile."""
import json
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
UNITY = Path("Assets/FrostArk/Unity")
EDITOR = Path("Assets/FrostArk/Editor")
TESTS = Path("Assets/FrostArk/Tests/Editor")
PLAYMODE = Path("Assets/FrostArk/Tests/PlayMode")
FOLDERS = [
    Path("Assets/FrostArk/Unity"),
    Path("Assets/FrostArk/Editor"),
    Path("Assets/FrostArk/Tests"),
    Path("Assets/FrostArk/Tests/Editor"),
    Path("Assets/FrostArk/Tests/PlayMode"),
    Path("Assets/FrostArk/Scenes"),
]


class UnityBridgeStructureTests(unittest.TestCase):
    def test_runtime_assembly_references_only_core_and_vcontainer(self):
        asm = json.loads((ROOT / UNITY / "FrostArk.Unity.asmdef").read_text())
        self.assertEqual(asm["references"], ["FrostArk.Core", "VContainer", "Unity.InputSystem"])
        self.assertEqual(asm["includePlatforms"], [])
        self.assertFalse(asm["noEngineReferences"])

    def test_editor_and_editmode_assemblies_are_editor_only(self):
        editor = json.loads((ROOT / EDITOR / "FrostArk.Unity.Editor.asmdef").read_text())
        smoke = json.loads((ROOT / TESTS / "FrostArk.Unity.Tests.asmdef").read_text())
        self.assertEqual(editor["includePlatforms"], ["Editor"])
        self.assertEqual(smoke["includePlatforms"], ["Editor"])
        self.assertEqual(smoke["optionalUnityReferences"], ["TestAssemblies"])
        self.assertIn("FrostArk.Unity", editor["references"])
        self.assertIn("FrostArk.Unity", smoke["references"])

    def test_all_bridge_sources_and_directories_have_meta(self):
        for folder in FOLDERS:
            self.assertTrue((ROOT / folder).is_dir(), folder)
            self.assertTrue((ROOT / (str(folder) + ".meta")).is_file(), folder)
        for folder in (UNITY, EDITOR, TESTS, PLAYMODE):
            for source in (ROOT / folder).glob("*"):
                if source.is_file() and source.suffix != ".meta":
                    self.assertTrue(Path(str(source) + ".meta").is_file(), source)

    def test_scoped_session_and_presenter_use_the_same_authority(self):
        scope = (ROOT / UNITY / "WorldSessionScope.cs").read_text()
        presenter = (ROOT / UNITY / "WorldSessionPresenter.cs").read_text()
        self.assertIn("Register<WorldSession>", scope)
        self.assertIn("Lifetime.Scoped", scope)
        self.assertIn("RegisterComponentInHierarchy<WorldSessionPresenter>", scope)
        self.assertIn("[Inject]", presenter)
        self.assertIn("_session.Read()", presenter)
        self.assertNotIn("void Update(", presenter)
        self.assertNotIn("void FixedUpdate(", presenter)


if __name__ == "__main__":
    unittest.main()
