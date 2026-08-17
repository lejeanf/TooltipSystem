# Navigation path setup (Unity 6, SubScene-baked geometry)

How to make the NavigationTooltip draw a path across a floor the VR player can also teleport onto,
now that room geometry (floor + walls) is baked into ECS SubScenes.

Two menu commands cover the whole thing:

| Command | What it does |
| --- | --- |
| `Tools/TooltipSystem/Validate Setup` | Runs 14 checks over the loaded scenes and logs the fix for each problem. Also runs automatically before entering Play mode when the scene uses navigation paths. |
| `Tools/TooltipSystem/Setup Navigation Floor` | On the selected floor object(s): adds a solid collider, an XRI `TeleportationArea`, and a `NavMeshSurface` bounded to that floor, then bakes and saves the NavMeshData asset. |

## 1. Why the components cannot live in the SubScene

A SubScene is baked to entities. A MonoBehaviour with no baker simply does not exist at runtime, and
neither do its GameObject colliders. So inside a SubScene:

- a `NavMeshSurface` never calls `AddData()` → **no navmesh at runtime**;
- a `TeleportationArea` never registers, and the XRI teleport ray (a plain `Physics.Raycast`) has no
  collider to hit → **the player cannot teleport there**;
- a `NavigationTooltip` never runs.

All three look perfectly configured in the editor while the SubScene is open, and silently do nothing
in a build. `Validate Setup` reports this as **SubScene: baked-away components**.

**Put the geometry in the SubScene; put the navigation components in the floor's additively-loaded
dependency scene.**

## 2. Why baking still works

At edit time `NavMeshSurface` collects geometry through `NavMeshBuilder.CollectSourcesInStage`, which
walks the whole editor **stage** — every scene currently open, including open SubScenes. So a surface
sitting in `Floor_00_Dependencies` bakes the floor and wall meshes that live in the `00_Lobby_Walls`
SubScene, as long as that SubScene is **open** when you bake.

At runtime the surface needs no geometry at all: it re-adds its baked `NavMeshData` asset in
`OnEnable`, positioned at its own transform. Loading the dependency scene brings the floor's navmesh
in; unloading it takes it back out. That gives per-floor navmesh streaming for free.

## 3. The procedure, per floor

1. Open the floor's **dependency scene** (the additive GameObject scene, not a SubScene) *and* the
   room SubScenes that hold its floor/wall meshes. Closed SubScenes contribute nothing to the bake.
2. Select the floor object in the dependency scene (a flat "floor plate" object is fine — it does not
   need to be the visible mesh).
3. Run `Tools/TooltipSystem/Setup Navigation Floor`. It writes:
   - a non-trigger `Collider` (existing trigger colliders get `Is Trigger` cleared — the teleport
     ray's *Raycast Trigger Interaction* defaults to `Ignore`, so triggers are invisible to it);
   - a `TeleportationArea` with that collider listed and a non-empty Interaction Layer Mask (copied
     from the project's other areas);
   - a `NavMeshSurface` with **Collect Objects = Volume** sized to the floor plus 4 m of head room,
     and Agent Type set to the default;
   - the baked `NavMesh-<object>.asset` next to the scene, the same place and naming the Navigation
     window uses.
4. Run `Tools/TooltipSystem/Validate Setup` and fix anything it reports.
5. Save the dependency scene.

### Volume, not All

`Collect Objects = All` bakes every scene open in the stage. With one surface per floor that means
each floor's asset contains the whole building, and loading two floors stacks overlapping navmeshes —
paths then jump between duplicate surfaces. `Validate Setup` warns about this as
**NavMesh: collection scope**.

### Render Meshes vs Physics Colliders

The surface defaults to `Use Geometry = Render Meshes`, which is right when the volume covers the
SubScene room meshes. A floor plate that has a collider but no renderer contributes nothing under that
setting — switch the surface to `Physics Colliders` if the floor is collider-only. The bake reports
`baked … but it is EMPTY` when nothing was collected.

## 4. What the validator checks

**Tooltip** — the `NavigationTooltip` exists, is active and enabled; marker material / LineRenderer /
thresholds / `Player` tag are set; every `NavigationDestinationSender` sits on the navmesh.

**NavMesh** — a navmesh exists at all; every `NavMeshSurface` has baked data and is enabled; bake
scope is bounded; surfaces use the **default agent type** (`NavMesh.CalculatePath`, which the tooltip
uses, only ever queries the default agent — a surface baked for another agent is invisible to it); the
player spawns on the navmesh.

**Teleportation** — at least one active teleport surface; each has a solid (non-trigger) collider; a
non-empty Interaction Layer Mask; a layer included in the teleport `XRRayInteractor`'s Raycast Mask;
and **navmesh above it**, so the path can actually route onto every place the player can land.

**SubScene** — none of the above components live inside a SubScene.

The Tooltip package has no compile-time dependency on the XR Interaction Toolkit: the teleportation
components are resolved by type name, the same way the project's other validators handle
pipeline-specific components. Without XRI installed those checks report as skipped.
