#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace jeanf.tooltip
{
    [CustomEditor(typeof(NavigationTooltipTester))]
    public class NavigationTooltipTesterEditor : Editor
    {
        private bool _hasNavMesh;
        private NavigationTooltip _tooltip;

        private void OnEnable()
        {
            // Checked once per selection — CalculateTriangulation is too heavy for every repaint.
            _hasNavMesh = NavMesh.CalculateTriangulation().indices.Length > 0;
            _tooltip = FindFirstObjectByType<NavigationTooltip>(FindObjectsInactive.Include);
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var tester = (NavigationTooltipTester)target;

            EditorGUILayout.Space();
            if (_tooltip == null)
            {
                EditorGUILayout.HelpBox(
                    "No NavigationTooltip found in the loaded scenes — nothing can draw the path. " +
                    "Add the NavigationTooltip component to an object (a LineRenderer is added automatically).",
                    MessageType.Error);
            }
            else if (!_tooltip.gameObject.activeInHierarchy)
            {
                EditorGUILayout.HelpBox("The NavigationTooltip in the scene is inactive — the path will not render.", MessageType.Error);
            }
            if (!_hasNavMesh)
            {
                EditorGUILayout.HelpBox(
                    "No baked navmesh found — the navigation path cannot be computed. " +
                    "Bake a NavMesh in this scene before entering Play mode.",
                    MessageType.Error);
            }

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "Enter Play mode: a random destination spawns automatically (Auto Spawn On Enable), " +
                    "and a new one appears after each arrival (Auto Respawn). Buttons appear here in Play mode.",
                    MessageType.Info);
                return;
            }

            if (GUILayout.Button("Spawn random destination"))
                tester.SpawnRandomTarget();
            using (new EditorGUI.DisabledScope(tester.Target == null))
            {
                if (GUILayout.Button("Resend current destination"))
                    tester.PlaceAt(tester.Target.position);
            }
            if (GUILayout.Button("Hide path (fade out)"))
                tester.Hide();
        }
    }
}
#endif
