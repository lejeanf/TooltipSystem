#if UNITY_EDITOR
using System.Collections.Generic;
using jeanf.tooltip;
using jeanf.universalplayer;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(InteractableTooltipController))]
[CanEditMultipleObjects]
public class CustomInspectorInstanciateTooltip : Editor
{
    // Multi-selection edits the shared serialized fields; the per-instance blocks (scene preview, candidate
    // positions, live debug panel, scene handles) are single-selection only.
    private bool Multi => targets.Length > 1;

    private const string PreviewName = "[TooltipPreview]";

    private GameObject _preview;
    private int _previewPos;                                        // 0 = None, 1 = Base, 2+ = candidate index
    private int _appliedPreviewPos = -1;                            // last position applied to the preview (switch -> sequenced move)
    private bool _appliedIconSide;                                  // last icon side applied (change -> sequenced move)
    private BroadcastControlsStatus.ControlScheme _previewMode = BroadcastControlsStatus.ControlScheme.KeyboardMouse;
    private enum PreviewState { Expanded, Minimized, Auto }          // Auto = driven by the scene-camera proxy
    private PreviewState _previewState = PreviewState.Expanded;
    private bool _showRanges = true;                                // draw range/trigger gizmos in the scene
    private bool _followBest = true;                                // preview follows the best candidate for the scene cam
    private bool _previewFoldout = true;                            // collapse the scene-preview block
    private bool _sceneMulti;                                       // cached Multi for OnSceneGUI (can't read `targets` there)
    private int _tab;                                               // 0 = Content, 1 = In-world, 2 = Debug

    private void OnEnable()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;

        _sceneMulti = Multi; // cache here: OnSceneGUI must not touch the `targets` array

        var controller = target as InteractableTooltipController;
        if (controller == null || Application.isPlaying || Multi) return;

        // Auto-visualise on selection: default to the first ASSIGNED candidate position, or the script root
        // (Base) when none are actually informed. Counting arraySize alone would preview an empty/placeholder
        // slot (a candidate list with rows but no Transform assigned), so find the first non-null entry.
        var anchors = serializedObject.FindProperty("candidateAnchors");
        int firstAssigned = -1;
        if (anchors != null && anchors.isArray)
            for (int i = 0; i < anchors.arraySize; i++)
                if (anchors.GetArrayElementAtIndex(i).objectReferenceValue != null) { firstAssigned = i; break; }
        _previewPos = firstAssigned >= 0 ? firstAssigned + 2 : 1; // 1 = Base (this object)
        BuildPreview(controller, false); // quiet: don't warn about a missing pool just for selecting
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
        var controller = target as InteractableTooltipController;
        if (controller == null) return;

        serializedObject.Update();

