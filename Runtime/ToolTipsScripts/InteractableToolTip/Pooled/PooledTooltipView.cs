using System;
using LitMotion;
using TMPro;
using UnityEngine;

namespace jeanf.tooltip
{
    /// <summary>
    /// A single pooled tooltip rendered as ONE quad using the UVS/RoundedRectTooltip SDF shader, plus a
    /// 3D <see cref="TextMeshPro"/> and a sprite icon (no Canvas anywhere). It morphs between a minimized
    /// state (small, fully rounded "disc") and an expanded state (wide pill that fits the text), animating
    /// the quad's size + per-corner radius via LitMotion + a MaterialPropertyBlock — so many instances
    /// share one material and batch.
    ///
    /// Hierarchy expected (text/icon are siblings of the quad so they don't inherit its animated scale):
    ///   PooledTooltip (this)
    ///     ├─ Background  (MeshRenderer quad + RoundedRectTooltip material)   -> background / backgroundTransform
    ///     ├─ Text        (3D TextMeshPro)                                    -> descriptionText
    ///     └─ Icon        (SpriteRenderer)                                    -> iconRenderer
    /// </summary>
    public class PooledTooltipView : MonoBehaviour
    {
        [Header("Renderers")]
        [SerializeField] private Renderer background;            // quad with the RoundedRectTooltip material
        [SerializeField] private Transform backgroundTransform; // the quad transform (scaled to the box size)
        [SerializeField] private TMP_Text descriptionText;      // 3D TextMeshPro
        [SerializeField] private SpriteRenderer iconRenderer;

        [Header("Minimized state")]
        [SerializeField] private Vector2 minSize = new Vector2(0.4f, 0.4f);
        [SerializeField] private Vector4 minRadius = new Vector4(0.2f, 0.2f, 0.2f, 0.2f); // TR,BR,TL,BL

        [Header("Expanded state")]
        [Tooltip("Default side for the icon (right when on, left when off). The controller can override this per shown tooltip.")]
        [SerializeField] private bool iconOnRight = true;
        [SerializeField] private float expandedHeight = 0.4f;
        [Tooltip("Font size for the expanded text (TMP units). 0 = keep the size authored on the TextMeshPro component. Lower this if the text looks too big for a short pill.")]
        [SerializeField, Min(0f)] private float fontSize = 0f;
        [Tooltip("Radius of the icon-side corners when expanded (kept round). ~half the height = a full pill end.")]
        [SerializeField] private float expandedRoundedRadius = 0.2f;
        [Tooltip("Radius of the opposite (non-icon-side) corners when expanded (flattened).")]
        [SerializeField] private float expandedFlatRadius = 0.04f;
        [SerializeField] private float horizontalPadding = 0.1f;
        [Tooltip("Horizontal space reserved for the icon at the anchored end (layout).")]
        [SerializeField] private float iconWidth = 0.4f;
        [Tooltip("Rendered world size of the icon's larger dimension — kept constant regardless of the source sprite's resolution / pixels-per-unit.")]
        [SerializeField] private float iconSize = 0.25f;
        [SerializeField] private Color color = new Color(1f, 0.6588f, 0f, 1f);
        [Tooltip("Tint applied to BOTH the text and the icon (white = untinted). Its alpha is multiplied by the fade-in.")]
        [SerializeField] private Color contentColor = Color.white;

        [Header("Click")]
        [Tooltip("Depth (Z) of the auto-sized click collider. A non-trigger BoxCollider is added automatically and sized to the current pill/disc each frame, so every tooltip is clickable; the controller's On Click Channel decides what the click does.")]
        [SerializeField] private float colliderDepth = 0.05f;
        private BoxCollider clickCollider; // auto-added in Awake — clicking is always wired

        [Tooltip("On click, the background flashes toward this colour then fades back (visual 'click!' feedback).")]
        [SerializeField] private Color clickFlashColor = Color.white;
        [Tooltip("How long the click colour-flash takes to fade back (seconds). 0 = no flash.")]
        [SerializeField, Min(0f)] private float clickFlashDuration = 0.12f;

        [Header("Animation")]
        [SerializeField, Min(0f)] private float animationDuration = 0.2f;
        [Tooltip("Seconds to glide to a new spot when the tooltip moves between positions (editor click or the runtime distance/angle picker). 0 = snap.")]
        [SerializeField, Min(0f)] private float positionLerpDuration = 0.25f;

