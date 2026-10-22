# VFX Mesh Tool (Unity)

**English** | [日本語](README.ja.md)

A procedural mesh generator for real-time VFX that runs inside the Unity Editor.
Cylinders, discs, spheres, revolve and path shapes, vertex colour / alpha gradients, many UV layouts, live 3D and UV previews, OBJ / FBX export and a round trip to Blender, all in one window.

![VFX Mesh Tool window (numbers match the Window layout table below)](Docs~/ui_layout.png)

## Install

Requires Unity 2021.3 or later, Built-in or URP.

1. Open **Window > Package Manager**.
2. Click **+ > Add package from git URL...** and enter:

   ```
   https://github.com/deeee5/VfxMeshTool.git#v0.3.0
   ```

3. Open the tool from **Window > VFX Mesh Tool**.

- Drop `#v0.3.0` to get the latest `main`. You can also add `"com.deeee5.vfxmeshtool": "https://github.com/deeee5/VfxMeshTool.git#v0.3.0"` to `Packages/manifest.json`.
- The dependency, FBX Exporter (`com.unity.formats.fbx` 5.1.6), is installed automatically.
- To modify the tool, clone the repository and add it with **Add package from disk...** (pick `package.json`). If you copy the folder under `Assets/` instead, add FBX Exporter yourself from the Package Manager (everything except FBX export works without it).
- The preview shaders are unlit CG. On HDRP the preview may not render (UserMaterial mode works with HDRP materials).

## Window layout

| No. | Name | Contents |
| --- | --- | --- |
| 1 | Preset bar | Saving and loading a full set of settings (`VfxMeshPreset` asset): Save As / Apply / Reload |
| 2 | Mesh settings panel | Settings for the shape, transform, vertex distribution, UV, vertex colour, export and DCC link. Changes here change the mesh and the exported result (undoable) |
| 3 | 3D view toolbar | Display settings of the 3D preview: preview mode, preview texture / material, wireframe, shading, culling, grid, background colour, Scene view display, camera (Frame / Reset / Home), reset of display settings (Default) |
| 4 | 3D preview | Live 3D view of the generated mesh, with mouse camera controls |
| 5 | Mesh info | Vertex count, triangle count and size (width × height × depth) |
| 6 | UV view toolbar | Display settings of the UV preview: range, fill, opacity, view reset, UV scroll playback |
| 7 | UV preview | UV layout: the texture in the background, the mesh wireframe and fill on top |
| 8 | Status bar | Control hints and error messages |

- The toolbars (3, 6) only change the display. They never change the mesh or the exported values.
- The borders between the panel and the previews, and between the 3D and UV previews, can be dragged. On a narrow window the toolbars wrap onto two rows.

### Sections of the mesh settings panel (2)

The colour band at the left of each section header shows what the section changes.

| Colour | Section | Contents |
| --- | --- | --- |
| Blue | Shape | Shape type, its dimensions and segment counts |
| Blue | Transform & Normals | Align to Origin, Pivot Offset, rotation, scale, normal direction, double-sided |
| Blue | Vertex Distribution | Distribution curves for the vertex spacing |
| Green | UV | UV projection, UV-only distribution, real-size UV, tiling / offset |
| Orange | Vertex Color | Values and gradients of the R / G / B / A vertex colour |
| Purple | Export | Saving as a mesh asset, OBJ or FBX, and placing in the scene |
| Purple | DCC Link | Editing in Blender or another DCC tool and sending the mesh back to Unity |

The ⋮ menu at the right of each section header has **Reset to Default**, which resets only that section (Shape keeps the shape type and resets its parameters; undoable).

![Mesh settings panel with Transform & Normals and Vertex Distribution folded (UV, Vertex Color, Export with the Mesh Asset slot, DCC Link)](Docs~/ui_panel_uv_export.png)

### 3D view toolbar (3)

