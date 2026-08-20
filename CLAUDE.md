# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A **Unity Package Manager (UPM) package** (`com.skymavis.axiemixer3d`), not a standalone application. It assembles fully-rigged 3D Axie characters from genetic data at runtime by mixing modular body parts. Target: Unity **2021.3+**, **URP** (Universal Render Pipeline). Consumers install it via Git URL (`Window → Package Manager → Add package from Git URL`).

There is no CLI build/lint/test workflow — the package is developed and tested by opening it (or a host project that references it) inside the Unity Editor. Tests are Unity Test Runner **PlayMode** tests (`Window → General → Test Runner`); the `Tests/PlayMode` assembly currently contains only its asmdef, no test `.cs` files.

## Assemblies & API visibility

Three assembly definitions, each defining what's compiled and what depends on what:

- `Runtime/SkyMavis.AxieMixer3D.asmdef` — runtime code, namespace `SkyMavis.AxieMixer3D`. References URP + `com.unity.editorcoroutines` (by GUID).
- `Editor/SkyMavis.AxieMixer3D.Editor.asmdef` — editor-only tools, namespace `SkyMavis.AxieMixer3D.Editor` (`includePlatforms: [Editor]`).
- `Tests/PlayMode/SkyMavis.AxieMixer3D.Tests.PlayMode.asmdef` — gated behind `UNITY_INCLUDE_TESTS`, references NUnit + PerformanceTesting.

`Runtime/AssemblyInfo.cs` grants `InternalsVisibleTo` for the Editor, Tests, and `SkyMavis.AxieMixer3D.Dev.Editor` assemblies. **The public API surface is deliberately narrow** — most data-carrying types are `internal`:
- **Public**: `AxieCharacter3D`, `AxieCharacter3DBehaviour`, `AxieDescriptor`, `AxiePartDescriptor`, `AxieInstantiationParams`, `AxieAvatarRenderParams`, the enums (`AxieBodyType`, `AxiePartType`, `AxieRigType`), and `OutlinePostProcessRendererFeature`.
- **Internal**: `AxieFactory`, `AxieBodyData`, `AxiePartData`, `AxieRigData`, `AxieAnimationData`, `AxieMixerConfig`.

## Core pipeline (genes → character)

The whole system is a data-driven assembly pipeline. Trace it top-down:

1. **`AxieDescriptor.FromGenes(string)`** (`Runtime/AxieDescriptor.cs`) — decodes a 512-bit hex gene string by bit-unpacking. Genes are parsed least-significant-first (`genes[^(i+1)]`), then `PopGenesBits` walks fields in order: main class, body skin/details, colors, then 6 parts (Eye, Mouth, Ear, Horn, Back, Tail). Produces body type, `colorVariant`, and 6 `AxiePartDescriptor`s. Class numbers and color-variant indices are hard-coded lookup tables — edit these switch expressions when Axie adds classes/colors.

2. **`AxieFactory.CreateCharacter(descriptor, instantiationParams)`** (`Runtime/AxieFactory.cs`) — the heart of assembly:
   - `AxieFactory.Default` is a singleton `ScriptableObject` loaded from `Resources/AxieMixer3D/AxieFactory`. It serializes the `_config` (colors), `_addonPaths` (mystic skins), and `_defaultInstantiationParams`.
   - `CoerceDescriptor` normalizes each part to the assets that actually exist: `skin` collapses to `1` only for the mystic case (`skin==1 && variant==2`) else `0`; `level` is forced to `1`.
   - Instantiates the body prefab from `AxieBodyData`, then finds bone attach points by regex on transform names (`Root_{rigType}_JNT`) — see `CollectAttachPoints`. `Weapon_R`/`Weapon_L` become the weapon attach points.
   - For each part, loads `AxiePartData`, and for each rig within it creates a child GameObject (MeshFilter+MeshRenderer) parented to the matching attach point, picks the LOD mesh, assigns the material, applies mystic addon materials/prefabs, and applies per-part layer overrides.
   - `Colorize()` sets `_PrimaryColor`/`_SecondaryColor` on materials, either per-instance (duplicating materials) or via a `MaterialPropertyBlock` when `useMaterialPropertyBlocks` is set (saves memory but **breaks SRP batching**).
   - Returns `AxieCharacter3D`.

