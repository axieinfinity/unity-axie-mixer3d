# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.1.0]

### Added
- `AxiePlayable` — the official Playables-API animator (accessed via `AxieCharacter3D.Playable`),
  which drives clips through a `PlayableGraph` so animation works in player builds.
- `AxieFactory` runtime animation registry: `RegisterAnimations` / `RegisterAnimation` /
  `UnregisterAnimation` / `ClearRegisteredAnimations`, plus the public `AxieNamedClip` struct.
  Optional packages (e.g. `com.skymavis.axiemixer3d.weaponanims`) register extra clips here.
- `AxieCharacter3D.GetAnimClip` now falls back to clips registered on `AxieFactory.Default` for the
  character's body, so registered weapon clips play through both `Playable` and `AxieAnimator` with
  no caller changes.

### Changed
- `AxiePlayable` is now the only official animator. `AxieCharacter3D.Playable` /
  `AxieCharacter3DBehaviour.Playable` replace the removed `.Animator` properties, and the shared
  animation handle types (`AnimPlayParams`, `AnimTrack`, `AnimBlend`) now live in `AxiePlayable.cs`.
- **Weapon/action clips (`Action.*`) moved to the new optional package
  `com.skymavis.axiemixer3d.weaponanims`.** The main package now ships only the `Default.*`
  locomotion clips (~188 MB smaller). Install the weapon package and register its catalog to
  restore weapon actions.
- **Breaking:** `AnimNames` no longer contains the weapon/action constants (`AxeAttack`, `BowSkill`,
  `CannonWalk`, `AttackCombo`, …) — those now live in `WeaponAnimNames` in the weapon package.
  Update references from `AnimNames.X` to `WeaponAnimNames.X` for weapon clips.

### Removed
- `AxieCharacter3D.Animator` and `AxieCharacter3DBehaviour.Animator`. `AxieAnimator` (legacy
  `Animation`-based) is retained for reference only — it does not animate in player builds.

## [0.2.0]

### Added
- `AxieMixer3DVersion.Version` runtime constant exposing the package version.

### Changed
- Restructured the repository into a single Unity project with this package embedded at
  `Packages/com.skymavis.axiemixer3d/`. Example/demo content now lives in the project's `Assets/`.
- Renamed `README.MD` to `README.md` and moved PlayMode tests from `Tests/PlayMode` to `Tests/Runtime`
  to follow UPM conventions.
- `GetLiteAnimationClip` now matches animation names case-insensitively
  (e.g. `"default.idle"` resolves `"Default.Idle"`).
- Animation names and color-variant lookups are now built once per shared asset instead of being
  re-allocated per character, reducing per-character allocations when spawning many Axies.
- Assembled part GameObjects are now named deterministically (`{part}_{rigType}`) instead of using
  the prefab instance ID.
- `RenderAvatar` reuses a cached `CommandBuffer` and renderer/material data instead of allocating
  every frame; several editor/sample paths also drop per-`OnGUI`/per-frame allocations.

### Removed
- Dropped the "full"/part animation tier (`GetFullAnimationClip` and the full bake); only lite
  (body) animations remain. This shrinks the package from ~2.4 GB to ~628 MB (also removes orphaned
  LOD meshes and editor-only bake inputs).

### Fixed
- `AxieMixerConfigImporter` no longer hijacks every `.json` in the consumer project — it now uses a
  unique `.axiemixerconfig` extension.
- `AxieCharacter3D.Dispose` no longer destroys shared catalog materials; it frees only the materials
  the factory cloned for that instance.
- `AxieBodyData` no longer throws (and null out its animation maps) on duplicate/empty clip names.
- `AxieDescriptor.FromGenes` guards null, `0x`-prefixed, and over-length gene strings.
- `GetLiteAnimationClip` returns `null` for an unknown name instead of throwing.
- `AxieFactory` no longer throws on a null `sharedMaterial` or a hand-built `AxieDescriptor` with a
  null `parts` list.
- `AxieGenesDecoder` disposes its web request and aborts an in-flight fetch when the window closes.
- Sample scenes now dispose the characters, clips, and avatar textures they create, and guard the
  weapon-name split and clip instantiation that could throw.

## [0.0.1]

### Added
- Initial release: runtime assembly of fully-rigged 3D Axie characters from genetic data by mixing
  modular body parts (URP, Unity 2021.3+).