| Item | Contents |
| --- | --- |
| Preview mode | Textured (texture × vertex colour) / TexturedAdditive / VertexRGB / VertexAlpha / UvChecker (checker plus UV as colour) / Normals / UserMaterial (your material in Mat) |
| Tex / Mat | Preview texture and material. With Tex empty, `_MainTex` / `_BaseMap` of Mat is used |
| Wire / Shade / Grid | Wireframe, view-direction shading, floor grid |
| Cull | Faces not drawn: Off = both, Back = back faces hidden, Front = front faces hidden (to see the inside) |
| BG | Background colour |
| Scene / Handles | The generated mesh in the Scene view / Path control-point handles (Path with Points only) |
| Frame / Reset / Home | Frame the whole mesh (F) / go to the home view (R) / set or reset the home view |
| Default | Reset of the display settings (background, mode, UV view, scroll, 3D / UV split, camera). Tex / Mat and the mesh settings are kept |

### UV view toolbar (6)

| Item | Contents |
| --- | --- |
| Range | Unit = 0..1, Extended = -1..2 |
| Fill / Opacity | Mesh fill in the same mode as the 3D view, and its opacity (to see where vertex alpha lands on the texture) |
| Reset | Reset of the UV view position |
| Scroll | Preview of a scrolling texture: Play, U / V speed per second, 0 to rewind. Display only; the mesh UVs do not change (no effect in UserMaterial mode) |

### Camera controls (4, 7)

| Input | Action |
| --- | --- |
| Left drag / Alt + left drag | Orbit |
| Middle drag / Shift + left drag | Pan |
| Right drag / wheel | Zoom |
| `F` | Frame the whole mesh |
| `R` | Go to the home view |

- The camera is fixed in world space and does not follow the mesh size, so a bigger shape looks bigger. The grid spacing depends only on the camera distance.
- The UV preview pans with drag, zooms with the wheel and resets with Reset in the UV view toolbar. The texture wrap mode (Repeat / Clamp) shows in the background.

## Shapes

Every shape is a parametric surface over (u, v) ∈ [0,1]². U runs around / across, V along the height, length or radius.

| Shape | Main parameters | Typical use |
| --- | --- | --- |
| Plane | Width / Length / Orientation / Bend / Width Profile | Quads, tapered trails, curved sheets |
| Cylinder | Bottom / top radius, Height, Arc, Twist, Radius Profile, Pivot | Tubes, cones (top radius 0), auras, tornadoes |
| Sphere | Radius, latitude range, Arc | Spheres, half domes, shields |
| Disc | Inner / outer radius, Arc, Height, Height Profile, UV Mode | Discs, rings, fan slashes, shockwaves |
| Torus | Major radius, tube radius, Arc, Tube Arc, Tube Start | Ring shockwaves, loops |
| Cross | Plane count, Width, Height, Alternate Flip U | Crossed planes (pillars of fire instead of billboards) |
| Revolve | Profile points (radius, height), Smooth, Top Cap Curvature, Arc, Twist | Any lathe shape (bell, drop, vase) |
| Path | Path Source (Points / Curve / Coil), section (Ribbon / Tube / Cross / Profile), Scale / Scale Y / Twist Profile, Up Mode | Spline sweeps (trails, spirals, crossed ribbons) |
| Imported | Source Mesh, Keep Original Colors | An existing mesh (.asset, a mesh inside an FBX, ...) used as is, with only UV, vertex colour, transform and normals applied |

### Revolve and Path notes

- The first Revolve profile point is the bottom (V = 0). Smooth uses Catmull-Rom.
- **Top Cap Curvature**: roundness of the top. 0 = pointed like a cone, 1 = round like a sphere pole. For profiles that close on the axis (radius 0) at the top; only with Smooth on.
- Profile and control-point lists are edited per row with `+` (insert a midpoint after) and `-` (delete). At least two points are kept.

| Path Source | Contents |
| --- | --- |
| Points | Control points, editable with Scene view handles (`Handles` in the 3D view toolbar) |
| Curve | A straight line along X bent by two curves (Offset Y / Offset Z, in metres). Good for wavy trails |
| Coil | A spiral: Turns, Height, Radius, plus curves for the coil thickness (Radius Profile) and spacing (Height Profile) |

- `Scale (V)` scales the whole section, `Scale Y (V)` only its height. `Twist Profile (V)` distributes the twist. Preset buttons sit under each curve.
- Section `Cross` is two perpendicular ribbons sharing UVs; `Profile` is any 2D section.
- `Up Mode` decides the section's up direction; `RotationMinimizing` is the default.
- Path V is normalised by arc length. Use UV Tiling for distance-based tiling.

