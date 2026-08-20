# Axie Mixer 3D

A Unity Package Manager (UPM) library for assembling fully-rigged 3D Axie characters at runtime by mixing
modular body parts from genetic data. Target: Unity **6000.0** with the **Universal Render Pipeline (URP)**.

This repository is a Unity **development project** that embeds two packages. Open the repo root in the
Unity Editor to develop/test them; consumers install them via Git URL (below).

| Package | Path | What it is |
| ------- | ---- | ---------- |
| **`com.skymavis.axiemixer3d`** | [`Packages/com.skymavis.axiemixer3d`](Packages/com.skymavis.axiemixer3d) | Core package. Assembles characters and ships the `Default.*` locomotion clips (Idle, Walk, Run, Dead, …). |
| **`com.skymavis.axiemixer3d.weaponanims`** | [`Packages/com.skymavis.axiemixer3d.weaponanims`](Packages/com.skymavis.axiemixer3d.weaponanims) | **Optional** add-on. The ~675 weapon/action clips (`AxeAttack`, `BowSkill`, …), split out to keep the base install small. Install it and register its catalog to enable weapon actions. |

## Installation

In Unity, go to **Window → Package Manager → Add package from Git URL...**, then paste the core package:

```txt
https://github.com/axieinfinity/unity-axie-mixer3d.git?path=/Packages/com.skymavis.axiemixer3d
```

For weapon/action animations, also add the optional package:

```txt
https://github.com/axieinfinity/unity-axie-mixer3d.git?path=/Packages/com.skymavis.axiemixer3d.weaponanims
```

The `?path=` query is required because the packages live in subfolders of the repository. To pin a tag or
branch, append `#<tag-or-branch>`. See the package READMEs for `manifest.json` usage and version pinning.

## Documentation

Full usage, API reference, outline setup, avatars, editor tools, and samples:

- **Core →** [`Packages/com.skymavis.axiemixer3d/README.md`](Packages/com.skymavis.axiemixer3d/README.md)
- **Weapon animations (optional) →** [`Packages/com.skymavis.axiemixer3d.weaponanims/README.md`](Packages/com.skymavis.axiemixer3d.weaponanims/README.md)
