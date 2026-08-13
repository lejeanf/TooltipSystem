using System.Collections.Generic;
using System.Text;
using jeanf.propertyDrawer;
using jeanf.validationTools;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

namespace jeanf.tooltip
{
    /// <summary>
    /// Draws a guidance path from the player to a target over the navmesh.
    /// The raw NavMesh path is post-processed by <see cref="NavigationPathProcessor"/>
    /// (orthogonal legs, navmesh-validated rounded corners, uniform markers) and rendered
    /// canvas-free by <see cref="NavigationPathRenderer"/> (instanced SDF arrows/dots) or the
    /// attached LineRenderer. Pulse/fade animation runs in the shader; the whole loop is GC-free.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public class NavigationTooltip : Tooltip, IValidatable
    {
        [Tooltip("Set automatically at runtime via NavigationDestinationSender (or the tester).")]
        [ReadOnly]
        [SerializeField] private Transform target;

        [Header("General Settings")]
        [Tooltip("Threshold at which the player is arrived at destination")]
        [SerializeField] private float destinationThreshold = 1f;
        [Tooltip("Distance at which the player and the target is considered on navmesh even if not on it")]
        [SerializeField] private float navmeshDetectionDistance = 10f;
        [Tooltip("Time interval in seconds between path-recomputation checks (0 = every frame)")]
        [SerializeField] private float pathSamplingRate = 0.2f;
        [Tooltip("How far the player may stray from the drawn path before it is recomputed. Walking roughly along the path never repaths — the path stays put and is consumed.")]
        [Range(0.5f, 5f)]
        [SerializeField] private float repathDeviation = 1.5f;

        [Header("Path Shape")]
        [EnumToolbar]
        [SerializeField] private NavigationPathMode pathMode = NavigationPathMode.Orthogonal;
        [Tooltip("Corner rounding radius in meters; each corner is validated against the navmesh and shrunk before it would clip a wall.")]
        [Range(0f, 2f)]
        [SerializeField] private float cornerRadius = 1.5f;
        [Tooltip("Re-center orthogonal legs inside doorways/corridors.")]
        [SerializeField] private bool centerLegs = true;
        [Range(0f, 2f)]
        [SerializeField] private float maxLegShift = 1f;

        [Header("Path Style")]
        [EnumToolbar]
        [FormerlySerializedAs("navigationTooltipType")]
        [FormerlySerializedAs("navigationToolTipType")]
        [SerializeField] private NavigationPathStyle pathStyle = NavigationPathStyle.Arrows;
        [Tooltip("Distance between markers in meters (drives the marker count).")]
        [Range(0.2f, 3f)]
        [SerializeField] private float spacing = 0.5f;
        [Tooltip("First marker appears this many meters after the path start (keeps arrows out from under the player's feet).")]
        [Range(0f, 3f)]
        [SerializeField] private float markerStartOffset = 0.79f;
        [Tooltip("Markers stop this many meters before the target (keeps arrows off the target ring).")]
        [Range(0f, 3f)]
        [SerializeField] private float markerEndOffset = 1.13f;
        [Range(0.1f, 1f)]
        [SerializeField] private float markerSize = 0.312f;
        [Range(0.001f, 0.6f)]
        [SerializeField] private float chevronWeight = 0.145f;
        [SerializeField] private float lineWidth = 0.05f;
        [Tooltip("Markers/line float this many meters above the navmesh.")]
        [SerializeField] private float elevationOffset = 0.05f;

        [Header("Colors")]
        [SerializeField] private Color baseColor = new Color(1f, 0.627f, 0.094f, 1f);
        [SerializeField] private Color pulseColor = new Color(1f, 0.957f, 0.847f, 1f);

        [Header("Pulse")]
        [EnumToolbar]
        [SerializeField] private NavigationPulseMode pulseMode = NavigationPulseMode.Train;
        [Tooltip("Speed of the brightness pulse traveling toward the target, in m/s.")]
        [Range(0.5f, 15f)]
        [SerializeField] private float pulseSpeed = 5f;
        [Tooltip("How many meters the pulse takes to fade back to the base color.")]
        [Range(0.5f, 15f)]
        [SerializeField] private float pulseTrail = 4f;

        [Header("Target Marker")]
        [SerializeField] private bool showTargetMarker = true;
        [Range(0.2f, 2f)]
        [SerializeField] private float targetSize = 1.047f;

        [Header("Transitions")]
        [Tooltip("Fade duration for show/hide state changes, in seconds.")]
        [Range(0.05f, 2f)]
        [SerializeField] private float fadeDuration = 0.2f;
        [Tooltip("On arrival, how long the start-to-target hide wipe takes, in seconds.")]
        [Range(0.1f, 2f)]
        [SerializeField] private float arrivalWipeDuration = 0.5f;
        [Tooltip("When the path is recomputed, the old and new paths blend over this duration instead of popping.")]
        [Range(0f, 1f)]
        [SerializeField] private float repathBlendDuration = 0.3f;

        [Header("Rendering")]
        [Tooltip("NavigationMarker material for the current render pipeline (URP or HDRP variant).")]
        [Validation("No Marker Material assigned — dots/arrows and the target ring cannot render. Assign NavigationMarker_URP or NavigationMarker_HDRP (Runtime/Shaders).")]
        [SerializeField] private Material markerMaterial;

        [Header("Map Display Settings")]
        [SerializeField] private Transform topLeft;
        [SerializeField] private Transform topRight;
        [SerializeField] private Transform bottomLeft;
        [SerializeField] private Transform bottomRight;

        public delegate void BroadcastPathDelegate(NavMeshPath path);
        public static BroadcastPathDelegate OnBroadcastPath;

        private enum VisState { Hidden, FadingIn, Active, Arriving, FadingOut }

        private const float FarBehind = -100f;
        private const float ArrivalPopTime = 0.15f;
        private const float ArrivalWipeDelay = 0.1f;
        // Keep drawing the last good path this long when CalculatePath transiently fails
        // (crossing surface seams while walking) instead of blinking the whole path off.
        private const float PathLossGraceSeconds = 1f;
        private const float TargetMovementThreshold = 0.05f;
        private const int LineSampleCount = 256;

        private VisState _state = VisState.Hidden;
        private float _fade;
        private float _arrivalTimer;
        private float _hideDist = FarBehind;
        private float _targetFade = 1f;
        private float _targetGlow;
        private float _targetPop = 1f;

        private NavMeshPath _navMeshPath;
        private NavigationPathBuffers _buffers;
        private NavigationPathSettings _settings;
        private NavMeshPathQuery _query;
        private NavigationPathRenderer _pathRenderer;
        private NavMeshHit _navMeshHit;

        private LineRenderer _lineRenderer;
        private MaterialPropertyBlock _lineProps;
        private Vector3[] _lineFrom;
        private Vector3[] _lineTo;
        private Vector3[] _lineDraw;
        private float _lineMorphT = 1f;

        private Transform _playerTransform;
        private Vector3 _playerNavMeshPosition;
        private Vector3 _targetNavMeshPosition;
        private float _playerDistance;
        private int _projectionHint;
        private float _pathUpdateTimer;
        private float _destinationThresholdSqr;
        private float _pulseHead;
        private float _pulseInterval;
        private float _lastGoodPathTime = -999f;
        private Vector3 _lastRepathTargetPos;
        private bool _repathQueued;

        private static readonly int ShapeId = Shader.PropertyToID("_Shape");
        private static readonly int WeightId = Shader.PropertyToID("_Weight");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int PulseColorId = Shader.PropertyToID("_PulseColor");
        private static readonly int PathLengthId = Shader.PropertyToID("_PathLength");
        private static readonly int PulseHeadId = Shader.PropertyToID("_PulseHead");
        private static readonly int PulseTrailId = Shader.PropertyToID("_PulseTrail");
        private static readonly int PulseIntervalId = Shader.PropertyToID("_PulseInterval");
        private static readonly int PulseModeId = Shader.PropertyToID("_PulseMode");
        private static readonly int PlayerDistId = Shader.PropertyToID("_PlayerDist");
        private static readonly int HideDistId = Shader.PropertyToID("_HideDist");
        private static readonly int GlobalFadeId = Shader.PropertyToID("_GlobalFade");

        // ---------- runtime API (drives the same fields the inspector toggle lists expose) ----------
        public NavigationPathMode PathMode { get => pathMode; set { pathMode = value; ForceRepath(); } }
        public NavigationPathStyle PathStyle { get => pathStyle; set { pathStyle = value; ForceRepath(); } }
        public NavigationPulseMode PulseMode { get => pulseMode; set => pulseMode = value; }
        public float CornerRadius { get => cornerRadius; set { cornerRadius = Mathf.Clamp(value, 0f, 2f); ForceRepath(); } }
        public float Spacing { get => spacing; set { spacing = Mathf.Clamp(value, 0.2f, 3f); ForceRepath(); } }
        public float MarkerSize { get => markerSize; set { markerSize = Mathf.Clamp(value, 0.1f, 1f); ForceRepath(); } }
        public float ChevronWeight { get => chevronWeight; set => chevronWeight = Mathf.Clamp(value, 0.001f, 0.6f); }
        public Color BaseColor { get => baseColor; set => baseColor = value; }
        public Color PulseColor { get => pulseColor; set => pulseColor = value; }
        public float PulseSpeed { get => pulseSpeed; set => pulseSpeed = Mathf.Clamp(value, 0.5f, 15f); }
        public float PulseTrail { get => pulseTrail; set => pulseTrail = Mathf.Clamp(value, 0.5f, 15f); }

        private void Awake()
        {
            _navMeshPath = new NavMeshPath();
            _buffers = new NavigationPathBuffers();
            _query = new NavMeshPathQuery();
            _pathRenderer = new NavigationPathRenderer();
            _pathRenderer.SetMaterial(markerMaterial);
            _lineProps = new MaterialPropertyBlock();
            _lineFrom = new Vector3[LineSampleCount];
            _lineTo = new Vector3[LineSampleCount];
            _lineDraw = new Vector3[LineSampleCount];

            _lineRenderer = GetComponent<LineRenderer>();
            _lineRenderer.startWidth = lineWidth;
            _lineRenderer.endWidth = lineWidth;
            _lineRenderer.textureMode = LineTextureMode.Stretch; // uv.x = distance along the line, used by the shader
            if (markerMaterial != null) _lineRenderer.sharedMaterial = markerMaterial;
            _lineRenderer.enabled = false;

            var player = GameObject.FindGameObjectWithTag("Player");
            _playerTransform = player != null ? player.transform : null;

            CacheSquaredDistances();
        }

        private void CacheSquaredDistances() => _destinationThresholdSqr = destinationThreshold * destinationThreshold;

        public void RefreshDistanceThresholds() => CacheSquaredDistances();

        private void OnEnable() => Subscribe();
        private void OnDisable() => Unsubscribe();

        private void OnDestroy()
        {
            Unsubscribe();
            _pathRenderer?.Dispose();
        }

        private void Subscribe()
        {
            NavigationDestinationSender.OnSendDestination += SetDestination;
            NavigationMapCornerSender.OnSendNewMapCorner += SetNewMapCorner;
        }

        private void Unsubscribe()
        {
            NavigationDestinationSender.OnSendDestination -= SetDestination;
            NavigationMapCornerSender.OnSendNewMapCorner -= SetNewMapCorner;
        }

        private void OnValidate()
        {
            CacheSquaredDistances();
            if (Application.isPlaying && _pathRenderer != null)
            {
                _pathRenderer.SetMaterial(markerMaterial);
                if (_lineRenderer != null)
                {
                    _lineRenderer.startWidth = lineWidth;
                    _lineRenderer.endWidth = lineWidth;
                }
                ForceRepath();
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            switch (_state)
            {
                case VisState.Hidden:
                    if (showTooltip && target != null && _playerTransform != null) BeginShow();
                    else return;
                    break;

                case VisState.FadingIn:
                    if (!showTooltip) { _state = VisState.FadingOut; break; }
                    _fade = Mathf.Min(1f, _fade + dt / Mathf.Max(fadeDuration, 0.01f));
                    if (_fade >= 1f) _state = VisState.Active;
                    TickActive(dt);
                    break;

                case VisState.Active:
                    if (!showTooltip || target == null) { _state = VisState.FadingOut; break; }
                    if (PlayerArrivedToDestination()) { BeginArrival(); break; }
                    TickActive(dt);
                    break;

                case VisState.Arriving:
                    if (TickArrival(dt)) return;
                    break;

                case VisState.FadingOut:
                    _fade = Mathf.Max(0f, _fade - dt / Mathf.Max(fadeDuration, 0.01f));
                    if (_fade <= 0f) { BecomeHidden(); return; }
                    break;
            }
            AdvancePulse(dt);
            _pathRenderer.Tick(dt);
            if (_lineMorphT < 1f)
                _lineMorphT = Mathf.Min(1f, _lineMorphT + dt / Mathf.Max(repathBlendDuration, 0.01f));
            Render();
        }

        /// <summary>
        /// The pulse head advances in meters on the CPU so its phase stays continuous when the
        /// path length changes between repaths — a _Time-based phase in the shader would jump
        /// every resample while walking, which reads as flicker.
        /// </summary>
        private void AdvancePulse(float dt)
        {
            float total = _buffers.TotalLength;
            _pulseInterval = Mathf.Max(pulseTrail * 2.5f, total / 3f);
            if (total <= 0f) return;
            _pulseHead += dt * pulseSpeed;
            float cycle = pulseMode == NavigationPulseMode.Train ? _pulseInterval : total + pulseTrail * 2.5f;
            if (_pulseHead > cycle) _pulseHead -= cycle;
        }

        private void BeginShow()
        {
            _fade = 0f;
            _targetFade = 1f;
            _targetGlow = 0f;
            _hideDist = FarBehind;
            _playerDistance = 0f;
            _pathUpdateTimer = 0f;
            _pulseHead = 0f;
            _repathQueued = true;
            _state = VisState.FadingIn;
            RecomputePath();
        }

        private void BeginArrival()
        {
            _arrivalTimer = 0f;
            _targetGlow = 0f;
            _hideDist = 0f;
            _targetFade = 1f;
            _targetPop = 1f;
            _state = VisState.Arriving;
        }

        private void BecomeHidden()
        {
            _state = VisState.Hidden;
            _fade = 0f;
            _hideDist = FarBehind;
            _targetGlow = 0f;
            if (_lineRenderer.enabled) _lineRenderer.enabled = false;
            _pathRenderer.ClearTarget();
        }

        private void TickActive(float dt)
        {
            if (_playerTransform == null || target == null) return;
            _playerDistance = NavigationPathProcessor.ProjectDistance(
                _buffers, _playerTransform.position, ref _projectionHint, out float deviationSqr);

            _pathUpdateTimer += dt;
            if (pathSamplingRate <= 0f || _pathUpdateTimer >= pathSamplingRate)
            {
                _pathUpdateTimer = 0f;
                // The drawn path is a stable world object: walking roughly along it never repaths
                // (it gets consumed instead). Recompute only when the player strays past the
                // deviation corridor, the target moves, or there is no valid path yet.
                bool targetMoved = (target.position - _lastRepathTargetPos).sqrMagnitude >
                                   TargetMovementThreshold * TargetMovementThreshold;
                bool deviated = _buffers.PointCount > 1 && deviationSqr > repathDeviation * repathDeviation;
                bool pathMissing = _buffers.PointCount < 2;
                if (_repathQueued || targetMoved || deviated || pathMissing)
                    RecomputePath();
            }
        }

        /// <returns>True when the arrival sequence finished and the tooltip is now hidden.</returns>
        private bool TickArrival(float dt)
        {
            _arrivalTimer += dt;
            // 1. pop the target ring, 2. wipe the path start->target, 3. fade the ring out
            _targetGlow = Mathf.Clamp01(_arrivalTimer / ArrivalPopTime);
            float wipeT = Mathf.Clamp01((_arrivalTimer - ArrivalWipeDelay) / Mathf.Max(arrivalWipeDuration, 0.01f));
            _hideDist = wipeT * (_buffers.TotalLength + 1f);
            float fadeStart = ArrivalWipeDelay + arrivalWipeDuration;
            if (_arrivalTimer > fadeStart)
            {
                // Bubble pop: the ring grows ~1.8x with an ease-out while it fades away.
                float pop = Mathf.Clamp01((_arrivalTimer - fadeStart) / Mathf.Max(fadeDuration, 0.01f));
                _targetFade = 1f - pop;
                _targetPop = 1f + 0.8f * pop * (2f - pop);
                if (_targetFade <= 0f)
                {
                    showTooltip = false;
                    BecomeHidden();
                    return true;
                }
            }
            return false;
        }

        private bool RecomputePath()
        {
            _repathQueued = false;
            _lastRepathTargetPos = target.position;
            _playerNavMeshPosition = GetNearestNavMeshPoint(_playerTransform.position);
            _targetNavMeshPosition = GetNearestNavMeshPoint(target.position);

            bool found = NavMesh.CalculatePath(_playerNavMeshPosition, _targetNavMeshPosition, NavMesh.AllAreas, _navMeshPath);
            _buffers.CornerCount = found ? _navMeshPath.GetCornersNonAlloc(_buffers.Corners) : 0;
            if (_buffers.CornerCount < 2)
            {
                // Transient CalculatePath failures happen while walking across surface seams —
                // keep drawing the last good path for a grace window instead of blinking off.
                if (Time.time - _lastGoodPathTime > PathLossGraceSeconds)
                {
                    _buffers.PointCount = 0;
                    _buffers.MarkerCount = 0;
                    _buffers.TotalLength = 0f;
                    _pathRenderer.Rebuild(_buffers, markerSize, elevationOffset);
                    UpdateLinePositions(false);
                }
                return false;
            }
            _lastGoodPathTime = Time.time;

            OnBroadcastPath?.Invoke(_navMeshPath);

            _settings.mode = pathMode;
            _settings.cornerRadius = cornerRadius;
            _settings.spacing = spacing;
            _settings.startMargin = markerStartOffset;
            _settings.endMargin = markerEndOffset;
            _settings.centerLegs = centerLegs;
            _settings.maxLegShift = maxLegShift;
            NavigationPathProcessor.Process(in _settings, _query, _buffers);

            _projectionHint = 0;
            _playerDistance = 0f;
            float blend = _fade > 0f ? repathBlendDuration : 0f;
            _pathRenderer.Rebuild(_buffers, markerSize, elevationOffset, blend);
            _pathRenderer.SetTarget(target.position, targetSize, elevationOffset);
            UpdateLinePositions(blend > 0f);
            return true;
        }

        /// <summary>
        /// The line is resampled to a fixed resolution so old and new paths can be lerped 1:1
        /// during a repath blend (samples are index-matched, tail-aligned at the target).
        /// </summary>
        private void UpdateLinePositions(bool blend)
        {
            if (_buffers.PointCount < 2)
            {
                _lineRenderer.positionCount = 0;
                _lineMorphT = 1f;
                return;
            }
            float tPrev = Smooth01(_lineMorphT);
            for (int k = 0; k < LineSampleCount; k++)
            {
                if (blend) _lineFrom[k] = Vector3.LerpUnclamped(_lineFrom[k], _lineTo[k], tPrev);
                Vector3 p = NavigationPathProcessor.PointAt(_buffers, _buffers.TotalLength * k / (LineSampleCount - 1));
                p.y += elevationOffset;
                _lineTo[k] = p;
                if (!blend) _lineFrom[k] = p;
            }
            _lineMorphT = blend ? 0f : 1f;
        }

        private static float Smooth01(float t) => t * t * (3f - 2f * t);

        private void Render()
        {
            bool isLine = pathStyle == NavigationPathStyle.Line;
            bool arriving = _state == VisState.Arriving;
            float pathFade = arriving ? 1f : _fade;
            float targetFade = arriving ? _targetFade : _fade;
            float hideDist = arriving ? _hideDist : FarBehind;
            float targetGlow = arriving ? _targetGlow : 0f;

            _pathRenderer.SetFrameParams(pathStyle, _buffers.TotalLength, chevronWeight, baseColor, pulseColor,
                _pulseHead, pulseTrail, _pulseInterval, pulseMode, _playerDistance, hideDist, pathFade, targetFade, targetGlow);

            bool lineVisible = isLine && _buffers.PointCount > 1;
            if (_lineRenderer.enabled != lineVisible) _lineRenderer.enabled = lineVisible;
            if (lineVisible)
            {
                float t = Smooth01(_lineMorphT);
                for (int k = 0; k < LineSampleCount; k++)
                    _lineDraw[k] = Vector3.LerpUnclamped(_lineFrom[k], _lineTo[k], t);
                _lineRenderer.positionCount = LineSampleCount;
                _lineRenderer.SetPositions(_lineDraw);
                ApplyLineProps(pathFade, hideDist);
            }

            _pathRenderer.SetTargetPop(arriving ? _targetPop : 1f);
            _pathRenderer.Draw(!isLine, showTargetMarker);
        }

        private void ApplyLineProps(float pathFade, float hideDist)
        {
            _lineProps.SetFloat(ShapeId, 2f);
            _lineProps.SetFloat(WeightId, chevronWeight);
            _lineProps.SetColor(BaseColorId, baseColor);
            _lineProps.SetColor(PulseColorId, pulseColor);
            _lineProps.SetFloat(PathLengthId, _buffers.TotalLength);
            _lineProps.SetFloat(PulseHeadId, _pulseHead);
            _lineProps.SetFloat(PulseTrailId, pulseTrail);
            _lineProps.SetFloat(PulseIntervalId, _pulseInterval);
            _lineProps.SetFloat(PulseModeId, pulseMode == NavigationPulseMode.Train ? 1f : 0f);
            _lineProps.SetFloat(PlayerDistId, _playerDistance);
            _lineProps.SetFloat(HideDistId, hideDist);
            _lineProps.SetFloat(GlobalFadeId, pathFade);
            _lineRenderer.SetPropertyBlock(_lineProps);
        }

        private bool PlayerArrivedToDestination()
        {
            if (target == null || _playerTransform == null) return true;
            Vector3 delta = _playerTransform.position - target.position;
            return delta.sqrMagnitude <= _destinationThresholdSqr;
        }

        private Vector3 GetNearestNavMeshPoint(Vector3 position)
        {
            if (NavMesh.SamplePosition(position, out _navMeshHit, navmeshDetectionDistance, NavMesh.AllAreas))
                return _navMeshHit.position;
            return position;
        }

        private void ForceRepath()
        {
            // Explicitly queued reprocess (inspector tweaks / runtime API) — bypasses the
            // deviation gate that suppresses repaths during normal walking.
            _repathQueued = true;
            _pathUpdateTimer = float.MaxValue;
        }

        /// <summary>True when the path is currently being drawn (state machine active and markers exist).</summary>
        public bool IsPathVisible => _state != VisState.Hidden && _buffers != null && _buffers.MarkerCount > 0;

        /// <summary>
        /// Checks everything the path needs to be displayed and appends one message per problem.
        /// Used by the inspector, the pre-play preflight, and the tester's auto-diagnosis.
        /// </summary>
        public void ValidateSetup(List<string> problems)
        {
            if (markerMaterial == null)
            {
                problems.Add("No Marker Material assigned — assign NavigationMarker_URP or NavigationMarker_HDRP (Runtime/Shaders).");
            }
            else
            {
                string materialProblem = GetMaterialProblem(markerMaterial);
                if (materialProblem != null) problems.Add(materialProblem);
            }

            if (GetComponent<LineRenderer>() == null)
                problems.Add("Missing LineRenderer component (required for the Line style).");

            if (destinationThreshold <= 0f)
                problems.Add("Destination Threshold must be greater than 0 — otherwise the player is never detected as arrived.");
            if (pathSamplingRate < 0f)
                problems.Add("Path Sampling Rate cannot be negative.");
            if (navmeshDetectionDistance <= 0f)
                problems.Add("Navmesh Detection Distance must be greater than 0 — player/target can never be snapped onto the navmesh.");

            try
            {
                if (GameObject.FindGameObjectWithTag("Player") == null)
                    problems.Add("No GameObject tagged 'Player' in the scene — the path origin cannot be resolved.");
            }
            catch (UnityException)
            {
                problems.Add("The 'Player' tag does not exist in this project — the path origin cannot be resolved.");
            }
        }

        /// <summary>
        /// First problem with an assigned marker material (shader failed to compile, GPU instancing
        /// unticked, wrong-pipeline variant), or null when the material is fine or not assigned
        /// (the unset case is reported by the [Validation] field itself).
        /// </summary>
        public static string GetMaterialProblem(Material material)
        {
            if (material == null) return null;
            Shader shader = material.shader;
            string shaderName = shader != null ? shader.name : "<none>";
            if (shader == null || !shader.isSupported)
                return $"Marker shader '{shaderName}' did not compile for the active pipeline — nothing will render. " +
                       "On HDRP, rebuild it as an HDRP Unlit Shader Graph wrapping NavigationMarker.hlsl (see Documentation~/navigation-path-redesign.md §11).";
            if (!material.enableInstancing)
                return "Marker material has 'Enable GPU Instancing' unticked — dots/arrows are drawn with RenderMeshInstanced and will not render without it.";

            RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
            string pipelineName = pipeline != null ? pipeline.GetType().FullName : "Built-in";
            if (pipelineName.Contains("HighDefinition") && shaderName.EndsWith("URP"))
                return "Project runs HDRP but the URP marker material is assigned — use NavigationMarker_HDRP.";
            if (pipelineName.Contains("Universal") && shaderName.EndsWith("HDRP"))
                return "Project runs URP but the HDRP marker material is assigned — use NavigationMarker_URP.";
            return null;
        }

        /// <summary>
        /// Component-level validity for the validation tools (scene scanner, build check, hierarchy
        /// highlighter). The unset marker material is reported by its [Validation] field, so this
        /// covers the value-level rules only.
        /// </summary>
        public bool IsValid =>
            destinationThreshold > 0f &&
            navmeshDetectionDistance > 0f &&
            pathSamplingRate >= 0f &&
            GetMaterialProblem(markerMaterial) == null;

        /// <summary>Full state dump of the display chain, for debugging an invisible path.</summary>
        public string BuildDiagnosticsReport()
        {
            var sb = new StringBuilder(512);
            sb.AppendLine("[NavigationTooltip] diagnostics");
            sb.AppendLine($"- state: {_state}, showTooltip: {showTooltip}, fade: {_fade:F2}");
            sb.AppendLine($"- player: {(_playerTransform != null ? _playerTransform.name : "NOT FOUND (tag 'Player') at Awake")}");
            sb.AppendLine($"- target: {(target != null ? $"{target.name} @ {target.position}" : "none")}");
            if (_playerTransform != null && target != null)
            {
                bool playerOnMesh = NavMesh.SamplePosition(_playerTransform.position, out _, navmeshDetectionDistance, NavMesh.AllAreas);
                bool targetOnMesh = NavMesh.SamplePosition(target.position, out _, navmeshDetectionDistance, NavMesh.AllAreas);
                sb.AppendLine($"- on navmesh (within {navmeshDetectionDistance}m): player {playerOnMesh}, target {targetOnMesh}");
            }
            if (_buffers != null)
                sb.AppendLine($"- path: corners {_buffers.CornerCount}, points {_buffers.PointCount}, markers {_buffers.MarkerCount}, length {_buffers.TotalLength:F1}m");
            sb.AppendLine($"- style: {pathStyle}, mode: {pathMode}, spacing: {spacing}, markerSize: {markerSize}, playerDist: {_playerDistance:F1}m");

            var problems = new List<string>();
            ValidateSetup(problems);
            foreach (string p in problems) sb.AppendLine("- PROBLEM: " + p);
            if (problems.Count == 0) sb.AppendLine("- setup checks: all passed");
            return sb.ToString();
        }

        [ContextMenu("Log Diagnostics")]
        public void LogDiagnostics() => Debug.Log(BuildDiagnosticsReport(), this);

        /// <summary>Show the path to a destination (same effect as receiving it from NavigationDestinationSender).</summary>
        public void NavigateTo(Transform destination) => SetDestination(destination);

        /// <summary>Hide the path with a fade-out.</summary>
        public void Hide() => showTooltip = false;

        private void SetDestination(Transform targetTransform)
        {
            target = targetTransform;
            showTooltip = true;
            _arrivalTimer = 0f;
            if (_state == VisState.Hidden) return; // BeginShow runs next Update
            if (_state != VisState.Active) _state = VisState.FadingIn;
            ForceRepath();
        }

        private void SetNewMapCorner(Transform newCorner, NavigationMapCornerType cornerType)
        {
            switch (cornerType)
            {
                case NavigationMapCornerType.TopLeft:
                    topLeft = newCorner;
                    break;
                case NavigationMapCornerType.TopRight:
                    topRight = newCorner;
                    break;
                case NavigationMapCornerType.BottomLeft:
                    bottomLeft = newCorner;
                    break;
                case NavigationMapCornerType.BottomRight:
                    bottomRight = newCorner;
                    break;
            }
        }
    }
}
