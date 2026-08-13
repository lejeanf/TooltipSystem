# Navigation Path Redesign — Arrows, Pulse & Orthogonal Paths

**Status:** Proposal — iterating on look & feel via interactive mockup before implementation.
**Target version:** 3.2.0 (minor — additive features, `SpriteLine` kept as deprecated alias).
**Date:** 2026-08-13

## 1. Problem

Users reported the current navigation guidance is unclear. Today [NavigationTooltip](../Runtime/Scripts/NavigationTooltip/NavigationTooltip.cs) draws either:

- a `LineRenderer` over the raw NavMesh corners, or
- pooled `SpriteRenderer` dots ([Dot.prefab](../Runtime/Prefabs/Dot.prefab)) spaced along the path.

Two legibility issues:

1. **Dots carry no direction.** A trail of identical dots doesn't say *which way* to walk.
2. **The raw NavMesh path looks robotic.** The funnel algorithm hugs corners and door jambs — nobody walks like that, so the path reads as "debug line", not "walk here".

## 2. Goals

- Replace dots with **arrows oriented along the path** toward the target.
- **Animated brightness pulse** traveling toward the target; each arrow lights up as the pulse passes and fades back to base color slowly.
- Arrow drawn **in-shader (SDF chevron)** — no canvas, no texture, crisp at any distance.
- **Zero GC alloc** at runtime, minimal CPU cost (pulse animated entirely on GPU).
- New **path mode: Orthogonal** — 90°-turn path that passes through the *center* of doorways (how humans actually walk), with **rounded corners** (radius slider).
- Inspector **toggle-list selectors** for path mode (Shortest / Orthogonal) and style (Line / Dots / Arrows).

## 3. Architecture

```
NavMesh.CalculatePath (shortest corners)
        │
        ▼
NavigationPathProcessor  (pure static, preallocated buffers, testable)
  1. [Orthogonal mode] rectilinearize + validate on navmesh
  2. fillet corners (arc, radius slider)
  3. resample every `spacing` meters → position, tangent, cumulative distance
        │
        ▼
NavigationPathRenderer
  • Line  → LineRenderer fed processed points (rounded corners for free)
  • Dots / Arrows → Graphics.RenderMeshInstanced (quad + SDF shader)
        │
        ▼
Shader (URP + HDRP Shader Graph, like existing roundedRect_*)
  • SDF chevron / dot / (line uses same pulse via stretched UV)
  • pulse brightness from _Time + per-instance path distance
  • player-progress fade for "consumed" markers
```

New/changed types:

| Type | Change |
|---|---|
| `NavigationPathMode` | new enum: `Shortest`, `Orthogonal` |
| `NavigationPathStyle` | new enum: `Line`, `Dots`, `Arrows` (replaces `NavigationTooltipType`; `SpriteLine` maps to `Dots`) |
| `NavigationPathProcessor` | new static class — pure path math, edit-mode testable |
| `NavigationPathRenderer` | new component — instanced draw, owns matrices/property blocks |
| `NavigationTooltip` | slimmed: path acquisition, player progress, orchestration |
| `NavigationObjectPool`, `Dot.prefab` | retired (kept one minor version with `[Obsolete]`) |

## 4. Path processing

### 4.1 Orthogonalization

For each shortest-path segment, decompose into two axis-aligned legs (world X/Z). Two candidate L-shapes exist (x-then-z / z-then-x); pick the first whose legs pass `NavMesh.Raycast` (stays on navmesh). If neither is valid (diagonal corridor), keep the original segment — graceful degradation, never a broken path.

Post-passes:
- **Doorway centering:** the funnel corners sit at door jambs (± agent radius). For each corner, `NavMesh.FindClosestEdge` gives distance to the nearest edge; nudge the crossing point perpendicular to the wall until clearance is balanced → path crosses the middle of the opening. Budgeted iterations, skipped for wide openings where centering doesn't matter.
- Merge collinear runs, drop jogs shorter than a threshold (avoids staircase noise).

*Decided: snap to world X/Z (covers typical architecture); per-area dominant-axis detection noted as a possible future extension.*

### 4.2 Corner rounding