3. **`AxieCharacter3D`** (`Runtime/AxieCharacter3D.cs`) — the assembled result. `IDisposable`: **callers must `Dispose()`** — it destroys per-instance duplicated materials (skipped when `useMaterialPropertyBlocks`) and the root GameObject. Also exposes `GetLiteAnimationClip`/`GetFullAnimationClip`, weapon attach points, and `RenderAvatar`.

`AxieCharacter3DBehaviour` (`Runtime/AxieCharacter3DBehaviour.cs`) is the MonoBehaviour wrapper that owns an `AxieCharacter3D`'s lifecycle (builds on `Start`, disposes on `OnDestroy`, `Rebuild()` to regenerate) and manages avatar `RenderTexture`s.

## Resource loading conventions

Everything is loaded at runtime via `Resources.Load` from `Resources/AxieMixer3D/`. The string-formatted asset names are load-bearing — changing a format string means renaming assets too:

- Bodies: `Resources/AxieMixer3D/Data/Bodies/{AxieBodyType}` → `AxieBodyData`.
- Parts: `Resources/AxieMixer3D/Data/Parts/{partName}` → `AxiePartData`, where `partName = S{skin:00}_{class}{variant:00}_L{level}_{type}` (e.g. `S00_Beast02_L1_Horn`).
- Addons/mystic: `{addonPath}/S{skin:00}_{class}{variant:00}_L{level}_{rigType}` — loaded via `Resources.LoadAll`, cached in `_addonCache`.
- `BuiltAssets/` holds generated prefabs, meshes, and the `S_Axie_Mixer_V4` shader; `AddonAssets/mystic-axie/` holds mystic skin overrides.

## Rendering

- **Avatars**: `AxieCharacter3D.RenderAvatar` renders into a caller-provided `RenderTexture` using a `CommandBuffer` with an orthographic projection, drawing each `SkinnedMeshRenderer`'s `ExtraPrePass` then `Forward` shader passes. Skips 3 frames after entering Play Mode before rendering (graphics device init).
- **Outlines**, two independent approaches (see README for setup):
  - *Draw Objects* — URP built-in Render Objects feature + `partLayerOverrides` to move parts onto an "Outline" layer + `Outline_RenderObjects.mat`. Best for Forward rendering.
  - *Post-process* — `OutlinePostProcessRendererFeature` (`Runtime/Outline/`), screen-space depth/normal edge detection. Best for Deferred.

## Editor tools (Tools → Axie Mixer 3D)

- **Axie Genes Decoder** (`Editor/AxieGenesDecoder.cs`) — fetches real Axie genes from the Axie Infinity GraphQL gateway (`https://graphql-gateway.axieinfinity.com/graphql`) by ID, or decodes a pasted gene string, into an inspectable `AxieDescriptor`.
- **Axie Avatar Preview** (`Editor/AxieAvatarPreview.cs`) — live avatar preview for a selected `AxieCharacter3DBehaviour`.
- `AxieMixerConfigImporter` (`Editor/AxieMixerConfigImporter.cs`) — a `ScriptedImporter` that turns `AxieMixerConfig.json` into an `AxieMixerConfig` ScriptableObject asset at import time.

## Working in this repo

- **Never create or delete a Unity asset/script without its `.meta` file.** Every file and folder has a sibling `.meta` carrying its GUID; asmdef references and prefab links resolve by GUID, so a missing/regenerated meta breaks references. Let Unity generate metas rather than hand-editing GUIDs.
- `Samples~/` is intentionally suffixed with `~` so Unity ignores it in-package; its contents are imported into consumer projects via the Package Manager Samples UI. Sample scripts there (`AxieCollection.cs`, `AxieAvatars.cs`, `AnimatorSample.cs`, etc.) are the canonical usage examples.
- A single animation set exists (the old lite/full two-tier split was dropped): body clips are stored in `AxieBodyData.animations` (a `List<AxieAnimationData>`, each a `name` + direct `AnimationClip`) and looked up by name via `AxieCharacter3D.GetAnimClip`. Asset references are plain direct references — `LazyLoadReference` was removed (it only defers loading in the Editor and gives no build-size or player-memory benefit).
- LOD is index-based: `lodLevel` selects from the `lodMeshes` list on body/rig data (0 = highest detail), clamped to the available range.