        [Header("Editor preview (no play-mode effect)")]
        [SerializeField] private bool previewExpandedInEditor = false;
#if UNITY_EDITOR
        [Tooltip("Optional: a sample action-content SO whose icon/text is shown in the editor preview.")]
        [SerializeField] private ToolTipActionContentSo previewContentSo;
        [Tooltip("Which control scheme (play mode) to visualise in the editor preview.")]
        [SerializeField] private jeanf.universalplayer.BroadcastControlsStatus.ControlScheme previewMode
            = jeanf.universalplayer.BroadcastControlsStatus.ControlScheme.KeyboardMouse;
#endif

        public bool InUse { get; private set; }
        public Transform T => transform;
        public bool IsExpanded => _expanded;

        // Per-tooltip billboard override pushed by the controller that owns this checkout: null = follow
        // the pool manager's global setting, true/false = force on/off for this tooltip only. Reset on
        // Release so a recycled view never inherits the previous owner's preference.
        private bool? _billboardOverride;
        public void SetBillboardOverride(bool? enabled) => _billboardOverride = enabled;
        public bool ShouldBillboard(bool managerDefault) => _billboardOverride ?? managerDefault;

        private bool _expanded;
        private bool _iconOnRight = true;
        private float _morph;          // 0 = minimized, 1 = expanded (current)
        private float _targetMorph;    // the value we're animating toward
        private float _expandedWidth;
        private MaterialPropertyBlock _mpb;
        private MotionHandle _motion;
        private MotionHandle _posMotion;
        // Cached tween delegates: a method-group/closure passed to Bind/WithOnComplete allocates a new delegate
        // each call, and tweens (re)start on every reposition — which fires repeatedly as the player moves.
        private System.Action<float> _applyMorph;
        private System.Action<Vector3> _applyPos;
        private System.Action _onMoveTravel;
        private System.Action _onMoveExpand;
        private System.Action<float> _applyFlash;
        private MotionHandle _flashMotion;
        private float _flashAmount;          // 0 = base colour, 1 = full clickFlashColor
        private float _curWidth, _curHeight; // last dims applied (so the flash can re-tint without a full morph)
        private Vector4 _curRadius;
        private bool _hasPosition;     // first placement snaps; later moves can lerp
        private bool _pendingIconSide; // side to apply at the round trough of a move
        private Vector3 _moveTarget;   // destination of a collapse->travel->expand reposition

        private static readonly int SizeId = Shader.PropertyToID("_Size");
        private static readonly int RadiusId = Shader.PropertyToID("_Radius");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        // Click routing: the recycled view forwards a click to whoever currently owns it. The controller sets
        // this to its RaiseClick when it checks the view out, and Release() clears it. Scene click detectors
        // (M&K interactable hook / VR XRSimpleInteractable.SelectEntered) call Click() on this component.
        private Action _onClick;

        private void Awake()
        {
            _iconOnRight = iconOnRight;
            // Clicking is not optional: ensure a BoxCollider exists for click detectors to hit. Auto-sized to
            // the pill/disc in ApplyMorph. It's a TRIGGER so the tooltip never physically collides with / blocks
            // scene objects — clicks still register because OnMouseDown and raycasts hit triggers while
            // Physics.queriesHitTriggers is true (the default). No Rigidbody is needed for click detection.
            clickCollider = GetComponent<BoxCollider>();
            if (clickCollider == null) clickCollider = gameObject.AddComponent<BoxCollider>();
            clickCollider.isTrigger = true;
            // Include all layers in the query/contact filter (the override defaults to Nothing) so a click
            // raycast finds this collider regardless of the layer collision matrix.
            clickCollider.includeLayers = ~0;
            clickCollider.excludeLayers = 0;

            // Cache tween delegates once (see fields) so reposition tweens don't allocate.
            _applyMorph = ApplyMorph;
            _applyPos = pos => transform.position = pos;
            _onMoveTravel = MoveTravel;
            _onMoveExpand = MoveExpand;
            _applyFlash = SetFlash;

            ToPoolState();
        }

        #region Public API (called by the controller / pool)

        /// <summary>Route this view's clicks to the owning tooltip (set on checkout, cleared on Release).</summary>
        public void SetClickHandler(Action handler) => _onClick = handler;

        /// <summary>Wire your scene click detectors to this — the built-in <see cref="OnMouseDown"/> (M&amp;K) and a
        /// VR <c>XRSimpleInteractable</c>'s Select Entered both call it. Forwards to the currently-owning
        /// tooltip's onClickChannel; no-op while pooled/unowned. De-dupes per frame so multiple detectors firing
        /// together count as a single click.</summary>
        private int _lastClickFrame = -1;
        public void Click()
        {
            if (Time.frameCount == _lastClickFrame) return; // OnMouseDown + XR Select can both fire this frame
            _lastClickFrame = Time.frameCount;
            _onClick?.Invoke();
        }

