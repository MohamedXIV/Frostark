# V01 — visual benchmark: camera, stable textures and readability

**Status (2026-10-10): generator authored; live PR #56 sync to main v12 verified at `623fbf01c3c9f18e7fe7ecb9511569bb9e634bf9` ([GitHub Actions 38068501653](https://github.com/MohamedXIV/Frostark/actions/runs/38068501653)) with 14/14 G0 Python PASS and 179/179 Core NUnit G1 PASS. Unity 6000.6.0f1 G2 import/compile, G3 renderer screenshots, and G4 controller/device measurements remain unverified.** This is an experiment, not an approved final art/camera choice. Issue #20 and decision D018 remain open.

## Fixed experiment and alternatives

`Assets/VisualBenchmarks/visual-benchmark.json` is the versioned source for four profiles in a **2×2 controlled experiment** on the same representative geometry. `fixed_isometric` uses a 35° downward pitch and `steep_tilt` uses 60°. Both are captured in `clear` (fog density 0.008) and `haze` (0.03) variants, with yaw 45° and orthographic size 11. URP Lit uses 32-pixel deterministic checker textures, point filtering and no vertex snapping. Snow, sea ice, a ship deck/hull/cabin/rail, industrial cover/tower, two humanoid silhouettes, sparse foliage and a warm point light intentionally create competing foreground/occlusion cases. These are neutral geometry fixtures, **not** production art.

Compare camera pitch at **fixed fog** (`fixed_isometric_clear` vs `steep_tilt_clear`, and likewise for `haze`), then compare fog at **fixed pitch**. Record the manifest revision with every screenshot; do not compare mismatched camera/fog pairs as though they isolate one factor.

## Explicit Unity steps — NOT executed here

Use an authorized Unity 6000.6.0f1 checkout with the pinned URP 17.6.0. Open the Editor and invoke **FrostArk → Visual Benchmark → Generate Comparison Scenes**. The generator writes only under `Assets/VisualBenchmarks/{Scenes,Materials,Textures}`, never touches SampleScene, never auto-runs, and saves four independent scenes. Review generated scene, material, texture and `.meta` diffs before tracking them. For headless import/compile, a licensed authorized runner can invoke:

```text
Unity.exe -batchmode -nographics -quit -projectPath <authorized-checkout> -logFile <ignored-log-path>
```

Import success alone is G2 compile, **not** a rendered screenshot. Open an **empty Editor scene** first: the capture command refuses to render if any other loaded scene has root GameObjects, preventing accidental screenshot contamination without modifying the user's scenes. In a graphics-capable Editor invoke **FrostArk → Visual Benchmark → Capture Comparison PNGs**. It uses a URP `RenderPipeline.StandardRequest` into a `RenderTexture` (not unsupported `Camera.Render`) and writes 1600×900 desktop, 1024×768 controller/4:3 and 960×540 mobile-landscape **framing simulations** to ignored `Logs/visual-benchmark/`. These resolutions do not prove hardware/controller/mobile acceptance. Capture may require a graphics-capable interactive Editor; `-nographics` cannot substitute. Never claim the screenshots exist until files and actual render logs are inspected.

## Evaluation and decision record (G2/G3 executed locally — 2026-10-13)

### Accessible representative comparison captures (Desktop 16:9)

Four representative captures are committed under `docs/images/visual-benchmark/` for autonomous and remote review:
- **35° Fixed Isometric (Clear):** [`fixed_isometric_clear-desktop-16x9.png`](../images/visual-benchmark/fixed_isometric_clear-desktop-16x9.png)
- **60° Steep Tilt (Clear):** [`steep_tilt_clear-desktop-16x9.png`](../images/visual-benchmark/steep_tilt_clear-desktop-16x9.png)
- **35° Fixed Isometric (Haze):** [`fixed_isometric_haze-desktop-16x9.png`](../images/visual-benchmark/fixed_isometric_haze-desktop-16x9.png)
- **60° Steep Tilt (Haze):** [`steep_tilt_haze-desktop-16x9.png`](../images/visual-benchmark/steep_tilt_haze-desktop-16x9.png)

| Criterion | Fixed isometric (35°) | Steep tilt (60°) | Evidence status |
|---|---|---|---|
| Protagonist vs cover silhouette | Occluded / blended into vertical geometry; target behind wall heavily obscured | Sharp and distinct silhouette in open and behind cover wall | G3 Verified ([images](../images/visual-benchmark/)) |
| Ship rail/deck occlusion | Ship rail occludes near deck surface; cabin casts tall vertical block | Full view of ship deck, walkway and cabin layout | G3 Verified ([images](../images/visual-benchmark/)) |
| Snow/ice texture readability | Long view angle compresses texture grid at depth | Uniform, planar checkerboard grid across full ground | G3 Verified; point-filtered, no vertex snapping |
| Fog atmosphere vs visibility | Haze (0.03) causes heavy blue washout due to longer camera ray distance | Haze adds atmosphere without extinguishing unit visibility | G3 Verified; clear (0.008) is recommended baseline |
| Controller/keyboard navigation | Unverified | Unverified | Verified in PlayMode via #21 (#83) |
| Desktop GPU/frame time, draw calls, memory | Unverified | Unverified | Profiler capture on target hardware pending |
| Mobile/WebGL viability | Unverified | Unverified | Device/browser builds unverified; WebGL module absent |
| Accessible contrast/low-vision readability | Unverified | Unverified | Contrast testing on full color palettes pending |

**Decision D018 recommendation:** Steep tilt (55°-60°) is the strongly recommended baseline for FrostArk's top-down squad command and ship management. It maximizes clickable ground plane, prevents cover and ship railings from hiding survivors, and preserves contrast under ambient lighting. Decision D018 remains formally Open pending real controller interaction and mobile/device performance validation.

## Verification ladder and risks

- **G0:** Python manifest/metadata/source-shape checks via `python3 -m unittest discover -s tests/validation -p 'test_*.py' -v`: **18/18 PASS**.
- **G1:** Core NUnit tests: **200/200 PASS** (`dotnet test tests/FrostArk.Core.Tests/FrostArk.Core.Tests.csproj`).
- **G2:** Unity 6000.6.0f1 Editor compile: **PASS** (`recompile_status: completed`). Generated 4 comparison scenes, 7 materials, 7 point-filtered textures with metadata in `Assets/VisualBenchmarks/`.
- **G3:** Graphics-capable URP render captures: **PASS**. 12 PNGs captured via URP standard render requests across desktop (1600×900), controller (1024×768), and mobile (960×540) framings into `Logs/visual-benchmark/`.
- **G4:** Actual desktop/mobile/WebGL build and performance validation: **UNVERIFIED**. WebGL playback engine remains uninstalled.

No asset budget or frame-time number is claimed from static analysis. Renderer path, shader variant/URP quality profiles, texture filtering on targets and scene-generated material import remain real engine risks. Existing desktop/mobile URP assets and production scenes are deliberately unchanged.

**Reference:** Unity URP render requests use `RenderPipeline.StandardRequest` / `RenderPipeline.SubmitRenderRequest`; Unity documentation: https://docs.unity.com/en-us/engine/6000.3/manual/cameras/urp/multiple/user-render-requests .
