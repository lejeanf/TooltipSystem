#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace jeanf.tooltip
{
    /// <summary>
    /// Editor-only global toggle (persisted in EditorPrefs) for the live tooltip debug panel that
    /// <see cref="CustomInspectorInstanciateTooltip"/> draws on a selected InteractableTooltipController.
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
    /// Custom inspector for <see cref="TooltipPoolManager"/>. The View Prefab is a GameObject field, whose
    /// object-picker (⊙) only lists scene objects — not project prefabs — which makes it look like a prefab
    /// can't be assigned. This adds a one-click "find &amp; assign" button (and a clear warning when empty),
    /// so the pool prefab can be wired without fighting the picker.
    /// </summary>
    [CustomEditor(typeof(TooltipPoolManager))]
    public class TooltipPoolManagerEditor : Editor
    {
        // Pooling is always used, so only the View Prefab + prewarm capacity are routinely interesting.
        // Everything else (billboard default, occlusion, repositioning perf, legacy player layer) lives here.
        private static bool _advanced;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // Grayed-out "Script" field, same as Unity's default inspector — lets you double-click the
            // classname here to open the script, which a fully custom OnInspectorGUI otherwise hides.
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));

            var viewPrefab = serializedObject.FindProperty("viewPrefab");
            if (viewPrefab != null)
            {
                // Project-asset picker (allowSceneObjects: false) so the ⊙ browses PROJECT prefabs — including a
                // per-render-pipeline variant (e.g. a URP PooledTooltip). The default GameObject field only lists
                // scene objects. Validates that the chosen prefab actually has a PooledTooltipView on its root.
                EditorGUI.BeginChangeCheck();
                var picked = EditorGUILayout.ObjectField(
                    new GUIContent("View Prefab",
                        "Prefab instantiated for every pooled tooltip (must have a PooledTooltipView on its root). " +
                        "Make a Prefab Variant per render pipeline (URP / HDRP) — same view, different material/shader — and assign the right one here."),
                    viewPrefab.objectReferenceValue, typeof(GameObject), false) as GameObject;
                if (EditorGUI.EndChangeCheck())
                {
                    if (picked == null || picked.GetComponent<PooledTooltipView>() != null)
                        viewPrefab.objectReferenceValue = picked;
                    else
                        Debug.LogWarning("[TooltipPoolManager] That prefab has no PooledTooltipView on its root — not assigned.", picked);
                }

                if (viewPrefab.objectReferenceValue == null)
                {
                    EditorGUILayout.HelpBox(
                        "View Prefab is unassigned — the pool stays empty and no pooled tooltips render.",
                        MessageType.Warning);
                    if (GUILayout.Button("Find & assign PooledTooltip prefab"))
                    {
                        var prefab = FindPooledTooltipPrefab();
                        if (prefab != null) viewPrefab.objectReferenceValue = prefab;
                        else Debug.LogWarning("[TooltipPoolManager] No prefab with a PooledTooltipView component was found in the project.");
                    }
                }

                DrawPipelineVariantHook(viewPrefab);
                EditorGUILayout.Space();
            }

            // Primary: just the prewarm count (the pool grows on demand past it).
            var cap = serializedObject.FindProperty("capacity");
            if (cap != null) EditorGUILayout.PropertyField(cap);

            EditorGUILayout.Space();
            _advanced = EditorGUILayout.Foldout(_advanced,
                "Advanced (billboard, occlusion, repositioning perf, legacy)", true, EditorStyles.foldoutHeader);
            if (_advanced)
            {
                EditorGUI.indentLevel++;
                DrawPropertiesExcluding(serializedObject, "m_Script", "viewPrefab", "capacity");
                EditorGUI.indentLevel--;
            }

            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();
            TooltipDebugPrefs.Enabled = EditorGUILayout.ToggleLeft(
                new GUIContent("Show tooltip debug panel",
                    "When on, selecting an InteractableTooltipController shows a live state panel in its inspector " +
                    "(updates in play mode). Global editor toggle."),
                TooltipDebugPrefs.Enabled);
        }

        // ---- Per-render-pipeline prefab variant hook -------------------------------------------------
        // The package ships ONE PooledTooltip prefab. Its background quad needs a material whose shader matches
        // the project's active render pipeline (URP or HDRP) — and the package prefab usually lives in Packages/
        // (read-only). This draws a one-click action that creates a LINKED prefab variant in the local project
        // (Assets/) with the background material swapped to the matching pipeline material, then re-points the
        // pool manager at that variant. It is button-gated and only appears while the assigned prefab's material
        // does NOT match the active pipeline — so once the reference is the fresh variant, nothing reruns.
        private void DrawPipelineVariantHook(SerializedProperty viewPrefab)
        {
            var current = viewPrefab.objectReferenceValue as GameObject;
            if (current == null) return; // nothing assigned yet — the find/assign button above handles that.

            string suffix = ActivePipelineSuffix();
            if (suffix == null) return; // Built-in / unknown pipeline: no automated material to choose.

            var curMat = GetBackgroundMaterial(current);
            if (curMat != null && curMat.name.EndsWith("_" + suffix))
            {
                // Already correct (e.g. the local variant we created) — confirm quietly, draw no button, do nothing.
                EditorGUILayout.HelpBox(
                    $"View Prefab background uses \"{curMat.name}\" — matches the active {suffix} render pipeline.",
                    MessageType.None);
                return;
            }

            var desired = FindPipelineMaterial(suffix);
            if (desired == null)
            {
                EditorGUILayout.HelpBox(
                    $"Active render pipeline is {suffix}, but no \"RoundedRectTooltip_{suffix}\" material was found " +
                    "to build a variant from. Add the matching pipeline material to the project.",
                    MessageType.Warning);
                return;
            }

            EditorGUILayout.HelpBox(
                $"Active render pipeline is {suffix}, but the View Prefab's background uses " +
                $"\"{(curMat != null ? curMat.name : "no material")}\". Create a local, linked prefab variant " +
                $"that uses the {suffix} material and assign it here.",
                MessageType.Info);
            if (GUILayout.Button($"Create local {suffix} prefab variant"))
                CreateLocalVariant(current, suffix, desired, viewPrefab);
        }

        // Creates a Prefab Variant of `source` in the project, overrides its background material to `mat`, and
        // assigns the variant to the pool manager. Runs only on explicit button click.
        private void CreateLocalVariant(GameObject source, string suffix, Material mat, SerializedProperty viewPrefab)
        {
            string path = EditorUtility.SaveFilePanelInProject(
                $"Create local {suffix} tooltip prefab variant",
                $"PooledTooltip_{suffix}", "prefab",
                "Choose where to save the linked prefab variant (inside your project's Assets folder).");
            if (string.IsNullOrEmpty(path)) return;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            try
            {
                var bg = GetBackgroundRenderer(instance);
                if (bg != null) bg.sharedMaterial = mat; // recorded as an override on the variant
                else Debug.LogWarning("[TooltipPoolManager] Variant created but no background renderer was found to assign the material.", source);

                var variant = PrefabUtility.SaveAsPrefabAsset(instance, path, out bool ok);
                if (ok && variant != null)
                {
                    viewPrefab.objectReferenceValue = variant;
                    viewPrefab.serializedObject.ApplyModifiedProperties();
                    EditorGUIUtility.PingObject(variant);
                    Debug.Log($"[TooltipPoolManager] Created {suffix} prefab variant at \"{path}\" (background → {mat.name}) and assigned it.", variant);
                }
                else
                {
                    Debug.LogError($"[TooltipPoolManager] Failed to save the prefab variant at \"{path}\".");
                }
            }
            finally
            {
                DestroyImmediate(instance);
            }
            GUIUtility.ExitGUI(); // value changed mid-GUI — abort this pass cleanly so the inspector rebuilds.
        }

        // "URP" / "HDRP" for the active render pipeline asset, or null for Built-in / unrecognized.
        private static string ActivePipelineSuffix()
        {
            var rp = GraphicsSettings.currentRenderPipeline != null
                ? GraphicsSettings.currentRenderPipeline
                : GraphicsSettings.defaultRenderPipeline;
            if (rp == null) return null;
            var n = rp.GetType().FullName ?? string.Empty;
            if (n.Contains("Universal")) return "URP";
            if (n.Contains("HighDefinition")) return "HDRP";
            return null;
        }

        // The "RoundedRectTooltip_<suffix>" material in the project (exact suffix match — never guesses a pipeline).
        private static Material FindPipelineMaterial(string suffix)
        {
            foreach (var guid in AssetDatabase.FindAssets("RoundedRectTooltip t:Material"))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (m != null && m.name.EndsWith("_" + suffix)) return m;
            }
            return null;
        }

        // The PooledTooltipView's serialized `background` Renderer on a prefab root (private field, read via SO).
        private static Renderer GetBackgroundRenderer(GameObject root)
        {
            var view = root.GetComponentInChildren<PooledTooltipView>(true);
            if (view == null) return null;
            var prop = new SerializedObject(view).FindProperty("background");
            return prop != null ? prop.objectReferenceValue as Renderer : null;
        }

        private static Material GetBackgroundMaterial(GameObject root)
        {
            var bg = GetBackgroundRenderer(root);
            return bg != null ? bg.sharedMaterial : null;
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