        // Grayed-out "Script" field, same as Unity's default inspector — lets you double-click the classname
        // here to open the script, which a fully custom OnInspectorGUI otherwise hides.
        using (new EditorGUI.DisabledScope(true))
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));

        // Multi-selection: edit the shared fields only. PropertyFields on a multi-target serializedObject
        // already handle mixed values; the per-instance blocks below (preview, candidate list, debug) are
        // meaningless across several tooltips at once, so skip them.
        if (Multi)
        {
            EditorGUILayout.HelpBox(
                $"Editing {targets.Length} tooltips — shared settings only. Scene preview, candidate positions " +
                "and the debug panel need a single selection.", MessageType.None);

            var mExclude = new List<string>
            {
                "m_Script", "candidateAnchors", "showTooltip",
                "enableRepositioning", "distanceWeight", "rejectOccluded", "obstacleMask",
                "tooltipGameObjectPrefab", "inputIconSo", "interactableTooltipInputSo"
            };
            // Same contextual hiding as single-select, but only when every selected tooltip agrees (a mixed
            // selection keeps the field visible so the shared edit isn't silently hidden).
            var mBillboard = serializedObject.FindProperty("billboardMode");
            if (mBillboard != null && !mBillboard.hasMultipleDifferentValues
                && mBillboard.enumValueIndex == (int)BillboardMode.Never)
                mExclude.Add("billboardConstraints");

            DrawPropertiesExcluding(serializedObject, mExclude.ToArray());
            DrawRepositioning();
            serializedObject.ApplyModifiedProperties();
            return;
        }

        // Tabs: Content = what the tooltip is / when it shows; In-world = how it looks & where it sits;
        // Debug = the isDebug toggle + live gate state.
        EditorGUILayout.Space();
        _tab = GUILayout.Toolbar(_tab, TabLabels);
        EditorGUILayout.Space(2);

        if (_tab == 0) DrawContentTab();
        else if (_tab == 1) DrawInWorldTab(controller);
        else DrawDebugState(controller);

        serializedObject.ApplyModifiedProperties();

        if (_preview != null) ConfigurePreview(controller); // keep preview in sync with field edits

        // Force a repaint while playing on the Debug tab so the live gate state updates each frame.
        if (Application.isPlaying && _tab == 2) Repaint();
    }

    private static readonly string[] TabLabels = { "Content", "In-world", "Debug" };

    // Draw a serialized property by name (with children). Used for the explicit per-tab field ordering.
    private void Prop(string name)
    {
        var p = serializedObject.FindProperty(name);
        if (p != null) EditorGUILayout.PropertyField(p, true);
    }

    // "Content" tab — what the tooltip is and when it shows.
    private void DrawContentTab()
    {
        Prop("objectToBeViewed");
        Prop("currentZone");
        Prop("actionContentSo");
        Prop("interactableTooltipSettingsSo");
        EditorGUILayout.Space();
        Prop("onClick");
        Prop("clickMinimizeDuration");
    }

    // "In-world" tab — how the tooltip looks and where it sits (orientation, rendering, placement, preview).
    private void DrawInWorldTab(InteractableTooltipController controller)
    {
        // Appearance / orientation.
        Prop("iconOnRight");
        Prop("billboardMode");
        if (controller.UsesCandidatesEditor)
        {
            EditorGUILayout.HelpBox(
                "Billboarding and its limits are set per candidate position (see \"Selected position overrides\" " +
                "below). The general billboard settings apply only when there are no candidate positions.",
                MessageType.None);
        }
        else if (controller.BillboardModeDefault != BillboardMode.Never)
        {
            // Not billboarding -> no per-axis limits to set (orient via the Scene rotation handle instead).
            Prop("billboardConstraints");
        }

        // Rendering (pooling is always on) — how close the player must be for the tooltip to appear at all.
        EditorGUILayout.Space();
        Prop("showDistance");

        // Placement. Candidate positions list is drawn LAST (per request); the overrides / preview above
        // act on whichever position is selected in it.
        DrawRepositioning();
        DrawSelectedPositionOverrides(controller);
        DrawScenePreview(controller);
        DrawCandidatePositions(controller);
    }

    // Flat (no foldout) — short block, kept inline to reduce the number of collapsibles in this tab.
    private void DrawRepositioning()
    {
        EditorGUILayout.Space();
        var enableProp = serializedObject.FindProperty("enableRepositioning");
        if (enableProp != null) EditorGUILayout.PropertyField(enableProp);
        // The scoring knobs only matter once repositioning is on — hide them otherwise.
        if (enableProp != null && enableProp.boolValue)
        {
            foreach (var propName in new[] { "distanceWeight", "rejectOccluded", "obstacleMask" })
            {
                var p = serializedObject.FindProperty(propName);
                if (p != null) EditorGUILayout.PropertyField(p);
            }
            EditorGUILayout.HelpBox(
                "Evaluation Interval and Reposition Hysteresis moved to TooltipPoolManager — they affect performance, " +
                "not appearance, so they're tuned once for every tooltip instead of per instance.",
                MessageType.None);
        }
    }

    // Live gate-state panel — its own inspector tab. Read-only; values update each frame in play mode
    // (OnInspectorGUI forces a repaint while the Debug tab is active).
    private void DrawDebugState(InteractableTooltipController controller)
    {
        Prop("isDebug");
        EditorGUILayout.Space();

        if (!Application.isPlaying)
            EditorGUILayout.HelpBox("Enter Play mode for live gate state (zone / proximity / looking update each frame).", MessageType.None);

        EditorGUILayout.LabelField("Pooled", $"{controller.Dbg_Pooled}   ·   show state: {controller.Dbg_ShowState}");
        EditorGUILayout.LabelField("Distance to viewpoint", controller.Dbg_DistanceToViewpoint >= 0f
            ? $"{controller.Dbg_DistanceToViewpoint:0.0} m   (to the head / main camera)"
            : "-");
        DrawDebugBool("In zone", controller.Dbg_InZone);
        DrawDebugBool("Near (within range)", controller.Dbg_Near);
        DrawDebugBool("Looking at it", controller.Dbg_Looking);
        DrawDebugBool("Maximized", controller.Dbg_Maximized);
        DrawDebugBool("Permission manager present", controller.Dbg_PermissionManagerPresent);
        EditorGUILayout.LabelField("Player zone", controller.Dbg_PlayerZone);
        EditorGUILayout.LabelField("Tooltip zone", controller.Dbg_TargetZone);

        DrawDebugCandidates(controller);
    }

    // Live candidate scoring (play mode): each candidate's score with the player-facing + distance breakdown,
    // the selected one highlighted, and the would-be pick (highest score) flagged so a pending switch is visible.
    private static void DrawDebugCandidates(InteractableTooltipController controller)
    {
        EditorGUILayout.Space();
        if (!controller.Dbg_Repositioning)
        {
            EditorGUILayout.LabelField("Candidates", controller.Dbg_CandidateCount == 0
                ? "repositioning off — fixed at the object"
                : "repositioning off (enable it to use the candidates)");
            return;
        }

        if (!Application.isPlaying)
        {
            EditorGUILayout.LabelField("Candidates",
                $"{controller.Dbg_CandidateCount} — live scoring in Play mode (Scene view shows the edit-mode scoring)");
            return;
        }

        var cands = controller.Dbg_Candidates();
        EditorGUILayout.LabelField($"Candidates — {cands.Count} (selection = highest score)", EditorStyles.miniBoldLabel);

        // Find the best-scoring candidate so we can flag it (it may differ from the selected one mid-hysteresis).
        int bestIdx = -1; float bestScore = float.NegativeInfinity;
        for (int i = 0; i < cands.Count; i++)
            if (cands[i].scored && cands[i].score > bestScore) { bestScore = cands[i].score; bestIdx = i; }

        var prev = GUI.color;
        for (int i = 0; i < cands.Count; i++)
        {
            var c = cands[i];
            string detail = c.scored
                ? $"score {c.score:0.00}   (facing {c.facing:+0.00;-0.00}, {c.dist:0.0} m)"
                : c.occluded ? "occluded — rejected" : "not scorable";

            string marker = c.selected ? "● in use" : (i == bestIdx ? "○ best" : "");
            GUI.color = c.selected ? new Color(0.45f, 0.9f, 0.45f)
                      : i == bestIdx ? new Color(0.9f, 0.85f, 0.4f)
                      : c.scored ? Color.white : new Color(0.7f, 0.7f, 0.7f);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(c.label, GUILayout.Width(160f));
            EditorGUILayout.LabelField(detail);
            if (!string.IsNullOrEmpty(marker)) EditorGUILayout.LabelField(marker, GUILayout.Width(60f));
            EditorGUILayout.EndHorizontal();
        }
        GUI.color = prev;

        if (bestIdx >= 0 && !cands[bestIdx].selected)
            EditorGUILayout.HelpBox("The current pick isn't the highest right now — it keeps a +hysteresis bias and re-evaluates on the interval, so it'll switch shortly.", MessageType.None);
    }

    private static void DrawDebugBool(string label, bool value)
    {
        var prev = GUI.color;
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(label, GUILayout.Width(200f));
        GUI.color = value ? new Color(0.45f, 0.9f, 0.45f) : new Color(0.9f, 0.55f, 0.5f);
        EditorGUILayout.LabelField(value ? "● yes" : "○ no");
        GUI.color = prev;
        EditorGUILayout.EndHorizontal();
    }

    // Pure scene visualization: which position/mode/state to show, range gizmos, and the play-mode force-show.
    // Per-candidate overrides live in DrawSelectedPositionOverrides (they used to be mixed in here, which made
    // this block read as "just a preview" when it actually WROTE to the selected position's TooltipAnchor).
    private void DrawScenePreview(InteractableTooltipController controller)
    {
        EditorGUILayout.Space();
        _previewFoldout = EditorGUILayout.Foldout(_previewFoldout, "Scene preview", true, EditorStyles.foldoutHeader);
        if (!_previewFoldout) return;

        // Build the "preview at" options: None, Base, then each candidate position.
        var anchors = serializedObject.FindProperty("candidateAnchors");
        int anchorCount = anchors != null && anchors.isArray ? anchors.arraySize : 0;

        var options = new List<string> { "None", "Base (this object)" };
        for (int i = 0; i < anchorCount; i++) options.Add($"Position {i}");

        EditorGUI.BeginChangeCheck();
        using (new EditorGUI.DisabledScope(_followBest && anchorCount > 0))
            _previewPos = EditorGUILayout.Popup("Preview at", Mathf.Clamp(_previewPos, 0, options.Count - 1), options.ToArray());
        bool posChanged = EditorGUI.EndChangeCheck();
        if (posChanged) _followBest = false; // picking a position locks focus to it (stop auto-following)

        EditorGUI.BeginChangeCheck();
        _previewMode = (BroadcastControlsStatus.ControlScheme)EditorGUILayout.EnumPopup("Preview mode", _previewMode);
        _previewState = (PreviewState)EditorGUILayout.EnumPopup(new GUIContent("Preview state",
            "Expanded / Minimized force a state; Auto follows the scene-camera proxy (in range + looking = maximized)."), _previewState);
        bool otherChanged = EditorGUI.EndChangeCheck();

        // Auto-follow the best candidate for the scene camera (off = keep the selected position focused for setup).
        using (new EditorGUI.DisabledScope(anchorCount == 0))
            _followBest = EditorGUILayout.Toggle(new GUIContent("Follow best candidate",
                "On: the preview switches to the position the runtime picker would choose for the scene camera. Off: stays on the position above so you can set it up."), anchorCount > 0 && _followBest);

        if (posChanged || otherChanged)
        {
            if (_previewPos == 0) DestroyPreview();
            else if (_preview != null) ConfigurePreview(controller); // reuse -> lerps to the new target
            else BuildPreview(controller);
        }

        _showRanges = EditorGUILayout.Toggle(new GUIContent("Visualize ranges (scene)",
            "Draw the minimized range, the maximize gaze-cone state, and candidate scoring in the Scene view, using the Scene camera as a stand-in for the player."), _showRanges);

        // Editor-only override: force the real tooltip maximized in play mode, skipping every gate (zone /
        // proximity / looking / permission). Transient (resets on play) and compiled out of builds.
        using (new EditorGUI.DisabledScope(!Application.isPlaying))
            controller.EditorForceShow = EditorGUILayout.ToggleLeft(new GUIContent(
                Application.isPlaying ? "Force show (play mode override)" : "Force show (enter Play mode to use)",
                "Editor-only testing override: forces THIS tooltip maximized in play mode regardless of zone, range, gaze or permission. Not saved; never affects a build."),
                Application.isPlaying && controller.EditorForceShow);

        if (controller.ActionContentSo == null)
            EditorGUILayout.HelpBox("Assign an Action Content SO to preview each mode's icon/text. Without it the preview shows the prefab's placeholder content.", MessageType.None);
    }

    // Per-candidate overrides for the position currently selected in Scene preview: its TooltipAnchor's icon
    // side, billboarding and axis limits. Editing here WRITES to that TooltipAnchor (with Undo) — it is not just
    // a preview. Pick which position in "Scene preview" below. One dropdown each (Inherit / Left / Right and
    // Inherit / Always / Never) — no two-checkbox "override" trap.
    private void DrawSelectedPositionOverrides(InteractableTooltipController controller)
    {
        EditorGUILayout.Space();

        if (_previewPos < 2) // None / Base -> no candidate selected; the defaults live above in this tab.
        {
            EditorGUILayout.HelpBox(
                "Pick a candidate position in \"Scene preview\" below to override its icon side, billboarding and " +
                "axis limits. With none selected, the tooltip uses its defaults (Icon side + Billboard Mode above).",
                MessageType.None);
            return;
        }

        var anchorTf = GetPreviewAnchorTransform();
        if (anchorTf == null) return;

        var anchor = anchorTf.GetComponent<TooltipAnchor>();
        if (anchor == null)
        {
            EditorGUILayout.HelpBox("This position has no TooltipAnchor, so it uses the tooltip defaults.", MessageType.Info);
            if (GUILayout.Button("Add TooltipAnchor to this position"))
            {
                Undo.AddComponent<TooltipAnchor>(anchorTf.gameObject);
                if (_preview != null) ConfigurePreview(controller);
            }
            return;
        }

        EditorGUI.BeginChangeCheck();
        var side = (TooltipAnchor.IconSide)EditorGUILayout.EnumPopup(
            new GUIContent("Icon side", "Icon side for this position. Inherit = use the tooltip's default."), anchor.iconSide);
        var bill = (TooltipAnchor.Billboard)EditorGUILayout.EnumPopup(
            new GUIContent("Billboard", "Billboarding for this position. Inherit = use the tooltip's Auto-orient mode."), anchor.billboard);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(anchor, "Edit Tooltip Anchor");
            anchor.iconSide = side;
            anchor.billboard = bill;
            EditorUtility.SetDirty(anchor);
            if (_preview != null) ConfigurePreview(controller); // flip the live preview immediately
        }

        // Per-position billboard axis limits (optional). Each candidate has its own orientation, so its allowed
        // facing arc is usually position-specific; off = inherit the controller's constraints.
        EditorGUI.BeginChangeCheck();
        bool ov = EditorGUILayout.ToggleLeft(new GUIContent("Override billboard limits",
            "Give this position its own yaw/pitch/roll limits instead of the controller's."), anchor.overrideBillboardConstraints);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(anchor, "Edit Tooltip Anchor");
            anchor.overrideBillboardConstraints = ov;
            EditorUtility.SetDirty(anchor);
            if (_preview != null) ConfigurePreview(controller);
        }
        if (anchor.overrideBillboardConstraints)
        {
            var aso = new SerializedObject(anchor);
            EditorGUI.indentLevel++;
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(aso.FindProperty("billboardConstraints"), true);
            EditorGUI.indentLevel--;
            if (EditorGUI.EndChangeCheck())
            {
                aso.ApplyModifiedProperties();
                if (_preview != null) ConfigurePreview(controller);
            }

            // The centre offset is the same DOF as this position's rotation: fold it into the Transform so the
            // candidate's own orientation becomes the limit's home and the scene handles line up with the world.
            var c = anchor.billboardConstraints;
            bool hasCenter = c.yawCenter != 0f || c.pitchCenter != 0f || c.rollCenter != 0f;
            using (new EditorGUI.DisabledScope(!hasCenter))
            {
                if (GUILayout.Button(new GUIContent("Align position to limit centre",
                    "Rotate this candidate's transform by the centre offsets and zero them — the band is unchanged but its home becomes the position's own forward, so the gizmo aligns with the world.")))
                {
                    Undo.RecordObject(anchorTf, "Align tooltip limit to position");
                    Undo.RecordObject(anchor, "Align tooltip limit to position");
                    anchorTf.rotation = anchorTf.rotation * Quaternion.Euler(c.pitchCenter, c.yawCenter, c.rollCenter);
                    c.yawCenter = c.pitchCenter = c.rollCenter = 0f;
                    EditorUtility.SetDirty(anchor);
                    if (_preview != null) ConfigurePreview(controller);
                }
            }
        }
    }

    private Transform GetPreviewAnchorTransform()
    {
        // Read the live list off the target (works in both OnInspectorGUI and OnSceneGUI, where the Editor's
        // serializedObject is off-limits).
        var anchors = (target as InteractableTooltipController)?.CandidateAnchorsEditor;
        int idx = _previewPos - 2;
        if (anchors == null || idx < 0 || idx >= anchors.Count) return null;
        return anchors[idx];
    }

    // Scene-view authoring: place the tooltip and edit its candidate positions with handles.
    private void OnSceneGUI()
    {
        // Single-selection only: the handles write to the target (e.g. the range radius), which on a
        // multi-target editor would silently stamp one tooltip's dragged value onto every selected tooltip.
        // Uses the cached flag because OnSceneGUI must not read the `targets` array.
        if (_sceneMulti) return;

        var controller = target as InteractableTooltipController;
        if (controller == null) return;

        // Draw all authoring gizmos ON TOP regardless of scene depth. URP and HDRP apply different default
        // Handles depth-tests, so under URP the range disc / radius handle were depth-occluded by geometry and
        // read as "missing"; forcing zTest = Always makes both pipelines render them identically. The
        // try/finally guarantees it's restored even if a draw throws.
        var prevZTest = Handles.zTest;
        Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
        try { DrawSceneGizmos(controller); }
        finally { Handles.zTest = prevZTest; }
    }

    private void DrawSceneGizmos(InteractableTooltipController controller)
    {
        Vector3 basePos = controller.transform.position;

        // Live, scene-camera-driven preview: auto-follow the best candidate and/or Auto expand-collapse.
        if (_preview != null)
        {
            if (_followBest && controller.EnableRepositioning && ComputeBestCandidate(controller, out int bestArrayIdx))
                _previewPos = bestArrayIdx + 2;

            // ConfigurePreview resolves position + Auto expand/minimize from the scene-camera proxy.
            if (_followBest || _previewState == PreviewState.Auto)
                ConfigurePreview(controller);

            // The minimized disc is runtime-sized (tiny from afar); ring it so it's locatable while authoring.
            if (!ResolvePreviewExpanded(controller))
            {
                Vector3 p = _preview.transform.position;
                var cam = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.camera : null;
                Vector3 normal = cam != null ? (p - cam.transform.position).normalized : Vector3.up;
                float s = HandleUtility.GetHandleSize(p) * 0.18f;
                Handles.color = new Color(1f, 0.65f, 0f, 0.9f);
                Handles.DrawWireDisc(p, normal, s);            // panel already labels the state; ring just locates it
            }
        }

        if (_showRanges) DrawRangeGizmos(controller);

        DrawBillboardConstraintHandles(controller);
        DrawOrientationHandle(controller);

        // Script root (Base) — just a marker (the object's own transform tool moves it).
        DrawTargetMarker(controller, 1, basePos, "root", Color.cyan);

        var anchors = controller.CandidateAnchorsEditor; // read off the target, not the Editor serializedObject
        if (anchors == null) return;

        for (int i = 0; i < anchors.Count; i++)
        {
            var anchor = anchors[i];
            if (anchor == null) continue;

            bool active = _previewPos == i + 2;
            Handles.color = active ? Color.green : new Color(1f, 0.8f, 0.2f, 0.5f);
            Handles.DrawDottedLine(basePos, anchor.position, 3f);

            // Only the active position gets a full move handle — keeps the view uncluttered. Click another
            // position's dot to activate it, then move it.
            if (active)
            {
                EditorGUI.BeginChangeCheck();
                Vector3 moved = Handles.PositionHandle(anchor.position, anchor.rotation);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(anchor, "Move Tooltip Candidate Position");
                    anchor.position = moved;
                    if (_preview != null) ConfigurePreview(controller); // follow the dragged anchor
                }
            }

            DrawTargetMarker(controller, i + 2, anchor.position, $"pos {i}", Color.yellow);
        }
    }

    // Visualises the runtime range/trigger logic using the Scene camera as a stand-in for the player, so
    // flying the Scene view shows when the tooltip would minimize / maximize and which candidate would win.
    private void DrawRangeGizmos(InteractableTooltipController controller)
    {
        Vector3 origin = controller.transform.position;
        float range = controller.ShowDistance;

        // --- Minimized range: an editable radius handle (drag to set) + faint disc. Measured from the
        // controller root at runtime; edits write straight back to the inspector field. ---
        if (range > 0f)
        {
            var blue = new Color(0.25f, 0.6f, 1f, 1f);
            Handles.color = blue;

            EditorGUI.BeginChangeCheck();
            float newRange = Handles.RadiusHandle(Quaternion.identity, origin, range);
            if (EditorGUI.EndChangeCheck())
            {
                // Local SerializedObject (not the Editor's) so the write is legal inside OnSceneGUI.
                var so = new SerializedObject(controller);
                var p = so.FindProperty("showDistance");
                if (p != null)
                {
                    p.floatValue = Mathf.Max(0f, newRange);
                    so.ApplyModifiedProperties(); // updates the inspector + Undo
                }
            }

            Handles.color = new Color(blue.r, blue.g, blue.b, 0.04f);
            Handles.DrawSolidDisc(origin, Vector3.up, range);
        }

        // --- Player proxy = Scene camera: live minimize/maximize state. ---
        var sv = SceneView.lastActiveSceneView;
        if (sv == null || sv.camera == null) return;
        Vector3 eye = sv.camera.transform.position;
        Vector3 eyeFwd = sv.camera.transform.forward;

        Transform lookT = controller.LookTarget;
        Vector3 toTarget = lookT.position - eye;
        float dist = toTarget.magnitude;

        bool inRange = range <= 0f || (origin - eye).magnitude <= range;
        float fovThresh = controller.FieldOfViewThreshold;
        float gazeDot = dist > 1e-4f ? Vector3.Dot(eyeFwd, toTarget / dist) : 1f;
        bool looking = gazeDot > fovThresh;
        float gazeAngle = Mathf.Acos(Mathf.Clamp(gazeDot, -1f, 1f)) * Mathf.Rad2Deg;
        float threshAngle = Mathf.Acos(Mathf.Clamp(fovThresh, -1f, 1f)) * Mathf.Rad2Deg;

        Color amber = new Color(1f, 0.7f, 0.2f);
        Color stateCol = !inRange ? Color.gray : looking ? Color.green : amber;
        string state = !inRange ? "OUT OF RANGE — minimized / none"
                     : looking ? "MAXIMIZED — looking"
                                : "MINIMIZED — in range, not looking";

        // Line of sight from the proxy to the look target (colour mirrors the state).
        Handles.color = new Color(stateCol.r, stateCol.g, stateCol.b, 0.85f);
        Handles.DrawDottedLine(eye, lookT.position, 2f);

        // Compose the readout in SCREEN space so the coloured text stays readable over any background.
        var lines = new List<(string text, Color col)>
        {
            ("TOOLTIP RANGES — player proxy = Scene cam", Color.white),
            (state, stateCol),
            ($"gaze {gazeAngle:0.#}°   (maximize ≤ {threshAngle:0.#}°)", looking ? Color.green : amber),
            ($"distance {(origin - eye).magnitude:0.#} m   /   range {(range > 0f ? range.ToString("0.#") + " m" : "no limit")}",
                inRange ? Color.green : Color.gray),
        };
        if (range > 0f) lines.Add(("drag the blue sphere handle to edit range", new Color(0.5f, 0.7f, 1f)));

        // Candidate scoring: colour-code the lines/markers + list scores in the panel (no floating numbers).
        if (controller.EnableRepositioning) AppendCandidateScores(controller, eye, lines);

        DrawReadoutPanel(lines);
    }

    // --- Billboard axis-limit handles ------------------------------------------------------------------
    // Visualises the per-axis billboard constraints around the (previewed) tooltip, in the rest frame: a
    // faint full ring for a free axis, a "locked" label for a disabled one, and a solid sector with two
    // draggable endpoint handles for a clamped one (drag writes the min/max straight back to the inspector).
    private void DrawBillboardConstraintHandles(InteractableTooltipController controller)
    {
        if (controller.BillboardModeEditor == BillboardMode.Never) return; // not billboarding -> nothing to limit

        Transform anchorTf = _previewPos >= 2 ? GetPreviewAnchorTransform() : null;

        // Effective constraints for the previewed position, and the object that OWNS them (the candidate's
        // TooltipAnchor when it overrides, else the controller) so handle drags write to the right place.
        var c = controller.BillboardConstraintsForEditor(anchorTf);
        Object owner = controller;
        if (anchorTf != null)
        {
            var a = anchorTf.GetComponent<TooltipAnchor>();
            if (a != null && a.ConstraintsOverride != null) owner = a;
        }
        if (c == null || c.IsUnconstrained) return; // free + upright: don't clutter the view

        var so = new SerializedObject(owner);

        Vector3 pos = _preview != null ? _preview.transform.position
                    : anchorTf != null ? anchorTf.position
                    : controller.transform.position;

        Quaternion rest = controller.BillboardRestForEditor(anchorTf);
        Vector3 fwd = rest * Vector3.forward;
        Vector3 up = rest * Vector3.up;
        Vector3 right = rest * Vector3.right;
        float radius = HandleUtility.GetHandleSize(pos) * 1.3f;

        // Chain the frames like the runtime gimbal (rest * Euler(pitch, yaw, roll)): yaw turns about rest up,
        // THEN pitch about the yaw-rotated right, THEN roll about the yaw+pitch-rotated forward. So the pitch
        // (red) and roll (blue) arcs sit in front of where the tooltip actually faces after the yaw/pitch
        // centre, instead of floating around the raw rest forward.
        Quaternion qYaw = Quaternion.AngleAxis(c.yawCenter, up);
        Vector3 fwdYaw = qYaw * fwd;       // yaw-centred forward (pitch's reference)
        Vector3 rightYaw = qYaw * right;   // yaw-rotated right  (pitch's axis)
        Quaternion qPitch = Quaternion.AngleAxis(c.pitchCenter, rightYaw);
        Vector3 fwdYP = qPitch * fwdYaw;   // yaw+pitch-centred forward (roll's axis = the home face dir)
        Vector3 upYP = qPitch * up;        // up after yaw (unchanged) then pitch (roll's reference)

        // The "home" facing the limits are measured from (rest forward).
        Handles.color = new Color(0.3f, 0.9f, 1f, 0.9f);
        Handles.DrawLine(pos, pos + fwd * radius);

        // Axis colours match Unity's rotation gizmo: yaw=Y (green), pitch=X (red), roll=Z (blue) — same as the
        // coloured titles in the inspector.
        DrawBillboardAxis(so, "Yaw",   "yawCenter",   "yawMin",   "yawMax",   pos, up,       fwd,    radius,         Handles.yAxisColor, c.yawAxis,   c.clampYaw,   c.yawCenter,   c.yawMin,   c.yawMax,   c.limitEaseDegrees);
        DrawBillboardAxis(so, "Pitch", "pitchCenter", "pitchMin", "pitchMax", pos, rightYaw, fwdYaw, radius * 0.85f, Handles.xAxisColor, c.pitchAxis, c.clampPitch, c.pitchCenter, c.pitchMin, c.pitchMax, c.limitEaseDegrees);
        DrawBillboardAxis(so, "Roll",  "rollCenter",  "rollMin",  "rollMax",  pos, fwdYP,    upYP,   radius * 0.7f,  Handles.zAxisColor, c.rollAxis,  c.clampRoll,  c.rollCenter,  c.rollMin,  c.rollMax,   c.limitEaseDegrees);

        // Hint that Alt mirrors an end-handle drag onto the opposite end (highlighted while Alt is held).
        bool mirroring = Event.current != null && Event.current.alt;
        if (_billboardHintStyle == null) _billboardHintStyle = new GUIStyle(EditorStyles.miniLabel);
        _billboardHintStyle.normal.textColor = mirroring ? new Color(1f, 0.9f, 0.3f) : new Color(1f, 1f, 1f, 0.6f);
        Handles.Label(pos - up * radius * 1.35f,
            mirroring ? "Alt: mirroring min/max" : "Hold Alt to mirror min/max", _billboardHintStyle);
        if (mirroring) SceneView.RepaintAll(); // keep the hint state live as Alt is pressed/released
    }

    private GUIStyle _billboardHintStyle;

    // --- Fixed-facing rotation handle -----------------------------------------------------------------
    // When the previewed target does NOT billboard, its orientation is authored by rotating the transform
    // (base = the controller, candidate = the position). Give it a proper rotation handle + a forward arrow.
    // Billboarding targets use the constraint arcs above instead — the two cases are mutually exclusive.
    private void DrawOrientationHandle(InteractableTooltipController controller)
    {
        if (GetPreviewBillboard(controller)) return; // billboarding -> the axis-limit arcs own the handles

        Transform anchorTf = _previewPos >= 2 ? GetPreviewAnchorTransform() : null;
        Transform target = anchorTf != null ? anchorTf : controller.transform;
        if (target == null) return;

        Vector3 pos = _preview != null ? _preview.transform.position
                    : anchorTf != null ? anchorTf.position
                    : controller.transform.position;

        // Forward arrow so the authored facing is obvious even before you grab the handle.
        Handles.color = new Color(0.3f, 0.9f, 1f, 0.95f);
        float len = HandleUtility.GetHandleSize(pos) * 1.1f;
        Handles.ArrowHandleCap(0, pos, target.rotation, len, EventType.Repaint);
        Handles.Label(pos + target.rotation * Vector3.forward * len,
            _previewPos >= 2 ? "facing (position)" : "facing (tooltip)");

        EditorGUI.BeginChangeCheck();
        Quaternion newRot = Handles.RotationHandle(target.rotation, pos);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(target, "Rotate Tooltip Facing");
            target.rotation = newRot;
            if (_preview != null) ConfigurePreview(controller); // push the new rest rotation to the live preview
            Repaint();
        }
    }

    private static float Wrap180(float a) => Mathf.Repeat(a + 180f, 360f) - 180f;

    private void DrawBillboardAxis(SerializedObject so, string label, string centerProp, string minProp, string maxProp,
        Vector3 center, Vector3 axis, Vector3 fromDir, float radius, Color col,
        bool enabled, bool clamped, float bandCenter, float min, float max, float ease)
    {
        if (!enabled)
        {
            Handles.color = new Color(col.r, col.g, col.b, 0.7f);
            Handles.Label(center + fromDir * radius * 1.02f, $"{label}: locked");
            return;
        }

        if (!clamped)
        {
            Handles.color = new Color(col.r, col.g, col.b, 0.18f);
            Handles.DrawWireDisc(center, axis, radius);
            Handles.color = new Color(col.r, col.g, col.b, 0.7f);
            Handles.Label(center + fromDir * radius * 1.02f, $"{label}: free");
            return;
        }

        // The band is [center+min, center+max] around the (movable) band center, drawn in wrapped space.
        Vector3 startDir = Quaternion.AngleAxis(bandCenter + min, axis) * fromDir;
        Handles.color = new Color(col.r, col.g, col.b, 0.12f);
        Handles.DrawSolidArc(center, axis, startDir, max - min, radius);
        Handles.color = col;
        Handles.DrawWireArc(center, axis, startDir, max - min, radius);

        // The eased "resistance" band before each limit: a denser fill + a radial tick where softening begins.
        float zone = BillboardConstraints.SoftZone(min, max, ease);
        if (zone > 0.01f)
        {
            Handles.color = new Color(col.r, col.g, col.b, 0.22f);
            Handles.DrawSolidArc(center, axis, Quaternion.AngleAxis(bandCenter + max - zone, axis) * fromDir, zone, radius);
            Handles.DrawSolidArc(center, axis, Quaternion.AngleAxis(bandCenter + min, axis) * fromDir, zone, radius);

            Handles.color = new Color(col.r, col.g, col.b, 0.55f);
            foreach (float a in new[] { bandCenter + min + zone, bandCenter + max - zone })
            {
                Vector3 d = Quaternion.AngleAxis(a, axis) * fromDir;
                Handles.DrawLine(center + d * radius * 0.9f, center + d * radius);
            }
        }

        // A line from the tooltip to the band center marks the "home" of the limit.
        Handles.color = new Color(col.r, col.g, col.b, 0.5f);
        Vector3 centerDir = Quaternion.AngleAxis(bandCenter, axis) * fromDir;
        Handles.DrawLine(center, center + centerDir * radius);

        // Handles: a cube at the band center rotates the whole band; spheres at the ends set min/max (relative).
        float newCenterAbs = DragAngle(center, axis, fromDir, radius, bandCenter, col, Handles.CubeHandleCap, 0.09f);
        float minAbs = DragAngle(center, axis, fromDir, radius, bandCenter + min, col);
        float maxAbs = DragAngle(center, axis, fromDir, radius, bandCenter + max, col);

        bool centerMoved = !Mathf.Approximately(newCenterAbs, bandCenter);
        float newMin = Mathf.DeltaAngle(bandCenter, minAbs); // ends are read relative to the OLD center
        float newMax = Mathf.DeltaAngle(bandCenter, maxAbs);
        bool minMoved = !Mathf.Approximately(newMin, min);
        bool maxMoved = !Mathf.Approximately(newMax, max);

        // Alt = mirror: dragging one end sets the other to its negative, keeping the band symmetric about the centre.
        if (Event.current != null && Event.current.alt)
        {
            if (minMoved && !maxMoved) { newMax = -newMin; maxMoved = true; }
            else if (maxMoved && !minMoved) { newMin = -newMax; minMoved = true; }
        }

        if (centerMoved || minMoved || maxMoved)
        {
            so.Update();
            var cp = so.FindProperty("billboardConstraints");
            if (centerMoved) cp.FindPropertyRelative(centerProp).floatValue = Wrap180(newCenterAbs);
            cp.FindPropertyRelative(minProp).floatValue = Mathf.Clamp(Mathf.Min(newMin, newMax), -180f, 180f);
            cp.FindPropertyRelative(maxProp).floatValue = Mathf.Clamp(Mathf.Max(newMin, newMax), -180f, 180f);
            so.ApplyModifiedProperties(); // updates the inspector + records Undo
        }

        Handles.color = col;
        Handles.Label(center + centerDir * radius * 1.06f, $"{label} ctr {Wrap180(bandCenter):0}°");
        Handles.Label(center + (Quaternion.AngleAxis(bandCenter + min, axis) * fromDir) * radius * 1.06f, $"{Wrap180(bandCenter + min):0}°");
        Handles.Label(center + (Quaternion.AngleAxis(bandCenter + max, axis) * fromDir) * radius * 1.06f, $"{Wrap180(bandCenter + max):0}°");
    }

    // A handle at `angle` on the arc; dragging it returns the new signed angle around `axis`.
    private static float DragAngle(Vector3 center, Vector3 axis, Vector3 fromDir, float radius, float angle, Color col,
        Handles.CapFunction cap = null, float sizeScale = 0.07f)
    {
        Vector3 p = center + (Quaternion.AngleAxis(angle, axis) * fromDir) * radius;
        float size = HandleUtility.GetHandleSize(p) * sizeScale;
        Handles.color = col;
        EditorGUI.BeginChangeCheck();
        Vector3 moved = Handles.FreeMoveHandle(p, size, Vector3.zero, cap ?? Handles.SphereHandleCap);
        if (EditorGUI.EndChangeCheck())
        {
            Vector3 v = Vector3.ProjectOnPlane(moved - center, axis);
            if (v.sqrMagnitude > 1e-6f)
                return Vector3.SignedAngle(fromDir, v.normalized, axis);
        }
        return angle;
    }

    // Mirrors InteractableTooltipController.EvaluateBestAnchor: a colour-coded line to each candidate
    // (green = the one that would be chosen) and the scores appended to the screen-space panel.
    private void AppendCandidateScores(InteractableTooltipController controller, Vector3 eye,
        List<(string text, Color col)> lines)
    {
        var anchors = controller.CandidateAnchorsEditor; // off the target (OnSceneGUI: no Editor serializedObject)
        if (anchors == null || anchors.Count == 0) return;

        float w = controller.DistanceWeight;
        Transform centerT = controller.LookTarget;
        Vector3 centerPos = centerT != null ? centerT.position : controller.transform.position;
        Vector3 dirCenterToEye = eye - centerPos;
        if (dirCenterToEye.sqrMagnitude > 1e-6f) dirCenterToEye.Normalize();

        var scores = new float[anchors.Count];
        var positions = new Vector3[anchors.Count];
        int bestIdx = -1;
        float bestScore = float.NegativeInfinity;

        for (int i = 0; i < anchors.Count; i++)
        {
            var t = anchors[i];
            scores[i] = float.NegativeInfinity;
            if (t == null) continue;
            positions[i] = t.position;

            Vector3 to = t.position - eye;
            float d = to.magnitude;
            if (d < 1e-4f) continue;

            // Position-based facing (mirrors EvaluateBestAnchor): which side of the object faces the player.
            Vector3 cToA = t.position - centerPos;
            float facingScore = cToA.sqrMagnitude > 1e-6f ? Vector3.Dot(dirCenterToEye, cToA.normalized) : 0f;
            float distScore = 1f / (1f + d);                // closer -> higher
            scores[i] = facingScore + w * distScore;
            if (scores[i] > bestScore) { bestScore = scores[i]; bestIdx = i; }
        }

        var amber = new Color(1f, 0.8f, 0.3f);
        lines.Add(("candidates (green = chosen):", Color.white));
        for (int i = 0; i < anchors.Count; i++)
        {
            if (float.IsNegativeInfinity(scores[i])) continue;
            bool best = i == bestIdx;

            Handles.color = best ? Color.green : new Color(amber.r, amber.g, amber.b, 0.5f);
            Handles.DrawDottedLine(eye, positions[i], 1f);

            lines.Add(($"   pos {i}:  {scores[i]:0.00}{(best ? "   ◄ best" : "")}", best ? Color.green : amber));
        }
    }

    // Fixed top-left screen-space panel with a dark background so the coloured text is always readable.
    private static void DrawReadoutPanel(List<(string text, Color col)> lines)
    {
        Handles.BeginGUI();

        const float pad = 6f, lineH = 15f, width = 300f;
        float height = pad * 2f + lines.Count * lineH;
        var box = new Rect(10f, 10f, width, height);

        EditorGUI.DrawRect(box, new Color(0f, 0f, 0f, 0.66f));

        var style = new GUIStyle(EditorStyles.miniBoldLabel);
        for (int i = 0; i < lines.Count; i++)
        {
            style.normal.textColor = lines[i].col;
            GUI.Label(new Rect(box.x + pad, box.y + pad + i * lineH, width - pad * 2f, lineH), lines[i].text, style);
        }

        Handles.EndGUI();
    }

    // Marks a target: the active one is a green dot (the move handle marks its spot); the others are
    // clickable dots that switch the preview to that target. One short label each.
    private void DrawTargetMarker(InteractableTooltipController controller, int posIndex, Vector3 pos, string label, Color baseColor)
    {
        bool active = _previewPos == posIndex;
        float size = HandleUtility.GetHandleSize(pos);

        if (active)
        {
            Handles.color = Color.green;
            Handles.SphereHandleCap(0, pos, Quaternion.identity, size * 0.08f, EventType.Repaint);
            Handles.Label(pos + Vector3.up * size * 0.22f, $"{label} ◄");
        }
        else
        {
            Handles.color = baseColor;
            if (Handles.Button(pos, Quaternion.identity, size * 0.08f, size * 0.12f, ColorSafeDotCap))
            {
                _previewPos = posIndex;
                _followBest = false; // clicking a position focuses it (stop auto-following)
                if (_preview != null) ConfigurePreview(controller); // reuse -> glides to the clicked target
                else BuildPreview(controller);
                Repaint();
            }
            Handles.Label(pos + Vector3.up * size * 0.22f, label);
        }
    }

    // Handles.DotHandleCap (and other built-in cap functions) share a batched draw under SRP (URP/HDRP): when
    // several are drawn in the same OnSceneGUI pass with different Handles.color values — like the root marker
    // (cyan) followed by each candidate marker (yellow) — URP renders them all in whatever color the FIRST one
    // in the batch used, so every dot after the root shows cyan instead of its own color. DrawSolidDisc isn't
    // part of that batched path and always respects the Handles.color set right before it, so use it for the
    // visible (Repaint) draw and fall back to the built-in cap only for hit-testing (Layout), which has no color.
    private static void ColorSafeDotCap(int controlId, Vector3 position, Quaternion rotation, float size, EventType eventType)
    {
        if (eventType == EventType.Repaint)
        {
            Camera cam = Camera.current;
            Vector3 normal = cam != null ? -cam.transform.forward : rotation * Vector3.forward;
            Handles.DrawSolidDisc(position, normal, size);
            return;
        }
        Handles.DotHandleCap(controlId, position, rotation, size, eventType);
    }

    private void DrawCandidatePositions(InteractableTooltipController controller)
    {
        var anchors = serializedObject.FindProperty("candidateAnchors");
        if (anchors == null) return;

        EditorGUILayout.Space();
        // Unity's built-in list drawer: add (+), remove (-) and drag-reorder, with its own collapse arrow.
        // Assign scene Transforms directly, or use the sphere buttons below to create positioned children.
        // (Removing here only unlinks the reference; it doesn't delete a generated TooltipPosition object.)
        EditorGUILayout.PropertyField(anchors, new GUIContent("Candidate positions (repositioning)"), true);

        // --- Quick setup: spread positions evenly on a sphere around the root (Fibonacci / golden spiral) ---
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Auto-place on a sphere", EditorStyles.miniBoldLabel);

        // Two 50/50 rows: [Count | Generate] then [Radius | Distribute]. Rect-split so each column is exactly
        // half the content width (labeled fields otherwise expand and clip the buttons).
        float prevLabel = EditorGUIUtility.labelWidth;

        SplitRow(out Rect countRect, out Rect genRect);
        EditorGUIUtility.labelWidth = 46f;
        _spawnCount = Mathf.Max(1, EditorGUI.IntField(countRect,
            new GUIContent("Count", "How many positions to spread evenly on a sphere around the root."), _spawnCount));
        EditorGUIUtility.labelWidth = prevLabel;
        if (GUI.Button(genRect, new GUIContent("Generate", "Create Count positions spread evenly on a sphere (replaces the current list).")))
            GenerateOnSphere(controller, anchors);

        SplitRow(out Rect radiusRect, out Rect distRect);
        EditorGUIUtility.labelWidth = 46f;
        _spawnRadius = Mathf.Max(0f, EditorGUI.FloatField(radiusRect,
            new GUIContent("Radius", "Distance (local units) each position sits from the root."), _spawnRadius));
        EditorGUIUtility.labelWidth = prevLabel;
        using (new EditorGUI.DisabledScope(anchors.arraySize < 2))
            if (GUI.Button(distRect, new GUIContent("Distribute existing evenly on sphere",
                "Reposition the current positions evenly on the sphere (keeps their per-position overrides).")))
                DistributeEvenly(controller, anchors);
    }

    private int _spawnCount = 8;
    private float _spawnRadius = 0.5f;

    // Reserve one inspector line and split it into two equal columns with a small gap between them.
    private static void SplitRow(out Rect left, out Rect right, float gap = 4f)
    {
        Rect row = EditorGUILayout.GetControlRect();
        float half = (row.width - gap) * 0.5f;
        left = new Rect(row.x, row.y, half, row.height);
        right = new Rect(row.x + half + gap, row.y, half, row.height);
    }

    // Evenly-distributed point on a UNIT sphere via the Fibonacci / golden-spiral method (good spread for any n).
    private static Vector3 FibonacciSpherePoint(int i, int n)
    {
        if (n <= 1) return Vector3.up;
        float y = 1f - (i / (float)(n - 1)) * 2f;                 // 1 .. -1
        float r = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
        float theta = Mathf.PI * (3f - Mathf.Sqrt(5f)) * i;        // golden angle
        return new Vector3(Mathf.Cos(theta) * r, y, Mathf.Sin(theta) * r);
    }

    // Create a TooltipPosition child (with a TooltipAnchor) at a root-local position; returns its transform.
    private static Transform CreateCandidateObject(Transform root, string name, Vector3 localPos)
    {
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Add Tooltip Candidate Position");
        go.transform.SetParent(root, false);
        go.transform.localPosition = localPos;
        go.AddComponent<TooltipAnchor>();
        return go.transform;
    }

    // Quick setup: replace the current candidates with `_spawnCount` fresh ones spread evenly on the sphere.
    private void GenerateOnSphere(InteractableTooltipController controller, SerializedProperty anchors)
    {
        if (anchors.arraySize > 0 &&
            !EditorUtility.DisplayDialog("Generate candidate positions",
                $"This removes the current {anchors.arraySize} candidate position(s) and creates {_spawnCount} " +
                "spread evenly on a sphere. Continue?", "Generate", "Cancel"))
            return;

        // Destroy the auto-created children we own; external transforms are just dropped from the list.
        for (int i = anchors.arraySize - 1; i >= 0; i--)
        {
            var t = anchors.GetArrayElementAtIndex(i).objectReferenceValue as Transform;
            if (t != null && t.parent == controller.transform && t.name.StartsWith("TooltipPosition"))
                Undo.DestroyObjectImmediate(t.gameObject);
        }
        anchors.ClearArray();

        for (int i = 0; i < _spawnCount; i++)
        {
            var tf = CreateCandidateObject(controller.transform, $"TooltipPosition {i}",
                FibonacciSpherePoint(i, _spawnCount) * _spawnRadius);
            anchors.arraySize = i + 1;
            anchors.GetArrayElementAtIndex(i).objectReferenceValue = tf;
        }

        _previewPos = 2; // focus the first generated position in the preview
        _followBest = false;
        serializedObject.ApplyModifiedProperties();
        GUIUtility.ExitGUI(); // objects created/destroyed mid-GUI — abort this pass so the inspector rebuilds
    }

    // Non-destructive: reposition the EXISTING candidates evenly on the sphere (keeps their TooltipAnchor overrides).
    private void DistributeEvenly(InteractableTooltipController controller, SerializedProperty anchors)
    {
        int n = anchors.arraySize;
        for (int i = 0; i < n; i++)
        {
            var t = anchors.GetArrayElementAtIndex(i).objectReferenceValue as Transform;
            if (t == null) continue;
            Undo.RecordObject(t, "Distribute Tooltip Candidates");
            t.position = controller.transform.TransformPoint(FibonacciSpherePoint(i, n) * _spawnRadius);
        }
        if (_preview != null) ConfigurePreview(controller);
    }


    private void BuildPreview(InteractableTooltipController controller, bool warnIfNoPool = true)
    {
        DestroyPreview();
        if (Application.isPlaying || _previewPos == 0) return;

        PooledTooltipView prefab = ResolveViewPrefab();
        if (prefab == null)
        {
            if (warnIfNoPool)
                Debug.LogWarning("[TooltipPreview] No PooledTooltipView prefab found (no TooltipPoolManager in the scene and none in the project).", controller);
            return;
        }

        _preview = Instantiate(prefab.gameObject, controller.transform);
        _preview.name = PreviewName;
        _preview.hideFlags = HideFlags.HideAndDontSave;

        ConfigurePreview(controller); // _appliedPreviewPos == -1 here -> fresh build snaps
    }

    // Preview doesn't need a TooltipPoolManager in the scene: prefer one if present (uses the exact prefab
    // the game will pool), else fall back to the PooledTooltipView prefab in the project so authoring works
    // in any scene.
    private static PooledTooltipView ResolveViewPrefab()
    {
        var pool = Object.FindFirstObjectByType<TooltipPoolManager>();
        if (pool != null && pool.ViewPrefab != null)
        {
            var v = pool.ViewPrefab.GetComponent<PooledTooltipView>();
            if (v != null) return v;
        }

        // Cheap pass first (by likely name), then a full scan as a fallback if the prefab was renamed.
        var found = FindPrefabWithView("PooledTooltip t:Prefab") ?? FindPrefabWithView("t:Prefab");
        return found;
    }

    private static PooledTooltipView FindPrefabWithView(string filter)
    {
        foreach (var guid in AssetDatabase.FindAssets(filter))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go == null) continue;
            var view = go.GetComponent<PooledTooltipView>();
            if (view != null) return view;
        }
        return null;
    }

    private void ConfigurePreview(InteractableTooltipController controller)
    {
        if (_preview == null) return;
        var view = _preview.GetComponent<PooledTooltipView>();
        if (view == null) return;

        GetPreviewTransform(controller, out Vector3 worldPos, out bool iconOnRight);
        bool expanded = ResolvePreviewExpanded(controller);

        // Content + mode. The iconOnRight field is also the resting side once a move settles.
        var so = new SerializedObject(view);
        SetBool(so, "iconOnRight", iconOnRight);
        SetObject(so, "previewContentSo", controller.ActionContentSo);
        SetEnum(so, "previewMode", (int)_previewMode);
        so.ApplyModifiedProperties();

        view.SetEditorBillboard(GetPreviewBillboard(controller));
        // Push the live per-axis constraints (this position's override if any) + rest so clamped previews match
        // runtime while authoring.
        var restAnchor = _previewPos >= 2 ? GetPreviewAnchorTransform() : null;
        view.SetBillboardConstraints(controller.BillboardConstraintsForEditor(restAnchor), controller.BillboardRestForEditor(restAnchor));
        view.SetEditorExpanded(expanded); // drives the expand/collapse morph (and keeps the editor tick alive)

        bool firstApply = _appliedPreviewPos == -1;
        bool moved = _appliedPreviewPos != _previewPos || _appliedIconSide != iconOnRight;
        _appliedPreviewPos = _previewPos;
        _appliedIconSide = iconOnRight;

        // Moving while expanded plays the sequenced collapse/travel/expand; moving while minimized glides the
        // disc (slide, don't pop); no move just pins. MovePreviewTo would force a re-expand for the disc.
        if (moved && expanded)
            view.MovePreviewTo(worldPos, iconOnRight, !firstApply);
        else if (moved)
            view.GlidePreviewTo(worldPos, iconOnRight, !firstApply);
        else
            view.SyncPreviewPos(worldPos);
    }

    // Effective billboard for the previewed position, mirroring the runtime decision: a TooltipAnchor
    // override wins, otherwise the controller's mode (UseManagerDefault -> the scene pool's default, or on
    // if there's no pool yet).
    private bool GetPreviewBillboard(InteractableTooltipController controller)
    {
        if (_previewPos >= 2)
        {
            var a = GetPreviewAnchorTransform()?.GetComponent<TooltipAnchor>();
            if (a != null && a.BillboardOverride.HasValue) return a.BillboardOverride.Value;
            // Candidate without override -> manager default (the general Auto-orient mode is self-only).
            var poolC = Object.FindFirstObjectByType<TooltipPoolManager>();
            return poolC == null || poolC.BillboardDefault;
        }

        switch (controller.BillboardModeDefault)
        {
            case BillboardMode.Always: return true;
            case BillboardMode.Never: return false;
            default:
                var pool = Object.FindFirstObjectByType<TooltipPoolManager>();
                return pool == null || pool.BillboardDefault;
        }
    }

    // Expanded-or-minimized for the current Preview state. Auto mirrors runtime: in range AND looking -> expanded.
    private bool ResolvePreviewExpanded(InteractableTooltipController controller)
    {
        switch (_previewState)
        {
            case PreviewState.Minimized: return false;
            case PreviewState.Auto:
                return ComputePlayerProxyState(controller, out bool inRange, out bool looking) && inRange && looking;
            default: return true; // Expanded
        }
    }

    // Evaluates the runtime visibility triggers using the Scene camera as a stand-in for the player.
    private static bool ComputePlayerProxyState(InteractableTooltipController controller, out bool inRange, out bool looking)
    {
        inRange = false;
        looking = false;

        var sv = SceneView.lastActiveSceneView;
        if (sv == null || sv.camera == null) return false;

        Vector3 eye = sv.camera.transform.position;
        Vector3 eyeFwd = sv.camera.transform.forward;
        Vector3 origin = controller.transform.position;

        float range = controller.ShowDistance;
        inRange = range <= 0f || (origin - eye).magnitude <= range;

        Transform lookT = controller.LookTarget;
        Vector3 toTarget = lookT.position - eye;
        float dist = toTarget.magnitude;
        float gazeDot = dist > 1e-4f ? Vector3.Dot(eyeFwd, toTarget / dist) : 1f;
        looking = gazeDot > controller.FieldOfViewThreshold;
        return true;
    }

    // The candidate array index the runtime picker would choose for the Scene camera (mirrors EvaluateBestAnchor).
    private bool ComputeBestCandidate(InteractableTooltipController controller, out int bestArrayIdx)
    {
        bestArrayIdx = -1;

        var sv = SceneView.lastActiveSceneView;
        if (sv == null || sv.camera == null) return false;

        var anchors = controller.CandidateAnchorsEditor; // off the target (OnSceneGUI: no Editor serializedObject)
        if (anchors == null || anchors.Count == 0) return false;

        Vector3 eye = sv.camera.transform.position;
        float w = controller.DistanceWeight;
        Transform centerT = controller.LookTarget;
        Vector3 centerPos = centerT != null ? centerT.position : controller.transform.position;
        Vector3 dirCenterToEye = eye - centerPos;
        if (dirCenterToEye.sqrMagnitude > 1e-6f) dirCenterToEye.Normalize();
        float bestScore = float.NegativeInfinity;

        for (int i = 0; i < anchors.Count; i++)
        {
            var t = anchors[i];
            if (t == null) continue;
            Vector3 to = t.position - eye;
            float d = to.magnitude;
            if (d < 1e-4f) continue;

            // Position-based facing (mirrors EvaluateBestAnchor): gaze-independent.
            Vector3 cToA = t.position - centerPos;
            float facingScore = cToA.sqrMagnitude > 1e-6f ? Vector3.Dot(dirCenterToEye, cToA.normalized) : 0f;
            float score = facingScore + w * (1f / (1f + d));
            if (score > bestScore) { bestScore = score; bestArrayIdx = i; }
        }
        return bestArrayIdx >= 0;
    }

    private void GetPreviewTransform(InteractableTooltipController controller, out Vector3 worldPos, out bool iconOnRight)
    {
        worldPos = controller.transform.position;
        iconOnRight = controller.IconOnRightDefault;

        if (_previewPos < 2) return; // Base

        var anchor = GetPreviewAnchorTransform();
        if (anchor == null) return;

        worldPos = anchor.position;
        var a = anchor.GetComponent<TooltipAnchor>();
        if (a != null && a.IconOnRightOverride.HasValue) iconOnRight = a.IconOnRightOverride.Value;
    }

    private static void SetBool(SerializedObject so, string prop, bool v) { var p = so.FindProperty(prop); if (p != null) p.boolValue = v; }
    private static void SetObject(SerializedObject so, string prop, Object v) { var p = so.FindProperty(prop); if (p != null) p.objectReferenceValue = v; }
    private static void SetEnum(SerializedObject so, string prop, int v) { var p = so.FindProperty(prop); if (p != null) p.enumValueIndex = v; }

    private void DestroyPreview()
    {
        _appliedPreviewPos = -1;
        if (_preview != null)
        {
            DestroyImmediate(_preview);
            _preview = null;
        }

        var controller = target as InteractableTooltipController;
        if (controller == null) return;
        for (int i = controller.transform.childCount - 1; i >= 0; i--)
        {
            var child = controller.transform.GetChild(i);
            if (child != null && child.name == PreviewName) DestroyImmediate(child.gameObject);
        }
    }
}
#endif