Each interior corner → circular arc fillet: tangent distance `d = r / tan(θ/2)`, `r` clamped to 0.45 × min(adjacent leg lengths) so short legs never invert. Arc sampled at fixed angular step into the point buffer. `[Range(0, 2)]` slider `cornerRadius` (meters). Applies to both modes (also softens the shortest path).

**Navmesh validation:** an arc can leave the navmesh (e.g. rounding a corner that wraps a door jamb pulls the path into the wall). Per corner, validate the sampled arc with `NavMesh.Raycast` along its chords; on failure shrink that corner's tangent distance (×0.8, bounded iterations) and retry, falling back to the sharp corner if no radius fits. Radius saturates against walls instead of clipping through them.

### 4.3 Resampling

Walk the filleted polyline, emit a marker every `spacing` meters: position (floor + elevation offset), tangent (for arrow orientation), cumulative distance (for the pulse). Output into preallocated arrays.

## 5. Rendering

### 5.1 Arrows/dots — instanced, no GameObjects

`Graphics.RenderMeshInstanced` with one shared quad, one material, per-instance `Matrix4x4` (position, yaw from tangent, lying flat) + float array of normalized path distances via `MaterialPropertyBlock`. One draw call, no pooled GameObjects, no canvas. 1023-instance cap per call is far above realistic path lengths (spacing 1 m → 1 km of path).

### 5.2 SDF chevron shader (URP + HDRP Shader Graph)

- Chevron `>` drawn as signed-distance function in UV space; `_Shape` switches chevron/dot so one shader serves both styles.
- Unlit, transparent, no shadows — same setup pattern as `roundedRect_URP/HDRP`.

### 5.3 Pulse (all GPU)

```
head   = frac(_Time.y * _PulseSpeed / _PathLength)          // 0→1 toward target
behind = (instanceDist01 - head) wrapped to [0,1)            // how long ago the pulse passed
glow   = exp(-behind * _PathLength / _PulseTrail)            // slow fade back to base
color  = lerp(_BaseColor, _PulseColor, glow)
```

CPU cost per frame: zero (time is `_Time`; only `_PathLength` changes on repath). Optional `_PulseCount` for a wave-train instead of a single pulse — mockup has both to choose from.

### 5.4 Transitions (added 2026-08-13)

- **Show/hide fade:** `_GlobalFade` (0–1) multiplies alpha; the tooltip animates it over `fadeDuration` on state changes instead of popping.
- **Arrival sequence:** target ring pops (`_TargetGlow`), then the path wipes start→target (`_HideDist` animates 0→length; shader hides everything behind it with a soft edge), then the target ring fades out. Driven by a small state machine in `NavigationTooltip` (Hidden / FadingIn / Active / Arriving / FadingOut).
- **Target marker:** the system now renders a target ring itself (SDF shape 3, one extra `RenderMesh` call) — pulses light it up on arrival of each wave.

### 5.4bis Stability & repath policy (added 2026-08-13, after in-game testing)

- **The drawn path is a stable world object.** Walking roughly along it never repaths — it is
  consumed via `_PlayerDist` (markers fade out fully ~2.5 m behind the player). A repath happens
  only when the player strays past `repathDeviation` (default 1.5 m), the target moves, or the
  path was lost.
- **Repath blending:** old → new path lerp over `repathBlendDuration` (markers matched by their
  offset from the target end — they are end-anchored — and the line resampled to a fixed
  256-sample strip lerped 1:1).
- **Pulse head is CPU-advanced in meters** (`_PulseHead`/`_PulseInterval`), not `_Time`-derived:
  a time-based phase scrambles on every path-length change while walking.
- **Markers are end-anchored** (distances measured from the target), so repaths don't re-seed
  the whole trail from the moving start.
- **Path-loss grace:** transient `CalculatePath` failures keep drawing the last good path for 1 s
  instead of blinking off.
- **Arrival "bubble pop":** after the wipe, the target ring grows ~1.8× (ease-out) while fading.
- **Orthogonal mode refinements:** a direct 2-corner path stays a straight line; per segment,
  near-axis segments use L-shapes (keeps doorway centering) while strongly diagonal ones
  (aspect > 0.5) use octilinear axis + 45° legs; all bends navmesh-validated with fallback to raw.

