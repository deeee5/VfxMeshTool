# Changelog

All notable changes to this package are documented here. The format follows [Keep a Changelog](https://keepachangelog.com/), and the package uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- Mesh Asset slot in Export (replaces Output Mesh Asset): dropping a mesh in loads it as the base. Meshes saved by the tool restore their settings; any other mesh loads as the new Imported shape to apply UV, vertex colour, transform and normals.
- DCC Link: Send Format (OBJ / FBX) for Blender too; Edit in Blender exports the current settings in that format and Blender imports it (FBX scale baked to 1). Blender picked as a Custom application uses the same automation.
- DCC Link: the Executable field is now an Application row showing the app in use, with Use Other... / Auto.
- Override Imported Mesh button (with a confirmation dialog) overwrites the loaded Mesh Asset; it is greyed out when there is nothing overwritable. Turning on Auto Write also asks for confirmation, and Auto Write turns off whenever a new mesh is loaded. Otherwise results are saved as a separate asset with Save As New Asset.
- Create GameObject in Scene asks whether to overwrite the Mesh Asset or save a new asset before placing it.
- Saved mesh assets carry their generation settings as a hidden sub-asset.
- UI screenshots in the README, with a numbered layout image matched to a table of window areas.
- Japanese documentation moved to README.ja.md; README.md is now in English.

## [0.3.0] - 2026-10-22

First public release.

### Added
- UV projections: Planar XY / XZ / YZ, Cylindrical, Spherical, Polar (in addition to Parametric).
- Planar options: Unfold (Center / Ring / Cut) to separate overlapping faces, Fill Square, Circle to Square with Equalize and Radial Distribution.
- UV Distribution curves (UV only, independent of the vertex distribution) and Real Size UV (Tile Size + Scale).
- OBJ (quads, welded seams) and FBX (binary, RGBA vertex colours) export.
- DCC Link: edit in Blender and send the mesh back to the scene object (button or on save); other DCCs via a custom executable and a watched return folder.
- Align to Origin per axis (Min / Center / Max) in Transform.
- Reset to Default per section; preview display settings moved to the preview toolbars with a Default button.
- Vertex / triangle counts and mesh size shown at the top-left of the 3D view.

### Changed
- Package name is now `com.deeee5.vfxmeshtool` (was `com.vfxmeshtool.core`).
- Depends on the Unity FBX Exporter (`com.unity.formats.fbx` 5.1.6).

### Fixed
- Assigning a mesh to Output Mesh Asset with Auto Write on no longer overwrites it immediately.

## [0.1.0]

- Initial internal version: shapes (Plane, Cylinder, Sphere, Disc, Torus, Cross, Revolve, Path), vertex colour channels, 3D / UV preview, mesh asset export and presets.
