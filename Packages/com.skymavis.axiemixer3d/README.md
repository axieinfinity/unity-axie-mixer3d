# Unity Axie Mixer 3D

This package provides the core functionality for assembling fully-rigged 3D Axie characters at runtime by mixing modular body parts from genetic data. Target: Unity **2021.3+** with the **Universal Render Pipeline (URP)**.

## Table of Contents

- [Installation](#installation)
- [Setup](#setup)
- [Usage](#usage)
  - [Basic Character Creation](#basic-character-creation)
  - [Instantiation Parameters](#instantiation-parameters)
  - [Working with Animations](#working-with-animations)
    - [Weapon animations](#weapon-animations)
  - [Weapon Attachment Points](#weapon-attachment-points)
- [Visual Effects](#visual-effects)
  - [Outline Rendering](#outline-rendering)
- [Components](#components)
  - [AxieCharacter3D](#axiecharacter3d)
  - [AxieCharacter3DBehaviour](#axiecharacter3dbehaviour)
  - [AxieMixerInitializer](#axiemixerinitializer)
- [Axie Avatars](#axie-avatars)
  - [Rendering an Avatar](#rendering-an-avatar)
  - [Avatar Render Parameters](#avatar-render-parameters)
  - [Important Notes](#important-notes)
- [Editor Tools](#editor-tools)
  - [Axie Genes Decoder](#axie-genes-decoder)
- [Sample Projects](#sample-projects)

## Installation

The package lives in a subfolder of the repository (`Packages/com.skymavis.axiemixer3d`), so the Git URL
must include a `?path=` query pointing at it.

In Unity, go to **Window → Package Manager → Add package from Git URL...**, then paste:

```txt
https://github.com/axieinfinity/unity-axie-mixer3d.git?path=/Packages/com.skymavis.axiemixer3d
```

To pin a specific tag or branch, append it as a fragment after the path:

```txt
https://github.com/axieinfinity/unity-axie-mixer3d.git?path=/Packages/com.skymavis.axiemixer3d#v0.0.1
```

Alternatively, add it to your project's `Packages/manifest.json` directly:

```jsonc
{
  "dependencies": {
    "com.skymavis.axiemixer3d": "https://github.com/axieinfinity/unity-axie-mixer3d.git?path=/Packages/com.skymavis.axiemixer3d"
  }
}
```

> **Requires** Unity **6000.0** (URP). `?path=` subfolder installs are supported on Unity 2019.3.4+.

## Setup

Before you can create any character, the mixer needs a **catalog** — an `AxieFactory` asset that
references the body/part/addon assets — assigned to `AxieFactory.Default`. The package does **not**
auto-load this from `Resources` (that would force every consumer to ship all the heavy assets), so you
register it explicitly with an [`AxieMixerInitializer`](#axiemixerinitializer):

1. Create an empty GameObject in your **bootstrap scene** (or a prefab loaded at startup).
2. Add the **Axie Mixer 3D → Axie Mixer Initializer** component to it.
3. Assign your `AxieFactory` catalog asset to its **Catalog** field.

`AxieMixerInitializer` runs early (`DefaultExecutionOrder(-10000)`) and assigns the catalog to
`AxieFactory.Default` in `Awake`, so any character created in `Awake`/`Start` afterwards can resolve it.
If the catalog is not assigned, [`AxieCharacter3D.FromGenes`](#axiecharacter3d) /
[`FromDescriptor`](#axiecharacter3d) log an error and return `null`.

## Usage

### Basic Character Creation

Characters are created through the static factory methods on `AxieCharacter3D` (the constructor is
internal — always use these):

```csharp
using SkyMavis.AxieMixer3D;

// Create from a gene string
var axie = AxieCharacter3D.FromGenes("0x...");

// Or create from a descriptor
var descriptor = AxieDescriptor.FromGenes("0x...");
var axie2 = AxieCharacter3D.FromDescriptor(descriptor);

// FromGenes / FromDescriptor return null if the catalog isn't initialized
// or the body can't be resolved — always null-check.
if (axie == null) return;

// Access the root GameObject
GameObject character = axie.Root;

// Clean up when done — this destroys the per-instance materials and the GameObject
axie.Dispose();
```

> **Ownership:** `AxieCharacter3D` implements `IDisposable`. You **must** call `Dispose()` when finished
> with a character. It destroys the per-instance materials the factory cloned for colorization plus the
> root GameObject. Shared catalog assets are never touched. (If you use
> [`AxieCharacter3DBehaviour`](#axiecharacter3dbehaviour), it disposes for you in `OnDestroy`.)

### Instantiation Parameters

[`AxieInstantiationParams`](Runtime/AxieInstantiationParams.cs) controls how a character is built at creation time.

| Field | Default | Description |
|-------|---------|-------------|
| `combineMeshes` | `true` | Merge the ~16 per-part `SkinnedMeshRenderer`s into two renderers (one for normal geometry, one for outline-excluded eyes/mouth). Cuts skinning dispatches and the Draw-Objects redraw from ~16 to 2. Disable to keep the original per-part hierarchy (e.g. for debugging). |
| `partLayerOverrides` | `[]` | Override the rendering layer of specific parts at creation time — useful for the [Draw Objects outline](#outline-rendering) approach or any selective-rendering effect. With `combineMeshes` on, layer control applies at the group level rather than per-part. |

```csharp
using System.Collections.Generic;
using SkyMavis.AxieMixer3D;

var instantiationParams = new AxieInstantiationParams
{
    combineMeshes = true,   // default — merge renderers to cut draw calls
    partLayerOverrides = new List<AxieInstantiationParams.PartLayerOverride>
    {
        new() { type = AxiePartType.Horn, layer = LayerMask.NameToLayer("Outline") },
        new() { type = AxiePartType.Back, layer = LayerMask.NameToLayer("Outline") },
        new() { type = AxiePartType.Tail, layer = LayerMask.NameToLayer("Outline") },
    },
};

var axie = AxieCharacter3D.FromGenes("0x...", instantiationParams);
```

**Available part types:** `Eye`, `Mouth`, `Ear`, `Horn`, `Back`, `Tail`.

### Working with Animations

This package ships the **`Default.*` locomotion set** (Idle, Walk, Run, Dead, Stun, and the
carry-item / get-hit / attack loco variants). The ~675 weapon/action clips (`AxeAttack`, `BowSkill`,
`SwordSkill`, `AttackCombo`, …) live in the **optional**
[`com.skymavis.axiemixer3d.weaponanims`](../com.skymavis.axiemixer3d.weaponanims) package; install
and register it to make those play (see [Weapon animations](#weapon-animations) below). The API is
identical either way — a registered weapon clip resolves through the same `Play` funnel.

Drive clips through [`AxiePlayable`](Runtime/AxiePlayable.cs) — a plain C# player built on Unity's
[Playables API](https://docs.unity3d.com/Manual/Playables.html) that handles one-shots, looping
defaults, queued sequences, and 1D locomotion blends. It plays the clips through a `PlayableGraph`
bound to a controller-less `Animator`, so animation works in **player builds** (unlike Unity's legacy
`Animation` component, which silently does nothing in a build). You don't author Mecanim controllers,
override controllers, or clone clips yourself.

> **Note:** `AxiePlayable` is the official animator. The package also contains
> [`AxieAnimator`](Runtime/AxieAnimator.cs), a legacy-`Animation` implementation kept **for reference
> only** — it does not animate in player builds. Always use `AxiePlayable` (`character.Playable`).

#### Getting an animator

The simplest path is the lazily-created `Playable` property on the character itself. It's created on
first access and disposed with the character (no manual cleanup):

```csharp
AxiePlayable playable = axie.Playable;
```

[`AxieCharacter3DBehaviour`](#axiecharacter3dbehaviour) forwards the same property to its character:

```csharp
AxiePlayable playable = axieBehaviour.Playable;
```

Or construct one directly for an `AxieCharacter3D`. You then own its lifecycle and **must** call
`Dispose()` before/with the character. Only one animator may own a character's `Root`, so don't
mix a hand-constructed `AxiePlayable` with the lazy `Playable` property on the same character:

```csharp
var playable = new AxiePlayable(axie) { Fade = 0.2f };
// ...
playable.Dispose();
```

#### Clip names

Clip names are **bare** constants on the generated [`AnimNames`](Runtime/AnimNames.cs) class — e.g.
`AnimNames.Idle`, `AnimNames.Walk`, `AnimNames.Run`, `AnimNames.Dead`, `AnimNames.Stun`. Weapon/action
clip names are constants on `WeaponAnimNames` in the [optional weapon package](#weapon-animations)
(e.g. `WeaponAnimNames.SwordAttack`, `WeaponAnimNames.AxeSkill`). Names resolve case-insensitively
(raw string literals work too; the constants just give you compile-time safety). To fetch the shared
source clip directly, use [`AxieCharacter3D.GetAnimClip(name)`](Runtime/AxieCharacter3D.cs) — but for
playback prefer the animator, which resolves and drives clips through its `PlayableGraph` internally.

> **Migration:** the old prefixed names (`Default.Idle`, `Action.SwordAttack`) and
> `GetLiteAnimationClip` are gone. Use the bare `AnimNames` constants (or `WeaponAnimNames` for weapon
> clips) with `GetAnimClip` / the `AxiePlayable` API. Weapon constants that used to live on `AnimNames`
> (`AnimNames.AxeAttack`, …) moved to `WeaponAnimNames` when the clips were split into the optional
> package — update those references.

#### Playing clips

```csharp
// Loop a clip as the base state
playable.Play(AnimNames.Idle, loop: true);

// Play a one-shot; when it finishes the animator returns to the default state (see below)
playable.Play(AnimNames.Stun, loop: false);

// Fire a callback on completion
playable.Play(AnimNames.Dead, loop: false, onComplete: () => Debug.Log("done"));
```

For full control pass an [`AnimPlayParams`](Runtime/AxiePlayable.cs):

```csharp
playable.Play(new AnimPlayParams
{
    ClipName        = AnimNames.Stun,
    Loop            = false,
    Fade            = 0.15f,   // crossfade seconds; < 0 inherits playable.Fade, 0 = snap
    TimeScale       = 1f,      // per-clip speed multiplier
    NormalizedStart = 0f,      // start offset (0-1); or set StartTime (seconds)
    OnComplete      = () => { },
});
```

`Play` returns an [`AnimTrack`](Runtime/AxiePlayable.cs) handle (or `null` if the clip doesn't
resolve on this body). Read `track.Progress` / `track.Duration` / `track.IsPlaying`, or call
`track.Complete()` to end it early.

#### Default (return-to) state

Set the clip the animator falls back to whenever a one-shot finishes:

```csharp
playable.SetDefault(AnimNames.Idle);   // one-shots return to looping Idle
```

#### Queued sequences

Chain clips off an `AnimTrack`; the animator returns to the default after the last one finishes:

```csharp
playable.Play(AnimNames.Stun, loop: false)
        ?.Queue(AnimNames.Dead, loop: false);
```

#### 1D locomotion blend

[`PlayBlend`](Runtime/AxiePlayable.cs) cross-weights clips along a single parameter — the classic
Idle→Walk→Run. Drive it every frame by setting [`AnimBlend.Speed`](Runtime/AxiePlayable.cs):

```csharp
AnimBlend loco = playable.PlayBlend(new (string, float)[]
{
    (AnimNames.Idle, 0f),
    (AnimNames.Walk, 1f),
    (AnimNames.Run,  3.5f),
});

void Update() => loco.Speed = currentMoveSpeed;   // 0 → Idle, cross-blends up to Run
```

- Only the two points straddling `Speed` carry weight (piecewise-linear); below the lowest / above
  the highest threshold clamps to that endpoint clip.
- Clips are **phase-locked** (time-scale synced), so feet don't slide while cross-fading, and the
  gait naturally cycles faster as `Speed` rises.
- Points may be listed in any order (sorted by threshold internally); missing clips are skipped with
  a warning.
- Starting any single-clip `Play`, or calling `loco.Stop()`, tears the blend down.

Use [`SetDefaultBlend`](Runtime/AxiePlayable.cs) to make the blend the **return-to** state, so
one-shots (cast, attack) blend back into locomotion when they finish. The returned handle stays
valid across those round-trips:

```csharp
AnimBlend loco = playable.SetDefaultBlend(new (string, float)[]
{
    (AnimNames.Idle, 0f),
    (AnimNames.Walk, 1f),
    (AnimNames.Run,  3.5f),
});

loco.Speed = moveSpeed;                             // every frame
playable.Play(AnimNames.Stun, loop: false);        // auto-returns to the blend, crossfaded over Fade
```

You can also drive the active/default blend without holding the handle via `playable.SetSpeed(v)`
(or the `playable.Speed` property) — handy when the blend is set as the default and you just want to
feed it a speed each frame.

#### Playback control

```csharp
playable.TimeScale = 1.5f;            // global speed multiplier (applied live)
playable.Fade      = 0.2f;            // default crossfade seconds for Play / return-to
playable.Pause();                     // freeze; Resume() to continue
playable.Interrupt();                 // stop current, then play queued-next or the default
playable.Stop();                      // stop everything (no return-to)
playable.Completed += name => { };    // fires when a one-shot finishes
```

#### Registering custom clips

Feed your own `AnimationClip` through the same `Play` / `PlayBlend` / `SetDefault` APIs — a
registered clip **shadows** a baked body clip of the same name:

```csharp
playable.Register("MyCast", myCastClip);
playable.Play("MyCast", loop: false);
```

`Unregister(name)` / `IsRegistered(name)` manage the set; registered clips are cleared on `Dispose`.

See [`AxieMixer3DExample.cs`](../../Assets/Examples/AxieMixer3DExample/AxieMixer3DExample.cs) in this
repository for an end-to-end demo: a default Idle→Walk→Run blend driven by a slider, one-shot
playback, and a queued sequence.

#### Weapon animations

This package intentionally ships **only** the `Default.*` locomotion clips. The weapon/action clips
(`SwordAttack`, `AxeSkill`, `BowRun`, `AttackCombo`, …) are ~190 MB and live in the separate, optional
[`com.skymavis.axiemixer3d.weaponanims`](../com.skymavis.axiemixer3d.weaponanims) package so a consumer
that only needs locomotion doesn't pay for them.

To enable weapon clips: install that package, then **register its catalog** — either drop an
**Axie Weapon Anim Initializer** component on your bootstrap GameObject (assign the catalog asset), or
call it in code after `AxieFactory.Default` is set:

```csharp
using SkyMavis.AxieMixer3D.WeaponAnims;

AxieWeaponAnims.Register(myWeaponCatalog);              // once, at bootstrap
character.Playable.Play(WeaponAnimNames.SwordAttack);  // then plays on every character of that body
```

Registration is factory-level (keyed by body type), so it applies to every character built from
`AxieFactory.Default` — no per-instance wiring. Playing a weapon clip **before** the catalog is
registered returns `null` and logs the usual "clip not found on this body" warning (graceful no-op).
See the weapon package's [README](../com.skymavis.axiemixer3d.weaponanims/README.md) for details.

### Weapon Attachment Points

```csharp
// Weapon attachment transforms (null if the body has no weapon bones)
Transform rightHand = axie.RightWeaponAttachPoint;
Transform leftHand  = axie.LeftWeaponAttachPoint;

var sword  = Instantiate(swordPrefab, rightHand);
var shield = Instantiate(shieldPrefab, leftHand);
```

## Visual Effects

### Outline Rendering

The package provides two approaches for outlining Axie characters, optimized for different rendering paths.

#### Choosing the Right Approach

| Feature | Draw Objects | Post-Process |
|---------|-------------|--------------|
| **Best Rendering Path** | Forward Rendering | Deferred Rendering |
| **Setup Complexity** | Simple | Moderate |
| **Additional Overhead** | Minimal | Extra passes in Forward Rendering |
| **Visual Quality** | Simple, clean outline | Sophisticated edge detection |
| **Data Requirements** | None | Depth & Normal buffers |
| **Use Case** | Per-object selection highlights | Screen-wide stylized effects |
| **Crowd scaling** | O(renderers × Axies) — cost grows with count | O(1) fixed screen-space cost — preferred for crowds |

**Choosing for crowds:** Draw-Objects cost scales with the number of renderers × the number of Axies on screen. Post-process is a fixed pair of fullscreen passes regardless of how many Axies are visible — prefer it whenever many characters appear simultaneously. Set **Default Outline Mode** to `PostProcess` on [`AxieMixerInitializer`](#axiemixerinitializer) and add the `OutlinePostProcessRendererFeature` to your URP Renderer asset; no per-character layer work is needed.

#### Approach A: Draw Objects Renderer Feature

**✅ Recommended for Forward Rendering**

This approach re-draws the character with an inflating outline material via URP's built-in
**Render Objects** feature, filtered to a dedicated layer.

**Setup:**

1. **Configure a rendering layer (one-time):**
   - Add an `Outline` layer in **Project Settings → Tags and Layers** (or reuse an existing layer).

2. **Add the Render Objects feature:**
   - Open your **URP Renderer** asset → **Add Renderer Feature → Render Objects**.
   - Configure:
     - **Name:** `Axie Outline`
     - **Event:** `AfterRenderingTransparents`
     - **Layer Mask:** your outline layer
     - **Override Material:** [`Outline_RenderObjects`](AxieMixerAssets/Materials/Outline/Outline_RenderObjects.mat)

3. **Move characters onto the outline layer.** Three options:

   **Option 1 — project-wide default via the initializer.** On your
   [`AxieMixerInitializer`](#axiemixerinitializer), set **Default Outline Mode** to `DrawObjects` and pick
   the outline / base layers. Every character built afterwards is outlined automatically, with eyes and
   mouth kept un-outlined to match the reference art.

   **Option 2 — per-character at runtime:**

   ```csharp
   // Outline this character; eyes/mouth stay on baseLayer (default 0) automatically.
   axie.SetOutlineLayer(LayerMask.NameToLayer("Outline"));

   // Remove the outline again (put everything back on the base layer):
   axie.SetOutlineLayer(0);
   ```

   **Option 3 — selected parts at instantiation** via
   [`partLayerOverrides`](#instantiation-parameters).

**Customization:** adjust `_Thickness` on the
[`Outline_RenderObjects`](AxieMixerAssets/Materials/Outline/Outline_RenderObjects.mat) material (`0.02`
default; increase for thicker outlines).

#### Approach B: Screen-based Post-Process Renderer Feature

**✅ Recommended for Deferred Rendering**

Screen-space edge detection from depth and normal discontinuities, applied across the whole screen.

**⚠️ Caution for Forward Rendering:** Unity must generate depth/normal data through additional draw calls,
adding overhead. Prefer Draw Objects for Forward unless you specifically need screen-space edge detection.

**Setup:**

1. Open your **URP Renderer** asset → **Add Renderer Feature → Outline Post Process Renderer Feature**.
2. Configure:

```
Outline Appearance:
  - Outline Color: Black (or your preferred color)
  - Thickness: 1-10 (higher = thicker outlines)

Depth Settings:
  - Depth Scale: 50.0 (sensitivity to depth edges)
  - Depth Bias: 50.0 (threshold for edge detection)

Normal Settings:
  - Normal Scale: 0.7 (sensitivity to surface angle changes)
  - Normal Bias: 10.0 (threshold for normal discontinuities)

Render Settings:
  - Render Pass Event: After Rendering Post Processing
```

No per-character code is required — the effect applies screen-wide.

**Performance comparison:**

| Rendering Path | Draw Objects | Post-Process |
|---------------|-------------|--------------|
| **Forward** | ✅ Minimal overhead | ⚠️ Additional depth/normal passes |
| **Deferred** | ✅ Minimal overhead | ✅ Reuses existing buffers |

## Components

### `AxieCharacter3D`

The core class representing a fully assembled 3D Axie character. Implements `IDisposable`.

#### Properties

| Property | Description | Type |
| -------- | ----------- | ---- |
| [`InstantiationParams`](Runtime/AxieCharacter3D.cs) | Parameters used to create this character | `AxieInstantiationParams` |
| [`Root`](Runtime/AxieCharacter3D.cs) | The generated character GameObject | `GameObject` |
| [`RightWeaponAttachPoint`](Runtime/AxieCharacter3D.cs) | Transform for right-hand weapon attachments | `Transform` |
| [`LeftWeaponAttachPoint`](Runtime/AxieCharacter3D.cs) | Transform for left-hand weapon attachments | `Transform` |
| [`Descriptor`](Runtime/AxieCharacter3D.cs) | The descriptor this character was assembled from (read-only; change in place with `ApplyDescriptor`) | `AxieDescriptor` |
| [`Playable`](Runtime/AxieCharacter3D.cs) | The character's [`AxiePlayable`](#working-with-animations) animator, created on first access and disposed with the character | `AxiePlayable` |

#### Methods

| Method | Description | Returns |
| ------ | ----------- | ------- |
| [`GetAnimClip(string)`](Runtime/AxieCharacter3D.cs) | Animation clip by name, matched case-insensitively (null if unknown). Resolves the body's `Default.*` clips first, then falls back to any weapon clips registered on the factory (see [Weapon animations](#weapon-animations)). For playback prefer [`AxiePlayable`](#working-with-animations). | `AnimationClip` |
| [`ApplyDescriptor(AxieDescriptor)`](Runtime/AxieCharacter3D.cs) | Rebuild the character in place from a new descriptor — same handle and scene transform, `Root` re-instantiated. Re-read `Root`/attach points/`Playable` afterwards. | `void` |
| [`ApplyGenes(string)`](Runtime/AxieCharacter3D.cs) | Convenience `ApplyDescriptor` that decodes a gene string first | `void` |
| [`SetOutlineLayer(int, int)`](Runtime/AxieCharacter3D.cs) | Move the character onto an outline layer (eyes/mouth excluded) | `void` |
| [`Dispose()`](Runtime/AxieCharacter3D.cs) | Dispose the animator, then destroy per-instance materials and the character GameObject | `void` |

#### Static Members

| Member | Description | Type |
| ------ | ----------- | ---- |
| `FromGenes(string, AxieInstantiationParams)` | Create a character from a gene string | `AxieCharacter3D` |
| `FromDescriptor(AxieDescriptor, AxieInstantiationParams)` | Create a character from a descriptor | `AxieCharacter3D` |
| `DefaultOutlineLayer` | Project-wide outline layer applied on creation (`-1` disables) | `int` |
| `DefaultOutlineBaseLayer` | Layer for un-outlined parts (eyes/mouth) | `int` |
| `OutlineExcludedPartTypes` | Part types deliberately left un-outlined (`Eye`, `Mouth`) | `IReadOnlyList<AxiePartType>` |

```csharp
var axie = AxieCharacter3D.FromGenes("0x...");
var axie2 = AxieCharacter3D.FromDescriptor(descriptor, instantiationParams);
```

### `AxieCharacter3DBehaviour`

A [`MonoBehaviour`](https://docs.unity3d.com/ScriptReference/MonoBehaviour.html) wrapper that owns an
[`AxieCharacter3D`](Runtime/AxieCharacter3D.cs) lifecycle — it builds on `Start`, disposes on
`OnDestroy`, and forwards the character's [`Playable`](#working-with-animations). `Rebuild()` swaps
the character in place (via `ApplyDescriptor`) when one already exists.

#### Inspector Fields

| Field | Description | Type |
| ----- | ----------- | ---- |
| [`axieGenes`](Runtime/AxieCharacter3DBehaviour.cs) | Gene string (takes precedence when non-empty) | `string` |
| [`axieDescriptor`](Runtime/AxieCharacter3DBehaviour.cs) | Descriptor used when `axieGenes` is empty | `AxieDescriptor` |

#### Runtime Properties

| Property | Description | Type |
| -------- | ----------- | ---- |
| [`Character`](Runtime/AxieCharacter3DBehaviour.cs) | The underlying `AxieCharacter3D` instance | `AxieCharacter3D` |
| [`Playable`](Runtime/AxieCharacter3DBehaviour.cs) | Forwards `Character.Playable` (null before the character is built) | `AxiePlayable` |

```csharp
// Rebuild after changing axieGenes or axieDescriptor
axieBehaviour.Rebuild();
```

### `AxieMixerInitializer`

Bootstrap component that assigns an `AxieFactory` catalog to `AxieFactory.Default` (see [Setup](#setup)).

#### Inspector Fields

| Field | Description |
| ----- | ----------- |
| **Catalog** | The `AxieFactory` asset to register as `AxieFactory.Default`. |
| **Persist Across Scenes** | Keep this object alive via `DontDestroyOnLoad` (only works on a root GameObject). |
| **Default Outline Mode** | `None`, `DrawObjects` (per-character layer, scales with Axie count), or `PostProcess` (fixed screen-space cost — no per-character layer work; add `OutlinePostProcessRendererFeature` to the URP Renderer). |
| **Outline Layer** | Layer the URP Render Objects outline feature filters on (Draw Objects mode). |
| **Base Outline Layer** | Layer for un-outlined parts (eyes/mouth); usually `Default` (0). |

## Axie Avatars

Axie avatars are rendered 2D representations of 3D Axie characters, useful for UI portraits, inventory
icons, or profile pictures. Rendering is an optional addon — [`AxieAvatarRenderer`](Runtime/AxieAvatarRenderer.cs)
— kept separate from the character. It uses an orthographic `CommandBuffer` pass with customizable
camera angle, model orientation, and output resolution.

### Rendering an Avatar

Create an `AxieAvatarRenderer` for a character, `Render` as many frames as you need, then dispose it
(or the whole character). `AxieAvatarRenderer` implements `IDisposable`:

```csharp
var axie = AxieCharacter3D.FromGenes("0x...");
if (axie == null) return;

// The texture is resized to match renderParams.width/height on the first render,
// so any starting size is fine.
var avatarTexture = new RenderTexture(256, 256, 16, RenderTextureFormat.ARGB32);

using (var avatarRenderer = new AxieAvatarRenderer(axie))
{
    avatarRenderer.Render(avatarTexture, new AxieAvatarRenderParams
    {
        width = 256,
        height = 256,
        modelHeading = 180f,                       // Model facing direction (0-360°)
        viewCenter = new Vector3(0f, 0.75f, 0f),   // Camera focal point
        viewDirection = new Vector3(-1f, -1f, -3f) // Camera viewing direction
    });
}

someImage.texture = avatarTexture;

// Clean up when done
avatarTexture.Release(); // Manual texture cleanup required
axie.Dispose();
```

For a **realtime** avatar (e.g. a spinning portrait that plays back animation), keep the renderer
alive and call `Render` every frame — it caches its draw list and reuses one `CommandBuffer`, so
per-frame calls are cheap. The renderer stays correct across an in-place
[`ApplyDescriptor`](#axiecharacter3d): it rebuilds its cache when the character's `Root` changes.

### Avatar Render Parameters

[`AxieAvatarRenderParams`](Runtime/AxieAvatarRenderParams.cs) controls avatar rendering:

| Parameter | Description | Default |
| --------- | ----------- | ------- |
| [`width`](Runtime/AxieAvatarRenderParams.cs) | Output texture width in pixels (min 1) | 128 |
| [`height`](Runtime/AxieAvatarRenderParams.cs) | Output texture height in pixels (min 1) | 128 |
| [`modelHeading`](Runtime/AxieAvatarRenderParams.cs) | Model heading in world-space degrees (0-360) | 180° |
| [`viewCenter`](Runtime/AxieAvatarRenderParams.cs) | Orthographic camera focal point in model space | (0, 0.75, 0) |
| [`viewDirection`](Runtime/AxieAvatarRenderParams.cs) | Camera viewing direction in model space | (-1, -1, -1) |

### Important Notes

- **Play Mode timing:** after entering Play Mode, skip the first ~3 frames before rendering avatars so the graphics device is fully initialized.
- **Memory management:** dispose the `AxieAvatarRenderer` (or the character) when done, and release any [`RenderTexture`](https://docs.unity3d.com/ScriptReference/RenderTexture.html) you created.
- **Performance:** the renderer caches its draw list and reuses a single `CommandBuffer` across frames, so it is cheap to call every frame for realtime avatars.

## Editor Tools

Editor utilities under **Tools → Axie Mixer 3D**.

### Axie Genes Decoder

Fetch and decode Axie genetic data for testing and development.

**Access:** **Tools → Axie Mixer 3D → Axie Genes Decoder**

**Fetch by Axie ID:** enter an Axie ID and click **Fetch** to query the Axie Infinity GraphQL gateway
(`https://graphql-gateway.axieinfinity.com/graphql`) and auto-decode the result into an inspectable
[`AxieDescriptor`](Runtime/AxieDescriptor.cs).

**Manual decode:** paste a gene string (e.g. `0x...`) and click **Decode**.

The decoded descriptor shows the body type, color variant, and all 6 parts (class, variant, skin, level).

**Code equivalent:**

```csharp
var descriptor = AxieDescriptor.FromGenes("0x...");
var axie = AxieCharacter3D.FromDescriptor(descriptor);
// or, directly:
var axie2 = AxieCharacter3D.FromGenes("0x...");
```

## Sample Projects

Import samples through the Package Manager to explore practical implementations:

- **Axie Collection** — builds a grid of characters across body/part types from
  [`AxieDescriptor`](Runtime/AxieDescriptor.cs) data, and disposes them on teardown.
- **Axie Animations** — interactive [`AxiePlayable`](#working-with-animations) examples: a
  slider-driven Idle→Walk→Run default blend, one-shot playback, queued sequences, and weapon
  attachment (see [`AxieMixer3DExample.cs`](../../Assets/Examples/AxieMixer3DExample/AxieMixer3DExample.cs)).
- **Axie Avatars** — static and realtime avatar texture generation for UI via [`AxieAvatarRenderer`](Runtime/AxieAvatarRenderer.cs).

### Importing Samples

1. Open the **Package Manager** (`Window → Package Manager`).
2. Select **In Project** and find **Axie Mixer 3D**.
3. Expand **Samples** and click **Import** next to the desired sample.
4. Samples are imported to `Assets/Samples/Axie Mixer 3D/[Version]/[Sample Name]/`.

> **Note:** the samples require a catalog registered via an [`AxieMixerInitializer`](#axiemixerinitializer)
> in the scene (see [Setup](#setup)).
