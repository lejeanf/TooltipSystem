#if UNITY_EDITOR
using System.Reflection;
using jeanf.validationTools;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace jeanf.tooltip
{
    /// <summary>
    /// Draws the inspector field-by-field so every setup issue appears inline, directly under the
    /// serialized value that causes it (the unset Marker Material is handled by its [Validation]
    /// drawer). Scene-level checks (navmesh, Player tag, LineRenderer) stay at the bottom.
    /// </summary>
    [CustomEditor(typeof(NavigationTooltip))]
    public class NavigationTooltipEditor : Editor
    {
        private bool _hasNavMesh;

        private void OnEnable()
        {
            // Checked once per selection — CalculateTriangulation is too heavy for every repaint.
            _hasNavMesh = NavMesh.CalculateTriangulation().indices.Length > 0;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var mode = (NavigationPathMode)serializedObject.FindProperty("pathMode").enumValueIndex;
            var style = (NavigationPathStyle)serializedObject.FindProperty("pathStyle").enumValueIndex;
            bool centerLegs = serializedObject.FindProperty("centerLegs").boolValue;
            bool showTarget = serializedObject.FindProperty("showTargetMarker").boolValue;

            SerializedProperty property = serializedObject.GetIterator();
            bool enterChildren = true;
            while (property.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (!IsRelevant(property.name, mode, style, centerLegs, showTarget)) continue;
                if (property.name == "markerMaterial")
                    DrawMarkerMaterialRow(property);
                else
                    using (new EditorGUI.DisabledScope(property.name == "m_Script"))
                        EditorGUILayout.PropertyField(property, true);
                DrawInlineIssue(property);
            }
            serializedObject.ApplyModifiedProperties();

            DrawSceneLevelChecks();
        }

        /// <summary>Hides parameters that have no effect under the current path mode / style.</summary>
        private static bool IsRelevant(string name, NavigationPathMode mode, NavigationPathStyle style, bool centerLegs, bool showTarget)
        {
            switch (name)
            {
                case "centerLegs":
                    return mode == NavigationPathMode.Orthogonal;
                case "maxLegShift":
                    return mode == NavigationPathMode.Orthogonal && centerLegs;
                case "spacing":
                case "markerSize":
                case "markerStartOffset":
                case "markerEndOffset":
                    return style != NavigationPathStyle.Line;
                case "chevronWeight":
                    return style == NavigationPathStyle.Arrows;
                case "lineWidth":
                    return style == NavigationPathStyle.Line;
                case "targetSize":
                    return showTarget;
                default:
                    return true;
            }
        }

        private const float ButtonWidth = 66f; // same as the SO-channel drawer's Create button

        private const string UrpMaterialGuid = "6712bdfa68774946a84c09596df860cd";
        private const string HdrpMaterialGuid = "0d5a4d469eb24a01b2fae233868e2f2e";

        private static bool IsHdrp =>
            UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null &&
            UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.GetType().FullName.Contains("HighDefinition");

        private static string ExpectedShaderName => IsHdrp ? "jeanf/Tooltip/NavigationMarker HDRP" : "jeanf/Tooltip/NavigationMarker URP";
        private static string ExpectedMaterialName => IsHdrp ? "NavigationMarker_HDRP" : "NavigationMarker_URP";

        private static string _unsetMessage;
        private static string UnsetMessage
        {
            get
            {
                if (_unsetMessage == null)
                {
                    FieldInfo field = typeof(NavigationTooltip).GetField("markerMaterial", BindingFlags.Instance | BindingFlags.NonPublic);
                    _unsetMessage = field?.GetCustomAttribute<ValidationAttribute>()?.Text ?? "'Marker Material' is not assigned.";
                }
                return _unsetMessage;
            }
        }

        /// <summary>Marker material field with a context-aware Assign / Create / Fix button, like the SO-channel drawer.</summary>
        private static void DrawMarkerMaterialRow(SerializedProperty property)
        {
            var current = property.objectReferenceValue as Material;
            if (current == null)
            {
                // Re-create the [Validation] unset styling by hand: the drawer would clamp its help
                // box to the field column, and here it should span the full width, button included.
                Rect block = EditorGUILayout.BeginVertical();
                if (Event.current.type == EventType.Repaint)
                    EditorGUI.DrawRect(block, ValidationUi.OrangeWash);
                EditorGUILayout.HelpBox(UnsetMessage, MessageType.Warning);

                EditorGUILayout.BeginHorizontal();
                Color previousColor = GUI.backgroundColor;
                GUI.backgroundColor = ValidationUi.Orange;
                EditorGUILayout.ObjectField(property, typeof(Material));
                GUI.backgroundColor = previousColor;
                string action = FindExpectedMaterial() != null ? "Assign" : "Create";
                if (GUILayout.Button(action, GUILayout.Width(ButtonWidth)))
                    ApplyMaterialAction(property, null, action);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                return;
            }

            // Assigned: normal field; Fix appears only for wrong-pipeline or instancing-off materials.
            // A correct-variant shader that failed to compile has no auto-fix — the inline box explains.
            bool fixable = IsWrongPipeline(current) || !current.enableInstancing;
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(property, true);
            if (fixable && GUILayout.Button("Fix", GUILayout.Width(ButtonWidth)))
                ApplyMaterialAction(property, current, "Fix");
            EditorGUILayout.EndHorizontal();
        }

        private static void ApplyMaterialAction(SerializedProperty property, Material current, string action)
        {
            switch (action)
            {
                case "Assign":
                {
                    Material found = FindExpectedMaterial();
                    if (found == null) return;
                    property.objectReferenceValue = found;
                    EditorGUIUtility.PingObject(found);
                    break;
                }
                case "Create":
                {
                    Material created = CreateMarkerMaterial();
                    if (created != null) property.objectReferenceValue = created;
                    break;
                }
                case "Fix":
                {
                    if (IsWrongPipeline(current))
                    {
                        Material replacement = FindExpectedMaterial();
                        if (replacement == null) replacement = CreateMarkerMaterial();
                        if (replacement == null) return;
                        EnsureInstancing(replacement);
                        property.objectReferenceValue = replacement;
                        EditorGUIUtility.PingObject(replacement);
                    }
                    else
                    {
                        EnsureInstancing(current);
                    }
                    break;
                }
            }
        }

        private static bool IsWrongPipeline(Material material)
        {
            string shaderName = material.shader != null ? material.shader.name : string.Empty;
            if (IsHdrp) return shaderName.EndsWith("URP");
            return UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null && shaderName.EndsWith("HDRP");
        }

        private static Material FindExpectedMaterial()
        {
            // The shipped asset first (stable GUID), then any material with the expected name.
            string path = AssetDatabase.GUIDToAssetPath(IsHdrp ? HdrpMaterialGuid : UrpMaterialGuid);
            Material material = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;

            foreach (string guid in AssetDatabase.FindAssets($"t:Material {ExpectedMaterialName}"))
            {
                material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (material != null && material.name == ExpectedMaterialName) return material;
            }
            return null;
        }

        private static Material CreateMarkerMaterial()
        {
            Shader shader = Shader.Find(ExpectedShaderName);
            if (shader == null)
            {
                EditorUtility.DisplayDialog("Tooltip System",
                    $"Shader '{ExpectedShaderName}' was not found — is the TooltipSystem package imported correctly?", "OK");
                return null;
            }
            string path = EditorUtility.SaveFilePanelInProject("Save Navigation Marker Material",
                ExpectedMaterialName + ".mat", "mat", "Choose where to save the marker material.");
            if (string.IsNullOrEmpty(path)) return null;

            var material = new Material(shader) { enableInstancing = true };
            AssetDatabase.CreateAsset(material, path);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(material);
            return material;
        }

        private static void EnsureInstancing(Material material)
        {
            if (material.enableInstancing) return;
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
        }

        private static void DrawInlineIssue(SerializedProperty property)
        {
            string issue = null;
            switch (property.name)
            {
                case "markerMaterial":
                    issue = NavigationTooltip.GetMaterialProblem(property.objectReferenceValue as Material);
                    break;
                case "destinationThreshold":
                    if (property.floatValue <= 0f)
                        issue = "Must be greater than 0 — otherwise the player is never detected as arrived.";
                    break;
                case "navmeshDetectionDistance":
                    if (property.floatValue <= 0f)
                        issue = "Must be greater than 0 — player/target can never be snapped onto the navmesh.";
                    break;
                case "pathSamplingRate":
                    if (property.floatValue < 0f)
                        issue = "Cannot be negative.";
                    break;
            }
            if (issue != null) EditorGUILayout.HelpBox(issue, MessageType.Error);
        }

        private void DrawSceneLevelChecks()
        {
            var tooltip = (NavigationTooltip)target;

            EditorGUILayout.Space();
            int problems = 0;
            if (!_hasNavMesh)
            {
                problems++;
                EditorGUILayout.HelpBox(
                    "No baked navmesh in the loaded scenes — the path cannot be computed. " +
                    "(Re-select this object to re-check after baking or loading the navmesh scene.)",
                    MessageType.Error);
            }
            try
            {
                if (GameObject.FindGameObjectWithTag("Player") == null)
                {
                    problems++;
                    EditorGUILayout.HelpBox("No GameObject tagged 'Player' in the scene — the path origin cannot be resolved.", MessageType.Error);
                }
            }
            catch (UnityException)
            {
                problems++;
                EditorGUILayout.HelpBox("The 'Player' tag does not exist in this project — the path origin cannot be resolved.", MessageType.Error);
            }
            if (tooltip.GetComponent<LineRenderer>() == null)
            {
                problems++;
                EditorGUILayout.HelpBox("Missing LineRenderer component (required for the Line style).", MessageType.Error);
            }

            bool materialAssigned = serializedObject.FindProperty("markerMaterial").objectReferenceValue != null;
            if (problems == 0 && tooltip.IsValid && materialAssigned)
                EditorGUILayout.HelpBox("Setup checks passed.", MessageType.Info);

            if (Application.isPlaying && GUILayout.Button("Log diagnostics"))
                tooltip.LogDiagnostics();
        }
    }
}
#endif