        // Built-in mouse click (Active Input Handling = Both / Input Manager) for M&K / editor. VR drives Click()
        // from an XRSimpleInteractable's Select Entered instead.
        private void OnMouseDown() => Click();

        /// <summary>Brief click feedback: flash the background toward clickFlashColor, fading back over
        /// clickFlashDuration. Returns the flash duration so the controller can sequence its collapse to begin
        /// AFTER the flash (flash, then shrink — not both at once).</summary>
        public float FlashClick()
        {
            if (background == null) return 0f;
            CancelFlash();
            if (!Application.isPlaying || clickFlashDuration <= 0f) { _flashAmount = 0f; return 0f; }
            _flashAmount = 1f;
            _flashMotion = LMotion.Create(1f, 0f, clickFlashDuration).WithEase(Ease.OutQuad).Bind(_applyFlash);
            return clickFlashDuration;
        }

        private void SetFlash(float amount)
        {
            _flashAmount = amount;
            ApplyBackgroundProps(_curWidth, _curHeight, _curRadius); // re-tint with the current dims
        }

        private void CancelFlash()
        {
            try { if (_flashMotion.IsActive()) _flashMotion.Cancel(); }
            catch (Exception) { /* default/!active handle */ }
        }

        public void ShowMinimized(Sprite spriteOverride = null)
        {
            InUse = true;
            _expanded = false;
            if (background != null) background.enabled = true;
            gameObject.SetActive(true);
            AnimateTo(0f);
        }

        public void ShowExpanded(string description, Sprite icon, bool iconRight)
        {
            InUse = true;
            _expanded = true;
            _iconOnRight = iconRight;

            // Activate FIRST so TextMeshPro can build the mesh — textBounds is invalid while inactive.
            gameObject.SetActive(true);
            if (background != null) background.enabled = true;

            // Re-enable the content renderers: the occlusion pass disables them while minimized
            // (SetMinimizedVisible ties enabled to _expanded), and nothing turned them back on when expanding.
            // Visibility during the morph is driven by alpha (ApplyContentColor), not enabled.
            if (descriptionText != null)
            {
                descriptionText.enabled = true;
                EnsureSingleLine();
                descriptionText.text = description;
                descriptionText.ForceMeshUpdate();
            }
            if (iconRenderer != null)
            {
                iconRenderer.enabled = true;
                if (icon != null) iconRenderer.sprite = icon;
            }

            _expandedWidth = ComputeExpandedWidth();
            AnimateTo(1f);
        }

        public void SetIcon(Sprite icon)
        {
            if (icon != null && iconRenderer != null) iconRenderer.sprite = icon;
        }

        /// <summary>Set the icon side directly (no morph). Use while minimized so the right side is ready
        /// for the next expand; an expanded reposition should use <see cref="MoveExpandedTo"/> instead.</summary>
        public void SetIconSide(bool iconRight)
        {
            if (iconRight == _iconOnRight) return;
            _iconOnRight = iconRight;
            if (_expanded) { _expandedWidth = ComputeExpandedWidth(); ApplyMorph(_morph); }
        }

        /// <summary>Reposition an expanded tooltip with a sequenced morph: collapse to the round disc in
        /// place, travel as a disc to <paramref name="worldPosition"/> (swapping the icon side while round),
        /// then re-expand. Snaps when not animating. The minimized disc should use <see cref="SetPosition"/>.</summary>
        public void MoveExpandedTo(Vector3 worldPosition, bool iconRight, bool animate)
        {
            bool canAnimate = animate && Application.isPlaying && _expanded
                              && (animationDuration > 0f || positionLerpDuration > 0f);

            if (!canAnimate)
            {
                CancelMotion();
                CancelPosMotion();
                _iconOnRight = iconRight;
                transform.position = worldPosition;
                _hasPosition = true;
                if (_expanded) { _expandedWidth = ComputeExpandedWidth(); ApplyMorph(1f); }
                return;
            }

            _pendingIconSide = iconRight;
            _moveTarget = worldPosition;

            // Leg 1: collapse to the round disc in place. MoveTravel/MoveExpand continue the sequence.
            CancelMotion();
            CancelPosMotion();
            _targetMorph = 0f;
            _motion = LMotion.Create(_morph, 0f, animationDuration)
                .WithEase(Ease.InCubic)
                .WithOnComplete(_onMoveTravel)
                .Bind(_applyMorph);
        }

