# Resources

Meshes here are auto-loaded at runtime via `Resources.Load<Mesh>("Name")`.
Currently: a `House.obj` used by `Structure3DRenderer`.

## Editing in Blender

1. Open `House.obj` in Blender (`File → Import → Wavefront (.obj)`).
2. Tweak geometry however you like. Keep the building's *base center* at the
   origin and Y as up — the renderer relies on those.
3. Export back with `File → Export → Wavefront (.obj)`:
   - **Filename**: `House.obj` (overwrite the existing file).
   - **Forward**: -Z, **Up**: Y (these are Blender export-dialog options).
   - **Selected only**: optional, but make sure only your building is selected.
4. Switch to Unity. The Editor auto-reimports the changed asset. Hit Play and
   the new geometry replaces the procedural one.

## Falling back to procedural

Delete `House.obj` (or rename it) and `Structure3DRenderer` will rebuild the
procedural house. Useful as a sanity check.

## Adding more building types

Drop matching `.obj` files here (e.g. `Generator.obj`, `Well.obj`) and extend
`Structure3DRenderer.LoadHouseMesh` into a `LoadMesh(StructureType type)` that
maps each type to a Resources name. The procedural fallback can stay for any
type that doesn't have an asset yet.
