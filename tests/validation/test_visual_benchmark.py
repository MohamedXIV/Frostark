"""G0-only contracts for the V01 visual benchmark. NOT Unity G2 or rendered proof."""
import json
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
MANIFEST = ROOT / "Assets/VisualBenchmarks/visual-benchmark.json"
GENERATOR = ROOT / "Assets/Editor/VisualBenchmark/VisualBenchmarkBuilder.cs"
GUID_META = ROOT / "Assets/Editor/VisualBenchmark/VisualBenchmarkBuilder.cs.meta"
DOC = ROOT / "docs/engineering/visual-benchmark.md"


class VisualBenchmarkContractTests(unittest.TestCase):
    def spec(self):
        return json.loads(MANIFEST.read_text(encoding="utf-8"))

    def test_profiles_compare_camera_angle_and_fog(self):
        spec = self.spec()
        self.assertEqual(spec["schemaVersion"], 1)
        profiles = spec["profiles"]
        self.assertEqual([p["id"] for p in profiles], ["fixed_isometric", "steep_tilt"])
        self.assertNotEqual(profiles[0]["pitchDegrees"], profiles[1]["pitchDegrees"])
        self.assertNotEqual(profiles[0]["fogDensity"], profiles[1]["fogDensity"])
        for profile in profiles:
            self.assertTrue(20 <= profile["pitchDegrees"] <= 75)
            self.assertTrue(0 < profile["orthographicSize"] <= 20)
            self.assertTrue(0 <= profile["fogDensity"] <= 0.1)

    def test_stable_texturing_and_representative_silhouettes(self):
        spec = self.spec()
        self.assertEqual(spec["textureFilter"], "Point")
        self.assertFalse(spec["vertexSnapping"])
        self.assertIn(spec["texturePixels"], (16, 32, 64))
        self.assertGreater(spec["tileWorldUnits"], 0)
        self.assertTrue({
            "ship_deck", "snow", "sea_ice", "industrial_cover",
            "protagonist", "foliage", "artificial_light", "fog",
        }.issubset(set(spec["features"])))

    def test_editor_generator_is_explicit_and_uses_real_scenes(self):
        source = GENERATOR.read_text(encoding="utf-8")
        for token in (
            "[MenuItem(", "CreateScene", "EditorSceneManager.NewScene",
            "EditorSceneManager.SaveScene", "Camera", "GameObject.CreatePrimitive",
            "AssetDatabase.CreateFolder", "VisualBenchmarks/Scenes",
        ):
            self.assertIn(token, source, token)
        self.assertNotIn("[InitializeOnLoad", source)

    def test_render_capture_is_opt_in_and_not_claimed_by_g0(self):
        source = GENERATOR.read_text(encoding="utf-8")
        for token in ("CaptureScreenshots", "RenderTexture", "ReadPixels",
                      "EncodeToPNG", "Logs/visual-benchmark"):
            self.assertIn(token, source, token)
        self.assertNotIn("ScreenCapture.CaptureScreenshot", source)

    def test_unity_asset_metadata_and_editor_only_assembly(self):
        import re
        asm_path = ROOT / "Assets/Editor/VisualBenchmark/FrostArk.VisualBenchmark.Editor.asmdef"
        asm = json.loads(asm_path.read_text(encoding="utf-8"))
        self.assertEqual(asm["includePlatforms"], ["Editor"])
        self.assertEqual(asm["references"], [])
        for path in (
            ROOT / "Assets/Editor/VisualBenchmark",
            GENERATOR, asm_path, MANIFEST,
            ROOT / "Assets/VisualBenchmarks",
        ):
            meta = Path(str(path) + ".meta")
            self.assertTrue(meta.is_file(), meta)
            self.assertRegex(meta.read_text(encoding="utf-8"),
                             r"(?m)^guid: [0-9a-f]{32}$")
        self.assertTrue(GUID_META.is_file())

    def test_decision_record_names_unverified_gates_and_targets(self):
        doc = DOC.read_text(encoding="utf-8")
        for token in ("G0", "G1", "G2", "G3", "G4",
                      "Unity 6000.6.0f1", "desktop", "mobile", "WebGL",
                      "controller", "screenshot", "unverified"):
            self.assertIn(token.lower(), doc.lower(), token)


if __name__ == "__main__":
    unittest.main()
