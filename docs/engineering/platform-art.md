# Platforms, visual direction, controls and accessibility

## Platform priority and shipment gates

1. **Desktop:** Windows, Linux, macOS. First performance and UX baseline with keyboard/mouse + controller. Native desktop QA on each OS is still required.
2. **Mobile:** Android then iOS readiness; scalable UI, adaptive quality, thermals and touch interaction. Exact first-mobile target undecided.
3. **Web:** Unity Web build compatible with constrained memory, browser file/storage/input capabilities, downloadable footprint and web-safe plugins. Verify actual browser matrix; do not assume desktop URP feature parity.
4. **Handheld PCs:** Steam Deck/Linux and Windows handhelds with controller navigation, screen scaling, resume/suspend and power/performance constraints.
5. **Consoles:** Xbox/PlayStation/Nintendo ports after platform eligibility, licensed SDKs and profiling; no claim that repository by itself gives console support.

These are **priority of investment**, not a promise all platforms ship day one.

## Art bible (initial)

Low-poly readable 3D mesh silhouettes; expressive, pixel-art-inspired/pixel-detailed textures carry much of the visual information. Cold maritime scrap-punk and biopunk atmosphere: welded armor, corroded hulls, snow and ice, fog, experimental growths and harsh artificial lights. Overall result should be beautiful and stable **without forced PSX vertex snapping, jittery affine textures or perspective artifacts**.

Top-down / near-isometric camera framing is still under evaluation; art, cover and interiors require legibility under occlusion. User favors readable beauty over era-accurate PSX rendering. Optional restrained dithering/post-process can be tested, not made mandatory. Texture resolution, filtering, palette, outline, shadow and camera zoom require an in-engine art target test.

## Renderer and scalability

Use existing Unity 6.6 URP project with separate PC/mobile profiles. Establish quality tiers and measurable frame-time, memory and loading budgets per device through experiments. Prefer authored/static and baked lighting where appropriate, simple shaders, LOD, pooled VFX and sensible streaming; no expensive volumetrics required to express 'the Haze'. View distance and AI/materialization LOD are gameplay-critical.

## Player interactions

- Desktop: WASD/left stick locomotion, mouse/right stick aim, contextual interact, inventory, map and command wheel.
- Mobile: adapt controls and HUD after PC affordances validated; do not assume a mouse cursor, tiny tactical hit targets or a full RTS selection UI.
- Controller/handheld: focus navigation, radial commands, rescalable typography, safe zones and remapping.
- Accessibility baseline: readable color/contrast, scalable HUD/text, configurable motion/shake, subtitles, color-independent threat cues, input remapping and adjustable command assistance.

## Art and platform proofs

A representative scene must include ship deck, water/ice, island ruin, low-poly character, foliage/industrial silhouettes, dark/bright lighting, fog, VFX, cover and UI. Test under desktop, low-end/mobile, Web and controller footprints before committing to a heavy content pipeline. GPU instancing/URP feature assumptions need target evidence.
