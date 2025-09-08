# Unity Axie Mixer 3D

This package provides the core functionality for assembling 3D Axie characters by mixing modular body parts.

## Table of Contents

- [Installation](#installation)
- [Usage](#usage)
  - [Basic Character Creation](#basic-character-creation)
  - [Working with Animations](#working-with-animations)
  - [Weapon Attachment Points](#weapon-attachment-points)
- [Components](#components)
  - [AxieCharacter3D](#axiecharacter3d)
  - [AxieCharacter3DBehaviour](#axiecharacter3dbehaviour)
- [Axie Avatars](#axie-avatars)
  - [Manual Avatar Rendering](#manual-avatar-rendering)
  - [Automatic Avatar Rendering](#automatic-avatar-rendering)
  - [Avatar Render Parameters](#avatar-render-parameters)
  - [Editor Tools](#editor-tools)
  - [Important Notes](#important-notes)
- [Sample Projects](#sample-projects)

## Installation

Install via Git URL:

```txt
https://github.com/axieinfinity/unity-axie-mixer3d.git
```

In Unity, go to **Window → Package Manager → Add package from Git URL...**, then paste the URL above.

## Usage

The Unity Axie Mixer 3D system allows you to create fully rigged 3D Axie characters from genetic data and work with their animations and visual components.

### Basic Character Creation

```csharp
using SkyMavis.AxieMixer3D;

// Create from gene string
var axie = new AxieCharacter3D(AxieDescriptor.FromGenes("0x123"));

// Or create from descriptor
var descriptor = new AxieDescriptor(/* ... */);
var axie2 = new AxieCharacter3D(descriptor);

// Access the root GameObject
GameObject character = axie.Root;

// Clean up when done
axie.Dispose();
```

### Working with Animations

The system provides two types of animation clips: lite animations (body only) and full animations (body + parts). You can play animations using Unity's modern [`Animator`](https://docs.unity3d.com/ScriptReference/Animator.html) system or the legacy [`Animation`](https://docs.unity3d.com/ScriptReference/Animation.html) component.

#### Getting Animation Clips

```csharp
// Get available animation names
var animationNames = axie.AnimationNames;

// Get lite animations (body only) - lighter performance
AnimationClip idleClip = axie.GetLiteAnimationClip("Default.Idle");
AnimationClip walkClip = axie.GetLiteAnimationClip("Default.Walk");

// Get full animations (body + parts) - includes part animations
AnimationClip runClip = axie.GetFullAnimationClip("Default.Run");
AnimationClip jumpClip = axie.GetFullAnimationClip("Default.Jump");
```

#### Using Animator with Override Controller

```csharp
// Setup Animator with AnimatorOverrideController
var animator = axie.Root.GetComponent<Animator>();
var baseController = animator.runtimeAnimatorController;
var overrideController = new AnimatorOverrideController(baseController);

// Override specific animations
overrideController["Idle"] = axie.GetLiteAnimationClip("Default.Idle");
overrideController["Walk"] = axie.GetFullAnimationClip("Default.Walk");
overrideController["Run"] = axie.GetFullAnimationClip("Default.Run");

// Apply the override controller
animator.runtimeAnimatorController = overrideController;

// Play animations
animator.SetTrigger("StartWalking");
animator.SetBool("IsRunning", true);
```

#### Using Legacy Animation Component

For projects using the legacy animation system, clips must be converted to legacy format:

```csharp
// Get or add Animation component
var animation = axie.Root.GetComponent<Animation>();
if (animation == null)
{
    animation = axie.Root.AddComponent<Animation>();
}

// Convert clips to legacy format
AnimationClip idleClip = axie.GetLiteAnimationClip("Default.Idle");
AnimationClip legacyIdleClip = Object.Instantiate(idleClip);
legacyIdleClip.legacy = true;

AnimationClip walkClip = axie.GetFullAnimationClip("Default.Walk");
AnimationClip legacyWalkClip = Object.Instantiate(walkClip);
legacyWalkClip.legacy = true;

// Add clips to Animation component
animation.AddClip(legacyIdleClip, "Idle");
animation.AddClip(legacyWalkClip, "Walk");

// Set default animation
animation.clip = legacyIdleClip;

// Play animations
animation.Play("Idle");
animation.CrossFade("Walk", 0.3f);
```

#### Animation Loop Configuration

```csharp
// Configure looping for continuous animations
AnimationClip walkClip = axie.GetFullAnimationClip("Default.Walk");
AnimationClip loopingWalkClip = Object.Instantiate(walkClip);
loopingWalkClip.wrapMode = WrapMode.Loop;

// For one-shot animations like attacks
AnimationClip attackClip = axie.GetFullAnimationClip("Default.Attack");
AnimationClip oneShotAttackClip = Object.Instantiate(attackClip);
oneShotAttackClip.wrapMode = WrapMode.Once;

// For ping-pong effects
AnimationClip breatheClip = axie.GetLiteAnimationClip("Default.Breathe");
AnimationClip pingPongBreatheClip = Object.Instantiate(breatheClip);
pingPongBreatheClip.wrapMode = WrapMode.PingPong;

// Apply to Animation component (legacy)
if (loopingWalkClip.legacy)
{
    animation.AddClip(loopingWalkClip, "Walk");
}
```

#### Animation Events and Callbacks

```csharp
// Add animation events for gameplay integration
AnimationClip attackClip = axie.GetFullAnimationClip("Default.Attack");
AnimationClip customAttackClip = Object.Instantiate(attackClip);

// Add event at specific time (e.g., when sword should hit)
AnimationEvent hitEvent = new AnimationEvent();
hitEvent.time = 0.5f; // 50% through animation
hitEvent.functionName = "OnAttackHit";
hitEvent.stringParameter = "sword";

customAttackClip.AddEvent(hitEvent);
```

### Weapon Attachment Points

```csharp
// Get weapon attachment transforms
Transform rightHand = axie.RightWeaponAttachPoint;
Transform leftHand = axie.LeftWeaponAttachPoint;

// Attach weapon models
var sword = Instantiate(swordPrefab, rightHand);
var shield = Instantiate(shieldPrefab, leftHand);
```

## Components

### `AxieCharacter3D`

The core class that represents a fully assembled 3D Axie character. It provides access to the character's GameObject, animations, and attachment points for equipment.

#### Properties

| Property                                                  | Description                                       | Type                         |
| --------------------------------------------------------- | ------------------------------------------------- | ---------------------------- |
| [`Root`](Runtime/AxieCharacter3D.cs:11)                   | The generated character GameObject                | `GameObject`                 |
| [`RightWeaponAttachPoint`](Runtime/AxieCharacter3D.cs:14) | Transform for right-hand weapon attachments       | `Transform`                  |
| [`LeftWeaponAttachPoint`](Runtime/AxieCharacter3D.cs:15)  | Transform for left-hand weapon attachments        | `Transform`                  |
| [`AnimationNames`](Runtime/AxieCharacter3D.cs:16)         | Read-only collection of available animation names | `ReadOnlyCollection<string>` |

#### Methods

| Method                                                                                 | Description                                   | Returns         |
| -------------------------------------------------------------------------------------- | --------------------------------------------- | --------------- |
| [`GetLiteAnimationClip(string)`](Runtime/AxieCharacter3D.cs:44)                        | Get body-only animation clip                  | `AnimationClip` |
| [`GetFullAnimationClip(string)`](Runtime/AxieCharacter3D.cs:47)                        | Get full animation clip (body + parts)        | `AnimationClip` |
| [`RenderAvatar(RenderTexture, AxieAvatarRenderParams)`](Runtime/AxieCharacter3D.cs:60) | Render character avatar to texture            | `void`          |
| [`Dispose()`](Runtime/AxieCharacter3D.cs:38)                                           | Clean up and destroy the character GameObject | `void`          |

#### Factory Methods

```csharp
// Create from descriptor
var axie = AxieCharacter3D.FromDescriptor(descriptor);

// Create from gene string
var axie = AxieCharacter3D.FromGenes("0x123");
```

### `AxieCharacter3DBehaviour`

A [`MonoBehaviour`](https://docs.unity3d.com/ScriptReference/MonoBehaviour.html) wrapper that encapsulates an [`AxieCharacter3D`](Runtime/AxieCharacter3D.cs:9) instance. Use this component when you prefer Unity's component system or need scene-based character management.

#### Inspector Fields

| Field                                                          | Description                               | Type                       |
| -------------------------------------------------------------- | ----------------------------------------- | -------------------------- |
| [`axieGenes`](Runtime/AxieCharacter3DBehaviour.cs:8)           | Gene string for character generation      | `string`                   |
| [`axieDescriptor`](Runtime/AxieCharacter3DBehaviour.cs:9)      | Alternative to gene string                | `AxieDescriptor`           |
| [`avatarRenderParams`](Runtime/AxieCharacter3DBehaviour.cs:10) | Parameters for automatic avatar rendering | `AxieAvatarRenderParams[]` |

#### Runtime Properties

| Property                                              | Description                               | Type              |
| ----------------------------------------------------- | ----------------------------------------- | ----------------- |
| [`Character`](Runtime/AxieCharacter3DBehaviour.cs:12) | The underlying `AxieCharacter3D` instance | `AxieCharacter3D` |
| [`Avatars`](Runtime/AxieCharacter3DBehaviour.cs:13)   | Pre-rendered avatar textures              | `RenderTexture[]` |

#### Methods

```csharp
// Rebuild character (useful when changing genes or descriptor)
axieBehaviour.Rebuild();
```

The component automatically handles character lifecycle, creates avatars on `Start()`, and cleans up resources on `OnDestroy()`.

## Axie Avatars

Axie avatars are rendered 2D representations of 3D Axie characters, useful for UI elements like character portraits, inventory icons, or profile pictures. The system provides flexible avatar rendering with customizable camera angles, model orientations, and output resolutions.

### Manual Avatar Rendering

You can render avatars manually using [`AxieCharacter3D.RenderAvatar()`](Runtime/AxieCharacter3D.cs:60):

```csharp
var axie = new AxieCharacter3D(AxieDescriptor.FromGenes("0x123"));

// Create render texture (size will be automatically adjusted)
var avatarTexture = new RenderTexture(256, 256, 16, RenderTextureFormat.ARGB32);

// Render avatar with custom parameters
axie.RenderAvatar(avatarTexture, new AxieAvatarRenderParams
{
    width = 256,
    height = 256,
    modelHeading = 180f,                       // Model facing direction (0-360°)
    viewCenter = new Vector3(0f, 0.75f, 0f),   // Camera focal point
    viewDirection = new Vector3(-1f, -1f, -3f) // Camera viewing direction
});

// Use the rendered texture in your UI
someImage.texture = avatarTexture;

// Clean up when done
avatarTexture.Release(); // Manual texture cleanup required
axie.Dispose();
```

### Automatic Avatar Rendering

[`AxieCharacter3DBehaviour`](Runtime/AxieCharacter3DBehaviour.cs:6) automatically renders multiple avatars when the GameObject becomes active:

```csharp
public class MyScript : MonoBehaviour
{
    public AxieCharacter3DBehaviour axieBehaviour;

    void Start()
    {
        // Avatars are automatically rendered using axieBehaviour.avatarRenderParams
        RenderTexture[] avatars = axieBehaviour.Avatars;

        // Use rendered avatars in your UI
        for (int i = 0; i < avatars.Length; i++)
        {
            uiImages[i].texture = avatars[i];
        }
    }
}
```

Configure avatar rendering by setting the [`avatarRenderParams`](Runtime/AxieCharacter3DBehaviour.cs:10) array in the inspector or via code:

```csharp
axieBehaviour.avatarRenderParams = new AxieAvatarRenderParams[]
{
    // Front-facing view
    new AxieAvatarRenderParams {
        width = 128, height = 128,
        viewDirection = new Vector3(0f, 0f, -1f)
    },
    // Side view from the right
    new AxieAvatarRenderParams {
        width = 128, height = 128,
        viewDirection = new Vector3(-1f, 0f, 0f)
    },
    // Three-quarter view
    new AxieAvatarRenderParams {
        width = 128, height = 128,
        viewDirection = new Vector3(-0.7f, 0f, -0.7f)
    }
};
```

### Avatar Render Parameters

[`AxieAvatarRenderParams`](Runtime/AxieAvatarRenderParams.cs:10) provides fine-grained control over avatar rendering:

| Parameter                                               | Description                                                  | Default      |
| ------------------------------------------------------- | ------------------------------------------------------------ | ------------ |
| [`width`](Runtime/AxieAvatarRenderParams.cs:16)         | Output texture width in pixels                               | 128          |
| [`height`](Runtime/AxieAvatarRenderParams.cs:22)        | Output texture height in pixels                              | 128          |
| [`modelHeading`](Runtime/AxieAvatarRenderParams.cs:29)  | Model heading in world-space degrees - affects lighting only | 180°         |
| [`viewCenter`](Runtime/AxieAvatarRenderParams.cs:34)    | Orthographic camera focal point in model space               | (0, 0.75, 0) |
| [`viewDirection`](Runtime/AxieAvatarRenderParams.cs:39) | Camera viewing direction in model space                      | (-1, -1, -1) |

### Editor Tools

Use the **Axie Avatar Preview** tool to experiment with rendering parameters:

1. Open **Tools → Axie Mixer 3D → Axie Avatar Preview**
2. Select a GameObject with an [`AxieCharacter3DBehaviour`](Runtime/AxieCharacter3DBehaviour.cs:6) component
3. Adjust render parameters in real-time
4. Preview both live renders and cached avatars

### Important Notes

- **Play Mode Rendering**: When entering Play Mode, skip the first 3 frames before rendering avatars to ensure the graphics device is fully initialized.
- **Memory Management**: Avatar textures are automatically cleaned up when using [`AxieCharacter3DBehaviour`](Runtime/AxieCharacter3DBehaviour.cs:6), but manual [`RenderTexture`](https://docs.unity3d.com/ScriptReference/RenderTexture.html) instances must be released manually.
- **Performance**: Avatar rendering uses Unity's CommandBuffer system for efficient GPU-based rendering.

## Sample Projects

The package includes sample projects that demonstrate different aspects of the Unity Axie Mixer 3D system. Import samples through the Package Manager to explore practical implementations.

- **Axie Collection**: Demonstrates how different body types and part types are combined to create diverse Axie characters using [`AxieDescriptor`](Runtime/AxieDescriptor.cs) data structures
- **Axie Animations**: Interactive examples showcasing both modern [`Animator`](https://docs.unity3d.com/ScriptReference/Animator.html) and legacy [`Animation`](https://docs.unity3d.com/ScriptReference/Animation.html) systems with weapon attachment, animation switching, and lite vs. full animation comparisons
- **Axie Avatars**: Shows how to use avatar rendering for both pre-rendered and real-time texture generation in UI applications

### Importing Samples

1. Open the **Package Manager** (`Window → Package Manager`)
2. Select **In Project** and find **Axie Mixer 3D**
3. Expand **Samples** to see available sample projects
4. Click **Import** next to the desired sample
5. Samples will be imported to `Assets/Samples/Axie Mixer 3D/[Version]/[Sample Name]/`
