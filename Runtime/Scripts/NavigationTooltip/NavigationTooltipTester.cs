using UnityEngine;
using UnityEngine.AI;

namespace jeanf.tooltip
{
    /// <summary>
    /// Play-mode testing helper for <see cref="NavigationTooltip"/>: spawns a destination at a
    /// random point on the baked navmesh (area-weighted, at least <see cref="minDistanceFromPlayer"/>
    /// away from the player) and broadcasts it through
    /// <see cref="NavigationDestinationSender.OnSendDestination"/> — the same event chain the game
    /// uses. Drop it on any GameObject in a test scene: destinations spawn automatically on enable
    /// and after each arrival (no input bindings — works with any input handling), or via the
    /// inspector buttons.
    /// </summary>
    [AddComponentMenu("jeanf/Tooltip/Navigation Tooltip Tester")]
    public class NavigationTooltipTester : MonoBehaviour
    {
        [Tooltip("Spawn a destination automatically when entering Play mode.")]
        [SerializeField] private bool autoSpawnOnEnable = true;
        [Tooltip("Spawn a new random destination after the current one is reached.")]
        [SerializeField] private bool autoRespawn = true;
        [Tooltip("Delay before the next auto-respawn, in seconds.")]
        [Range(0.5f, 10f)]
        [SerializeField] private float respawnDelay = 2f;
        [Tooltip("Minimum distance between the player and the spawned destination, in meters.")]
        [SerializeField] private float minDistanceFromPlayer = 8f;
        [Tooltip("One second after spawning, log a full diagnostics report if the path is still not visible.")]
        [SerializeField] private bool autoDiagnose = true;

        private Transform _target;
        private NavigationTooltip _tooltip;
        private float _diagnoseAt = -1f;
        private float _spawnAt = -1f;
        private bool _sawPathVisible;
        private NavMeshTriangulation _triangulation;
        private float[] _cumulativeAreas;
        private float _totalArea;
        private bool _triangulationCached;

        public Transform Target => _target;

        private void OnEnable()
        {
            // Small delay so the navmesh and the tooltip finish initializing first.
            if (autoSpawnOnEnable) _spawnAt = Time.time + 0.5f;
        }

        private void Update()
        {
            if (_spawnAt > 0f && Time.time >= _spawnAt)
            {
                _spawnAt = -1f;
                SpawnRandomTarget();
            }
            if (_diagnoseAt > 0f && Time.time >= _diagnoseAt)
            {
                _diagnoseAt = -1f;
                Diagnose();
            }

            // Auto-respawn: once the path has been visible and then completed (arrival hides it),
            // schedule the next random destination.
            if (_tooltip == null) _tooltip = FindFirstObjectByType<NavigationTooltip>();
            if (_tooltip == null) return;
            if (_tooltip.IsPathVisible)
            {
                _sawPathVisible = true;
            }
            else if (_sawPathVisible && autoRespawn && _spawnAt < 0f)
            {
                _sawPathVisible = false;
                _spawnAt = Time.time + respawnDelay;
            }
        }

        /// <summary>Logs why the path is (not) showing: full state dump + setup validation.</summary>
        public void Diagnose()
        {
            if (_tooltip == null) _tooltip = FindFirstObjectByType<NavigationTooltip>(FindObjectsInactive.Include);
            if (_tooltip == null)
            {
                Debug.LogWarning("[NavigationTooltipTester] No NavigationTooltip found in the scene — nothing can draw the path.", this);
                return;
            }
            if (!_tooltip.isActiveAndEnabled)
            {
                Debug.LogWarning("[NavigationTooltipTester] The NavigationTooltip exists but is inactive/disabled.", _tooltip);
                return;
            }
            if (_tooltip.IsPathVisible)
                Debug.Log("[NavigationTooltipTester] Path is visible.\n" + _tooltip.BuildDiagnosticsReport(), _tooltip);
            else
                Debug.LogWarning("[NavigationTooltipTester] Path is NOT visible — diagnostics:\n" + _tooltip.BuildDiagnosticsReport(), _tooltip);
        }

