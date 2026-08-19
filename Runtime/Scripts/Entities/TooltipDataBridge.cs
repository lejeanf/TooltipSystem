using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace jeanf.tooltip
{
    /// <summary>
    /// Bridges SubScene-authored tooltips (<see cref="TooltipAuthoring"/>) back into the GameObject
    /// world. A baked <see cref="TooltipSpawnData"/> entity is just a placement record: while it
    /// exists (its section is streamed in) this bridge keeps an instance of the authored prefab
    /// alive at the baked pose in the main world; streaming out destroys the instance. Zones are
    /// wired into the spawned controllers via <see cref="InteractableTooltipController.AssignZone"/>.
    /// Mirrors <c>SeatDataBridge</c>'s reconcile: tooltips never move and only exist while their
    /// SubScene is loaded, so a slow re-scan is enough. Drop one on a persistent GameObject
    /// (e.g. next to the TooltipPoolManager); it's a singleton.
    /// </summary>
    public class TooltipDataBridge : MonoBehaviour
    {
        private const string LogPrefix = "[TooltipSystem]";

        public static TooltipDataBridge Instance { get; private set; }

        [SerializeField] private bool isDebug = false;
        [Tooltip("Seconds between re-scans of baked tooltip entities (handles SubScene streaming). Tooltips never move, so this can be slow.")]
        [SerializeField] private float refreshInterval = 0.25f;

        private EntityManager _em;
        private EntityQuery _query;
        private bool _worldReady;
        private float _timer;
        private int _lastLoggedCount = -1;

        private Transform _container;

        private readonly Dictionary<Entity, GameObject> _instances = new Dictionary<Entity, GameObject>(32);
        private readonly HashSet<Entity> _seen = new HashSet<Entity>();
        private readonly HashSet<Entity> _invalid = new HashSet<Entity>();
        private readonly List<Entity> _toRemove = new List<Entity>(8);

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            // A root container at identity scale, so an instance's localScale composes cleanly
            // with the authoring object's baked lossy scale.
            _container = new GameObject("SubSceneTooltips").transform;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_container != null) Destroy(_container.gameObject);
        }

        private void OnEnable()
        {
            _timer = float.MaxValue; // scan on the first Update
            TryInitWorld();
        }

        private void TryInitWorld()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) { _worldReady = false; return; }
            _em = world.EntityManager;
            _query = _em.CreateEntityQuery(ComponentType.ReadOnly<TooltipSpawnData>());
            _worldReady = true;
        }

        private void Update()
        {
            if (!_worldReady)
            {
                TryInitWorld();
                if (!_worldReady) return;
            }

            _timer += Time.deltaTime;
            if (_timer < refreshInterval) return;
            _timer = 0f;

            Reconcile();
        }

        private void Reconcile()
        {
            _seen.Clear();
            var entities = _query.ToEntityArray(Allocator.Temp);
            for (var i = 0; i < entities.Length; i++)
            {
                var e = entities[i];
                _seen.Add(e);
                if (_invalid.Contains(e)) continue;
                if (_instances.TryGetValue(e, out var existing) && existing != null) continue; // static: place once
                if (!_em.HasComponent<LocalToWorld>(e)) continue; // pose not resolved yet — retry next tick

                var data = _em.GetComponentData<TooltipSpawnData>(e);
                var prefab = data.Prefab.Value;
                if (prefab == null)
                {
                    // The baker refuses null prefabs, so this means the asset went missing after baking.
                    _invalid.Add(e);
                    Debug.LogWarning($"{LogPrefix} TooltipDataBridge: baked tooltip e{e.Index} has no prefab — re-bake its SubScene.", this);
                    continue;
                }

                _instances[e] = Spawn(prefab, data.Zone.Value, _em.GetComponentData<LocalToWorld>(e));
            }
            entities.Dispose();

            if (isDebug && _instances.Count != _lastLoggedCount)
            {
                _lastLoggedCount = _instances.Count;
                Debug.Log($"{LogPrefix} TooltipDataBridge: {_instances.Count} SubScene tooltip(s) spawned.", this);
            }

            _toRemove.Clear();
            foreach (var kv in _instances)
                if (kv.Value == null || !_seen.Contains(kv.Key)) _toRemove.Add(kv.Key);

            for (var i = 0; i < _toRemove.Count; i++)
            {
                if (_instances.TryGetValue(_toRemove[i], out var go) && go != null) Destroy(go);
                _instances.Remove(_toRemove[i]);
            }
            _invalid.RemoveWhere(e => !_seen.Contains(e)); // re-warn if a fixed bake reloads
        }

        private GameObject Spawn(GameObject prefab, jeanf.scenemanagement.Zone zone, in LocalToWorld l2w)
        {
            var instance = Instantiate(prefab, _container);
            var t = instance.transform;
            t.SetPositionAndRotation(l2w.Position, l2w.Rotation);

            // Compose the prefab's own scale with the authoring object's baked lossy scale.
            var m = l2w.Value;
            var lossyScale = new Vector3(math.length(m.c0.xyz), math.length(m.c1.xyz), math.length(m.c2.xyz));
            t.localScale = Vector3.Scale(t.localScale, lossyScale);

            // Zone wiring: the Zone asset can't be referenced by the prefab per-placement, so the
            // authored zone fills every controller that doesn't carry its own. AssignZone re-seeds
            // membership, since OnEnable already ran during Instantiate with the zone still unset.
            if (zone != null)
            {
                var controllers = instance.GetComponentsInChildren<InteractableTooltipController>(true);
                for (var i = 0; i < controllers.Length; i++)
                    if (controllers[i].currentZone == null) controllers[i].AssignZone(zone);
            }

            if (isDebug) Debug.Log($"{LogPrefix} TooltipDataBridge: spawned '{prefab.name}' at {(Vector3)l2w.Position}.", this);
            return instance;
        }
    }
}