        private void MoveTravel()
        {
            _iconOnRight = _pendingIconSide;          // swap the rounded end while it's a disc
            _expandedWidth = ComputeExpandedWidth();

            Vector3 from = transform.position;
            if (positionLerpDuration <= 0f || (from - _moveTarget).sqrMagnitude < 1e-8f)
            {
                transform.position = _moveTarget;
                _hasPosition = true;
                MoveExpand();
                return;
            }

            // Leg 2: travel as a disc to the destination.
            _posMotion = LMotion.Create(from, _moveTarget, positionLerpDuration)
                .WithEase(Ease.OutCubic)
                .WithOnComplete(_onMoveExpand)
                .Bind(_applyPos);
        }

        private void MoveExpand()
        {
            transform.position = _moveTarget;
            _hasPosition = true;

            // Leg 3: re-expand at the destination on the (possibly new) side.
            _targetMorph = 1f;
            _motion = LMotion.Create(0f, 1f, animationDuration)
                .WithEase(Ease.OutCubic)
                .Bind(_applyMorph);
        }

        /// <summary>Update text/icon/side of an already-expanded tooltip (e.g. control scheme changed),
        /// re-fitting the width without replaying the open animation.</summary>
        public void UpdateExpandedContent(string description, Sprite icon, bool iconRight)
        {
            if (!_expanded) return;
            _iconOnRight = iconRight;

            if (descriptionText != null)
            {
                EnsureSingleLine();
                descriptionText.text = description;
                descriptionText.ForceMeshUpdate();
            }
            if (icon != null && iconRenderer != null) iconRenderer.sprite = icon;

            _expandedWidth = ComputeExpandedWidth();
            ApplyMorph(1f);
        }

        /// <summary>Hide/show the background (used by the manager for occlusion of the minimized disc).</summary>
        public void SetMinimizedVisible(bool visible)
        {
            if (background != null) background.enabled = visible;
            if (descriptionText != null) descriptionText.enabled = visible && _expanded;
            if (iconRenderer != null) iconRenderer.enabled = visible && _expanded;
        }

        /// <summary>Move the tooltip. <paramref name="animate"/> glides there over
        /// <c>positionLerpDuration</c>; the first placement always snaps.</summary>
        public void SetPosition(Vector3 worldPosition, bool animate = false)
        {
            if (!animate || !Application.isPlaying || positionLerpDuration <= 0f || !_hasPosition)
            {
                CancelPosMotion();
                transform.position = worldPosition;
                _hasPosition = true;
                return;
            }

            Vector3 from = transform.position;
            if ((from - worldPosition).sqrMagnitude < 1e-8f) return; // already there

            CancelPosMotion();
            _posMotion = LMotion.Create(from, worldPosition, positionLerpDuration)
                .WithEase(Ease.OutCubic)
                .Bind(_applyPos);
        }

        public void Release()
        {
            InUse = false;
            _billboardOverride = null;
            _onClick = null; // drop the previous owner's click route before recycling
            transform.rotation = Quaternion.identity; // so a recycled non-billboarding view doesn't keep a stale facing
            CancelMotion();
            CancelPosMotion();
            CancelFlash();
            _flashAmount = 0f;
            _hasPosition = false;
            _morph = 0f;
            _targetMorph = 0f;
            ToPoolState();
        }

        #endregion

        #region Morph

        private void AnimateTo(float target)
        {
            bool targetChanged = !Mathf.Approximately(_targetMorph, target);
            _targetMorph = target;

            if (!Application.isPlaying || animationDuration <= 0f)
            {
                CancelMotion();
                ApplyMorph(target);
                return;
            }

            if (targetChanged)
            {
                // New target -> (re)start the tween from the current morph.
                CancelMotion();
                _motion = LMotion.Create(_morph, target, animationDuration)
                    .WithEase(Ease.OutCubic)
                    .Bind(_applyMorph);
            }
            else if (!IsMotionActive())
            {
                // Same target, already settled -> just re-apply so content/width edits show (no restart).
                ApplyMorph(_morph);
            }
            // Same target, still tweening -> let it finish (restarting it every frame was the bug).
        }

        private bool IsMotionActive()
        {
            try { return _motion.IsActive(); }
            catch (Exception) { return false; }
        }

