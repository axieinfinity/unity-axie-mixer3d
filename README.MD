# Unity Axie Mixer 3D

This package provides the core functionality for assembling 3D Axie characters by mixing modular body parts.

## Installation

Install via Git URL:

```txt
https://github.com/axieinfinity/unity-axie-mixer3d.git
```

In Unity, go to **Window → Package Manager → Add package from Git URL...**, then paste the URL above.

## Usage

```csharp
using SkyMavis.AxieMixer3D;

// Create a new 3D Axie character from a gene string
var axie = new AxieCharacter3D(AxieDescriptor.FromGenes("0x123"));

// Access the root GameObject of the generated character
axie.Root;

// Get visual attach points for right/left-hand weapon visuals
axie.RightWeaponAttachPoint;
axie.LeftWeaponAttachPoint;

// Access the set of avatars
axie.Avatars;

// Access the list of animation names
axie.AnimationNames;

// Get body AnimationClip with a specified name
axie.GetLiteAnimationClip("Default.Idle");

// Get body and body parts AnimationClip with a specified name
axie.GetFullAnimationClip("Default.Run");

// Clean up and destroy the generated GameObject when no longer needed
axie.Dispose();
```

## Components

### `AxieCharacter3D`

Represents a fully assembled 3D Axie character. It contains:

* `Root` – the generated GameObject.
* `RightWeaponAttachPoint` / `LeftWeaponAttachPoint` – transforms for visual attachments.
* `Avatars` – set of avatars from various directions.
* `AnimationNames` – list of animation names.
* `GetLiteAnimationClip(string)` – get AnimationClip that animates only body.
* `GetFullAnimationClip(string)` – get AnimationClip that animates both body and body parts.
* `Dispose()` – destroys the root GameObject and cleans up resources.

### `AxieCharacter3DBehaviour`

A `MonoBehaviour` wrapper that encapsulates an `AxieCharacter3D` instance. Use it if you prefer working with Unity’s component system. It provides Unity-friendly lifecycle handling and integration with scenes or prefabs.

## Notes

* Animation clips are auto-collected and mapped to their appropriate skeleton roots.
* The visual attach points are presentation-only and do not necessarily follow hand bones.
