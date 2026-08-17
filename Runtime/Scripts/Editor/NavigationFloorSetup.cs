#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace jeanf.tooltip
{
    /// <summary>
    /// Tools/TooltipSystem/Setup Navigation Floor — turns the selected object(s) into a complete
    /// "floor the player can be guided across and teleport onto": a solid collider, an XRI
    /// TeleportationArea, and a NavMeshSurface bounded to that floor, baked on the spot.
    ///
    /// Run it on the floor object in the floor's additively-loaded DEPENDENCY scene, with the room
    /// SubScenes that hold the floor/wall meshes OPEN — the bake collects geometry from the whole
    /// editor stage, so open SubScenes contribute, closed ones do not. The components themselves
    /// must stay outside the SubScene (they would be dropped when it is baked to entities); the
    /// command refuses to touch an object that lives inside one.
    ///
    /// Everything it writes is what <see cref="NavigationSetupValidation"/> checks for.
    /// </summary>
    public static class NavigationFloorSetup
    {
        private const string LogPrefix = "[TooltipSystem.Navigation]";

        /// <summary>Vertical padding of the bake volume around the floor, in meters (head room for walls/stairs).</summary>
        private const float VolumeHeadRoom = 4f;

        [MenuItem("Tools/TooltipSystem/Setup Navigation Floor", true)]
        private static bool ValidateSetupFloor() => Selection.gameObjects.Length > 0;

        [MenuItem("Tools/TooltipSystem/Setup Navigation Floor")]
        private static void SetupFloor()
        {
            var targets = Selection.gameObjects;
            var subScenePaths = NavigationSetupValidation.SubScenePaths();
            var report = new StringBuilder($"{LogPrefix} Setup Navigation Floor on {targets.Length} object(s).");
            int configured = 0;

            foreach (var target in targets)
            {
                if (subScenePaths.Contains(target.gameObject.scene.path))
                {
                    report.AppendLine();
                    report.Append($"✗ '{target.name}' lives inside the SubScene '{target.gameObject.scene.name}' — skipped. " +
                                  "SubScenes are baked to entities, so a NavMeshSurface or TeleportationArea placed there does " +
                                  "not exist at runtime. Put the floor plate in the floor's dependency scene instead and keep " +
                                  "the geometry in the SubScene.");
                    continue;
                }

                report.AppendLine();
                report.Append(Configure(target));
                configured++;
            }

            if (configured > 0)
            {
                AssetDatabase.SaveAssets();
                report.AppendLine();
                report.Append("Run Tools/TooltipSystem/Validate Setup to confirm the whole chain.");
            }
            Debug.Log(report.ToString(), targets.FirstOrDefault());
        }

        private static string Configure(GameObject target)
        {
            var steps = new List<string>();

            var collider = EnsureCollider(target, steps);
            EnsureTeleportationArea(target, collider, steps);
            var surface = EnsureSurface(target, steps);
            string bakeResult = Bake(surface);

            EditorSceneManager.MarkSceneDirty(target.scene);
            return $"• '{target.name}' ({target.scene.name}): {string.Join("; ", steps)}. {bakeResult}";
        }

        /// <summary>A solid (non-trigger) collider for the teleport ray to hit.</summary>
        private static Collider EnsureCollider(GameObject target, List<string> steps)
        {
            var existing = target.GetComponents<Collider>().FirstOrDefault(c => !c.isTrigger);
            if (existing != null)
            {
                steps.Add($"kept collider {existing.GetType().Name}");
                return existing;
            }

            // A trigger-only collider is the classic invisible failure: the teleport ray's Raycast
            // Trigger Interaction defaults to Ignore, so make it solid rather than adding a second one.
            var trigger = target.GetComponent<Collider>();
            if (trigger != null)
            {
                Undo.RecordObject(trigger, "Setup Navigation Floor");
                trigger.isTrigger = false;
                steps.Add($"cleared 'Is Trigger' on the {trigger.GetType().Name}");
                return trigger;
            }

            Collider added;
            if (target.GetComponent<MeshFilter>() != null)
            {
                var mesh = Undo.AddComponent<MeshCollider>(target);
                mesh.convex = false;
                added = mesh;
            }
            else
            {
                added = Undo.AddComponent<BoxCollider>(target);
            }
            steps.Add($"added a {added.GetType().Name}");
            return added;
        }

        /// <summary>
        /// Adds/wires the XRI TeleportationArea by type name, so the Tooltip package keeps no
        /// compile-time dependency on the XR Interaction Toolkit.
        /// </summary>
        private static void EnsureTeleportationArea(GameObject target, Collider collider, List<string> steps)
        {
            var type = NavigationSetupValidation.XriTeleportType();
            if (type == null)
            {
                steps.Add("no XR Interaction Toolkit in this project — teleportation skipped");
                return;
            }

            var area = target.GetComponent(type);
            if (area == null)
            {
                area = Undo.AddComponent(target, type);
                steps.Add("added a TeleportationArea");
            }
            else
            {
                steps.Add("kept the existing TeleportationArea");
            }

            var serialized = new SerializedObject(area);
            var colliders = serialized.FindProperty("m_Colliders");
            if (colliders != null && colliders.arraySize == 0 && collider != null)
            {
                colliders.arraySize = 1;
                colliders.GetArrayElementAtIndex(0).objectReferenceValue = collider;
                steps.Add($"listed the {collider.GetType().Name} in its Colliders");
            }

            // An empty Interaction Layer Mask matches no interactor at all. Prefer the convention
            // already used in this project over a blanket "Everything".
            var layers = serialized.FindProperty("m_InteractionLayers.m_Bits");
            if (layers != null && layers.uintValue == 0)
            {
                uint convention = ProjectInteractionLayers(type, area);
                layers.uintValue = convention;
                steps.Add(convention == uint.MaxValue
                    ? "set its Interaction Layer Mask to Everything (no other area to copy from)"
                    : "copied the Interaction Layer Mask used by the project's other teleport areas");
            }
            serialized.ApplyModifiedProperties();
        }

        private static uint ProjectInteractionLayers(Type teleportType, Component exclude)
        {
            foreach (var other in Object.FindObjectsByType(teleportType, FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (ReferenceEquals(other, exclude)) continue;
                var bits = new SerializedObject(other).FindProperty("m_InteractionLayers.m_Bits");
                if (bits != null && bits.uintValue != 0) return bits.uintValue;
            }
            return uint.MaxValue;
        }

        /// <summary>
        /// A NavMeshSurface bounded to this floor. Volume (not All) so each floor's asset holds only
        /// its own navmesh — with All, every floor bakes the whole building and loading two of them
        /// stacks overlapping navmeshes.
        /// </summary>
        private static NavMeshSurface EnsureSurface(GameObject target, List<string> steps)
        {
            var surface = target.GetComponent<NavMeshSurface>();
            if (surface == null)
            {
                surface = Undo.AddComponent<NavMeshSurface>(target);
                steps.Add("added a NavMeshSurface");
            }
            else
            {
                steps.Add("kept the existing NavMeshSurface");
            }

            Undo.RecordObject(surface, "Setup Navigation Floor");
            surface.collectObjects = CollectObjects.Volume;

            // NavigationTooltip paths with NavMesh.CalculatePath, which only ever queries the
            // default agent type — a surface baked for another agent is invisible to it.
            int defaultAgent = NavMesh.GetSettingsByIndex(0).agentTypeID;
            if (surface.agentTypeID != defaultAgent)
            {
                surface.agentTypeID = defaultAgent;
                steps.Add($"set Agent Type to '{NavMesh.GetSettingsNameFromID(defaultAgent)}'");
            }

            // The bake volume is built from Matrix4x4.TRS(position, rotation, Vector3.one) — the
            // surface's SCALE is deliberately ignored by NavMeshSurface. So center/size are meters
            // in the object's unscaled local frame, NOT InverseTransformPoint/lossyScale space; a
            // flattened floor plate (scale y 0.1) would otherwise get a 10x too tall volume.
            var bounds = FloorBounds(target);
            var worldCenter = bounds.center + Vector3.up * (VolumeHeadRoom * 0.5f);
            surface.center = Quaternion.Inverse(target.transform.rotation) * (worldCenter - target.transform.position);
            surface.size = new Vector3(bounds.size.x, bounds.size.y + VolumeHeadRoom, bounds.size.z);
            steps.Add($"sized its bake volume to {bounds.size.x:F1} × {bounds.size.z:F1}m of floor (+{VolumeHeadRoom}m head room)");

            EditorUtility.SetDirty(surface);
            return surface;
        }

        /// <summary>World bounds of the floor object itself (renderers, else colliders, else a default plate).</summary>
        private static Bounds FloorBounds(GameObject target)
        {
            var renderers = target.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length > 0)
            {
                var bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
                return bounds;
            }

            var colliders = target.GetComponentsInChildren<Collider>(true);
            if (colliders.Length > 0)
            {
                var bounds = colliders[0].bounds;
                for (int i = 1; i < colliders.Length; i++) bounds.Encapsulate(colliders[i].bounds);
                return bounds;
            }

            return new Bounds(target.transform.position, new Vector3(20f, 1f, 20f));
        }

        /// <summary>
        /// Bakes and saves the result next to the scene, the way the Navigation window does
        /// (&lt;sceneFolder&gt;/&lt;sceneName&gt;/NavMesh-&lt;object&gt;.asset) — an in-memory bake would be
        /// lost on the next domain reload.
        /// </summary>
        private static string Bake(NavMeshSurface surface)
        {
            string previousAssetPath = surface.navMeshData != null
                ? AssetDatabase.GetAssetPath(surface.navMeshData)
                : null;

            surface.BuildNavMesh();
            var data = surface.navMeshData;
            if (data == null)
                return "BAKE FAILED — the volume collected no geometry. Open the room SubScenes that hold this floor's " +
                       "meshes (the bake only sees scenes open in the stage) and run this again.";

            data.name = "NavMesh-" + surface.name;
            string path = string.IsNullOrEmpty(previousAssetPath) ? NewAssetPath(surface) : previousAssetPath;
            AssetDatabase.CreateAsset(data, path);
            surface.navMeshData = data;
            EditorUtility.SetDirty(surface);

            if (data.sourceBounds.size == Vector3.zero)
                return $"baked to '{path}' but it is EMPTY — the volume collected no geometry. Open the room SubScenes that " +
                       "hold this floor's meshes and run this again (Use Geometry is 'Render Meshes'; switch it to " +
                       "'Physics Colliders' if this floor is collider-only).";

            return $"baked to '{path}'.";
        }

        private static string NewAssetPath(NavMeshSurface surface)
        {
            string scenePath = surface.gameObject.scene.path;
            string folder = "Assets";
            if (!string.IsNullOrEmpty(scenePath))
            {
                folder = Path.Combine(Path.GetDirectoryName(scenePath) ?? "Assets",
                    Path.GetFileNameWithoutExtension(scenePath));
            }
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

            return AssetDatabase.GenerateUniqueAssetPath(Path.Combine(folder, $"NavMesh-{surface.name}.asset")
                .Replace('\\', '/'));
        }
    }
}
#endif
