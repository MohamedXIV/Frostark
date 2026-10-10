# FrostArk.Core independent verification

The `src/FrostArk.Core` .NET Standard 2.1 project links **the exact same C# sources** Unity compiles under `Assets/FrostArk/Core`. Unity's `FrostArk.Core.asmdef` excludes UnityEngine references. Do not copy the C# files into two codebases.

With .NET **8 SDK** and NuGet access available, run from the repository root:

```bash
dotnet restore tests/FrostArk.Core.Tests/FrostArk.Core.Tests.csproj
dotnet test tests/FrostArk.Core.Tests/FrostArk.Core.Tests.csproj --no-restore --logger "console;verbosity=normal"
```

This compiles the linked C# and executes the NUnit contract tests without Unity, VContainer or other runtime packages. Test dependencies are pinned in the .csproj; SDK-generated `bin/`, `obj/`, `TestResults/` must not be checked in.

**Verification boundary:** .NET test success is G1, not Unity G2. An actual authorized Unity compile/import is needed separately before engine integration can be called verified. The initial environment establishing this PR did **not** provide a .NET SDK or NuGet network connectivity, so the .NET commands above remain **NOT RUN** until executed on a compatible runner. No GitHub Actions or ShellWarden required. All expected numeric vectors are fixed in `CoreContractTests.cs`.

This foundation provides a valid clock value type, stable opaque IDs and a version-sensitive hash primitive. World scheduler, command bus, save DTOs, and actual seeded ecosystem belong to subsequent issues #16–#18.

## Optional offline G0 structural preflight

Python 3.10+ can verify the Unity/.NET shared-source wiring, Unity `.meta` files and GUIDs, project references, and pinned NUnit dependencies without Unity or .NET:

```bash
python tools/validate_core_structure.py
python -m unittest discover -s tests/validation -p 'test_*.py' -v
```

The checker has seven independent Python fixture tests (including an observed red/green cycle for nested Unity folder metadata). G0 PASS is **not** C# compilation, a successful NUnit run, or a Unity import. Actual `dotnet test` and Unity engine verification remain separate gates before merge.