        private void ApplyMorph(float t)
        {
            _morph = t;

            float width = Mathf.Lerp(minSize.x, _expandedWidth > 0f ? _expandedWidth : minSize.x, t);
            float height = Mathf.Lerp(minSize.y, expandedHeight, t);
            Vector4 radius = Vector4.Lerp(minRadius, ExpandedRadius(), t);

            // Keep the icon-side rounded end anchored at the root (where the minimized circle sits) and
            // grow the box AWAY from it, so the expansion is directional rather than centered.
            float side = _iconOnRight ? 1f : -1f; // +1 = icon/anchor right (grows left), -1 = anchor left (grows right)
            float boxCenterX = -side * (width - height) * 0.5f;

            if (backgroundTransform != null)
            {
                backgroundTransform.localScale = new Vector3(width, height, 1f);
                backgroundTransform.localPosition = new Vector3(boxCenterX, 0f, 0f);
            }

            // Keep the click collider matching the visible box (root-local: background is a unit quad scaled here).
            if (clickCollider != null)
            {
                clickCollider.size = new Vector3(width, height, colliderDepth);
                clickCollider.center = new Vector3(boxCenterX, 0f, 0f);
            }

            ApplyBackgroundProps(width, height, radius);

            // Text + icon fade/appear over the second half of the morph (tinted by contentColor).
            float contentAlpha = Mathf.InverseLerp(0.55f, 1f, t);
            ApplyContentColor(contentAlpha);
            LayoutContent(width, height, side);
        }

        private void ApplyBackgroundProps(float width, float height, Vector4 radius)
        {
            if (background == null) return;
            _curWidth = width; _curHeight = height; _curRadius = radius; // remembered so FlashClick can re-tint
            _mpb ??= new MaterialPropertyBlock();
            background.GetPropertyBlock(_mpb);
            _mpb.SetVector(SizeId, new Vector4(width, height, 0f, 0f));
            _mpb.SetVector(RadiusId, radius);
            _mpb.SetColor(ColorId, _flashAmount > 0f ? Color.Lerp(color, clickFlashColor, _flashAmount) : color);
            background.SetPropertyBlock(_mpb);
        }

        private void ApplyContentColor(float fade)
        {
            if (descriptionText != null)
            {
                descriptionText.color = contentColor;            // RGB + base alpha
                descriptionText.alpha = contentColor.a * fade;   // fade multiplier on top
            }
            if (iconRenderer != null)
            {
                Color c = contentColor;
                c.a = contentColor.a * fade;
                iconRenderer.color = c;
            }
        }

        // Icon sits at the anchored (circle) end = root origin; text is centered in the area that grows
        // away from it. Children of THIS root (not the scaled quad), so their height stays constant — we
        // only POSITION them, never resize the text rect (that fed back into the width).
        private void LayoutContent(float width, float height, float side)
        {
            if (iconRenderer != null)
            {
                NormalizeIconScale(iconRenderer.sprite);
                iconRenderer.transform.localPosition = new Vector3(0f, 0f, -0.001f);
            }

            if (descriptionText == null) return;

            // Span the text rect across the whole pill and reserve the icon end with a TMP margin, then let
            // TMP centre the text in what's left. A zero-width rect made "Center" degenerate (TMP left-anchors
            // it), which left-shifted long labels even when the width was right — margins + a real width fix it.
            var rt = descriptionText.rectTransform;
            rt.sizeDelta = new Vector2(width, rt.sizeDelta.y);
            float boxCenterX = -side * (width - height) * 0.5f;
            rt.localPosition = new Vector3(boxCenterX, 0f, -0.001f);

            // margin = (left, top, right, bottom). Reserve icon + padding on the icon side, padding on the other.
            float iconReserve = iconWidth + horizontalPadding;
            descriptionText.margin = side > 0f
                ? new Vector4(horizontalPadding, 0f, iconReserve, 0f)  // icon right
                : new Vector4(iconReserve, 0f, horizontalPadding, 0f); // icon left
            descriptionText.alignment = TextAlignmentOptions.Center;
        }

        // Keep the icon a fixed world size regardless of the source sprite's resolution / pixels-per-unit.
        private void NormalizeIconScale(Sprite sprite)
        {
            if (iconRenderer == null || sprite == null) return;
            Vector2 size = sprite.bounds.size; // world units = sprite pixels / PPU
            float largest = Mathf.Max(size.x, size.y);
            if (largest <= 0.0001f) return;
            float s = iconSize / largest;
            iconRenderer.transform.localScale = new Vector3(s, s, s);
        }

        // Per-corner radius for the expanded pill: the icon side stays rounded, the opposite side flattens.
        // Shader _Radius order is (TR, BR, TL, BL).
        private Vector4 ExpandedRadius()
        {
            float r = expandedRoundedRadius; // icon-side corners
            float f = expandedFlatRadius;    // opposite corners
            return _iconOnRight
                ? new Vector4(r, r, f, f)    // icon right -> round right corners (TR, BR)
                : new Vector4(f, f, r, r);   // icon left  -> round left corners (TL, BL)
        }

