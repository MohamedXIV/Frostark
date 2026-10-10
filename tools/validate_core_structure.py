#!/usr/bin/env python3
"""Dependency-free G0 checks for FrostArk's shared Unity/.NET Core sources.

This is structural validation only. A PASS does NOT establish C# compilation,
NUnit success (G1), Unity import (G2), or runtime/gameplay correctness.
"""
from __future__ import annotations

import argparse
import json
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

CORE = Path("Assets/FrostArk/Core")
CORE_PROJECT = Path("src/FrostArk.Core/FrostArk.Core.csproj")
TEST_PROJECT = Path("tests/FrostArk.Core.Tests/FrostArk.Core.Tests.csproj")
TEST_FILE = Path("tests/FrostArk.Core.Tests/CoreContractTests.cs")
EXPECTED_COMPILE = "../../Assets/FrostArk/Core/**/*.cs"
EXPECTED_PROJECT_REF = "../../src/FrostArk.Core/FrostArk.Core.csproj"
REQUIRED_PACKAGES = {"Microsoft.NET.Test.Sdk", "NUnit", "NUnit3TestAdapter"}
GUID_PATTERN = re.compile(r"(?m)^guid:\s*([0-9a-fA-F]{32})\s*$")
ENGINE_PATTERN = re.compile(r"\b(?:using\s+(?:static\s+)?|global\s+using\s+)?(UnityEngine|UnityEditor)\b")


def validate(root: Path) -> list[str]:
    """Return actionable problems; empty means G0 structural checks passed."""
    root = Path(root).resolve()
    errors: list[str] = []

    def read(path: Path) -> str | None:
        full = root / path
        try:
            return full.read_text(encoding="utf-8")
        except (OSError, UnicodeError) as exc:
            errors.append(f"cannot read {path}: {exc}")
            return None

    def xml(path: Path) -> ET.Element | None:
        source = read(path)
        if source is None:
            return None
        try:
            return ET.fromstring(source)
        except ET.ParseError as exc:
            errors.append(f"invalid XML in {path}: {exc}")
            return None

    asm_path = CORE / "FrostArk.Core.asmdef"
    asm_text = read(asm_path)
    if asm_text is not None:
        try:
            asm = json.loads(asm_text)
            if asm.get("name") != "FrostArk.Core":
                errors.append(f"{asm_path}: assembly name must be FrostArk.Core")
            if asm.get("noEngineReferences") is not True:
                errors.append(f"{asm_path}: noEngineReferences must be true")
            if asm.get("references") != []:
                errors.append(f"{asm_path}: references must be empty for independent Core")
        except (json.JSONDecodeError, AttributeError) as exc:
            errors.append(f"invalid asmdef JSON in {asm_path}: {exc}")

    source_files = sorted((root / CORE).rglob("*.cs"))
    if not source_files:
        errors.append(f"{CORE}: no C# source files")
    for source in source_files:
        rel = source.relative_to(root)
        content = read(rel)
        if content is not None:
            match = ENGINE_PATTERN.search(content)
            if match:
                errors.append(f"{rel}: UnityEngine/UnityEditor dependency ({match.group(1)}) in Core")

    for path in [Path("Assets/FrostArk"), CORE, CORE / "World"]:
        if not (root / path).is_dir():
            errors.append(f"missing Unity directory: {path}")
        meta = Path(str(path) + ".meta")
        if not (root / meta).is_file():
            errors.append(f"missing Unity folder metadata: {meta}")

    for path in sorted((root / CORE).rglob("*")):
        if path.is_dir():
            rel = path.relative_to(root)
            meta = Path(str(rel) + ".meta")
            if not (root / meta).is_file():
                errors.append(f"missing Unity folder metadata: {meta}")
        elif path.is_file() and path.suffix != ".meta":
            rel = path.relative_to(root)
            meta = Path(str(rel) + ".meta")
            if not (root / meta).is_file():
                errors.append(f"missing Unity asset metadata: {meta}")

    seen_guids: dict[str, Path] = {}
    for meta in sorted((root / "Assets").rglob("*.meta")):
        rel = meta.relative_to(root)
        contents = read(rel)
        if contents is None:
            continue
        found = GUID_PATTERN.search(contents)
        if not found:
            errors.append(f"{rel}: missing or malformed GUID")
            continue
        guid = found.group(1).lower()
        if guid in seen_guids:
            errors.append(f"duplicate GUID {guid}: {seen_guids[guid]} and {rel}")
        else:
            seen_guids[guid] = rel

    core_xml = xml(CORE_PROJECT)
    if core_xml is not None:
        if core_xml.findtext("./PropertyGroup/TargetFramework") != "netstandard2.1":
            errors.append(f"{CORE_PROJECT}: TargetFramework must be netstandard2.1")
        if core_xml.findtext("./PropertyGroup/EnableDefaultCompileItems") != "false":
            errors.append(f"{CORE_PROJECT}: disable implicit source globbing")
        includes = [el.attrib.get("Include") for el in core_xml.findall("./ItemGroup/Compile")]
        if includes != [EXPECTED_COMPILE]:
            errors.append(f"{CORE_PROJECT}: linked Compile must be {EXPECTED_COMPILE} only")

    test_xml = xml(TEST_PROJECT)
    if test_xml is not None:
        if test_xml.findtext("./PropertyGroup/TargetFramework") != "net8.0":
            errors.append(f"{TEST_PROJECT}: TargetFramework must be net8.0")
        refs = [el.attrib.get("Include") for el in test_xml.findall("./ItemGroup/ProjectReference")]
        if refs != [EXPECTED_PROJECT_REF]:
            errors.append(f"{TEST_PROJECT}: must reference the shared Core project")
        packages = {el.attrib.get("Include"): el.attrib.get("Version")
                    for el in test_xml.findall("./ItemGroup/PackageReference")}
        for name in sorted(REQUIRED_PACKAGES):
            if not packages.get(name) or any(x in packages[name] for x in "*[](),"):
                errors.append(f"{TEST_PROJECT}: {name} must have an explicit pinned version")

    test_source = read(TEST_FILE)
    if test_source is not None and not re.search(r"\[(?:Test|TestCase)(?:\(|\])", test_source):
        errors.append(f"{TEST_FILE}: no NUnit test attribute found")

    ignore = read(Path(".gitignore"))
    if ignore is not None:
        lines = {line.strip() for line in ignore.splitlines()}
        for line in ("!/src/FrostArk.Core/FrostArk.Core.csproj",
                     "!/tests/FrostArk.Core.Tests/FrostArk.Core.Tests.csproj",
                     "**/[Bb]in/", "**/[Oo]bj/", "**/TestResults/"):
            if line not in lines:
                errors.append(f".gitignore: missing required rule {line}")

    return errors


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1],
                        help="FrostArk repository root (default: parent of tools/)")
    args = parser.parse_args(argv)
    problems = validate(args.root)
    if problems:
        for problem in problems:
            print(f"G0 FAIL: {problem}", file=sys.stderr)
        print(f"G0 FAIL: {len(problems)} structural problem(s). G1/G2 NOT RUN.", file=sys.stderr)
        return 1
    print("G0 PASS: shared Core source, Unity metadata, project links, and test harness structure.")
    print("G1 .NET/NUnit NOT RUN; G2 Unity import/compile NOT RUN by this script.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