### 5.5 Player progress ("consuming" the path)

Replace the pop-on-approach sprite removal with a shader fade: CPU tracks the player's closest sample index (incremental search from last index, O(1) amortized), writes `_PlayerDist01`; markers behind the player shrink/fade in-shader. Smoother than popping, and removes the whole sprite-list mutation logic (`CheckAndRemoveFirstSprite`, `IsPlayerNearFirstSprite`, …).

## 6. Zero-GC budget

- All buffers (`Vector3[]` points, `Matrix4x4[]` instances, `float[]` distances) preallocated at a serialized max path length; doubled only if exceeded (rare, logged in dev builds).
- Reused `NavMeshPath`, no LINQ, no closures, no string ops in Update.
- Locked by a play-mode test using NUnit's `AllocatingGCMemory` constraint around the update loop.

## 7. Inspector UX

- `NavigationPathMode` and `NavigationPathStyle` rendered as **toggle-list toolbars** (segmented buttons) via a small property drawer (check `fr.jeanf.propertydrawer` for an existing one before adding).
- Sliders: `cornerRadius`, `spacing` (controls marker count — inspector shows the resulting count for the current path), `markerSize`, `chevronWeight`, `pulseSpeed`, `pulseTrail`, `elevationOffset`.
- Colors: `baseColor`, `pulseColor` — forwarded to material properties (`_BaseColor`, `_PulseColor`; size/weight as `_Size`, `_Weight`).
- Runtime-switchable via public properties (mode/style changes just re-run the processor + swap renderer path).
- Editor validation (per package standard): warn if material/shader missing, spacing ≤ 0, radius vs spacing mismatch.

## 8. Tests & validation

- **Edit-mode:** orthogonalization determinism, L-shape validity fallback, fillet clamping on short legs, resample spacing accuracy, cumulative-distance monotonicity (processor is pure — no scene needed for the math; navmesh-dependent bits behind an interface).
- **Play-mode:** zero-alloc constraint; path re-anchors when target moves; style/mode switch at runtime doesn't leak instances.

## 9. Migration

- `navigationTooltipType` field → `pathStyle` with `[FormerlySerializedAs]`; `SpriteLine` deserializes to `Dots`.
- `Dot.prefab` / `NavigationObjectPool` marked `[Obsolete]`, removed in 4.0.0.
- README + CHANGELOG update; bump to 3.2.0.

## 10. Decisions (2026-08-13)

1. **Orthogonal axes** — world X/Z, with per-segment fallback to the shortest path when a snapped leg leaves the navmesh. ✅ decided
2. **Selector placement** — inspector toggle-list drawers **plus** runtime public API (properties/methods) so project-side UI can switch mode/style. ✅ decided
3. **Pulse look** — single traveling pulse or wave-train: still open, pick from the mockup.
4. **Consume behavior** — shader fade behind the player (`_PlayerDist`), replacing hard sprite removal. ✅ decided

## 11. Implementation status (2026-08-13)

1. ✅ `NavigationPathProcessor` + edit-mode tests (incl. zero-alloc constraint) — compiled via Unity Roslyn, tests not yet run (editor lock).
2. ✅ Shaders: hand-written HLSL (not Shader Graph) sharing `NavigationMarker.hlsl` — URP high-confidence; **HDRP variant needs in-Unity verification** (un-tagged pass rendered as SRPDefaultUnlit in the transparent queue; fallback: rebuild as HDRP Unlit Shader Graph wrapping the shared HLSL via Custom Function).
3. ✅ `NavigationPathRenderer` (plain class, instanced draw + target ring RenderMesh).
4. ✅ `NavigationTooltip` rewired with visibility state machine (fade/arrival wipe); defaults: Orthogonal, radius 1.5, Arrows, spacing 0.75, Single pulse. Pool/enum obsoleted, `FormerlySerializedAs` migration.
5. ✅ `[EnumToolbar]` toggle-list drawer.
6. ✅ README + version 3.2.0.
7. ⏳ In-Unity: run edit-mode tests, verify HDRP shader, assign `NavigationMarker_*` material on the scene's NavigationTooltip, visual pass.
