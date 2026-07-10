using System.Collections.Generic;
using jeanf.EventSystem;
using jeanf.scenemanagement;
using jeanf.universalplayer;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace jeanf.tooltip
{
    /// <summary>How a single tooltip decides whether to billboard (face the camera each frame).</summary>
    public enum BillboardMode
    {
        UseManagerDefault, // pooled: follow TooltipPoolManager's global setting; legacy: off
        Always,            // always face the camera, ignoring the manager default
        Never              // never billboard, ignoring the manager default
    }

    public class InteractableTooltipController : Tooltip
    {
        [Header("Tooltip Settings")]
        [FormerlySerializedAs("interactableToolTipSettingsSo")]
        [SerializeField] private InteractableTooltipSettingsSo interactableTooltipSettingsSo;
        [Tooltip("Easiest setup: one SO with the icon + text per control scheme (M&K / Gamepad / VR) for this action. If set, it supplies the per-mode icon and text. Leave the legacy glyph SOs (below) empty when using this.")]
        [SerializeField] private TooltipActionContentSo actionContentSo;
        [Tooltip("The object the player must look at for this tooltip to maximize.")]
        [SerializeField] private GameObject objectToBeViewed;
        [Tooltip("The tooltip only shows while the player is in this zone.")]
        public Zone currentZone;

        // Permanent (default) = a zone/proximity/gaze-driven hint that persists. Punctual = one-shot,
        // event-driven (hidden once its action completes). Punctual is no longer an inspector option — it
        // confused setup and nothing serialized it on purpose. It stays reachable at runtime via
        // DisableTooltipVisibility() for code that needs it, and the TooltipControlSchemeManager iPad-interruption flow
        // still honors IsPermanentTooltip. NOT serialized, so any stale inspector value is ignored and every
        // tooltip starts permanent.
        private bool isPermanentTooltip = true;
        [Tooltip("Log this tooltip's state changes to the console.")]
        public bool isDebug = false;
        [Tooltip("Per-tooltip billboarding. UseManagerDefault: follow the TooltipPoolManager's global setting (pooled) / off (legacy). Always / Never: force this tooltip on or off regardless of the manager.")]
        [SerializeField] private BillboardMode billboardMode = BillboardMode.Never;
        [Tooltip("Per-axis limits on the billboarding: free/locked/clamped yaw, pitch and roll, measured from this tooltip's (or the chosen candidate's) authored facing. Defaults = the classic free, world-upright billboard.")]
        [SerializeField] private BillboardConstraints billboardConstraints = new BillboardConstraints();
        [Tooltip("Default side for the expanded tooltip's icon (right when on, left when off). A candidate position with a TooltipAnchor can override this.")]
        [SerializeField] private bool iconOnRight = true;

        [Header("Click event (raised when the tooltip is clicked)")]
        [Tooltip("SO event channel raised when this tooltip is clicked. Clicking always works — the pooled view auto-adds a collider — so this channel + message just define WHAT the click does; the game side listens and acts. Leave empty if the click should do nothing.")]
        [SerializeField] private StringEventChannelSO onClickChannel;
        [Tooltip("Message sent on the channel for THIS tooltip.")]
        [SerializeField] private string clickMessage = "";
        [Tooltip("On click, flash the tooltip and briefly collapse it to the minimized disc for this long (seconds), then re-grow if the player is still looking at it. 0 = no click-minimize.")]
        [SerializeField, Min(0f)] private float clickMinimizeDuration = 0.2f;

        /// <summary>
        /// The text to show: the per-mode text from the action-content SO for the current control scheme,
        /// else the shared SettingsSO description.
        /// </summary>
        public string EffectiveDescription
        {
            get
            {
                if (actionContentSo != null)
                {
                    string text = actionContentSo.GetText(_currentControlScheme);
                    if (!string.IsNullOrEmpty(text)) return text;
                }
                return interactableTooltipSettingsSo != null ? interactableTooltipSettingsSo.description : "";
            }
        }

        /// <summary>Raises this tooltip's per-instance click event. Wire your click detector to this.</summary>
        public void RaiseClick()
        {
            if (isDebug)
                Debug.Log($"[InteractableTooltip '{name}'] RaiseClick -> channel=" +
                          $"{(onClickChannel != null ? onClickChannel.name : "<none>")} message='{clickMessage}'", this);

            // Click feedback: flash the pill FIRST, then once the flash ends collapse to the disc for the
            // minimize window; the gate (UpdateTooltipVisibility) re-grows it afterward if still looked at.
            float flash = _view != null ? _view.FlashClick() : 0f;
            if (clickMinimizeDuration > 0f)
            {
                _clickMinimizeFrom = Time.time + flash; // collapse begins after the flash, not during it
                _clickMinimizeUntil = _clickMinimizeFrom + clickMinimizeDuration;
            }

            if (onClickChannel == null) return;
            onClickChannel.RaiseEvent(clickMessage);
        }

        /// <summary>The channel this tooltip raises on click — lets a listener link to it without re-wiring.</summary>
        public StringEventChannelSO OnClickChannel => onClickChannel;
        /// <summary>The object the player looks at to maximize this tooltip.</summary>
        public GameObject ObjectToBeViewed => objectToBeViewed;

        // Editor-only live-preview state + the prefab used to render the in-editor preview.
        [HideInInspector] [SerializeField] private bool livePreview;
        public bool LivePreview { get => livePreview; set => livePreview = value; }
        public GameObject PreviewPrefab => tooltipGameObjectPrefab;

        // Drawn in a collapsible "Repositioning (optional)" foldout by the custom inspector — no [Header] here
        // so PropertyField doesn't redraw it inside the foldout.
        [Tooltip("Move the tooltip to the best of several candidate positions for the player's current viewpoint.")]
        [SerializeField] private bool enableRepositioning = false;
        [Tooltip("Candidate world positions (scene Transforms). The best one for the player's angle/distance is used. Any number is supported.")]
        [SerializeField] private List<Transform> candidateAnchors = new List<Transform>();
        [Tooltip("Weight of the distance term relative to the player-facing term when scoring candidates. Candidate choice depends on the player's position (which side faces them), not where they look.")]
        [SerializeField] private float distanceWeight = 0.25f;
        [Tooltip("Optional: reject candidate positions occluded from the camera by geometry on this mask. If every candidate is blocked, the tooltip is hidden.")]
        [SerializeField] private bool rejectOccluded = false;
        [SerializeField] private LayerMask obstacleMask = 0;

        // Drawn at the very bottom in a collapsible "Legacy references" foldout by the custom inspector
        // (CustomInspectorInstanciateTooltip) — no [Header] here so it isn't redrawn above.
        [Tooltip("Legacy per-tooltip canvas prefab. Only used when Use Pooled Rendering is off.")]
        [SerializeField] private GameObject tooltipGameObjectPrefab;
        [Tooltip("Legacy glyph map. Only used when no Action Content SO is set above.")]
        [SerializeField] private InputIconSo inputIconSo;
        [Tooltip("Legacy per-scheme input-binding SO. Only used when no Action Content SO is set above.")]
        [FormerlySerializedAs("interactableToolTipInputSo")]
        [SerializeField] private InteractableTooltipInputSo interactableTooltipInputSo;

        [Header("Pooled rendering (optional, opt-in)")]
        [Tooltip("Render via TooltipPoolManager (sprite minimized + pooled expanded canvas) instead of instantiating a per-tooltip canvas. Requires a TooltipPoolManager in the scene; falls back to the legacy per-tooltip canvas if none is present. Keep the legacy tooltip prefab assigned above for that fallback.")]
        [SerializeField] private bool usePooledRendering = false;
        [Tooltip("Pooled mode only: distance within which the minimized indicator shows (replaces the FarTooltip range trigger, which isn't instantiated in pooled mode). Set this to your old far-trigger radius.")]
        [SerializeField] private float minimizedRange = 15f;

        //Delegates
        public delegate bool RequestShowTooltipDelegate(float playerDirectionDot, InteractableTooltipController interactableTooltipController);
        public static RequestShowTooltipDelegate RequestShowTooltip;

        public delegate void WarnHideTooltipDelegate(InteractableTooltipController interactableTooltipController);
        public static WarnHideTooltipDelegate WarnHideTooltip;

        //States
        private bool isPlayerInZone = false;
        private bool _isPlayerNear;
        private bool _isTooltipDisplayed;
        private float _clickMinimizeFrom;  // start collapsing at this time (after the click flash has played)
        private float _clickMinimizeUntil; // ...and hold the minimized disc until this time, then re-grow
        private bool _wasInterruptedByIpad;
        private bool _tooltipWasShowingBeforeIpad;
        private bool _ipadIsShowing = false;
        
        private InteractableTooltipService _interactableTooltipService;
        // Cached main-camera transform, resolved lazily through CameraTransform so it survives additive scene
        // loading (the real player camera can live in a scene that loads AFTER this tooltip's Awake) and camera
        // swaps. TooltipPoolManager re-acquires its camera the same way rather than caching once at Awake.
        private Transform _cameraTransform;
        private Transform CameraTransform
        {
            get
            {
                // Unity's overloaded == reports a destroyed camera (scene unloaded) as null too, so this
                // re-acquires after a camera swap, not just on the very first access.
                if (_cameraTransform == null)
                {
                    var cam = Camera.main;
                    _cameraTransform = cam != null ? cam.transform : null;
                }
                return _cameraTransform;
            }
        }
        private Image _image;
        private GameObject _tooltip;
        private InteractableTooltip _interactableTooltip;
        
        private float _playerLookingDirectionDot;

        private int _playerLayerMask;

        // Placement state
        private float _nextEvaluationTime;
        private Transform _currentAnchor;
        private bool _hasValidPlacement = true;

        // Pooled-rendering state
        private bool _pooled;
        private PooledTooltipView _view;
        // Which pooled visual is currently shown. The show methods drive the morph/position only on the
        // transition INTO a state; while it persists, EnsurePlacement owns repositioning (animated) so the
        // per-frame show calls don't snap over the lerp.
        private enum PooledShowState { Released, Minimized, Expanded }
        private PooledShowState _pooledShow = PooledShowState.Released;
        private Sprite _iconSprite;
        // Cached once so handing the click route to a pooled view doesn't allocate a delegate every frame.
        private System.Action _pooledClickHandler;
        // Last position pushed to the view in the non-repositioning per-frame follow, so a stationary tooltip
        // skips the redundant SetPosition (transform write + motion-cancel) every frame.
        private Vector3 _pooledFollowPos;
        private bool _hasPooledFollowPos;
        private BroadcastControlsStatus.ControlScheme _currentControlScheme = BroadcastControlsStatus.ControlScheme.KeyboardMouse;

        public bool IsTooltipDisplayed => _isTooltipDisplayed;
        public bool IsShowingTooltip => showTooltip;
        public bool IsPermanentTooltip => isPermanentTooltip;

        // Exposed for the editor preview (visualise the pooled tooltip at any candidate position).
        public TooltipActionContentSo ActionContentSo => actionContentSo;
        public bool IconOnRightDefault => iconOnRight;
        public BillboardMode BillboardModeDefault => billboardMode;

        // Exposed for the editor range/trigger visualisation (read-only mirrors of the runtime thresholds).
        public float MinimizedRange => minimizedRange;
        public bool EnableRepositioning => enableRepositioning;
        public float DistanceWeight => distanceWeight;
        // Shared across every tooltip via TooltipPoolManager (performance knobs, not per-tooltip appearance) —
        // falls back to the pre-centralization defaults when no manager is in the scene (e.g. legacy-only setups).
        public float EvaluationInterval => TooltipPoolManager.Instance != null ? TooltipPoolManager.Instance.EvaluationInterval : 0.2f;
        public float RepositionHysteresis => TooltipPoolManager.Instance != null ? TooltipPoolManager.Instance.RepositionHysteresis : 0.05f;
        public Transform LookTarget => objectToBeViewed != null ? objectToBeViewed.transform : transform;
        public float FieldOfViewThreshold => interactableTooltipSettingsSo != null ? interactableTooltipSettingsSo.fieldOfViewThreshold : 0.9855f;

#if UNITY_EDITOR
        // Live gate state for the editor debug panel (read-only, safe to poll). Looking is play-mode only
        // (needs the runtime camera + look target). Permission only reports whether the arbiter EXISTS — we
        // must NOT call RequestPermissionToShowTooltip from the editor (it mutates the manager's selection).
        public bool Dbg_Pooled => _pooled;
        public string Dbg_ShowState => _pooledShow.ToString();
        public bool Dbg_InZone => isPlayerInZone;
        public bool Dbg_Near => IsNearForShow();
        public bool Dbg_Looking => Application.isPlaying && CameraTransform != null && CheckIfPlayerIsLooking();
        public bool Dbg_Maximized => _isTooltipDisplayed;
        public bool Dbg_PermissionManagerPresent => RequestShowTooltip != null;
        public string Dbg_PlayerZone => WorldManager.CurrentPlayerZone != null ? WorldManager.CurrentPlayerZone.id.id : "-";
        public string Dbg_TargetZone => currentZone != null ? currentZone.id.id : "-";

        // Distance from the tooltip to the player's viewpoint (the main camera / head), which is what the
        // minimized-range proximity gate measures against. -1 when the camera isn't resolved yet.
        public float Dbg_DistanceToViewpoint
        {
            get
            {
                var cam = CameraTransform;
                return cam != null ? Vector3.Distance(transform.position, cam.position) : -1f;
            }
        }

        // Editor-only "force show" testing override (Preview block). Not serialized — toggle it in play mode
        // to force the tooltip maximized regardless of zone / proximity / looking / permission. Compiled out
        // of player builds, so it can never affect a shipped game.
        [System.NonSerialized] public bool EditorForceShow;

        public bool Dbg_Repositioning => RepositioningActive;
        public int Dbg_CandidateCount => candidateAnchors != null ? candidateAnchors.Count : 0;

        public struct DbgCandidate
        {
            public string label;   // anchor name (or index when unassigned)
            public bool scored;     // a usable score (not null / coincident / occluded)
            public bool occluded;   // rejected by the occlusion test
            public bool selected;   // the anchor currently in use (_currentAnchor)
            public float facing;    // player-facing term (-1..1)
            public float dist;      // camera→anchor distance (m)
            public float score;     // final score incl. hysteresis bias when selected
        }

        // Live candidate scoring using the actual runtime camera — mirrors EvaluateBestAnchor exactly (shared
        // TryScoreAnchor). The highest `score` is the pick; the selected anchor carries a +RepositionHysteresis
        // (TooltipPoolManager) bias which is why it can stay chosen over a marginally higher rival.
        public List<DbgCandidate> Dbg_Candidates()
        {
            var result = new List<DbgCandidate>();
            if (candidateAnchors == null) return result;

            Vector3 camPos = _cameraTransform != null ? _cameraTransform.position : transform.position;
            Transform center = LookTarget;
            Vector3 centerPos = center != null ? center.position : transform.position;
            Vector3 dirCenterToCam = camPos - centerPos;
            if (dirCenterToCam.sqrMagnitude > 1e-6f) dirCenterToCam.Normalize();

            for (int i = 0; i < candidateAnchors.Count; i++)
            {
                var anchor = candidateAnchors[i];
                var c = new DbgCandidate
                {
                    label = anchor != null ? anchor.name : $"Position {i} (unassigned)",
                    selected = anchor != null && anchor == _currentAnchor
                };

                if (TryScoreAnchor(anchor, camPos, centerPos, dirCenterToCam,
                        out float facing, out float dist, out float score, out bool occluded))
                {
                    if (c.selected) score += RepositionHysteresis;
                    c.scored = true;
                    c.facing = facing;
                    c.dist = dist;
                    c.score = score;
                }
                else
                {
                    c.occluded = occluded;
                    c.dist = dist;
                }
                result.Add(c);
            }
            return result;
        }
#endif

        private float validationTime = 0.75f;
        
        private string timerName;
        private bool enableCountDown = false;
        
        private FunctionTimer _activeTimer;
        
        #region Unity Lifecycle
        
        private void Awake()
        {
            timerName = $"TooltipTimer_{GetInstanceID()}";
            
            if (isDebug)
                Debug.Log($"[InteractableTooltip] Created with timer name: {timerName}", this);
            
            _playerLayerMask = LayerMask.NameToLayer("Player");
            // Warm the cache but don't hard-require Camera.main here: under additive scene loading the player
            // camera may not exist yet at Awake. CameraTransform re-acquires it lazily, so a null now is fine.
            _ = CameraTransform;

            if (!ValidateComponents())
            {
                gameObject.SetActive(false);
                return;
            }
            
            InitializeTooltip();
        }

        private void OnEnable() => Subscribe();
        private void OnDisable() => UnSubscribe();
        private void OnDestroy()
        {
            StopActiveTimer();
            UnSubscribe();
        }
        
        private void Update()
        {
            if (isDebug) LogGateState();

#if UNITY_EDITOR
            // Editor-only testing override: force the tooltip maximized, skipping every gate.
            if (EditorForceShow) { ShowTooltip(); return; }
#endif

            if (isPermanentTooltip)
                HandlePermanentTooltipUpdate();
            else
                HandlePunctualTooltipUpdate();
        }

        // isDebug diagnostic: logs the show-gate chain whenever it changes so you can see exactly which gate
        // is blocking (the tooltip needs every one true to maximize). Pooled tooltips also need a pool with a
        // view prefab assigned. Order: showTooltip (punctual) -> inZone -> near -> looking + permission.
        private string _lastGateLog;
        private void LogGateState()
        {
            string s = $"pooled={_pooled} pool={(TooltipPoolManager.Instance != null)} " +
                       $"permanent={isPermanentTooltip} showTooltip={showTooltip} inZone={isPlayerInZone} " +
                       $"near={IsNearForShow()} (trigger={_isPlayerNear} inRange={IsWithinMinimizedRange()}) " +
                       $"looking={CheckIfPlayerIsLooking()} permMgr={(RequestShowTooltip != null)}";
            if (s == _lastGateLog) return;
            _lastGateLog = s;
            Debug.Log($"[InteractableTooltip '{name}'] gates: {s}", this);
        }

        private bool _warnedNoPool;
        private void WarnNoPoolOnce()
        {
            if (_warnedNoPool) return;
            _warnedNoPool = true;
            Debug.LogWarning($"[InteractableTooltip '{name}'] Pooled rendering is on but no active TooltipPoolManager " +
                             "was found (or its View Prefab is unassigned, so the pool is empty). Nothing will render.", this);
        }

        private void LateUpdate()
        {
            // In pooled mode the TooltipPoolManager billboards all active views centrally (per-view, honoring
            // this controller's BillboardMode via SetBillboardOverride at show time).
            if (_pooled) return;

            // Legacy path has no manager, so UseManagerDefault means off; only Always billboards.
            if (billboardMode == BillboardMode.Always && _isTooltipDisplayed)
                ApplyBillboard();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (IsPlayerCollider(other))
                _isPlayerNear = true;
        }

        private void OnTriggerExit(Collider other)
        {
            if (IsPlayerCollider(other))
            {
                _isPlayerNear = false;
                StopActiveTimer();
            }
        }

        // Legacy (non-pooled) near-trigger only: the player's collider is identified by the layer mask
        // configured on TooltipPoolManager when one is set, else by this project's "Player" layer name or the
        // built-in "Player" tag (so a project that only sets one of them still detects). Pooled tooltips never
        // get here — their proximity is a distance test to the camera (IsWithinMinimizedRange).
        private bool IsPlayerCollider(Collider other)
        {
            var pool = TooltipPoolManager.Instance;
            if (pool != null && pool.PlayerLayerMask.value != 0)
                return (pool.PlayerLayerMask.value & (1 << other.gameObject.layer)) != 0;

            return other.gameObject.layer == _playerLayerMask || other.CompareTag("Player");
        }
        
        #endregion

        #region Initialization
        
        // Pooled mode NEVER hard-fails here: everything it reads has a runtime fallback (default gaze
        // threshold, description from the settings SO or empty, icon optional), so a partially configured
        // tooltip renders what it can. It used to SetActive(false) the whole GameObject instead, which read
        // as "something is disabling all the tooltips" with no clue why — and since Awake never runs twice,
        // re-enabling the object in the hierarchy could not revive it. Legacy (non-pooled) rendering is
        // unusable without its prefab + animation, so that still disables — but says exactly what's missing.
        private bool ValidateComponents()
        {
            // Content source: the per-mode action SO, OR the legacy glyph-map + input SO.
            bool hasContent = actionContentSo != null ||
                              (inputIconSo != null && interactableTooltipInputSo != null);

            if (usePooledRendering)
            {
                if (interactableTooltipSettingsSo == null)
                    Debug.LogWarning($"[InteractableTooltip '{name}'] No Settings SO assigned — using the default gaze threshold and an empty description.", this);
                if (!hasContent)
                    Debug.LogWarning($"[InteractableTooltip '{name}'] No content source (Action Content SO, or Input Icon SO + Input SO) — the expanded tooltip will have no icon.", this);
                return true;
            }

            if (interactableTooltipSettingsSo == null || !hasContent ||
                tooltipGameObjectPrefab == null || interactableTooltipSettingsSo.animationSo == null)
            {
                Debug.LogError($"[InteractableTooltip '{name}'] Legacy (non-pooled) rendering is missing required references — disabling this tooltip. " +
                               $"Settings SO: {(interactableTooltipSettingsSo != null ? "ok" : "MISSING")}, " +
                               $"content source (Action Content SO or Input Icon SO + Input SO): {(hasContent ? "ok" : "MISSING")}, " +
                               $"tooltip prefab: {(tooltipGameObjectPrefab != null ? "ok" : "MISSING")}, " +
                               $"animation SO: {(interactableTooltipSettingsSo != null && interactableTooltipSettingsSo.animationSo != null ? "ok" : "MISSING")}.", this);
                return false;
            }

            return true;
        }

        private void InitializeTooltip()
        {
            // Decide pooled vs legacy from the user's choice ALONE — NOT from TooltipPoolManager.Instance,
            // which may not be set yet at Awake (script execution order). The pool is resolved lazily at show
            // time (ShowExpandedPooled / ShowMinimizedPooled already no-op when it's null), so a not-yet-ready
            // manager must never push us down the legacy path and SetActive(false) on a missing legacy prefab.
            _pooled = usePooledRendering;
            if (_pooled)
            {
                InitializePooled();
                return;
            }

            if (tooltipGameObjectPrefab == null)
            {
                if (isDebug) Debug.LogWarning("[InteractableTooltip] Pooled rendering requested but no TooltipPoolManager in scene and no legacy prefab assigned.", this);
                gameObject.SetActive(false);
                return;
            }

            _tooltip = Instantiate(tooltipGameObjectPrefab, transform, false);
            if (_tooltip == null)
            {
                gameObject.SetActive(false);
                return;
            }
            
            _tooltip.name = interactableTooltipSettingsSo.tooltipName;
            _interactableTooltip = _tooltip.GetComponent<InteractableTooltip>();
            
            if (_interactableTooltip == null)
            {
                gameObject.SetActive(false);
                return;
            }
            
            _interactableTooltip.ArrangeRotation();
            _interactableTooltip.UpdateDescription(EffectiveDescription);
            
            CacheImageComponent();
            
            _interactableTooltipService = new InteractableTooltipService(_interactableTooltip, interactableTooltipSettingsSo);
            SetIcon();

            ResetTooltipTransform();
        }

        private void CacheImageComponent()
        {
            var images = _tooltip.GetComponentsInChildren<Image>();
            _image = images.Length > 2 ? images[2] : (images.Length > 0 ? images[0] : null);
        }

        private void ResetTooltipTransform()
        {
            _tooltip.transform.localPosition = Vector3.zero;
            _tooltip.transform.localRotation = Quaternion.identity;
        }

        private void SetIcon()
        {
            if (_image != null)
                _image.sprite = EffectiveIcon(BroadcastControlsStatus.ControlScheme.KeyboardMouse);
        }
        
        #endregion
        
        #region Public API for Manager
        
        public bool HasIncompleteTooltip()
        {
            return _wasInterruptedByIpad && 
                   _tooltipWasShowingBeforeIpad && 
                   _isPlayerNear && 
                   isPlayerInZone && 
                   CheckIfPlayerIsLooking();
        }
        
        public void CheckAndUpdateTooltipVisibility()
        {
            //Method for future extension if necessary
        }

        public void EnableTooltipVisibility()
        {
            UpdateIsShowingTooltip(true);
            isPermanentTooltip = true;
        }
        
        public void DisableTooltipVisibility()
        {
            UpdateIsShowingTooltip(false);
            isPermanentTooltip = false;
        }
        
        public void ResumeTooltipAfterInterruption()
        {
            if (isPermanentTooltip || !HasIncompleteTooltip()) 
                return;
            
            showTooltip = true;
            ResetInterruptionState();
        }
        
        public void NotifyIpadHidden()
        {
            if (isPermanentTooltip)
                _ipadIsShowing = false;
        }
        
        #endregion

        #region Tooltip Update Logic
        
        private void HandlePermanentTooltipUpdate()
        {
            if (_ipadIsShowing || !isPlayerInZone) 
            { 
                HideTooltipWithoutAnimation();
                StopActiveTimer();
                return; 
            }
            
            if (!IsNearForShow())
            {
                NotifyAndHideTooltip();
                return;
            }
            
            UpdateTooltipVisibility();
        }
        
        private void HandlePunctualTooltipUpdate()
        {
            if (!showTooltip)
            {
                HandleInterruptionByIpad();
                HideTooltipWithoutAnimation();
                StopActiveTimer();
                return;
            }
            
            ResetInterruptionStateIfNeeded();
            
            if (!isPlayerInZone) 
            { 
                HideTooltipWithoutAnimation();
                StopActiveTimer();
                return; 
            }
            
            if (!IsNearForShow())
            {
                NotifyAndHideTooltip();
                return;
            }
            
            UpdateTooltipVisibility();
        }

        private void UpdateTooltipVisibility()
        {
            // Just clicked: let the flash play first (stays expanded), THEN collapse to the disc for the
            // window, then fall through to re-grow if still looked at.
            if (Time.time >= _clickMinimizeFrom && Time.time < _clickMinimizeUntil)
            {
                NotifyAndHideTooltip();
                return;
            }

            bool isLooking = CheckIfPlayerIsLooking();
            bool hasPermission = RequestPermissionToShowTooltip();
            // When repositioning is on, a valid (non-occluded) candidate must exist or the tooltip is filtered out.
            bool placementOk = !enableRepositioning || EnsurePlacement();

            if (isLooking && hasPermission && placementOk)
            {
                ShowTooltip();
                
                StopActiveTimer();
                enableCountDown = false;
                
            }
            else
            {
                // Not looking (or no permission/placement): show the minimized disc immediately on first
                // appearance rather than waiting out the validation timer. If it's currently maximized, leave
                // the timer to collapse it (gaze hysteresis). ShowMinimized is idempotent (AnimateTo no-ops
                // when already settled), so re-calling it each frame while minimized is safe.
                if (_pooled && (_view == null || !_view.IsExpanded)) ShowMinimizedPooled();

                if (!enableCountDown)
                {
                    enableCountDown = true;
                    
                    _activeTimer = FunctionTimer.Create(
                        delegate
                        {
                            if (isDebug)
                                Debug.Log($"[InteractableTooltip] Timer {timerName} completed - Hiding tooltip", this);
                            
                            NotifyAndHideTooltip();
                            enableCountDown = false;
                            _activeTimer = null;
                        }, 
                        validationTime, 
                        timerName
                    );
                    
                    if (isDebug)
                        Debug.Log($"[InteractableTooltip] Player not looking - Created timer {timerName} for {validationTime}s", this);
                }
            }
        }

        private void StopActiveTimer()
        {
            if (_activeTimer != null && _activeTimer.IsActive)
            {
                if (isDebug)
                    Debug.Log($"[InteractableTooltip] Manually stopping timer {timerName}", this);
                
                FunctionTimer.StopTimer(timerName);
                _activeTimer = null;
            }
            enableCountDown = false;
        }

        private void NotifyAndHideTooltip()
        {
            if (_isTooltipDisplayed)
                WarnHideTooltip?.Invoke(this);
            
            HideTooltip();
            StopActiveTimer();
        }

        private void HandleInterruptionByIpad()
        {
            if (!_wasInterruptedByIpad && _isTooltipDisplayed)
            {
                _wasInterruptedByIpad = true;
                _tooltipWasShowingBeforeIpad = true;
            }
        }

        private void ResetInterruptionStateIfNeeded()
        {
            if (_wasInterruptedByIpad && _tooltipWasShowingBeforeIpad)
                ResetInterruptionState();
        }

        private void ResetInterruptionState()
        {
            _wasInterruptedByIpad = false;
            _tooltipWasShowingBeforeIpad = false;
        }
        
        #endregion

        #region Tooltip Display Methods
        
        public void ShowTooltip()
        {
            if (_pooled) { ShowExpandedPooled(); return; }
            if (_interactableTooltip == null) return;

            _isTooltipDisplayed = true;
            _interactableTooltip.ShowCloseTooltip();
            _interactableTooltipService?.ShowIcons();
            _interactableTooltip.HideFarTooltip();
        }

        public void HideTooltip()
        {
            if (_pooled) { ShowMinimizedPooled(); return; }
            if (_interactableTooltip == null) return;

            _isTooltipDisplayed = false;
            _interactableTooltipService?.HideIcons();

            if (showTooltip)
                _interactableTooltip.ShowFarTooltip();
            else
                _interactableTooltip.HideFarTooltip();
        }

        private void HideTooltipWithoutAnimation()
        {
            if (_pooled)
            {
                _isTooltipDisplayed = false;
                ReleasePooledView();
                return;
            }
            if (_interactableTooltip == null) return;

            _isTooltipDisplayed = false;
            _interactableTooltip.HideCloseTooltip();
            _interactableTooltip.HideFarTooltip();
        }

        #endregion

        #region Player Detection
        
        private bool CheckIfPlayerIsLooking()
        {
            var cam = CameraTransform;
            if (cam == null) return false;

            // LookTarget falls back to this transform when objectToBeViewed is unassigned, so this can't NRE
            // on a tooltip that hasn't had a look target wired up (as the shipped example prefab ships).
            var directionToObject = (LookTarget.position - cam.position).normalized;
            _playerLookingDirectionDot = Vector3.Dot(cam.forward, directionToObject);

            // FieldOfViewThreshold (not the SO directly): falls back to the default threshold when no
            // settings SO is assigned, which pooled validation now allows.
            return _playerLookingDirectionDot > FieldOfViewThreshold;
        }
        
        private bool RequestPermissionToShowTooltip()
        {
            // No subscriber == no TooltipGazeArbiter in the scene. That's a misconfiguration (every
            // tooltip would be denied and never maximize), not the normal "another tooltip is more centered"
            // denial — so surface it once instead of failing silently.
            if (RequestShowTooltip == null)
            {
                WarnNoPermissionManagerOnce();
                return false;
            }

            return RequestShowTooltip(_playerLookingDirectionDot, this);
        }

        private bool _warnedNoPermissionManager;
        private void WarnNoPermissionManagerOnce()
        {
            if (_warnedNoPermissionManager) return;
            _warnedNoPermissionManager = true;
            Debug.LogWarning($"[InteractableTooltip '{name}'] No TooltipGazeArbiter in the scene, so the " +
                             "gaze-permission gate denies every tooltip (none will maximize). Add an " +
                             "TooltipGazeArbiter component to the scene.", this);
        }
        
        #endregion

        #region Event Subscription
        
        private void Subscribe()
        {
            TooltipControlSchemeManager.UpdateShowTooltip += UpdateIsShowingTooltip;
            TooltipControlSchemeManager.UpdateTooltipControlSchemeWithHmd += UpdateControlSchemeWithHmd;
            TooltipControlSchemeManager.UpdateTooltipControlScheme += UpdateControlScheme;
            TooltipControlSchemeManager.DisableTooltip += DisableTooltip;
            WorldManager.PublishCurrentZoneId += CheckIfPlayerInZone;
            WorldManager.InitComplete += OnWorldInitComplete;

            // PublishCurrentZoneId only fires on a zone CHANGE. If this tooltip is enabled while the player
            // is already inside its zone (the common case), it would never hear the broadcast and stay
            // hidden — so seed from the zone the player is currently in.
            SeedZoneFromWorld();
        }
        
        private void UnSubscribe()
        {
            StopActiveTimer();
            
            TooltipControlSchemeManager.UpdateShowTooltip -= UpdateIsShowingTooltip;
            TooltipControlSchemeManager.UpdateTooltipControlSchemeWithHmd -= UpdateControlSchemeWithHmd;
            TooltipControlSchemeManager.UpdateTooltipControlScheme -= UpdateControlScheme;
            TooltipControlSchemeManager.DisableTooltip -= DisableTooltip;
            WorldManager.PublishCurrentZoneId -= CheckIfPlayerInZone;
            WorldManager.InitComplete -= OnWorldInitComplete;

            _interactableTooltipService?.Destroy();
            ResetInterruptionState();

            ReleasePooledView();
        }

        private new void UpdateIsShowingTooltip(bool isShowing)
        {
            var wasIpadShowing = _ipadIsShowing;
            _ipadIsShowing = !isShowing;
            
            if (isPermanentTooltip)
            {
                if (_ipadIsShowing && !wasIpadShowing)
                {
                    HideTooltipWithoutAnimation();
                    StopActiveTimer();
                }
            }
            else
            {
                showTooltip = isShowing;
            }
        }

        private void UpdateControlScheme(BroadcastControlsStatus.ControlScheme controlScheme)
            => RefreshForScheme(controlScheme);

        private void UpdateControlSchemeWithHmd(bool hmdStatus)
            => RefreshForScheme(hmdStatus
                ? BroadcastControlsStatus.ControlScheme.XR
                : BroadcastControlsStatus.ControlScheme.KeyboardMouse);

        // Re-resolve icon + text for the active control scheme and push them to whatever is showing.
        private void RefreshForScheme(BroadcastControlsStatus.ControlScheme scheme)
        {
            _currentControlScheme = scheme;
            _iconSprite = EffectiveIcon(scheme);

            if (_pooled)
            {
                if (_view != null && _view.IsExpanded)
                    _view.UpdateExpandedContent(EffectiveDescription, _iconSprite, EffectiveIconSide());
                return;
            }

            if (_image != null) _image.sprite = _iconSprite;
            _interactableTooltip?.UpdateDescription(EffectiveDescription);
        }

        private void CheckIfPlayerInZone(string zoneId)
        {
            if (currentZone == null) return;

            isPlayerInZone = (zoneId == currentZone.id.id);
        }

        // Sync zone membership from the world's authoritative current zone (no-op when it isn't known yet).
        private void SeedZoneFromWorld()
        {
            var playerZone = WorldManager.CurrentPlayerZone;
            if (playerZone != null) CheckIfPlayerInZone(playerZone.id);
        }

        // Under additive loading a tooltip can come online AFTER the world's initial PublishCurrentZoneId has
        // already fired, so it never hears which zone the player is in and stays hidden. Re-seed once the world
        // signals it's initialized. Combined with the seed in Subscribe, this covers both load orders.
        private void OnWorldInitComplete(bool status) => SeedZoneFromWorld();

        #endregion

        #region Placement (Repositioning + Billboard)

        /// <summary>
        /// Throttled placement evaluation. Returns whether a usable position exists.
        /// Repositions the tooltip onto the best candidate when one is found.
        /// </summary>
        private bool EnsurePlacement()
        {
            if (!_pooled && _tooltip == null) return true;
            if (candidateAnchors == null || candidateAnchors.Count == 0) return true; // nothing to choose -> keep default position

            if (Time.time < _nextEvaluationTime)
                return _hasValidPlacement;

            _nextEvaluationTime = Time.time + Mathf.Max(0.02f, EvaluationInterval);

            _hasValidPlacement = EvaluateBestAnchor(out Transform best);
            if (_hasValidPlacement && best != null)
            {
                bool anchorChanged = best != _currentAnchor; // only react when we actually switch positions
                _currentAnchor = best;

                if (anchorChanged)
                {
                    // The new position may carry its own billboard override + a new rest frame.
                    if (_pooled) PushBillboardToView();

                    if (_pooled && _view != null && _view.IsExpanded)
                    {
                        // Expanded: collapse in place -> travel as a disc -> re-expand on the new side.
                        _view.MoveExpandedTo(best.position, EffectiveIconSide(), true);
                    }
                    else
                    {
                        // Minimized disc (or legacy): just glide; store the side for the next expand.
                        ApplyVisualPosition(best.position, true);
                        if (_pooled) _view?.SetIconSide(EffectiveIconSide());
                    }
                }
            }
            else
            {
                _currentAnchor = null;
            }

            return _hasValidPlacement;
        }

        private void ApplyVisualPosition(Vector3 worldPosition, bool animate = false)
        {
            if (_pooled)
            {
                _view?.SetPosition(worldPosition, animate);
            }
            else if (_tooltip != null)
            {
                _tooltip.transform.position = worldPosition;
            }
        }

        private bool EvaluateBestAnchor(out Transform best)
        {
            best = null;
            var cam = CameraTransform;
            if (cam == null) return false;

            float bestScore = float.NegativeInfinity;
            Vector3 camPos = cam.position;

            // Score by the player's POSITION, not gaze: prefer the candidate on the side of the object that
            // faces where the player is standing. Using camera.forward here made turning your head drag the
            // tooltip between candidates; the object->player direction is gaze-independent.
            Transform center = LookTarget;
            Vector3 centerPos = center != null ? center.position : transform.position;
            Vector3 dirCenterToCam = camPos - centerPos;
            if (dirCenterToCam.sqrMagnitude > 1e-6f) dirCenterToCam.Normalize();

            for (int i = 0; i < candidateAnchors.Count; i++)
            {
                Transform anchor = candidateAnchors[i];
                if (!TryScoreAnchor(anchor, camPos, centerPos, dirCenterToCam, out _, out _, out float score, out _))
                    continue;

                // Bias toward the anchor we're already using to avoid flip-flopping.
                if (anchor == _currentAnchor)
                    score += RepositionHysteresis;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = anchor;
                }
            }

            return best != null;
        }

        // Shared per-anchor scoring so the runtime picker and the editor debug readout never drift. Returns
        // false (score = -inf) for a null / coincident / occluded anchor; `occluded` distinguishes the last.
        // The hysteresis bias for the current anchor is applied by the caller, not here.
        private bool TryScoreAnchor(Transform anchor, Vector3 camPos, Vector3 centerPos, Vector3 dirCenterToCam,
            out float facing, out float dist, out float score, out bool occluded)
        {
            facing = 0f; dist = 0f; score = float.NegativeInfinity; occluded = false;
            if (anchor == null) return false;

            Vector3 toAnchor = anchor.position - camPos;
            dist = toAnchor.magnitude;
            if (dist < 0.0001f) return false;

            if (rejectOccluded && Physics.Linecast(camPos, anchor.position, obstacleMask, QueryTriggerInteraction.Ignore))
            {
                occluded = true;
                return false;
            }

            Vector3 dirCenterToAnchor = anchor.position - centerPos;
            facing = dirCenterToAnchor.sqrMagnitude > 1e-6f
                ? Vector3.Dot(dirCenterToCam, dirCenterToAnchor.normalized) // -1 (far side) .. 1 (player side)
                : 0f;
            score = facing + distanceWeight * (1f / (1f + dist)); // closer -> higher
            return true;
        }

        private void ApplyBillboard()
        {
            var cam = CameraTransform;
            if (_tooltip == null || cam == null) return;

            Vector3 dir = _tooltip.transform.position - cam.position;
            if (dir.sqrMagnitude < 0.0001f) return;

            // Front face of the world-space UI points toward the camera, within the per-axis constraints
            // (rest = the tooltip's own authored rotation in legacy mode).
            _tooltip.transform.rotation =
                billboardConstraints.Apply(transform.rotation, dir, cam.up);
        }

        #endregion

        #region Pooled Rendering

        private void InitializePooled()
        {
            // No per-tooltip canvas is instantiated; visuals are checked out from the pool on demand.
            _iconSprite = EffectiveIcon(_currentControlScheme);
            _pooledClickHandler = RaiseClick; // cache the delegate once (see _pooledClickHandler)
        }

        private Sprite EffectiveIcon(BroadcastControlsStatus.ControlScheme scheme)
        {
            if (actionContentSo != null)
            {
                Sprite icon = actionContentSo.GetIcon(scheme);
                if (icon != null) return icon;
            }
            return ResolveIconSprite(scheme);
        }

        private Sprite ResolveIconSprite(BroadcastControlsStatus.ControlScheme scheme)
        {
            if (inputIconSo == null || interactableTooltipInputSo == null) return null;
            return inputIconSo.GetInputIcon(interactableTooltipInputSo.GetBindingName(scheme));
        }

        private Vector3 GetVisualPosition()
        {
            if (enableRepositioning && _currentAnchor != null)
                return _currentAnchor.position;
            return transform.position;
        }

        // Default icon side, unless the chosen candidate position overrides it via a TooltipAnchor.
        private bool EffectiveIconSide()
        {
            if (enableRepositioning && _currentAnchor != null)
            {
                var anchor = _currentAnchor.GetComponent<TooltipAnchor>();
                if (anchor != null && anchor.IconOnRightOverride.HasValue)
                    return anchor.IconOnRightOverride.Value;
            }
            return iconOnRight;
        }

        // Repositioning is actually driving placement only when it's enabled AND there are candidates;
        // otherwise the show methods keep the view pinned to the object each frame (so it follows if it moves).
        private bool RepositioningActive =>
            enableRepositioning && candidateAnchors != null && candidateAnchors.Count > 0;

        private void ShowExpandedPooled()
        {
            var pool = TooltipPoolManager.Instance;
            if (pool == null) { WarnNoPoolOnce(); return; }

            bool newlyAcquired = false;
            if (_view == null) { _view = pool.Acquire(); newlyAcquired = true; }
            if (_view == null) { WarnNoPoolOnce(); return; } // pool empty -> View Prefab unassigned

            // Per-checkout setup + the expand morph run only on the transition into expanded — not every frame.
            // (Billboard/icon don't change between frames here; EnsurePlacement re-pushes them on an anchor
            // switch and RefreshForScheme on a control-scheme change. Keeping them out of the per-frame path
            // also avoids the SetClickHandler delegate alloc.)
            if (_pooledShow != PooledShowState.Expanded)
            {
                PushBillboardToView();
                _view.SetClickHandler(_pooledClickHandler);
                _iconSprite = EffectiveIcon(_currentControlScheme);
                _view.ShowExpanded(EffectiveDescription, _iconSprite, EffectiveIconSide());
                if (newlyAcquired) _view.SetPosition(GetVisualPosition());
                _pooledShow = PooledShowState.Expanded;
            }

            // No active repositioning -> follow the (possibly moving) object, but only when it actually moved.
            if (!RepositioningActive) FollowObjectPosition();

            _isTooltipDisplayed = true;
        }

        // Per-frame position sync for the non-repositioning case, skipped while the position is unchanged so a
        // stationary tooltip doesn't re-snap (transform write + motion cancel) every frame.
        private void FollowObjectPosition()
        {
            Vector3 p = GetVisualPosition();
            if (_hasPooledFollowPos && (p - _pooledFollowPos).sqrMagnitude <= 1e-10f) return;
            _view.SetPosition(p);
            _pooledFollowPos = p;
            _hasPooledFollowPos = true;
        }

        private void ShowMinimizedPooled()
        {
            _isTooltipDisplayed = false;

            var pool = TooltipPoolManager.Instance;
            if (pool == null) { WarnNoPoolOnce(); return; }

            // Permanent tooltips never receive the punctual `showTooltip` event, so don't gate the
            // minimized disc on it — show whenever in range. Punctual tooltips still require it.
            bool wantMinimized = (isPermanentTooltip || showTooltip) && IsWithinMinimizedRange();
            if (!wantMinimized) { ReleasePooledView(); return; }

            bool newlyAcquired = false;
            if (_view == null) { _view = pool.Acquire(); newlyAcquired = true; }
            if (_view == null) return;

            // Per-checkout setup + the collapse morph run only on the transition into minimized — not every
            // frame (avoids the per-frame SetClickHandler delegate alloc; ongoing moves are EnsurePlacement's).
            if (_pooledShow != PooledShowState.Minimized)
            {
                PushBillboardToView();
                _view.SetClickHandler(_pooledClickHandler);
                _view.ShowMinimized();
                if (newlyAcquired) _view.SetPosition(GetVisualPosition());
                _pooledShow = PooledShowState.Minimized;
            }

            // No active repositioning -> follow the (possibly moving) object, but only when it actually moved.
            if (!RepositioningActive) FollowObjectPosition();
        }

        // Proximity gate for the show-chain. Legacy mode uses the FarTooltip trigger volume
        // (OnTriggerEnter -> _isPlayerNear). Pooled mode has no such volume, so "near" is a pure
        // distance test against Minimized Range — no trigger collider required on the tooltip.
        private bool IsNearForShow() => _pooled ? IsWithinMinimizedRange() : _isPlayerNear;

        private bool IsWithinMinimizedRange()
        {
            if (minimizedRange <= 0f) return true; // 0 or negative -> no range limit

            // Measure to the player's VIEWPOINT (main camera / head), not the player GameObject's root. In a VR
            // or FPS rig the head is a moving child while the root sits at the feet/spawn and may not track the
            // player at all, so a root-based distance reads a tooltip as permanently out of range. The head is
            // the player's actual position and always resolves via Camera.main (self-healing CameraTransform).
            var cam = CameraTransform;
            if (cam == null) return true;

            float sqr = (transform.position - cam.position).sqrMagnitude;
            return sqr <= minimizedRange * minimizedRange;
        }

        // Pooled billboard preference for this tooltip: null = follow the manager default, true/false = force.
        // The current candidate position (TooltipAnchor) can override the controller's own mode.
        private bool? BillboardOverrideForPooled()
        {
            // Repositioning to candidates: each position owns its on/off via its TooltipAnchor; one without an
            // override follows the manager default. The general Auto-orient mode is self-only (hidden then).
            if (RepositioningActive && _currentAnchor != null)
            {
                var anchor = _currentAnchor.GetComponent<TooltipAnchor>();
                if (anchor != null && anchor.BillboardOverride.HasValue) return anchor.BillboardOverride;
                return null; // UseManagerDefault
            }

            switch (billboardMode)
            {
                case BillboardMode.Always: return true;
                case BillboardMode.Never: return false;
                default: return null; // UseManagerDefault
            }
        }

        // The orientation the per-axis constraints are measured against: the chosen candidate's authored
        // rotation when repositioning, otherwise this controller's transform. So designers set the "home"
        // facing by rotating the controller (or the TooltipAnchor), and the tooltip swings within its limits
        // around that.
        internal Quaternion BillboardRestRotation()
        {
            if (enableRepositioning && _currentAnchor != null)
                return _currentAnchor.rotation;
            return transform.rotation;
        }

        // Shared, never-mutated "no limits" constraints (default = free billboard) used as the candidate fallback.
        private static readonly BillboardConstraints _freeConstraints = new BillboardConstraints();

        // The constraints in effect. When repositioning to candidates, each position owns its limits via its
        // TooltipAnchor override; a position WITHOUT one billboards freely. The general controller constraints
        // apply only in the no-candidate "self" case (and are hidden in the inspector when candidates exist).
        private BillboardConstraints EffectiveBillboardConstraints()
        {
            if (RepositioningActive && _currentAnchor != null)
            {
                var anchor = _currentAnchor.GetComponent<TooltipAnchor>();
                if (anchor != null && anchor.ConstraintsOverride != null) return anchor.ConstraintsOverride;
                return _freeConstraints;
            }
            return billboardConstraints;
        }

        // Push both the on/off override and the per-axis constraints + rest to the pooled view. Called wherever
        // the view's billboard preference might have changed (show transitions, anchor switch).
        private void PushBillboardToView()
        {
            if (_view == null) return;
            _view.SetBillboardOverride(BillboardOverrideForPooled());
            _view.SetBillboardConstraints(EffectiveBillboardConstraints(), BillboardRestRotation());
        }

#if UNITY_EDITOR
        // Editor scene-GUI / preview access to the live constraint config and its rest frame.
        internal BillboardMode BillboardModeEditor => billboardMode;
        // Rest rotation for an explicitly previewed candidate (the inspector may preview a position that isn't
        // the runtime-selected _currentAnchor). Falls back to the controller transform.
        internal Quaternion BillboardRestForEditor(Transform previewAnchor) =>
            previewAnchor != null ? previewAnchor.rotation : transform.rotation;
        // Effective constraints for a previewed candidate: its TooltipAnchor override if any, else FREE (the
        // general controller constraints are self-only). Previewing the base/self position uses the general ones.
        internal BillboardConstraints BillboardConstraintsForEditor(Transform previewAnchor)
        {
            if (previewAnchor != null)
            {
                var anchor = previewAnchor.GetComponent<TooltipAnchor>();
                return anchor != null && anchor.ConstraintsOverride != null ? anchor.billboardConstraints : _freeConstraints;
            }
            return billboardConstraints;
        }
        // True when the tooltip repositions across candidates (so the general billboard limits don't apply).
        internal bool UsesCandidatesEditor => RepositioningActive;
#endif

        private void ReleasePooledView()
        {
            _pooledShow = PooledShowState.Released;
            _hasPooledFollowPos = false;
            if (_view == null) return;
            TooltipPoolManager.Instance?.Release(_view);
            _view = null;
        }

        #endregion

        #region Editor/Debug Methods

        public void InstantiateTooltip()
        {
            InitializeTooltip();
        }

        public void DestroyInstantiateTooltip()
        {
            StopActiveTimer();
            
            if (_tooltip != null)
                DestroyImmediate(_tooltip);
        }
        
        #endregion
    }
}