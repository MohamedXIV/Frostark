# Single-source Core contract (Issue #15)

## Production source and compilation

- Runtime source path: `Assets/FrostArk/Core/`; `FrostArk.Core.asmdef` intentionally has `noEngineReferences: true`.
- Standalone library: `src/FrostArk.Core/FrostArk.Core.csproj`, targets `netstandard2.1`, compiles those sources **by reference**, not copies.
- Tests: `tests/FrostArk.Core.Tests/FrostArk.Core.Tests.csproj`, targets `net8.0`, runs NUnit via `dotnet test` against the library.
- Rationale: Unity supports .NET Standard 2.1; standalone SDK can test domain logic without Unity. Test project may use net8-specific tooling, but **domain code may not**.
- Repo `.gitignore` excludes generated Unity .csproj files by default; two explicit exemptions preserve the authored SDK project files.

## Minimal stable contracts

- `WorldMinute`: nonnegative whole minutes, explicit checked increment, bounded decrement, value/ordering semantics. Day-night cycles and scheduler are not implemented here.
- `EntityId`: ordinal, case-sensitive persistent ID, never Unity instance ID. Default is invalid; reject empty/leading/trailing whitespace. No global IDs are generated implicitly.
- `StableHash32`: specified FNV-1a 32-bit UTF-8 hashing, seeded offset basis; deterministic golden vectors, not cryptography and not yet a full gameplay RNG. Do not change algorithm without save/replay migration strategy.

## Verification

Run `dotnet test tests/FrostArk.Core.Tests/FrostArk.Core.Tests.csproj` in a compatible .NET environment and capture actual runner exit/output before marking G1 PASS. An external, isolated engine-validation gate is still required before claiming Unity G2. This authored branch is not a claim of passing tests if a runner is unavailable.

## Forward work

#16 owns authoritative WorldClock/system scheduling and interruptible advancement. #17 owns command validation, canonical WorldState and atomic transitions. #18 owns versioned save/migration and replay. Keep these separate from Issue #15's assembly/fixture baseline and avoid speculative gameplay types.