## Transform & Normals

- **Align to Origin**: per-axis Off / Min / Center / Max buttons (Bottom / Top for Y). Puts the mesh edge or centre at 0, measured after rotation and scale, and keeps it there when the shape changes.
- **Pivot Offset**: added after the alignment (for example: bottom at 0, then up by 0.1).
- The Cylinder **Pivot (0=Bottom 1=Top)** is a point along the cylinder's height. When Align to Origin fixes that direction it has no effect and is shown greyed out as `Pivot (Align 優先)`.
- **Flip Normals / Double Sided / Recalculate Normals / Generate Tangents**: flip faces / duplicate vertices for both sides / Unity's normal recalculation / tangents for normal maps.

## Vertex Distribution

`Distribution U / V`: curves for the vertex spacing; a straight line means even spacing. Useful for finer segments at a fading edge. UVs follow the vertices, so the texture does not distort (to change how the texture is spread, use UV Distribution in the UV section).

## UV

The UV section is applied top to bottom: Projection → UV Distribution → Real Size → Tiling / Offset.

### Projection

| Projection | U | V | Use |
| --- | --- | --- | --- |
| Parametric | Shape's own (around / across) | Shape's own (height / length / radius) | Default. Disc offers Radial / Planar in `UV Mode` |
| Planar XY / XZ / YZ | X / X / Z | Y / Z / Y | Front, top and side projections, centred on (0.5, 0.5) |
| Cylindrical | Angle around Y | Height | Wrapping a texture around lathe shapes |
| Spherical | Longitude | Latitude (around the origin) | Domes and bells |
| Polar | Angle around Y | Distance from the axis | Ring layout seen from above (shockwave textures) |

- Projections use the shape before Transform, so the UVs follow the mesh when it is rotated.
- Angle-based projections do not tear at the seam (U may be offset by one turn).
- With anything but Parametric, the vertex colour sources `U` / `V` / `RadialUV` use the projected values.

### Planar options

| Option | Contents |
| --- | --- |
| Unfold | How overlapping faces are spread. Center = outward from the centre along the surface (domes, bells, vases, discs) / Ring = a ring with the far end inside and the near end outside (tubes, even with a narrow waist) / Cut = cut at the seam into a rectangle (strip textures). `Reverse` starts from the other side |
| Fill Square | Stretches each axis to fill the 0..1 square (aspect ratio changes) |
| Circle to Square | Keeps the direction from the centre and stretches the distance to the square's edge, so concentric circles become concentric squares and the corners are used |
| Equalize | How evenly Circle to Square sizes the UV cells: 0 = as is, 1 = equal area (the centre hole and the border stay) |
| Radial Distribution | Curve for the ring spacing from the centre to the border in Circle to Square |

| Circle to Square | Unfold: Ring | Unfold: Cut |
| --- | --- | --- |
| ![](Docs~/20_uv_circle_to_square.png) | ![](Docs~/21_uv_unfold_ring.png) | ![](Docs~/22_uv_unfold_cut.png) |

- Overlap removed (overlapping UV area): vase from the top (Center) 53.6% → 0%, three-quarter sphere from the top (Center) 75% → 0%, narrow-waisted tube from the top (Ring / Cut) 100% → 0%.
- A shape lying across the projection axis (a tube seen from the side) is not fully separated by any mode; use Cylindrical for it.
- On flat shapes Unfold gives the same result as plain Planar.
- Order when combined: Unfold → Fill Square → Circle to Square.

### UV Distribution

`Distribution U / V`: UV-only curves. They do not move vertices; they change how the texture is spread (e.g. `Stretch Start` stretches the start side). Evaluated per vertex, so use more segments for detailed curves. Vertex colours are not affected.

### Real Size

`Real Size U / V`: assigns UVs by real length instead of 0..1 (one tile = `Tile Size (m)`, with a per-axis `Scale`). The length is an area-weighted average and includes the Transform scale. With both on, the texture keeps its aspect ratio.

### Tiling / Offset

`Tiling` / `Offset` / `Rotation (deg)` / `Flip U` / `Flip V` / `Swap UV`: the final UV adjustments.

