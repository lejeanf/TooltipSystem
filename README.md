# Tooltip System

`fr.jeanf.tooltipsystem` — in-world player hints for Unity (VR-ready).

The headline feature is a **Canvas-free, pooled tooltip renderer**: each tooltip is a single SDF quad (plus a 3D `TextMeshPro` label and a sprite icon) that morphs between a small **minimized disc** and an expanded **pill**. Visibility is driven by **zone + proximity + gaze**, content adapts to the active **control scheme** (M&K / Gamepad / VR), and tooltips can **reposition** to face the player and be **clicked** to raise an action.

---

## Installation

Add the scoped registry in `Project Settings → Package Manager`:

- Click **+** under *Scoped Registries*
- **Name:** `jeanf`
- **URL:** `https://registry.npmjs.com`
- **Scope:** `fr.jeanf`

Then install **Tooltip System** from the Package Manager (*My Registries*). Import the **Setup** sample for ready-made Settings SOs and icons.

Requires **Unity 2022.3+** (uses `Collider.includeLayers`) and **LitMotion** for the pooled animations.

---

## Quick start — pooled in-world tooltips

### 1. Scene setup (once per scene)

Add these to the scene:

| Component | Purpose |
|---|---|
| **TooltipPoolManager** | Prewarms and recycles the pooled views. Set its **View Prefab** to the `PooledTooltip` prefab (use the inspector's *Find & assign* button — the ⊙ picker only lists scene objects). |
| **TooltipGazeArbiter** | The gaze **arbiter** — ensures only the tooltip you look at most directly maximizes. No fields; it just needs to exist. Without it, no tooltip will maximize. |
| **TooltipControlSchemeManager** | Routes control-scheme changes and iPad interruptions. |

### 2. Per-tooltip setup

Put an **InteractableTooltipController** on (or near) the thing to annotate and set:

- **Interactable Tool Tip Settings So** — shared settings (gaze threshold, etc.).
- **Action Content So** — per-control-scheme icon + text for this action (the easy path; falls back to the legacy glyph SOs only if unset).
- **Object To Be Viewed** — what the player must look at to maximize the tooltip.
- **Current Zone** — the `jeanf.scenemanagement` zone the tooltip shows in.
- **Show Distance** — how close the player must be for the tooltip to appear at all (beyond it, hidden).

The per-tooltip inspector has three tabs — **Content** (object to view, zone, action content, gaze settings, click event), **In-world** (icon side, billboarding, rendering, repositioning, per-position overrides, scene preview, candidate positions) and **Debug** (live gate state) — with `isDebug` pinned above them. Rendering is always pooled.

That's the minimum. Look at the object from inside the zone and within Show Distance → the tooltip maximizes.

---

## Behavior model

| Player state | Tooltip |
|---|---|
| Not in the tooltip's zone | hidden |
| In zone, beyond **Show Distance** | hidden |
| In zone, within range, **not** looking | **minimized disc** |
| In zone, within range, **looking** (gaze cone) + arbiter permission | **maximized pill** |

"Looking" is an **angle** test (`dot(cameraForward, dirToTarget) > fieldOfViewThreshold`), independent of distance.

---

## Repositioning (optional)

Give the tooltip a list of **candidate positions** (scene transforms). For quick setup, set a **Count** + **Radius** and hit **Generate** to spawn `TooltipAnchor` children spread evenly around the root (Fibonacci distribution), or **Distribute existing evenly** to re-spread the ones you have — then add/remove/reorder any in the list. The best one is chosen by the **player's position**, not their gaze:

```
score = facing + distanceWeight · 1/(1 + distance)
facing = dot( dir(object → camera), dir(object → candidate) )   // which side faces the player
```

So walking around the object moves the tooltip to the side facing you; turning your head does not. A `TooltipAnchor` can override the icon side, billboard on/off, **and the billboard limits** per position. `Reposition Hysteresis` keeps it from flip-flopping between near-equal spots.

---

## Billboard limits (optional)

By default a billboarding tooltip faces the camera freely. You can constrain that per **axis** — measured from a **rest** orientation (the tooltip's, or the candidate position's, authored facing):

- **Yaw** (horizontal), **Pitch** (vertical), **Roll** (lean to match camera tilt) — each can be **free**, **locked**, or **clamped** to a degree range.
- Each clamp has a **centre** (move the band anywhere, even across ±180°) and a **soft ease** so the motion glides to a stop instead of hitting a wall.

Edit it in the inspector or directly in the **Scene view**: axis-coloured arcs (X/Y/Z = red/green/blue) with a draggable centre handle (rotates the band) and end handles (set min/max). Hold **Alt** while dragging an end to mirror it onto the opposite side. Limits are set on the tooltip itself when it has **no candidate positions** ("self"); once it repositions across candidates, each position owns its own limits via its `TooltipAnchor` (turn on *Override billboard limits*).

---

## Orienting a non-billboard tooltip

Set **Billboard Mode → Never** to pin a tooltip to a fixed facing instead of turning to the camera. Author that facing by rotating the tooltip's transform (or the candidate position): the **Scene view shows a rotation handle + a forward arrow** for the non-billboard case (billboarding tooltips show the axis-limit arcs above instead). At runtime the pooled view holds that authored rotation. Turn on the **debug panel** (on the `TooltipPoolManager`) to also see a forward **arrow in the Game view** — for every active non-billboard tooltip — with the Game-view *Gizmos* toggle enabled.

---

## Sizing the pooled tooltip

On the `PooledTooltip` prefab (`PooledTooltipView`) two sliders set the overall size in each state — **Minimized Scale** (the disc) and **Expanded Scale** (the pill) — each a uniform multiplier over the granular per-shape sizes tucked under **Advanced**. One slider each is usually all you need for real-world tuning.

---

## Clicking

Every pooled tooltip is **clickable out of the box** — the view auto-adds a trigger `BoxCollider` (sized to the pill/disc, never collides with scene objects, no Rigidbody needed).

- **Mouse / editor:** the view's built-in `OnMouseDown`.
- **VR:** wire an `XRSimpleInteractable`'s **Select Entered → `PooledTooltipView.Click()`** on the `PooledTooltip` prefab.

A click invokes the controller's **On Click** UnityEvent — wire the game-side interaction to it in the inspector (or subscribe to the controller's `Clicked` C# event from code). Clicking only invokes that event — it does **not** change minimize/maximize (that stays gaze-driven). `Click()` de-dupes per frame, so multiple detectors are safe.

### Click feedback

On click the tooltip **flashes** (`Click Flash Color` over `Click Flash Duration`), **then** collapses to the disc for `Click Minimize Duration`, then re-grows if you're still looking — a clear, sequenced acknowledgment.

---

## Editor tooling

The `InteractableTooltipController` inspector includes:

- A **scene preview** of the pooled tooltip at any candidate position (no pool manager required to preview), with editable Show Distance handle and range/gaze gizmos.
- A **Force show** (play-mode, editor-only) toggle to display the tooltip regardless of gates while testing.
- A **Debug** tab showing live gate state and candidate scoring in Play mode. (The `TooltipPoolManager`'s debug toggle drives the in-world facing arrow gizmo.)

---

## Key components

- **InteractableTooltipController** — per-tooltip brain (visibility gates, content, repositioning, click).
- **PooledTooltipView** — the recycled visual (quad + 3D text + icon; morph, billboard, collider, flash).
- **TooltipPoolManager** — pool + central billboard/occlusion driver.
- **TooltipGazeArbiter** — gaze permission arbiter.
- **TooltipActionContentSo** — per-control-scheme icon/text for one action.

The package also contains other tooltip families (Help, Navigation, Far/legacy canvas tooltips) used elsewhere in the game.

---

## What's new in 3.0.0

- ⚠ **Breaking:** the click event is now an **On Click UnityEvent** on the controller (plus a `Clicked` C# event) instead of a `StringEventChannelSO` + string message. Re-wire any click listeners in the inspector. `TooltipClickRelay` changed the same way.
- ⚠ **Breaking:** rendering is always pooled (the `Use Pooled Rendering` field is removed), *Minimized Range* / `MinimizedRange` is renamed **Show Distance** / `ShowDistance` (serialized value preserved), and the `OnClickChannel` / `ClickMessage` public API is gone.
- **Tabbed inspector** (Content / In-world / Debug), a compact per-mode Action Content drawer with an icon preview, candidate positions shown in the viewport when deselected, and candidate **Generate on a sphere** quick-setup.
- **Two size sliders** on the pooled prefab — *Minimized Scale* / *Expanded Scale* — with every granular sizing/animation/wiring field moved under an **Advanced** foldout.
- **Non-billboard tooltips can be oriented**: they hold their authored rotation at runtime, with a Scene rotation handle + forward arrow to author it and a Game-view debug arrow (debug panel on) to see it.
- **Tabbed, contextual inspector**: the controller splits into **Content** / **In-world** / **Debug** tabs (`isDebug` pinned above); billboard limits hidden unless billboarding; repositioning knobs hidden until enabled. Rendering is always pooled (the *Use Pooled Rendering* toggle and the legacy-references section are gone), *Minimized Range* is renamed **Show Distance**, and candidate positions can be **generated evenly on a sphere**. The pool manager shows just *View Prefab* + *Capacity* (default **10**), everything else under **Advanced**.
- **Rendering**: double-sided background (URP + HDRP) so the back shows the background, not the text; crisper SDF corners.
- Range/gaze **Scene gizmos now render under URP** too (explicit `Handles.zTest`).

---

## What's new in 1.7.0

- New **Canvas-free pooled renderer** (`PooledTooltipView` + `TooltipPoolManager`): minimized-disc ↔ pill morph, central billboarding, occlusion.
- **Per-control-scheme content** via `TooltipActionContentSo`.
- **Position-based repositioning** across candidate anchors (gaze-independent) with per-anchor overrides.
- **Per-axis constrained billboarding** — limit **yaw / pitch / roll** independently (free, locked, or clamped to a degree range with a movable **centre** and a soft-ease approach to the limit). Set it on the tooltip (self) or **per candidate position**, with full scene-view tooling: axis-coloured arcs (X/Y/Z = red/green/blue), draggable centre + end handles, and **Alt to mirror** min/max.
- **Per-render-pipeline prefab variant** hook on the pool manager (one click makes a linked URP/HDRP `PooledTooltip` variant with the matching material).
- **Built-in clicking** (auto collider + `OnMouseDown` / XR) raising an SO event, with **flash → minimize → re-grow** feedback.
- **Editor preview, force-show, and live debug panel.**
- Allocation-free steady-state runtime path (cached delegates, reused MaterialPropertyBlock).
- Inspector cleanup: removed several confusing/optional fields (e.g. description/icon/minimized-sprite overrides, bypass-permission, the permanent toggle); some previously-serialized values are dropped on upgrade.

---

## Contributors

- [Code] Felix Cotes-Charlebois — <https://github.com/Percevent13>

## License

<img src="https://licensebuttons.net/l/by-nc-sa/3.0/88x31.png"></img>

CC BY-NC-SA 3.0
