# Packaged Blue Archive Touch Effect Candidate Set

This is an isolated extraction from `apk/Blue Archive_1.90.439170.apks`. It is
not connected to the BASpark implementation. The packaged Unity player version
is 2021.3.56f2.

## Main asset graph

The root prefab is `FX_Touch`, serialized as
`23c05e7f1fd70bc4a8325a6d77f7b244`. Its children are `ring`, `MeshTri`,
`Ring (3)`, `Ring (4)`, and `Trail`. Together they provide the click disc,
annular rings, click triangles, distance-emitted drag triangles, and additive
trail.

## Contents

- `textures/`: four decoded PNG source textures.
- `meshes/`: the triangle and annular meshes as OBJ.
- `unity_raw/`: the original prefab, materials, meshes, textures, and touch
  shaders, copied with readable filenames.
- `shaders/`: the packaged bloom settings plus the compiled bloom shader asset
  and readable pass metadata.
- `metadata/`: packaged Addressables settings and the Unity level containing
  the UI camera/root scale reference.
- `fx_touch_parameters.json`: full component curves, gradients, material blend
  values, runtime input lifecycle recovered from IL2CPP metadata, UI scale, and
  bloom parameters.
- `SHA256SUMS`: integrity hashes for every retained candidate artifact.

`texture_contact_sheet.png` is a quick visual inventory. The decoded PNG alpha
channels are significant; do not flatten them before rendering the preview.
