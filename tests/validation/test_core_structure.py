"""Contract tests for the optional G0 Core preflight; no Unity/.NET required."""
from __future__ import annotations

import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

VALIDATOR = Path(__file__).resolve().parents[2] / "tools" / "validate_core_structure.py"
spec = importlib.util.spec_from_file_location("validate_core_structure", VALIDATOR)
validator = importlib.util.module_from_spec(spec)
spec.loader.exec_module(validator)


def fixture(root: Path) -> None:
    def write(path: str, data: str) -> None:
        file = root / path
        file.parent.mkdir(parents=True, exist_ok=True)
        file.write_text(data, encoding="utf-8")

    def meta(guid: str) -> str:
        return f"fileFormatVersion: 2\nguid: {guid}\n"

    for index, path in enumerate([
        "Assets/FrostArk.meta", "Assets/FrostArk/Core.meta",
        "Assets/FrostArk/Core/World.meta",
        "Assets/FrostArk/Core/FrostArk.Core.asmdef.meta",
        "Assets/FrostArk/Core/World/EntityId.cs.meta",
    ]):
        write(path, meta(f"{index + 1:032x}"))
    write("Assets/FrostArk/Core/FrostArk.Core.asmdef", json.dumps({
        "name": "FrostArk.Core", "references": [], "noEngineReferences": True,
    }))
    write("Assets/FrostArk/Core/World/EntityId.cs", "namespace FrostArk.Core.World { public struct EntityId {} }\n")
    write("src/FrostArk.Core/FrostArk.Core.csproj", """<Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup><TargetFramework>netstandard2.1</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup>
    <ItemGroup><Compile Include="../../Assets/FrostArk/Core/**/*.cs" /></ItemGroup>
    </Project>""")
    write("tests/FrostArk.Core.Tests/FrostArk.Core.Tests.csproj", """<Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
    <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="NUnit" Version="3.14.0" />
    <PackageReference Include="NUnit3TestAdapter" Version="4.5.0" />
    <ProjectReference Include="../../src/FrostArk.Core/FrostArk.Core.csproj" />
    </ItemGroup></Project>""")
    write("tests/FrostArk.Core.Tests/CoreContractTests.cs", "[Test] public void StableHashMatchesVersionedGoldenVectors() {}")
    write(".gitignore", "*.csproj\n!/src/FrostArk.Core/FrostArk.Core.csproj\n!/tests/FrostArk.Core.Tests/FrostArk.Core.Tests.csproj\n**/[Bb]in/\n**/[Oo]bj/\n**/TestResults/\n")


class CoreStructureTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        fixture(self.root)

    def test_valid_single_source_contract(self) -> None:
        self.assertEqual(validator.validate(self.root), [])

    def test_missing_unity_meta_is_detected(self) -> None:
        (self.root / "Assets/FrostArk/Core/World/EntityId.cs.meta").unlink()
        self.assertTrue(any("EntityId.cs.meta" in e for e in validator.validate(self.root)))

    def test_engine_dependency_is_detected(self) -> None:
        (self.root / "Assets/FrostArk/Core/World/EntityId.cs").write_text(
            "using UnityEngine;\nnamespace FrostArk.Core.World {}", encoding="utf-8"
        )
        self.assertTrue(any("UnityEngine" in e for e in validator.validate(self.root)))

    def test_mismatched_compile_glob_is_detected(self) -> None:
        path = self.root / "src/FrostArk.Core/FrostArk.Core.csproj"
        path.write_text(path.read_text().replace("../../Assets/FrostArk/Core/**/*.cs", "Core/**/*.cs"), encoding="utf-8")
        self.assertTrue(any("linked Compile" in e for e in validator.validate(self.root)))

    def test_new_nested_unity_folder_requires_metadata(self) -> None:
        path = self.root / "Assets/FrostArk/Core/World/Weather"
        path.mkdir()
        (path / "Weather.cs").write_text("public struct Weather {}", encoding="utf-8")
        (path / "Weather.cs.meta").write_text("fileFormatVersion: 2\nguid: " + f"{31:032x}" + "\n", encoding="utf-8")
        problems = validator.validate(self.root)
        self.assertTrue(any("Weather.meta" in error for error in problems), problems)

    def test_duplicate_guids_are_detected(self) -> None:
        path = self.root / "Assets/FrostArk/Core/World/EntityId.cs.meta"
        path.write_text("fileFormatVersion: 2\nguid: " + f"{1:032x}" + "\n", encoding="utf-8")
        self.assertTrue(any("duplicate GUID" in e for e in validator.validate(self.root)))

    def test_unpinned_test_dependencies_are_detected(self) -> None:
        path = self.root / "tests/FrostArk.Core.Tests/FrostArk.Core.Tests.csproj"
        path.write_text(path.read_text().replace('Include="NUnit" Version="3.14.0"', 'Include="NUnit"'), encoding="utf-8")
        self.assertTrue(any("NUnit" in e and "version" in e for e in validator.validate(self.root)))


if __name__ == "__main__":
    unittest.main()
