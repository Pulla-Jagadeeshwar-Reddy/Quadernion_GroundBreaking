# Routes

Route-specific props, items, and challenge geometry (owner: Rahul). Structural rooms, walls, floors, and the lobby belong to Ritu and do not go here.

## Layout

```
Routes/
  _Sandbox/RahulRouteSandbox.unity   test scene; never edit OnlineFirstFloor.unity
  Route01_<Name>/
    Models/      .fbx exports (.blend sources may sit beside them)
    Materials/   URP Lit materials
    Textures/
    Prefabs/     integration-ready prefabs handed to Ritu
  Route02_<Name>/ ...
```

Create a subfolder only when it has content, so Unity does not leave orphaned folder `.meta` files.

## Conventions

- Names: `R<route>_<Object>`, for example `R1_Key`, `R1_FuseBox`, `R1_CrouchVent`.
- Scale: 1 unit = 1 m. Export FBX with transforms applied and check it at scale 1 in Unity.
- Pivots: bottom-centre for floor props, hinge for doors and lids, centre for handheld items.
- Colliders: simple Box/Capsule colliders on the prefab, not Mesh Colliders.
- No network scripts on prefabs; Chetana wires interactions. Note which object is interactable in the route brief.
- The sandbox has 2.0 m standing and 1.1 m crouched reference capsules for checking clearances.

Route designs and object IDs live in `TeamDocs/ROUTE_BRIEFS.md`.