        private float ComputeExpandedWidth()
        {
            if (descriptionText == null || string.IsNullOrEmpty(descriptionText.text))
                return horizontalPadding * 2f + iconWidth;

            EnsureSingleLine();

            // Measure textBounds against a generous rect so single-line text is never clamped (a small/zero
            // rect was what under-sized the longest label). textBounds is the tight rendered-glyph extent, so
            // the pill hugs the text — GetPreferredValues over-reports (trailing advance) and would leave a
            // symmetric gap once centred, so it's only a fallback. LayoutContent restores the real width.
            var rt = descriptionText.rectTransform;
            float prevRectWidth = rt.sizeDelta.x;
            rt.sizeDelta = new Vector2(10000f, rt.sizeDelta.y);
            descriptionText.ForceMeshUpdate();

            float localWidth = descriptionText.textBounds.size.x;
            if (localWidth <= 0f)
                localWidth = descriptionText.GetPreferredValues(descriptionText.text).x;

            rt.sizeDelta = new Vector2(prevRectWidth, rt.sizeDelta.y);

            // Convert that into THIS view's (root) units — robust to the text's scale & nesting,
            // since the box width / padding / iconWidth are all expressed in root units.
            Vector3 rootVec = transform.InverseTransformVector(
                descriptionText.transform.TransformVector(new Vector3(localWidth, 0f, 0f)));
            float textWidth = Mathf.Max(0f, rootVec.magnitude);

            return horizontalPadding * 2f + iconWidth + textWidth;
        }

        // Keep the label on one line so it never overflows the fixed pill height; the pill grows to fit.
        private void EnsureSingleLine()
        {
            if (descriptionText == null) return;
            descriptionText.textWrappingMode = TextWrappingModes.NoWrap;
            descriptionText.overflowMode = TextOverflowModes.Overflow;
            descriptionText.alignment = TextAlignmentOptions.Center; // we position the text by its centre

            // Drive the font size from the inspector so it stays in proportion when the pill height changes.
            // 0 = leave the size authored on the TMP component. Applied here (the single pre-content/pre-measure
            // chokepoint) so play mode and the editor preview's width measurement both use it.
            if (fontSize > 0f)
            {
                descriptionText.enableAutoSizing = false;
                descriptionText.fontSize = fontSize;
            }
        }

        private void CancelMotion()
        {
            try
            {
                if (_motion.IsActive()) _motion.Cancel();
            }
            catch (Exception)
            {
                // default/!active handle — nothing to cancel
            }
        }

        private void CancelPosMotion()
        {
            try
            {
                if (_posMotion.IsActive()) _posMotion.Cancel();
            }
            catch (Exception)
            {
                // default/!active handle — nothing to cancel
            }
        }

        private void ToPoolState()
        {
            gameObject.SetActive(false);
        }

        #endregion

#if UNITY_EDITOR
        [NonSerialized] private bool _editorPreviewInitialized;
        [NonSerialized] private bool _editorUpdating;
        [NonSerialized] private bool _editorBillboard = true; // preview faces the scene camera unless told not to

        /// <summary>Editor preview: whether to billboard toward the Scene-view camera (mirrors the runtime
        /// billboard decision so "Never" looks the same while authoring).</summary>
        public void SetEditorBillboard(bool enabled) => _editorBillboard = enabled;

        [NonSerialized] private double _editorAnimStart;
        [NonSerialized] private float _editorAnimFrom;
        [NonSerialized] private float _editorAnimTarget = -1f; // -1 = uninitialised

        // Editor preview move sequence: collapse in place (1) -> travel as a disc (2) -> expand (3).
        [NonSerialized] private int _editorMovePhase;       // 0 idle, 1 collapse, 2 travel, 3 expand
        [NonSerialized] private double _editorMoveStart;
        [NonSerialized] private float _editorMoveFromMorph;
        [NonSerialized] private Vector3 _editorMoveFromPos;
        [NonSerialized] private Vector3 _editorMoveTarget;
        [NonSerialized] private bool _editorMoveNewSide;
        [NonSerialized] private bool _editorMoveMinimized; // glide the disc only (travel leg, no collapse/expand)

