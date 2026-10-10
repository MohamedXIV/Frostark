# V01 — visual benchmark: camera, stable textures and readability

**Status (2026-10-10): generator authored; G0 structural tests only until hosted results. Unity 6000.6.0f1 G2 import/compile, G3 renderer screenshot, G4 controller/device measurements are unverified.** This is an experiment, not an approved final art/camera choice. Issue #20 and decision D018 remain open.

## Fixed experiment and alternatives

`Assets/VisualBenchmarks/visual-benchmark.json` is the versioned source for two camera profiles on the **same** representative geometry. `fixed_isometric` uses a 35° downward pitch and fog density 0.014; `steep_tilt` uses 60° and 0.024. Both use yaw 45°, orthographic size 11, URP Lit, 32-pixel deterministic checker textures, point filtering and no vertex snapping. Snow, sea ice, a ship deck/hull/cabin/rail, industrial cover/tower, two humanoid silhouettes, sparse foliage and a warm point light intentionally create competing foreground/occlusion cases. These are neutral geometry fixtures, **not** production art.

The camera pitch and fog vary together for the initial atmosphere stress test. **Do not attribute an observed difference to pitch alone.** For a controlled camera-only comparison, hold fog constant in the manifest and regenerate both scenes; repeat a fog-only sweep holding pitch constant. Record each manifest value beside the image. This avoids mistaking two correlated changes for a camera effect.

## Explicit Unity steps — NOT executed here

Use an authorized Unity 6000.6.0f1 checkout with the pinned URP 17.6.0. Open the Editor and invoke **FrostArk → Visual Benchmark → Generate Comparison Scenes**. The generator writes only under `Assets/VisualBenchmarks/{Scenes,Materials,Textures}`, never touches SampleScene, never auto-runs, and saves two independent scenes. Review generated scene, material, texture and `.meta` diffs before tracking them. For headless import/compile, a licensed authorized runner can invoke:

```text
Unity.exe -batchmode -nographics -quit -projectPath <authorized-checkout> -logFile <ignored-log-path>
```

Import success alone is G2 compile, **not** a rendered screenshot. In a graphics-capable Editor invoke **FrostArk → Visual Benchmark → Capture Comparison PNGs**. It uses a URP `RenderPipeline.StandardRequest` into a `RenderTexture` (not unsupported `Camera.Render`) and writes 1600×900 desktop, 1024×768 controller/4:3 and 960×540 mobile-landscape **framing simulations** to ignored `Logs/visual-benchmark/`. These resolutions do not prove hardware/controller/mobile acceptance. Capture may require a graphics-capable interactive Editor; `-nographics` cannot substitute. Never claim the screenshots exist until files and actual render logs are inspected.

## Evaluation and decision record (unresolved)

| Criterion | Fixed isometric | Steep tilt | Evidence needed |
|---|---|---|---|
| Protagonist vs cover silhouette | Unverified | Unverified | Actual PNGs; player behind wall and in open |
| Ship rail/deck occlusion | Unverified | Unverified | Actual PNGs; both foreground and cabin |
| Snow/ice texture readability | Unverified | Unverified | Real renderer at 100% scale, no PSX wobble |
| Fog atmosphere vs visibility | Unverified | Unverified | Separate fog-only sweep; danger cue legibility |
| Controller/keyboard navigation | Unverified | Unverified | Real controller + keyboard/mouse interaction |
| Desktop GPU/frame time, draw calls, memory | Unverified | Unverified | Profiler capture on representative hardware |
| Mobile/WebGL viability | Unverified | Unverified | Device/browser builds, memory/thermals, WebGL module |
| Accessible contrast/low-vision readability | Unverified | Unverified | Contrast, color-independent cues, scale tests |

**Decision D018 remains Open**: neither camera profile is accepted. A final ADR must link dated Unity screenshots, occlusion findings, real controller tests, measured budgets and platform risks; then choose a camera or explicitly request a new experiment.

## Verification ladder and risks

- **G0:** Python manifest/metadata/source-shape checks via `python3 -m unittest discover -s tests/validation -p 'test_*.py' -v`. These cannot compile or render C#.
- **G1:** Existing `dotnet test tests/FrostArk.Core.Tests/FrostArk.Core.Tests.csproj` covers unrelated pure-C# world logic. This editor-only experiment adds **no** new .NET runtime claim.
- **G2:** Licensed Unity import/compile of editor script and generated scene. Pending.
- **G3:** Graphics-capable URP screenshots plus visual review, scene/Game View interactions, controller readability. Pending.
- **G4:** Actual desktop/mobile/WebGL build and performance validation. Pending. The last verified local bootstrap lacked WebGL module; do not infer it is installed now.

No asset budget or frame-time number is claimed from static analysis. Renderer path, shader variant/URP quality profiles, texture filtering on targets and scene-generated material import remain real engine risks. Existing desktop/mobile URP assets and production scenes are deliberately unchanged.

**Reference:** Unity URP render requests use `RenderPipeline.StandardRequest` / `RenderPipeline.SubmitRenderRequest`; Unity documentation: https://docs.unity.com/en-us/engine/6000.3/manual/cameras/urp/multiple/user-render-requests .
