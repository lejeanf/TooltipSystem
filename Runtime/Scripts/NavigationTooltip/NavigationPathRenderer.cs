using UnityEngine;
using UnityEngine.Rendering;

namespace jeanf.tooltip
{
    /// <summary>
    /// Draws the path markers (dots/arrows) with a single Graphics.RenderMeshInstanced call —
    /// no GameObjects, no canvas — plus one RenderMesh for the target ring. All animation
    /// (pulse, consume fade, hide wipe) runs in the shader; per frame the CPU only refreshes
    /// a few MaterialPropertyBlock floats. Zero GC after construction.
    /// </summary>
    public sealed class NavigationPathRenderer
    {
        private static readonly int PathDist01Id = Shader.PropertyToID("_PathDist01");
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
        private static readonly int TargetGlowId = Shader.PropertyToID("_TargetGlow");

        private const float FarBehind = -100f;

        private readonly Matrix4x4[] _matrices;
        private readonly float[] _pathDist01;
        private readonly Vector3[] _toPositions;
        private readonly Vector3[] _toTangents;
        private readonly Vector3[] _fromPositions;
        private readonly Vector3[] _fromTangents;
        private readonly Vector3[] _scratchPositions;
        private readonly Vector3[] _scratchTangents;
        private readonly MaterialPropertyBlock _markerProps;
        private readonly MaterialPropertyBlock _targetProps;
        private Mesh _quad;
        private RenderParams _markerParams;
        private RenderParams _targetParams;
        private Matrix4x4 _targetMatrix;
        private Bounds _markerBounds;
        private Bounds _targetBounds;
        private Vector3 _markerScale = Vector3.one;
        private float _morphT = 1f;
        private float _blendDuration;
        private int _count;
        private bool _hasTarget;
        private bool _hasMaterial;

        public NavigationPathRenderer(int maxMarkers = 1023)
        {
            _matrices = new Matrix4x4[maxMarkers];
            _pathDist01 = new float[maxMarkers];
            _toPositions = new Vector3[maxMarkers];
            _toTangents = new Vector3[maxMarkers];
            _fromPositions = new Vector3[maxMarkers];
            _fromTangents = new Vector3[maxMarkers];
            _scratchPositions = new Vector3[maxMarkers];
            _scratchTangents = new Vector3[maxMarkers];
            _markerProps = new MaterialPropertyBlock();
            _targetProps = new MaterialPropertyBlock();
            _quad = BuildQuad();
        }

        public void SetMaterial(Material material)
        {
            _hasMaterial = material != null;
            if (!_hasMaterial) return;
            _markerParams = new RenderParams(material)
            {
                matProps = _markerProps,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                lightProbeUsage = LightProbeUsage.Off
            };
            _targetParams = new RenderParams(material)
            {
                matProps = _targetProps,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                lightProbeUsage = LightProbeUsage.Off
            };
            _targetProps.SetFloat(ShapeId, 3f);
            _targetProps.SetFloat(PathDist01Id, 1f);
            // The ring's fate is driven by GlobalFade/TargetGlow, never by consume fade or the wipe.
            _targetProps.SetFloat(PlayerDistId, FarBehind);
            _targetProps.SetFloat(HideDistId, FarBehind);
        }

        /// <summary>
        /// Rebuild instances from freshly processed buffers (call on repath, not per frame).
        /// With <paramref name="blendSeconds"/> &gt; 0, markers morph from their currently displayed
        /// state to the new one — matched by their offset from the target end, which stays stable
        /// across repaths since markers are end-anchored.
        /// </summary>
        public void Rebuild(NavigationPathBuffers buffers, float markerSize, float elevation, float blendSeconds = 0f)
        {
            // Capture what is currently on screen (possibly mid-morph) as the blend origin.
            int prevCount = _count;
            float tPrev = Smooth(_morphT);
            for (int i = 0; i < prevCount; i++)
            {
                _scratchPositions[i] = Vector3.LerpUnclamped(_fromPositions[i], _toPositions[i], tPrev);
                _scratchTangents[i] = Vector3.LerpUnclamped(_fromTangents[i], _toTangents[i], tPrev);
            }

            _count = Mathf.Min(buffers.MarkerCount, _matrices.Length);
            _markerScale = new Vector3(markerSize, markerSize, markerSize);
            float invLength = buffers.TotalLength > 1e-4f ? 1f / buffers.TotalLength : 0f;
            bool blend = blendSeconds > 0f && prevCount > 0;
            for (int i = 0; i < _count; i++)
            {
                Vector3 pos = buffers.MarkerPositions[i];
                pos.y += elevation;
                Vector3 tangent = buffers.MarkerTangents[i];
                tangent.y = 0f;
                _toPositions[i] = pos;
                _toTangents[i] = tangent;
                _pathDist01[i] = buffers.MarkerDistances[i] * invLength;

                int j = i + (prevCount - _count); // end-aligned match with the previous marker set
                bool matched = blend && j >= 0 && j < prevCount;
                _fromPositions[i] = matched ? _scratchPositions[j] : pos;
                _fromTangents[i] = matched ? _scratchTangents[j] : tangent;

                if (i == 0) _markerBounds = new Bounds(pos, Vector3.one);
                else _markerBounds.Encapsulate(pos);
                _markerBounds.Encapsulate(_fromPositions[i]);
            }
            if (_count > 0)
            {
                _markerBounds.Expand(4f);
                _markerProps.SetFloatArray(PathDist01Id, _pathDist01);
            }
            _blendDuration = blendSeconds;
            _morphT = blend ? 0f : 1f;
            BuildMatrices();
        }

