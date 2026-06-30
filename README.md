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
| **ToolTipPoolManager** | Prewarms and recycles the pooled views. Set its **View Prefab** to the `PooledTooltip` prefab (use the inspector's *Find & assign* button — the ⊙ picker only lists scene objects). |
| **InteractableToolTipManager** | The gaze **arbiter** — ensures only the tooltip you look at most directly maximizes. No fields; it just needs to exist. Without it, no tooltip will maximize. |
| **ToolTipManager** | Routes control-scheme changes and iPad interruptions. |

### 2. Per-tooltip setup

Put an **InteractableToolTipController** on (or near) the thing to annotate and set:

- **Interactable Tool Tip Settings So** — shared settings (gaze threshold, etc.).
- **Action Content So** — per-control-scheme icon + text for this action (the easy path; falls back to the legacy glyph SOs only if unset).
- **Object To Be Viewed** — what the player must look at to maximize the tooltip.
- **Current Zone** — the `jeanf.scenemanagement` zone the tooltip shows in.
- **Use Pooled Rendering** — on.
- **Minimized Range** — distance within which the minimized disc appears.

That's the minimum. Look at the object from inside the zone and within range → the tooltip maximizes.

---

## Behavior model

| Player state | Tooltip |
|---|---|
| Not in the tooltip's zone | hidden |
| In zone, beyond **Minimized Range** | hidden |
| In zone, within range, **not** looking | **minimized disc** |
| In zone, within range, **looking** (gaze cone) + arbiter permission | **maximized pill** |

"Looking" is an **angle** test (`dot(cameraForward, dirToTarget) > fieldOfViewThreshold`), independent of distance.

---

## Repositioning (optional)

Give the tooltip a list of **candidate positions** (scene transforms; the inspector's *Add candidate position* creates `ToolTipAnchor` children). The best one is chosen by the **player's position**, not their gaze:

```
score = facing + distanceWeight · 1/(1 + distance)
facing = dot( dir(object → camera), dir(object → candidate) )   // which side faces the player
```

So walking around the object moves the tooltip to the side facing you; turning your head does not. A `ToolTipAnchor` can override the icon side and billboard per position. `Reposition Hysteresis` keeps it from flip-flopping between near-equal spots.

---

## Clicking

Every pooled tooltip is **clickable out of the box** — the view auto-adds a trigger `BoxCollider` (sized to the pill/disc, never collides with scene objects, no Rigidbody needed).

- **Mouse / editor:** the view's built-in `OnMouseDown`.
- **VR:** wire an `XRSimpleInteractable`'s **Select Entered → `PooledTooltipView.Click()`** on the `PooledTooltip` prefab.

A click raises the controller's **On Click Channel** (`StringEventChannelSO`) with **Click Message**; the game listens and performs the interaction. Clicking only raises that event — it does **not** change minimize/maximize (that stays gaze-driven). `Click()` de-dupes per frame, so multiple detectors are safe.

### Click feedback

On click the tooltip **flashes** (`Click Flash Color` over `Click Flash Duration`), **then** collapses to the disc for `Click Minimize Duration`, then re-grows if you're still looking — a clear, sequenced acknowledgment.

---

## Editor tooling

The `InteractableToolTipController` inspector includes:

- A **scene preview** of the pooled tooltip at any candidate position (no pool manager required to preview), with editable Minimized Range handle and range/gaze gizmos.
- A **Force show** (play-mode, editor-only) toggle to display the tooltip regardless of gates while testing.
- A **Tooltip state (debug)** panel (toggle it on the `ToolTipPoolManager`) showing live gate state and candidate scoring in Play mode.

---

## Key components

- **InteractableToolTipController** — per-tooltip brain (visibility gates, content, repositioning, click).
- **PooledTooltipView** — the recycled visual (quad + 3D text + icon; morph, billboard, collider, flash).
- **ToolTipPoolManager** — pool + central billboard/occlusion driver.
- **InteractableToolTipManager** — gaze permission arbiter.
- **ToolTipActionContentSo** — per-control-scheme icon/text for one action.

The package also contains other tooltip families (Help, Navigation, Far/legacy canvas tooltips) used elsewhere in the game.

---

## What's new in 1.8.0

- New **Canvas-free pooled renderer** (`PooledTooltipView` + `ToolTipPoolManager`): minimized-disc ↔ pill morph, central billboarding, occlusion.
- **Per-control-scheme content** via `ToolTipActionContentSo`.
- **Position-based repositioning** across candidate anchors (gaze-independent) with per-anchor overrides.
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