        /// <summary>Pick a random navmesh point (far enough from the player) and broadcast it.</summary>
        public void SpawnRandomTarget()
        {
            if (!CacheTriangulation())
            {
                Debug.LogWarning("[NavigationTooltipTester] No baked navmesh found in the scene.", this);
                return;
            }

            var player = GameObject.FindGameObjectWithTag("Player");
            Vector3 playerPos = player != null ? player.transform.position : Vector3.zero;
            float minSqr = minDistanceFromPlayer * minDistanceFromPlayer;

            Vector3 farthest = default;
            float farthestSqr = -1f;
            for (int attempt = 0; attempt < 30; attempt++)
            {
                Vector3 candidate = RandomPointOnNavMesh();
                float distSqr = (candidate - playerPos).sqrMagnitude;
                if (distSqr >= minSqr)
                {
                    PlaceAt(candidate);
                    return;
                }
                if (distSqr > farthestSqr)
                {
                    farthestSqr = distSqr;
                    farthest = candidate;
                }
            }
            PlaceAt(farthest); // navmesh smaller than the min distance — use the farthest candidate found
        }

        /// <summary>Move the test destination to a world position and (re)broadcast it.</summary>
        public void PlaceAt(Vector3 position)
        {
            if (_target == null)
            {
                var go = new GameObject("NavTest Target");
                go.transform.SetParent(transform, false);
                _target = go.transform;
            }
            _target.position = position;
            NavigationDestinationSender.OnSendDestination?.Invoke(_target);
            if (autoDiagnose) _diagnoseAt = Time.time + 1f;
        }

        public void Hide()
        {
            // Manual hide also cancels any pending auto-respawn.
            _spawnAt = -1f;
            _sawPathVisible = false;
            var tooltip = FindFirstObjectByType<NavigationTooltip>();
            if (tooltip != null) tooltip.Hide();
        }

        private bool CacheTriangulation()
        {
            if (_triangulationCached) return _totalArea > 0f;
            _triangulationCached = true;
            _triangulation = NavMesh.CalculateTriangulation();
            int triangleCount = _triangulation.indices.Length / 3;
            _cumulativeAreas = new float[triangleCount];
            _totalArea = 0f;
            for (int i = 0; i < triangleCount; i++)
            {
                Vector3 a = _triangulation.vertices[_triangulation.indices[i * 3]];
                Vector3 b = _triangulation.vertices[_triangulation.indices[i * 3 + 1]];
                Vector3 c = _triangulation.vertices[_triangulation.indices[i * 3 + 2]];
                _totalArea += Vector3.Cross(b - a, c - a).magnitude * 0.5f;
                _cumulativeAreas[i] = _totalArea;
            }
            return _totalArea > 0f;
        }

        private Vector3 RandomPointOnNavMesh()
        {
            float pick = Random.value * _totalArea;
            int lo = 0, hi = _cumulativeAreas.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (_cumulativeAreas[mid] < pick) lo = mid + 1;
                else hi = mid;
            }
            Vector3 a = _triangulation.vertices[_triangulation.indices[lo * 3]];
            Vector3 b = _triangulation.vertices[_triangulation.indices[lo * 3 + 1]];
            Vector3 c = _triangulation.vertices[_triangulation.indices[lo * 3 + 2]];
            // uniform barycentric sample
            float r1 = Mathf.Sqrt(Random.value);
            float r2 = Random.value;
            return (1f - r1) * a + r1 * (1f - r2) * b + r1 * r2 * c;
        }

        private void OnDrawGizmos()
        {
            if (_target == null) return;
            Gizmos.color = new Color(1f, 0.63f, 0.09f, 1f);
            Gizmos.DrawWireSphere(_target.position, 0.3f);
            Gizmos.DrawLine(_target.position, _target.position + Vector3.up);
        }
    }
}