        /// <summary>Advance the repath blend (no-op once settled). Call every frame.</summary>
        public void Tick(float dt)
        {
            if (_morphT >= 1f) return;
            _morphT = Mathf.Min(1f, _morphT + dt / Mathf.Max(_blendDuration, 0.01f));
            BuildMatrices();
        }

        private void BuildMatrices()
        {
            float t = Smooth(_morphT);
            for (int i = 0; i < _count; i++)
            {
                Vector3 pos = Vector3.LerpUnclamped(_fromPositions[i], _toPositions[i], t);
                Vector3 tangent = Vector3.LerpUnclamped(_fromTangents[i], _toTangents[i], t);
                tangent.y = 0f;
                Quaternion rot = tangent.sqrMagnitude > 1e-6f
                    ? Quaternion.LookRotation(tangent, Vector3.up)
                    : Quaternion.identity;
                _matrices[i] = Matrix4x4.TRS(pos, rot, _markerScale);
            }
        }

        private static float Smooth(float t) => t * t * (3f - 2f * t);

        private Vector3 _targetPosition;
        private float _targetSize;
        private float _targetPop = 1f;

        public void SetTarget(Vector3 position, float size, float elevation)
        {
            position.y += elevation;
            _targetPosition = position;
            _targetSize = size;
            _targetMatrix = Matrix4x4.TRS(position, Quaternion.identity, new Vector3(size, size, size) * _targetPop);
            _targetBounds = new Bounds(position, Vector3.one * (size * 2f + 2f)); // covers the arrival pop growth
            _hasTarget = true;
        }

        /// <summary>Scale multiplier for the target ring (arrival "bubble pop" animation).</summary>
        public void SetTargetPop(float scaleMultiplier)
        {
            if (Mathf.Approximately(scaleMultiplier, _targetPop)) return;
            _targetPop = Mathf.Max(scaleMultiplier, 0.01f);
            float s = _targetSize * _targetPop;
            _targetMatrix = Matrix4x4.TRS(_targetPosition, Quaternion.identity, new Vector3(s, s, s));
        }

        public void ClearTarget() => _hasTarget = false;

        /// <summary>Refresh the animated/tunable shader parameters (cheap — call every frame while visible).</summary>
        public void SetFrameParams(NavigationPathStyle style, float pathLength, float chevronWeight,
            Color baseColor, Color pulseColor, float pulseHead, float pulseTrail, float pulseInterval,
            NavigationPulseMode pulseMode, float playerDist, float hideDist, float pathFade, float targetFade, float targetGlow)
        {
            ApplyShared(_markerProps, pathLength, chevronWeight, baseColor, pulseColor, pulseHead, pulseTrail, pulseInterval, pulseMode);
            _markerProps.SetFloat(ShapeId, style == NavigationPathStyle.Dots ? 0f : 1f);
            _markerProps.SetFloat(PlayerDistId, playerDist);
            _markerProps.SetFloat(HideDistId, hideDist);
            _markerProps.SetFloat(GlobalFadeId, pathFade);

            ApplyShared(_targetProps, pathLength, chevronWeight, baseColor, pulseColor, pulseHead, pulseTrail, pulseInterval, pulseMode);
            _targetProps.SetFloat(GlobalFadeId, targetFade);
            _targetProps.SetFloat(TargetGlowId, targetGlow);
        }

        private static void ApplyShared(MaterialPropertyBlock props, float pathLength, float chevronWeight,
            Color baseColor, Color pulseColor, float pulseHead, float pulseTrail, float pulseInterval, NavigationPulseMode pulseMode)
        {
            props.SetFloat(PathLengthId, pathLength);
            props.SetFloat(WeightId, chevronWeight);
            props.SetColor(BaseColorId, baseColor);
            props.SetColor(PulseColorId, pulseColor);
            props.SetFloat(PulseHeadId, pulseHead);
            props.SetFloat(PulseTrailId, pulseTrail);
            props.SetFloat(PulseIntervalId, pulseInterval);
            props.SetFloat(PulseModeId, pulseMode == NavigationPulseMode.Train ? 1f : 0f);
        }

        /// <summary>Submit this frame's draws. Must be called every frame the path is visible.</summary>
        public void Draw(bool drawMarkers, bool drawTarget)
        {
            if (!_hasMaterial) return;
            if (drawMarkers && _count > 0)
            {
                _markerParams.worldBounds = _markerBounds;
                Graphics.RenderMeshInstanced(_markerParams, _quad, 0, _matrices, _count);
            }
            if (drawTarget && _hasTarget)
            {
                _targetParams.worldBounds = _targetBounds;
                Graphics.RenderMesh(_targetParams, _quad, 0, _targetMatrix);
            }
        }

        public void Dispose()
        {
            if (_quad == null) return;
            Object.Destroy(_quad);
            _quad = null;
        }

        private static Mesh BuildQuad()
        {
            var mesh = new Mesh { name = "NavigationMarkerQuad" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f),
                new Vector3(-0.5f, 0f, 0.5f),
                new Vector3(0.5f, 0f, 0.5f),
                new Vector3(0.5f, 0f, -0.5f)
            };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
