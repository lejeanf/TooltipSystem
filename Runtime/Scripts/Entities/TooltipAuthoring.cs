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
    /// (the entity's LocalToWorld) and the gating <see cref="Zone"/>. <see cref="TooltipDataBridge"/>
    /// keeps an instance of the prefab alive in the main world while this entity exists.
    /// </summary>
    public struct TooltipSpawnData : IComponentData
    {
        /// <summary>The fully configured tooltip prefab to instantiate. A <see cref="UnityObjectRef{T}"/>
        /// survives SubScene serialization, so no companion object is needed in the main scene.</summary>
        public UnityObjectRef<GameObject> Prefab;
        /// <summary>Zone gating the tooltip; assigned to the spawned controller(s) whose zone is unset.</summary>
        public UnityObjectRef<Zone> Zone;
    }

    /// <summary>
    /// Put this on a GameObject inside an ECS SubScene to place a tooltip there. Assign a fully
    /// configured tooltip prefab (its root or children carry the <see cref="InteractableTooltipController"/>,
    /// the gaze target and any candidate positions — everything prefab-local). At runtime
    /// <see cref="TooltipDataBridge"/> instantiates the prefab in the MAIN world at this object's
    /// baked pose whenever the SubScene section streams in, and destroys it when it streams out.
    /// In a classic additive scene don't use this — drop the prefab in the scene directly.
    /// </summary>
    public class TooltipAuthoring : MonoBehaviour, IValidatable
    {
        [Validation("A tooltip prefab is required — without it this authoring bakes to nothing.")]
        [Tooltip("Fully configured tooltip prefab to spawn at this object's pose. Must contain the tooltip " +
                 "controller and its gaze target / candidate positions — no scene references.")]
        public GameObject tooltipPrefab;

        [Tooltip("Zone the tooltip only shows in. Applied to every InteractableTooltipController in the spawned " +
                 "instance that has no zone of its own. A Zone is an asset, so a SubScene can reference it safely. " +
                 "Leave empty only if the prefab already carries its zone(s).")]
        public Zone zone;

        /// <summary>
        /// A prefab whose controller has no zone from either side (its own inspector or this authoring)
        /// can never show — flag that at edit time instead of debugging an invisible tooltip.
        /// A null prefab returns true here because the [Validation] field above already flags it.
        /// </summary>
        public bool IsValid
        {
            get
            {
                if (tooltipPrefab == null) return true;
                var controller = tooltipPrefab.GetComponentInChildren<InteractableTooltipController>(true);
                if (controller == null) return true; // other tooltip types (e.g. navigation) don't use zones
                return controller.currentZone != null || zone != null;
            }
        }

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
