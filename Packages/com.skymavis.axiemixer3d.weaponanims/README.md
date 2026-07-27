# Axie Mixer 3D — Weapon Animations

Optional add-on for [`com.skymavis.axiemixer3d`](../com.skymavis.axiemixer3d). It ships the ~675
weapon/action animation clips (`AxeAttack`, `BowSkill`, `CannonWalk`, …) that were split out of the
main package to keep the base install small (~190 MB of clips). Clips not in this package are
unaffected — the main package still ships the `Default.*` locomotion set (Idle, Run, Walk, Dead, …).

## Install

Install the **main package first**, then this one. In Unity, go to
**Window → Package Manager → Add package from Git URL...** and paste:

```txt
https://github.com/axieinfinity/unity-axie-mixer3d.git?path=/Packages/com.skymavis.axiemixer3d.weaponanims
```

Or add it to `Packages/manifest.json` directly (alongside the main package):

```jsonc
{
  "dependencies": {
    "com.skymavis.axiemixer3d": "https://github.com/axieinfinity/unity-axie-mixer3d.git?path=/Packages/com.skymavis.axiemixer3d",
    "com.skymavis.axiemixer3d.weaponanims": "https://github.com/axieinfinity/unity-axie-mixer3d.git?path=/Packages/com.skymavis.axiemixer3d.weaponanims"
  }
}
```

This package declares a dependency on `com.skymavis.axiemixer3d` ≥ 1.1.0.

## Enable the weapon animations

A consumer **must register the catalog** with the factory. Two ways:

### Component (recommended)

Add an **Axie Weapon Anim Initializer** component to the same bootstrap GameObject that carries your
`Axie Mixer Initializer`, and assign the catalog asset
(`Packages/.../WeaponAnimAssets/Catalog/AxieWeaponAnimCatalog.asset`). Its execution order (-9000)
runs after `AxieMixerInitializer` (-10000) has assigned `AxieFactory.Default`.

### Code

```csharp
using SkyMavis.AxieMixer3D.WeaponAnims;

// after AxieFactory.Default is assigned (e.g. by AxieMixerInitializer):
AxieWeaponAnims.Register(myWeaponCatalog);
```

## Use

Once registered, weapon clips resolve transparently through the normal animation API — no
per-instance wiring:

```csharp
character.Playable.Play(WeaponAnimNames.AxeAttack, loop: false);
```

Playing a weapon clip **without** registering the catalog returns null and logs the usual
"clip not found on this body" warning — no crash.

## Re-baking

`Tools → Axie Mixer 3D → Update Weapon Anim Catalog` re-scans
`WeaponAnimAssets/Bodies/{Body}/Animations/` for `Action.*` clips, rebuilds the catalog asset, and
regenerates `Runtime/WeaponAnimNames.cs`.