## Vertex Color

| Item | Contents |
| --- | --- |
| R / G / B / A | Per channel: value source (Source), remap curve (Curve) and invert (Inv) |
| Source | Constant / U / V / RadialUV (distance from the UV centre) / PositionX, Y, Z (normalised in the bounds) / Random (deterministic per vertex) |
| Alpha Presets | One-click alpha fades (both ends, top, bottom, radial, ...) |
| Use RGB Gradient | Overrides RGB with a gradient (A keeps its channel setting) |

## Export

| Format | Extension | Faces | Vertex colour | Use |
| --- | --- | --- | --- | --- |
| MeshAsset | .asset | Triangles | RGBA | Use directly in Unity; Overwrite and Auto Write with Override on |
| Obj | .obj | Quads | RGB only | Editing in Blender and other tools |
| Fbx | .fbx (binary) | Triangles | RGBA | Handing over to DCC tools with vertex alpha |

| Button / item | Contents |
| --- | --- |
| Mesh Asset | The base mesh. Dropping a mesh in loads it: a mesh saved by this tool brings back the settings it was made with; any other mesh becomes the Imported shape and looks exactly like the original right after loading (original UVs and vertex colours) |
| Override | Off (default): the Mesh Asset is never written; save the result as a separate asset with Save As New Asset. On: Overwrite and Auto Write write into the Mesh Asset. Only for a writable .asset (not for meshes inside an FBX). Turns off again whenever a new mesh is loaded |
| Auto Write | With Override on: writes into the Mesh Asset on every change (scene objects update live). Not right after loading a mesh, turning it on or opening the window |
| Save As New Asset... | Saves the result as a new `.asset` and puts it in the Mesh Asset slot (the original is untouched) |
| Overwrite | With Override on: replaces the contents of the Mesh Asset and saves it (scene objects using it update) |
| Export OBJ... / Export FBX... | OBJ / FBX export, also outside the project |
| Create GameObject in Scene | Creates a GameObject with MeshFilter + MeshRenderer (assigns Mat if set) |

- An .asset saved by this tool carries the settings it was made with as a hidden sub-asset (restored when the mesh is loaded into the Mesh Asset slot).
- When an Imported mesh is written back into its own asset, the first write keeps a copy of the original shape inside the asset and later builds start from it, so rotations and the like do not stack.
- OBJ and FBX are converted to right-handed coordinates, so they import into Blender with the right orientation and size.
- OBJ welds vertices with the same position and colour within a surface (cylinder seams, cone tips) and keeps the UV seams. Vertex colours use `v x y z r g b` (checked with Blender 4.1 / 5.2).
- FBX is written with the Unity FBX Exporter in centimetres, so in Blender the object scale is 0.01 (the size is correct; apply it with `Ctrl+A > Scale` before editing).

## DCC Link

| Item | Contents |
| --- | --- |
| DCC | Blender (import and send-back are automated) / Custom (any other application) |
| Application | The application that is launched. Blender is auto-detected (shown as `Blender 5.2/blender.exe (自動検出)`); **Use Other...** picks a different one (another Blender version, or any app under Custom) and **Auto** goes back to auto-detection. If the app picked under Custom is Blender, the automatic import and send-back are used as well |
| Send Format | Format the current settings are exported in before launching: Obj (quads; for Blender, vertex colours with alpha are reapplied) / Fbx (triangles, RGBA vertex colours; for Blender, the centimetre scale is baked so the object scale is 1) |
| Arguments | Launch arguments for a non-Blender Custom app (`{file}` = the sent file, `{return}` = the return path) |
| Target | Scene object whose mesh is replaced when the mesh comes back. Empty = a new object is created |
| Edit in Blender / Open in DCC | Exports the current settings in the Send Format and launches the application (for Blender, the file is imported automatically) |
| Resume | Opens the previous .blend to continue editing in Blender (only when there is one) |
| Return | Path the DCC writes back to (Copy copies the absolute path) |

1. Choose a Target and press **Edit in Blender**. The current settings are exported as OBJ or FBX (Send Format), Blender starts and imports the file (including vertex alpha).
2. Edit in Blender and press **Send to Unity** in the 3D view sidebar (N) > **VFX Mesh** tab. With **Send on Save** on, every save (Ctrl+S) sends it. Modifiers and the object transform are baked.
3. Unity watches the return folder, imports the file even without focus and replaces the Target's mesh (undoable).

