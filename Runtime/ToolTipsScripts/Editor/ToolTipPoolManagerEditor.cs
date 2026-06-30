#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace jeanf.tooltip
{
    /// <summary>
    /// Editor-only global toggle (persisted in EditorPrefs) for the live tooltip debug panel that
    /// <see cref="CustomInspectorInstanciateTooltip"/> draws on a selected InteractableToolTipController.
    /// Surfaced on the pool manager so there's one obvious place to turn it on.
    /// </summary>
    internal static class TooltipDebugPrefs
    {
        private const string Key = "jeanf.tooltip.debugPanel";
        public static bool Enabled
        {
            get => EditorPrefs.GetBool(Key, false);
            set => EditorPrefs.SetBool(Key, value);
        }
    }

    /// <summary>
    /// Custom inspector for <see cref="ToolTipPoolManager"/>. The View Prefab is a GameObject field, whose
    /// object-picker (⊙) only lists scene objects — not project prefabs — which makes it look like a prefab
    /// can't be assigned. This adds a one-click "find &amp; assign" button (and a clear warning when empty),
    /// so the pool prefab can be wired without fighting the picker.
    /// </summary>
    [CustomEditor(typeof(ToolTipPoolManager))]
    public class ToolTipPoolManagerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var viewPrefab = serializedObject.FindProperty("viewPrefab");
            if (viewPrefab != null && viewPrefab.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox(
                    "View Prefab is unassigned — the pool stays empty and no pooled tooltips render. " +
                    "Click below to auto-assign the PooledTooltip prefab (or drag it onto the field; the ⊙ " +
                    "picker only shows scene objects for GameObject fields, so dragging is required there).",
                    MessageType.Warning);

                if (GUILayout.Button("Find & assign PooledTooltip prefab"))
                {
                    var prefab = FindPooledTooltipPrefab();
                    if (prefab != null)
                    {
                        viewPrefab.objectReferenceValue = prefab;
                        serializedObject.ApplyModifiedProperties();
                    }
                    else
                    {
                        Debug.LogWarning("[ToolTipPoolManager] No prefab with a PooledTooltipView component was found in the project.");
                    }
                }
                EditorGUILayout.Space();
            }

            DrawDefaultInspector();

            EditorGUILayout.Space();
            TooltipDebugPrefs.Enabled = EditorGUILayout.ToggleLeft(
                new GUIContent("Show tooltip debug panel",
                    "When on, selecting an InteractableToolTipController shows a live state panel in its inspector " +
                    "(updates in play mode). Global editor toggle."),
                TooltipDebugPrefs.Enabled);
        }

        // The project prefab whose root has a PooledTooltipView (the pooled tooltip view). Cheap name-filtered
        // pass first, then a full scan if it was renamed.
        internal static GameObject FindPooledTooltipPrefab()
        {
            return ScanForView("PooledTooltip t:Prefab") ?? ScanForView("t:Prefab");
        }

        private static GameObject ScanForView(string filter)
        {
            foreach (var guid in AssetDatabase.FindAssets(filter))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go != null && go.GetComponent<PooledTooltipView>() != null) return go;
            }
            return null;
        }
    }
}
#endif
