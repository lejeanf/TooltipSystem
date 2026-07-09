using System.Collections.Generic;
using UnityEngine;

namespace jeanf.tooltip
{
    /// <summary>
    /// Central pool + driver for pooled tooltips. Its presence in the scene enables pooled rendering:
    /// <see cref="InteractableTooltipController"/> instances with "use pooled rendering" check out a
    /// single combined <see cref="PooledTooltipView"/> (minimized + expanded parts, all Canvas-free)
    /// instead of instantiating their own canvases. One central <c>LateUpdate</c> billboards every
    /// active view and runs staggered occlusion raycasts for the minimized sprites.
    /// </summary>
    public class TooltipPoolManager : MonoBehaviour
    {
        public static TooltipPoolManager Instance { get; private set; }

        [Header("Player reference")]
        [Tooltip("The player GameObject in this scene. Used for tooltip proximity checks (show/hide range and the legacy near-trigger) instead of relying solely on a \"Player\" layer, which can silently fail to match if that layer isn't set up the same way in every project. Falls back to layer-based detection when unassigned.")]
        [SerializeField] private GameObject playerGameObject;

        public GameObject PlayerGameObject => playerGameObject;
        public Transform PlayerTransform => playerGameObject != null ? playerGameObject.transform : null;

        [Header("View prefab")]
        [Tooltip("The pooled tooltip prefab to instantiate (must have a PooledTooltipView on its root). Drag the PooledTooltip prefab from the project here.")]
        [SerializeField] private GameObject viewPrefab;

        public GameObject ViewPrefab => viewPrefab;
        public bool BillboardDefault => billboardToCamera;

        [Header("Pool size (prewarmed; grows if exceeded)")]
        [SerializeField, Min(0)] private int capacity = 24;

        [Header("Billboard")]
        [Tooltip("Default for tooltips that don't override it. A tooltip can force billboarding on/off per-instance (see the controller's Auto-orient mode).")]
        [SerializeField] private bool billboardToCamera = true;

        [Header("Occlusion (minimized sprites)")]
        [SerializeField] private bool checkOcclusion = true;
        [SerializeField] private LayerMask obstacleMask = 0;
        [Tooltip("Frames between a given view's occlusion raycast (checks are staggered across views).")]
        [SerializeField, Min(1)] private int framesBetweenOcclusionChecks = 5;

        [Header("Repositioning (performance)")]
        [Tooltip("Seconds between candidate-placement re-evaluations, shared by every tooltip that repositions. Higher = cheaper. Tuned once here instead of per tooltip since it affects performance, not appearance.")]
        [SerializeField, Min(0.02f)] private float evaluationInterval = 0.2f;
        [Tooltip("Score bias kept for a tooltip's current candidate so it doesn't flip-flop between near-equal positions.")]
        [SerializeField] private float repositionHysteresis = 0.05f;

        public float EvaluationInterval => evaluationInterval;
        public float RepositionHysteresis => repositionHysteresis;

        private readonly List<PooledTooltipView> _pool = new List<PooledTooltipView>();
        private readonly List<PooledTooltipView> _active = new List<PooledTooltipView>();

        private Transform _cam;

#if UNITY_EDITOR
        // Auto-fill the view prefab when the component is first added / Reset, so a fresh manager isn't a
        // silent empty pool. The custom inspector also offers a button + warning for existing ones. Kept
        // self-contained (no editor-class reference) so it compiles whether or not Editor/ is a split assembly.
        private void Reset()
        {
            if (viewPrefab != null) return;

            foreach (var guid in UnityEditor.AssetDatabase.FindAssets("PooledTooltip t:Prefab"))
            {
                var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                var go = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go != null && go.GetComponent<PooledTooltipView>() != null) { viewPrefab = go; break; }
            }
        }
#endif

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            CacheCamera();
            Prewarm();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void CacheCamera()
        {
            var main = Camera.main;
            _cam = main != null ? main.transform : null;
        }

        private void Prewarm()
        {
            if (viewPrefab == null) return;
            for (int i = 0; i < capacity; i++)
            {
                var v = CreateView();
                if (v == null) break; // misconfigured prefab -> CreateView already logged
                _pool.Add(v);
            }
        }

        private PooledTooltipView CreateView()
        {
            if (viewPrefab == null) return null;

            var go = Instantiate(viewPrefab, transform);
            var v = go.GetComponent<PooledTooltipView>();
            if (v == null)
            {
                Debug.LogError("[TooltipPoolManager] View Prefab has no PooledTooltipView on its root.", this);
                Destroy(go);
                return null;
            }
            v.Release();
            return v;
        }

        public PooledTooltipView Acquire()
        {
            if (viewPrefab == null) return null;

            for (int i = 0; i < _pool.Count; i++)
            {
                if (!_pool[i].InUse)
                {
                    _active.Add(_pool[i]);
                    return _pool[i];
                }
            }

            // Pool exhausted -> grow.
            var v = CreateView();
            if (v == null) return null;
            _pool.Add(v);
            _active.Add(v);
            return v;
        }

        public void Release(PooledTooltipView view)
        {
            if (view == null) return;
            view.Release();
            _active.Remove(view);
        }

        private void LateUpdate()
        {
            if (_cam == null)
            {
                CacheCamera();
                if (_cam == null) return;
            }

            Vector3 camPos = _cam.position;
            Vector3 camUp = _cam.up; // only used by views whose roll axis follows camera tilt

            // Billboard per view: each tooltip can override the global default (or follow it), and applies its
            // own per-axis constraints (free/locked/clamped yaw, pitch, roll) inside ApplyBillboard.
            for (int i = 0; i < _active.Count; i++)
                if (_active[i].ShouldBillboard(billboardToCamera))
                    _active[i].ApplyBillboard(camPos, camUp);

            if (checkOcclusion)
                UpdateOcclusionStaggered(camPos);
        }

        private void UpdateOcclusionStaggered(Vector3 camPos)
        {
            int stride = Mathf.Max(1, framesBetweenOcclusionChecks);
            int frame = Time.frameCount;

            for (int i = 0; i < _active.Count; i++)
            {
                var view = _active[i];
                if (view.IsExpanded) continue; // occlusion only gates the minimized disc

                // Stagger: each view is checked once every `stride` frames, spread by index.
                if ((frame + i) % stride != 0) continue;

                Vector3 worldPos = view.T.position;
                Vector3 dir = worldPos - camPos;
                float dist = dir.magnitude;
                if (dist < 1e-4f)
                {
                    view.SetMinimizedVisible(true);
                    continue;
                }

                bool blocked = Physics.Raycast(camPos, dir / dist, dist, obstacleMask, QueryTriggerInteraction.Ignore);
                view.SetMinimizedVisible(!blocked);
            }
        }
    }
}