        /// <summary>Editor-preview reposition. When <paramref name="animate"/>, plays the same sequenced
        /// morph as runtime — collapse in place, travel as a disc (swapping the icon side while round),
        /// then re-expand. Snaps otherwise (fresh build / handle drag).</summary>
        public void MovePreviewTo(Vector3 worldPosition, bool iconRight, bool animate)
        {
            if (!animate || (animationDuration <= 0f && positionLerpDuration <= 0f))
            {
                _editorMovePhase = 0;
                _iconOnRight = iconRight;
                transform.position = worldPosition;
                return;
            }

            _editorMoveFromPos = transform.position;
            _editorMoveFromMorph = _morph;
            _editorMoveTarget = worldPosition;
            _editorMoveNewSide = iconRight;
            _editorMoveMinimized = false;
            _editorMovePhase = 1;
            _editorMoveStart = UnityEditor.EditorApplication.timeSinceStartup;
            EnsureEditorUpdate();
        }

        /// <summary>Editor-preview reposition while MINIMIZED: glide the disc to <paramref name="worldPosition"/>
        /// (travel leg only — no collapse/expand) so switching candidates slides instead of popping.</summary>
        public void GlidePreviewTo(Vector3 worldPosition, bool iconRight, bool animate)
        {
            _iconOnRight = iconRight; // disc has no visible side, but ready it for the next expand

            if (!animate || positionLerpDuration <= 0f || (transform.position - worldPosition).sqrMagnitude < 1e-8f)
            {
                if (_editorMovePhase == 0) transform.position = worldPosition; // don't fight an in-flight glide
                return;
            }

            _editorMoveFromPos = transform.position;
            _editorMoveTarget = worldPosition;
            _editorMoveNewSide = iconRight;
            _editorMoveMinimized = true;
            _editorMovePhase = 2; // travel only
            _editorMoveStart = UnityEditor.EditorApplication.timeSinceStartup;
            EnsureEditorUpdate();
        }

        /// <summary>Keep the preview pinned to <paramref name="worldPosition"/> without animating (handle
        /// drag / per-repaint sync). Ignored while a move sequence is in flight so it doesn't fight it.</summary>
        public void SyncPreviewPos(Vector3 worldPosition)
        {
            if (_editorMovePhase != 0) return;
            transform.position = worldPosition;
        }

        private void OnValidate()
        {
            if (Application.isPlaying) return;
            UnityEditor.EditorApplication.delayCall += BeginEditorPreview;
        }

        private void OnDisable() => StopEditorUpdate();

        private void BeginEditorPreview()
        {
            if (this == null || Application.isPlaying) return;

            if (!_editorPreviewInitialized)
            {
                // Snap to the current toggle on first load (no animation).
                _editorPreviewInitialized = true;
                float t = previewExpandedInEditor ? 1f : 0f;
                _morph = t;
                _editorAnimTarget = t;
                _iconOnRight = iconOnRight;
                _expanded = t > 0f;
                if (_expanded)
                {
                    ApplyPreviewContentFromSo();
                    _expandedWidth = ComputeExpandedWidth();
                }
                ApplyMorph(t);
            }

            EnsureEditorUpdate();
        }

        private void EnsureEditorUpdate()
        {
            if (_editorUpdating) return;
            _editorUpdating = true;
            UnityEditor.EditorApplication.update += EditorTick;
        }

        private void StopEditorUpdate()
        {
            if (!_editorUpdating) return;
            _editorUpdating = false;
            UnityEditor.EditorApplication.update -= EditorTick;
        }

        /// <summary>Editor preview: drive the expanded/minimized toggle live (e.g. an Auto state computed from
        /// the scene-camera proxy). EditorTick eases toward it.</summary>
        public void SetEditorExpanded(bool expanded)
        {
            if (previewExpandedInEditor == expanded) return;
            previewExpandedInEditor = expanded;
            EnsureEditorUpdate();
        }

        // Editor-only: show the chosen mode's icon/text from a sample SO so designers can visualise each
        // play mode. No effect at runtime — the controller supplies the real per-scheme content there.
        private void ApplyPreviewContentFromSo()
        {
            if (previewContentSo == null) return;
            string text = previewContentSo.GetText(previewMode);
            Sprite icon = previewContentSo.GetIcon(previewMode);
            if (descriptionText != null && !string.IsNullOrEmpty(text)) descriptionText.text = text;
            if (iconRenderer != null && icon != null) iconRenderer.sprite = icon;
        }