- With a non-Blender Custom app, export an FBX to the `Return` path and it is picked up the same way.
- Send folder: `<project>/VfxMeshBridge/` (outside Assets). Return folder: `Assets/VfxMeshBridge/<Mesh Name>.fbx`.
- A mesh edited in Blender is no longer the parametric shape. Use **Resume** to keep editing; **Edit in Blender** sends the current parameters again and discards the Blender edits.
- DCC settings are stored per machine (EditorPrefs). The Blender script is `Editor/DccAdapters/vfxmesh_blender_bridge.py`.

## Runtime use

The `VfxMeshTool.Runtime` assembly alone can build meshes.

```csharp
var settings = new VfxMeshSettings { shape = VfxShapeType.Cylinder, cylinderRadiusTop = 0f };
Mesh mesh = VfxMeshBuilder.Build(settings);
```

## Automation API (tests / batch)

`VfxMeshToolWindow` can render previews to images without a GUI.

```csharp
var window = ScriptableObject.CreateInstance<VfxMeshToolWindow>();
window.SetSettings(settings);
window.SetPreviewOptions(VfxMeshToolWindow.PreviewMode.Textured, texture, null, wire: true, grid: true, VfxMeshToolWindow.UvViewRange.Unit);
Texture2D png3d = window.CaptureStatic3D(512, 384);
Texture2D pngUv = window.CaptureStaticUv(512, 384);
```

## Tests

Batch tests live in `Tests~/` (not compiled by default because of the `~`). Copy them into `Assets/Editor/` of any project and run:

```bash
Unity.exe -batchmode -projectPath <project> -executeMethod VfxMeshToolBatchTest.Run -logFile <project>/unity.log
```

| Test | Contents |
| --- | --- |
| VfxMeshToolBatchTest | All shapes: generation, normals, UV, vertex colour, asset save, preview images (`<project>/captures/`) |
| VfxUvProjectionTest | UV projections, Unfold, Circle to Square, UV Distribution, Real Size |
| VfxExportTest | OBJ / FBX export (`<project>/exports/`) |
| VfxResetTest | Section resets, display Default, Align to Origin |
| VfxImportTest | Mesh Asset slot (settings restore, Imported shape, mesh inside an FBX, Override, writing back into the source asset) |
| VfxDccLinkTest | Unity → Blender → Unity round trip (needs Blender) |
| VfxTopCapTest | Revolve Top Cap Curvature |

`VfxMeshToolBatchTestUrp.cs` is for projects with URP.

## Layout of the package

```
VfxMeshTool/
  package.json, README.md, README.ja.md, CHANGELOG.md, LICENSE.md
  Runtime/
    VfxMeshSettings.cs     … settings (shape / transform / UV / vertex colour)
    VfxMeshData.cs         … intermediate data (vertices, normals, params, UVs, colours, triangles)
    VfxMeshBuilder.cs      … shape generation and post-processing (transform, double-sided, UV, vertex colour)
    VfxMeshPreset.cs       … preset (ScriptableObject)
  Editor/
    VfxMeshToolWindow.cs   … main window
    VfxPreviewRenderer.cs  … PreviewRenderUtility wrapper
    VfxMeshExporter.cs     … asset save / scene placement
    VfxMeshFileExporter.cs … OBJ / FBX export
    VfxDccLink.cs          … DCC link (send, launch, watch the return folder, replace)
    DccAdapters/vfxmesh_blender_bridge.py … Blender side (import, Send to Unity, send on save)
    VfxMeshPresetEditor.cs
    Shaders/VfxMeshPreview.shader, VfxMeshWire.shader
  Docs~/   … README images
  Tests~/  … batch tests
```

## Known limitations

- On HDRP the hidden preview shaders may not render (UserMaterial mode works with HDRP materials).
- No caps for tubes yet; combine with a Disc if needed.
- No custom data in UV2 / UV3 yet.

## License

[MIT](LICENSE.md). Changes: [CHANGELOG](CHANGELOG.md).
