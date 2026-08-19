#if UNITY_EDITOR
using jeanf.scenemanagement;
using jeanf.validationTools;
using UnityEditor;
using UnityEngine;

namespace jeanf.tooltip
{
    /// <summary>
    /// Inspector + live scene preview for <see cref="TooltipAuthoring"/>. With just a prefab
    /// reference a SubScene tooltip would be invisible while authoring, so while the authoring is
    /// selected this spawns hidden (HideAndDontSave — never saved, gone on deselect) children:
    /// the tooltip prefab itself at the exact runtime spawn pose (gaze target, candidate
    /// positions, controller gizmos) plus the pooled pill configured from the prefab's controller,
    /// the same no-pool-needed preview the controller's own inspector uses. The inspector also
    /// reports which zone the position auto-detects to (or warns when it is outside every volume).
    /// </summary>
    [CustomEditor(typeof(TooltipAuthoring))]
    public class TooltipAuthoringEditor : Editor
    {
        private const string PreviewName = "[TooltipAuthoringPreview]";

        private GameObject _prefabPreview;
        private PooledTooltipView _viewPreview;
        private GameObject _appliedPrefab;

        private void OnEnable()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            BuildPreview();
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            DestroyPreview();
        }

        private void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode) DestroyPreview();
        }

        public override void OnInspectorGUI()
        {
            // This custom editor replaces the validation fallback inspector, so restore its banner.
            ValidationUi.DrawIssuesBanner(target as Component);

            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(TooltipAuthoring.tooltipPrefab)));
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(TooltipAuthoring.zone)),
                new GUIContent("Zone Override"));
            serializedObject.ApplyModifiedProperties();

            var authoring = (TooltipAuthoring)target;
            DrawZoneStatus(authoring);

            if (!Application.isPlaying && authoring.tooltipPrefab != _appliedPrefab) BuildPreview();
        }

        private static void DrawZoneStatus(TooltipAuthoring authoring)
        {
            if (authoring.zone != null)
            {
                EditorGUILayout.HelpBox($"Zone override: '{authoring.zone.zoneName}' — auto-detection is skipped.",
                    MessageType.Info);
                return;
            }

            var detected = TooltipAuthoring.DetectZoneAt(authoring.transform.position);
            if (detected != null)
                EditorGUILayout.HelpBox($"Zone auto-detected: '{detected.zoneName}' (zone volume containing this " +
                    "position — resolved again at runtime, so moving the tooltip re-detects).", MessageType.Info);
            else
                EditorGUILayout.HelpBox("Outside every zone volume in the OPEN scenes. If the volume lives in a " +
                    "scene that isn't loaded right now, runtime detection will still find it; otherwise set a " +
                    "Zone Override or the tooltip will never show.", MessageType.Warning);
        }

        // --- Scene preview -------------------------------------------------------

        private void BuildPreview()
        {
            DestroyPreview();
            if (Application.isPlaying) return;

            var authoring = (TooltipAuthoring)target;
            _appliedPrefab = authoring.tooltipPrefab;
            if (authoring.tooltipPrefab == null) return;

            // The tooltip prefab at the exact runtime spawn pose, parented under the authoring so it
            // follows while being placed (the runtime spawn composes the same world pose).
            _prefabPreview = Instantiate(authoring.tooltipPrefab, authoring.transform);
            _prefabPreview.name = PreviewName;
            _prefabPreview.hideFlags = HideFlags.HideAndDontSave;
            var t = _prefabPreview.transform;
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;

            var controller = _prefabPreview.GetComponentInChildren<InteractableTooltipController>(true);
            if (controller == null) return; // other tooltip families: the prefab contents are the preview

            var viewPrefab = CustomInspectorInstanciateTooltip.ResolveViewPrefab();
            if (viewPrefab == null) return; // no pooled view prefab anywhere — nothing visual to show

            var viewGo = Instantiate(viewPrefab.gameObject, controller.transform);
            viewGo.name = PreviewName;
            viewGo.hideFlags = HideFlags.HideAndDontSave;
            _viewPreview = viewGo.GetComponent<PooledTooltipView>();
            if (_viewPreview == null) return;
            ConfigureView(controller);
        }

        // Mirrors the essentials of the controller inspector's ConfigurePreview: content, overrides,
        // colours, billboard on/off and the expanded morph. Per-axis billboard constraint previewing
        // stays in the controller's own inspector (open the prefab to tune those).
        private void ConfigureView(InteractableTooltipController controller)
        {
            var so = new SerializedObject(_viewPreview);
            var content = so.FindProperty("previewContentSo");
            if (content != null) content.objectReferenceValue = controller.ActionContentSo;
            var icon = so.FindProperty("iconOnRight");
            if (icon != null) icon.boolValue = controller.IconOnRightDefault;
            so.ApplyModifiedProperties();

            _viewPreview.SetPreviewContentOverride(
                controller.UseCustomIcon ? controller.CustomIconSprite : null,
                !controller.IsTextShown);
            _viewPreview.SetColorOverride(
                controller.OverrideColor ? controller.TooltipColor : (Color?)null,
                controller.OverrideColor ? controller.TooltipContentColor : (Color?)null);

            bool billboard = controller.BillboardModeDefault != BillboardMode.Never;
            _viewPreview.SetEditorBillboard(billboard);
            if (!billboard) _viewPreview.transform.rotation = controller.transform.rotation;

            _viewPreview.SetEditorExpanded(true); // expanded pill; also keeps the editor tick alive
            _viewPreview.SyncPreviewPos(controller.transform.position);
        }

        private void OnSceneGUI()
        {
            // The view pins its own world position, so re-sync while the authoring is dragged.
            if (_viewPreview == null || _prefabPreview == null) return;
            var controller = _prefabPreview.GetComponentInChildren<InteractableTooltipController>(true);
            if (controller != null) _viewPreview.SyncPreviewPos(controller.transform.position);
        }

        private void DestroyPreview()
        {
            _viewPreview = null;
            _appliedPrefab = null;
            if (_prefabPreview != null)
            {
                DestroyImmediate(_prefabPreview);
                _prefabPreview = null;
            }

            // Sweep leftovers from a previous selection that never reached OnDisable (domain reload…).
            var authoring = target as TooltipAuthoring;
            if (authoring == null) return;
            for (int i = authoring.transform.childCount - 1; i >= 0; i--)
            {
                var child = authoring.transform.GetChild(i);
                if (child != null && child.name == PreviewName) DestroyImmediate(child.gameObject);
            }
        }
    }
}
#endif