        // Drives the editor preview: eases the morph toward the toggle and, while expanded, re-measures
        // the width every tick so editing the text (or any field) updates the pill live.
        private void EditorTick()
        {
            if (this == null || Application.isPlaying)
            {
                StopEditorUpdate();
                return;
            }

            float target = previewExpandedInEditor ? 1f : 0f;
            double now = UnityEditor.EditorApplication.timeSinceStartup;

            float morph;
            if (_editorMovePhase != 0)
            {
                morph = TickEditorMove(now); // collapse -> travel -> expand; manages position + side
                _expanded = morph > 0.001f;  // a disc while travelling (incl. a minimized glide); content only when grown
            }
            else
            {
                // Expand/collapse toggle (the view's own inspector preview). Restart the ease from the
                // current morph when the toggle changes.
                if (!Mathf.Approximately(_editorAnimTarget, target))
                {
                    _editorAnimFrom = _morph;
                    _editorAnimStart = now;
                    _editorAnimTarget = target;
                }

                if (animationDuration > 0f)
                {
                    float p = Mathf.Clamp01((float)((now - _editorAnimStart) / animationDuration));
                    float eased = 1f - Mathf.Pow(1f - p, 3f); // OutCubic
                    morph = Mathf.Lerp(_editorAnimFrom, target, eased);
                }
                else morph = target;

                _iconOnRight = iconOnRight;
                _expanded = morph > 0.001f || target > 0f;
            }

            if (_expanded)
            {
                ApplyPreviewContentFromSo();           // show the chosen mode's icon/text
                _expandedWidth = ComputeExpandedWidth(); // live re-measure
            }

            ApplyMorph(morph);

            // Billboard toward the Scene-view camera, like the runtime pool does at play time — unless this
            // tooltip's billboard mode is off (so "Never" looks fixed while authoring, matching play mode).
            if (_editorBillboard)
            {
                var sv = UnityEditor.SceneView.lastActiveSceneView;
                if (sv != null && sv.camera != null)
                {
                    Vector3 dir = transform.position - sv.camera.transform.position;
                    if (dir.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
                }
            }
            else
            {
                // "Never": match runtime, where an un-billboarded pooled view sits at identity world rotation.
                transform.rotation = Quaternion.identity;
            }

            UnityEditor.SceneView.RepaintAll();

            // Keep ticking while expanded / mid-move; stop once fully minimized and idle.
            if (target <= 0f && morph <= 0.0001f && _editorMovePhase == 0)
                StopEditorUpdate();
        }

        // Drives the 3-leg editor move: collapse in place -> travel as a disc (side swaps while round) ->
        // re-expand at the target. Returns the morph for this tick and updates transform.position.
        private float TickEditorMove(double now)
        {
            float mdur = animationDuration > 0f ? animationDuration : 0.0001f;
            float pdur = positionLerpDuration > 0f ? positionLerpDuration : 0.0001f;

            if (_editorMovePhase == 1) // collapse current -> 0 in place (keep old side)
            {
                transform.position = _editorMoveFromPos;
                float p = Mathf.Clamp01((float)((now - _editorMoveStart) / mdur));
                float m = Mathf.Lerp(_editorMoveFromMorph, 0f, p);
                if (p >= 1f)
                {
                    _iconOnRight = _editorMoveNewSide; // swap the rounded end while it's a disc
                    _editorMovePhase = 2;
                    _editorMoveStart = now;
                    m = 0f;
                }
                return m;
            }

            if (_editorMovePhase == 2) // travel as a disc (morph stays 0)
            {
                _iconOnRight = _editorMoveNewSide;
                // A minimized glide ends here (back to idle); a full reposition continues to the expand leg.
                int nextPhase = _editorMoveMinimized ? 0 : 3;
                if (positionLerpDuration <= 0f || (_editorMoveFromPos - _editorMoveTarget).sqrMagnitude < 1e-8f)
                {
                    transform.position = _editorMoveTarget;
                    _editorMovePhase = nextPhase;
                    _editorMoveStart = now;
                    _editorMoveMinimized = false;
                    return 0f;
                }
                float p = Mathf.Clamp01((float)((now - _editorMoveStart) / pdur));
                float eased = 1f - Mathf.Pow(1f - p, 3f); // OutCubic
                transform.position = Vector3.Lerp(_editorMoveFromPos, _editorMoveTarget, eased);
                if (p >= 1f)
                {
                    transform.position = _editorMoveTarget;
                    _editorMovePhase = nextPhase;
                    _editorMoveStart = now;
                    _editorMoveMinimized = false;
                }
                return 0f;
            }

            // phase 3: expand 0 -> 1 at the target on the new side
            _iconOnRight = _editorMoveNewSide;
            transform.position = _editorMoveTarget;
            float pe = Mathf.Clamp01((float)((now - _editorMoveStart) / mdur));
            float me = 1f - Mathf.Pow(1f - pe, 3f); // OutCubic
            if (pe >= 1f)
            {
                _editorMovePhase = 0;
                _editorAnimFrom = 1f;
                _editorAnimTarget = 1f;
                me = 1f;
            }
            return me;
        }
#endif
    }
}
