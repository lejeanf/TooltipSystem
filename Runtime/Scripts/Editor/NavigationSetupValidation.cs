#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace jeanf.tooltip
{
    /// <summary>
    /// Scene-level validation of the navigation path setup: everything the path needs to display
    /// (tooltip present + active, material/shader/instancing, Player tag, baked navmesh, sane fields).
    /// Runs automatically before entering Play mode when the scene uses navigation paths,
    /// and on demand via Tools &gt; Tooltip &gt; Validate Setup.
    /// </summary>
    [InitializeOnLoad]
    internal static class NavigationSetupValidation
    {
        static NavigationSetupValidation()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.ExitingEditMode) return;
            var tooltip = Object.FindFirstObjectByType<NavigationTooltip>(FindObjectsInactive.Include);
            var tester = Object.FindFirstObjectByType<NavigationTooltipTester>(FindObjectsInactive.Include);
            if (tooltip == null && tester == null) return; // scene doesn't use navigation paths
            ValidateScene(false);
        }

        [MenuItem("Tools/TooltipSystem/Validate Setup")]
        private static void ValidateFromMenu()
        {
            int problems = ValidateScene(true);
            EditorUtility.DisplayDialog("Tooltip setup validation",
                problems == 0
                    ? "All checks passed — the navigation path setup looks good."
                    : $"{problems} problem(s) found — see the Console for details.",
                "OK");
        }

        /// <returns>Number of problems found (each is logged with its context object).</returns>
        private static int ValidateScene(bool verbose)
        {
            int count = 0;
            var tooltip = Object.FindFirstObjectByType<NavigationTooltip>(FindObjectsInactive.Include);
            var tester = Object.FindFirstObjectByType<NavigationTooltipTester>(FindObjectsInactive.Include);

            if (tooltip == null)
            {
                Warn(ref count, tester != null
                        ? "A NavigationTooltipTester is in the scene but there is no NavigationTooltip — nothing can draw the path. " +
                          "Add the NavigationTooltip component (or the NavigationTooltip prefab from uvs)."
                        : "No NavigationTooltip in the loaded scenes.",
                    tester);
            }
            else
            {
                if (!tooltip.gameObject.activeInHierarchy)
                    Warn(ref count, "The NavigationTooltip is inactive — the path will not render.", tooltip);
                else if (!tooltip.enabled)
                    Warn(ref count, "The NavigationTooltip component is disabled — the path will not render.", tooltip);

                var problems = new List<string>();
                tooltip.ValidateSetup(problems);
                foreach (string problem in problems)
                    Warn(ref count, "NavigationTooltip: " + problem, tooltip);
            }

            if (NavMesh.CalculateTriangulation().indices.Length == 0)
                Warn(ref count, "No baked navmesh in the loaded scenes — the navigation path cannot be computed. " +
                                "Bake a NavMesh (or load the scene that provides it).");

            if (verbose)
            {
                if (tester == null)
                    Debug.Log("[TooltipSystem] Tip: add a NavigationTooltipTester component to spawn random test destinations (T key).");
                if (count == 0)
                    Debug.Log("[TooltipSystem] Validation passed — navigation path setup looks good.");
            }
            return count;
        }

        private static void Warn(ref int count, string message, Object context = null)
        {
            count++;
            Debug.LogWarning("[TooltipSystem] " + message, context);
        }
    }
}
#endif
