# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0]

### Added
- Initial release. Weapon/action animation clips (`Action.*`) split out of
  `com.skymavis.axiemixer3d` into this optional package.
- `AxieWeaponAnimCatalog` — serialized, per-body catalog of the weapon clips.
- `AxieWeaponAnims.Register` / `Unregister` — register the catalog with an `AxieFactory`.
- `AxieWeaponAnimInitializer` — optional component that registers on `Awake` (execution order
  -9000, after `AxieMixerInitializer`).
- `WeaponAnimNames` — generated typed constants for the weapon/action clip names (the names that
  left the main package's `AnimNames`).
