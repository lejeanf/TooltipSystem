using jeanf.scenemanagement;
using jeanf.validationTools;
using Unity.Entities;
using UnityEngine;

namespace jeanf.tooltip
{
    /// <summary>
    /// Baked placement record for a tooltip authored inside an ECS SubScene. Tooltips are pure
    /// GameObject constructs (pooled canvas-free views, zone/gaze gating, XR click) with no
    /// entity-world counterpart, so the SubScene only carries WHICH prefab to spawn, WHERE
    /// (the entity's LocalToWorld) and an optional zone override. <see cref="TooltipDataBridge"/>
    /// keeps an instance of the prefab alive in the main world while this entity exists.
    /// </summary>
    public struct TooltipSpawnData : IComponentData
    {
        /// <summary>The fully configured tooltip prefab to instantiate. A <see cref="UnityObjectRef{T}"/>
        /// survives SubScene serialization, so no companion object is needed in the main scene.</summary>
        public UnityObjectRef<GameObject> Prefab;
        /// <summary>Optional zone override; default (null) = the spawned controller auto-detects its
        /// zone from the zone volumes (ObjectZoneTrackingBridge).</summary>
        public UnityObjectRef<Zone> Zone;
    }

    /// <summary>
    /// Put this on a GameObject inside an ECS SubScene to place a tooltip there. Assign a fully
    /// configured tooltip prefab (its root or children carry the <see cref="InteractableTooltipController"/>,
    /// the gaze target and any candidate positions — everything prefab-local). At runtime
    /// <see cref="TooltipDataBridge"/> instantiates the prefab in the MAIN world at this object's
    /// baked pose whenever the SubScene section streams in, and destroys it when it streams out.
    /// The gating zone is auto-detected from the zone volumes at runtime; while editing, the
    /// inspector shows the detected zone and a live scene preview of the tooltip.
    /// In a classic additive scene don't use this — drop the prefab in the scene directly.
    /// </summary>
    public class TooltipAuthoring : MonoBehaviour, IValidatable
    {
        [Validation("A tooltip prefab is required — without it this authoring bakes to nothing.")]
        [Tooltip("Fully configured tooltip prefab to spawn at this object's pose. Must contain the tooltip " +
                 "controller and its gaze target / candidate positions — no scene references.")]
        public GameObject tooltipPrefab;

        [Tooltip("Optional override. Leave empty (recommended): the spawned tooltip auto-detects its zone " +
                 "from the zone volume containing this position, exactly like player/object zone tracking. " +
                 "Assign a Zone asset only when the tooltip sits outside every volume or must gate on a " +
                 "different zone than the one it is in.")]
        public Zone zone;

        /// <summary>
        /// Valid when the runtime can resolve a zone: an explicit override, a prefab whose controller
        /// carries its own zone, a prefab without a controller (other tooltip families), or a position
        /// inside a zone volume in the open scenes. A null prefab returns true here because the
        /// [Validation] field above already flags it. Volumes living in a scene that is not currently
        /// open can't be seen — the runtime detection still finds them, so this stays a soft warning.
        /// </summary>
        public bool IsValid
        {
            get
            {
#if UNITY_EDITOR
                if (tooltipPrefab == null || zone != null) return true;
                var controller = tooltipPrefab.GetComponentInChildren<InteractableTooltipController>(true);
                if (controller == null || controller.currentZone != null) return true;
                return DetectZoneAt(transform.position) != null;
#else
                return true;
#endif
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// Editor mirror of the runtime volume test: the zone of the first <see cref="VolumeAuthoring"/>
        /// in the open scenes whose box contains the point, using the same <see cref="VolumeMath"/>
        /// convention (orientation from the transform, extents from localScale, matrix scale ignored).
        /// </summary>
        public static Zone DetectZoneAt(Vector3 worldPosition)
        {
            var volumes = FindObjectsByType<VolumeAuthoring>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var volume in volumes)
            {
                if (volume.zone == null) continue;
                var t = volume.transform;
                var localToWorld = Unity.Mathematics.float4x4.TRS(t.position, t.rotation, new Unity.Mathematics.float3(1f));
                if (VolumeMath.ContainsPoint(localToWorld, t.localScale, worldPosition))
                    return volume.zone;
            }
            return null;
        }
#endif

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.6588f, 0f, 0.9f); // tooltip amber
            Gizmos.DrawWireSphere(transform.position, 0.08f);
            Gizmos.DrawLine(transform.position, transform.position + transform.forward * 0.25f);
        }

        class Baker : Baker<TooltipAuthoring>
        {
            public override void Bake(TooltipAuthoring authoring)
            {
                if (authoring.tooltipPrefab == null)
                {
                    Debug.LogWarning($"[TooltipAuthoring] '{authoring.name}' has no tooltip prefab assigned — skipped at bake, nothing will spawn.", authoring.gameObject);
                    return;
                }

                DependsOn(authoring.tooltipPrefab);
                if (authoring.zone != null) DependsOn(authoring.zone);

                // Renderable = guaranteed LocalToWorld; the bridge reads the pose once (tooltips never move).
                var entity = GetEntity(TransformUsageFlags.Renderable);
                AddComponent(entity, new TooltipSpawnData
                {
                    Prefab = authoring.tooltipPrefab,
                    Zone = authoring.zone,
                });
            }
        }
    }
}
