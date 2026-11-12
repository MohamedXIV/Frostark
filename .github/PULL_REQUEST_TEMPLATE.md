## What changed

Describe the player-facing result (or the engineering reason, if internal). Link the Issue(s).

Closes/Refs: #

## Changelog

- [ ] Added a concise notable change to `CHANGELOG.md` under **[Unreleased]**.
- [ ] Not release-relevant; no changelog entry needed (explain briefly below).

Reason (if no entry):

## Verification

- Exact HEAD SHA:
- G0 structure/docs:
- G1 .NET tests:
- G2 Unity import/EditMode:
- G3 Unity PlayMode/scene:
- G4 actual target platforms:
- Not run / known blockers:

## Release safety

- [ ] No release tag, GitHub Release or Unity `bundleVersion` bump in this ordinary PR.
- [ ] Any intended changes to existing released/public behavior are explained.
- [ ] Only real test/build evidence is claimed.

See [FrostArk release policy](https://github.com/MohamedXIV/Frostark/blob/main/docs/production/versioning-and-changelog.md). Untested Unity/platform gates must say **NOT RUN**, not inferred PASS.
