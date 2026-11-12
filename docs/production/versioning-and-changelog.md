# FrostArk release versioning and changelog policy

**Adopted:** 2026-11-12. Applies to future changes; do not retroactively invent releases, tags, past release dates or test evidence.

## Authorities and scope

- **Git tag** (`vX.Y.Z` or `vX.Y.Z-alpha.N`/`-beta.N`/`-rc.N`) identifies an actual immutable release source commit. **GitHub Release** points to that same tag and contains curated notes/build links when real artifacts exist. A merged PR is **not** a release.
- `CHANGELOG.md` is the human-readable source for **notable gameplay/product changes**, maintained under `[Unreleased]` and moved into a dated version section only during a real release.
- The Unity `ProjectSettings/ProjectSettings.asset` `bundleVersion` is the numeric `X.Y.Z` version used for the project and distributable builds, **not evidence of a published release by itself**. It currently reads `0.1.0`; no tag/release is implied. When a release is prepared, make the numeric Unity version match the base of its tag; keep platform-specific integer build codes separate and monotonically increasing when platform rules require it.
- Do not add a second `VERSION` file, automatic GitHub Actions release workflow or new versioning dependency just to create bureaucracy. Revisit automation once tagged test builds are routine.

## Version numbers — SemVer 2.0 conventions for a game

- **Pre-1.0:** `0.Y.Z` means active development; no public stability/compatibility promise. Advance `Y` for a meaningful **milestone/release containing new playable capability**, `Z` for a **fix-only release on the same milestone**. These are product release decisions, **not automatic increments for every `feat:` or `fix:` commit**.
- **1.0.0:** first deliberately stable/public product milestone; decide its criteria separately. After 1.0, use `MAJOR` for deliberate breaking compatibility to defined public contracts, `MINOR` for backward-compatible features, `PATCH` for fixes.
- **Prerelease stages** (only when distributing a build): `v0.1.0-alpha.1`, `v0.1.0-beta.1`, `v0.1.0-rc.1`. Use the corresponding numeric `0.1.0` for Unity's `bundleVersion` to avoid assumptions about target-platform acceptance of prerelease suffixes. An internal/editor development build can be identified by `git SHA` without bumping the public product version.
- **Do not tie save-schema versions, GameObject prefab revisions, GitHub issue/epic numbers, or R0–R8 roadmap stages to product release numbers.** Internal pre-release save backwards compatibility is not a release gate now; do not add migration work unless explicitly scoped. This does not authorize careless data corruption inside a current build.

## Commit and PR naming — Conventional Commits 1.0

Format: `type(optional-scope): imperative summary`. Use:
- `feat:` player-visible or meaningful gameplay capabilities;
- `fix:` defect corrections;
- `perf:` measurable performance improvements;
- `refactor:` non-behavior-changing code changes;
- `docs:`, `test:`, `build:`, `ci:`, `chore:` as applicable.
- Use `!` and explain `BREAKING CHANGE:` in the body/footer when deliberately changing a published contract; in 0.x this does **not** automatically force a 1.0 bump.
- Prefer conventional **PR titles** (e.g. `feat(crew): add named survivor recruitment (#31)`). PR bodies reference Issues, evidence and a proposed changelog entry.
- New standalone work generally uses a reviewed squash merge with a meaningful conventional title; **dependent stacked PRs may use merge commits** to preserve verified ancestry. Do not rewrite historical commits or risk integration correctness to satisfy a naming rule.

## CHANGELOG.md — Keep a Changelog 1.1

- Keep `## [Unreleased]` at the top. Add **one concise, player-facing or release-relevant bullet** for each notable merged PR, with the PR link; combine related implementation PRs when they form one feature.
- Categories: **Added, Changed, Fixed, Deprecated, Removed, Security**. Use only categories that have entries. Details such as file paths, test counts, pure refactors and routine docs are not player-facing release notes unless they materially affect users.
- **Do not dump the Git history** or maintain two competing changelogs. Changes not released stay under `[Unreleased]`; no speculative release dates.
- On an actual tagged release, move relevant `[Unreleased]` entries into `## [X.Y.Z[-stage.N]] - YYYY-MM-DD`, verify them against the tested commit, leave a fresh `[Unreleased]` above and link the GitHub Release. Do not rewrite existing published sections.
- Prior work may be described when preparing the first release, but only after confirming it exists in the tagged tested build. This adoption does **not** claim any prior public release.

## Practical PR and release workflow

1. **Every PR:** meaningful conventional title; link Issue; summarize scope, player impact, real tests and platform gates. Put an `[Unreleased]` entry in `CHANGELOG.md` if notable; otherwise say "No changelog entry" with a reason. Agents must not bump `bundleVersion` on normal feature PRs.
2. **During development:** merge reviewed PRs into `main`, leaving `[Unreleased]` accumulating. Avoid auto-tagging, auto-publishing or pushing new CI workflows.
3. **When a build is worth releasing:** choose a version and commit from `main`. Review changelog and actual feature status; update numeric Unity `bundleVersion`, release note section and any platform integer build codes. Run G0/G1 and, **for an advertised playable Unity build**, real G2/G3 on the release candidate; claim G4 only for platforms actually built/tested.
4. **Only after verification:** create an annotated `v...` Git tag on the exact verified commit, then publish the matching GitHub Release (mark prerelease for alpha/beta/rc) with notes and only existing build artifacts. Do not silently change the contents of a published release/tag; publish a new version for corrections.
5. **If no build/artifact exists:** it is valid to keep working under `[Unreleased]` and a Git SHA. No pretend milestone, release page or platform support claim.

References: [SemVer 2.0](https://semver.org/spec/v2.0.0.html), [Conventional Commits 1.0](https://www.conventionalcommits.org/en/v1.0.0/), [Keep a Changelog 1.1](https://keepachangelog.com/en/1.1.0/).

**GitHub owns engineering releases and implementation truth.** Miro owns game-design decisions, not release numbering.
