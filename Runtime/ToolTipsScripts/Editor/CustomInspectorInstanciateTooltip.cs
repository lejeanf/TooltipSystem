#if UNITY_EDITOR
using System.Collections.Generic;
using jeanf.tooltip;
using jeanf.universalplayer;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(InteractableToolTipController))]
public class CustomInspectorInstanciateTooltip : Editor
{
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
    private bool _previewFoldout = true;                            // collapse the editor-only preview block
    private bool _repositioningFoldout = true;                      // collapse the repositioning settings block
    private bool _candidatesFoldout = true;                         // collapse the candidate-positions list
    private bool _legacyFoldout = false;                            // collapse the bottom legacy-references block (closed by default)
    private bool _debugFoldout = true;                              // collapse the live debug-state panel (when globally enabled)

    private void OnEnable()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;

        var controller = target as InteractableToolTipController;
        if (controller == null || Application.isPlaying) return;

        // Auto-visualise on selection: default to the first candidate position, or the script root
        // (Base) when none were added. The user can still switch via the "Preview at" dropdown.
        var anchors = serializedObject.FindProperty("candidateAnchors");
        int anchorCount = anchors != null && anchors.isArray ? anchors.arraySize : 0;
        _previewPos = anchorCount > 0 ? 2 : 1;
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
        var controller = target as InteractableToolTipController;
        if (controller == null) return;

        serializedObject.Update();

        DrawPreviewControls(controller);

        EditorGUILayout.Space();
        // Repositioning, candidate list, and legacy refs are drawn below in their own collapsible blocks.
        var exclude = new List<string>
        {
            "m_Script", "candidateAnchors", "showToolTip",
            "enableRepositioning", "evaluationInterval", "repositionHysteresis", "distanceWeight",
            "rejectOccluded", "obstacleMask",
            "tooltipGameObjectPrefab", "inputIconSo", "interactableToolTipInputSo"
        };
        // The general (controller-level) billboard mode + limits apply only to the "self" position. Once the
        // tooltip repositions across candidates, each position owns its own (via ToolTipAnchor), so hide both.
        if (controller.UsesCandidatesEditor)
        {
            exclude.Add("billboardConstraints");
            exclude.Add("billboardMode");
        }

        DrawPropertiesExcluding(serializedObject, exclude.ToArray());

        if (controller.UsesCandidatesEditor)
            EditorGUILayout.HelpBox(
                "Billboarding and its limits are set per candidate position (select one below — the Billboard dropdown, " +
                "and \"Override billboard limits\"). The general billboard settings apply only when there are no candidates.",
                MessageType.None);

        DrawRepositioning();
        DrawCandidatePositions(controller);
        DrawLegacyReferences();
        DrawDebugState(controller);

        serializedObject.ApplyModifiedProperties();

        if (_preview != null) ConfigurePreview(controller); // keep preview in sync with field edits

        // The inspector only repaints on change by default; force it while playing so the debug panel updates.
        if (Application.isPlaying && TooltipDebugPrefs.Enabled && _debugFoldout) Repaint();
    }

    private void DrawRepositioning()
    {
        EditorGUILayout.Space();
        _repositioningFoldout = EditorGUILayout.Foldout(_repositioningFoldout, "Repositioning (optional)", true, EditorStyles.foldoutHeader);
        if (!_repositioningFoldout) return;

        EditorGUI.indentLevel++;
        foreach (var propName in new[] { "enableRepositioning", "evaluationInterval", "repositionHysteresis",
                                         "distanceWeight", "rejectOccluded", "obstacleMask" })
        {
            var p = serializedObject.FindProperty(propName);
            if (p != null) EditorGUILayout.PropertyField(p);
        }
        EditorGUI.indentLevel--;
    }

    // Live gate-state panel, shown only when the global toggle (on the pool manager) is on. Read-only; the
    // values update each frame in play mode (OnInspectorGUI forces a repaint above).
    private void DrawDebugState(InteractableToolTipController controller)
    {
        if (!TooltipDebugPrefs.Enabled) return;

        EditorGUILayout.Space();
        _debugFoldout = EditorGUILayout.Foldout(_debugFoldout, "Tooltip state (debug)", true, EditorStyles.foldoutHeader);
        if (!_debugFoldout) return;

        if (!Application.isPlaying)
            EditorGUILayout.HelpBox("Enter Play mode for live gate state (zone / proximity / looking update each frame).", MessageType.None);

        EditorGUILayout.LabelField("Pooled", $"{controller.Dbg_Pooled}   ·   show state: {controller.Dbg_ShowState}");
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
    private static void DrawDebugCandidates(InteractableToolTipController controller)
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

    private void DrawPreviewControls(InteractableToolTipController controller)
    {
        _previewFoldout = EditorGUILayout.Foldout(_previewFoldout, "Preview (pooled tooltip in the scene)", true, EditorStyles.foldoutHeader);
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

        DrawIconSideControls(controller);

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

    // Per-position icon side + billboarding for the thing currently being previewed, editable inline so the
    // flip is live. One dropdown each (Inherit / Left / Right and Inherit / Always / Never) — no two-checkbox
    // "override" trap.
    private void DrawIconSideControls(InteractableToolTipController controller)
    {
        if (_previewPos < 2) // None / Base -> the tooltip's own default, edited on the controller below.
        {
            if (_previewPos == 1)
                EditorGUILayout.LabelField("Icon side", controller.IconOnRightDefault
                    ? "Right (tooltip default — see Icon On Right below)"
                    : "Left (tooltip default — see Icon On Right below)");
            return;
        }

        var anchorTf = GetPreviewAnchorTransform();
        if (anchorTf == null) return;

        var anchor = anchorTf.GetComponent<ToolTipAnchor>();
        if (anchor == null)
        {
            EditorGUILayout.HelpBox("This position has no ToolTipAnchor, so it uses the tooltip defaults.", MessageType.Info);
            if (GUILayout.Button("Add ToolTipAnchor to this position"))
            {
                Undo.AddComponent<ToolTipAnchor>(anchorTf.gameObject);
                if (_preview != null) ConfigurePreview(controller);
            }
            return;
        }

        EditorGUI.BeginChangeCheck();
        var side = (ToolTipAnchor.IconSide)EditorGUILayout.EnumPopup(
            new GUIContent("Icon side", "Icon side for this position. Inherit = use the tooltip's default."), anchor.iconSide);
        var bill = (ToolTipAnchor.Billboard)EditorGUILayout.EnumPopup(
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
        var anchors = serializedObject.FindProperty("candidateAnchors");
        int idx = _previewPos - 2;
        if (anchors == null || idx < 0 || idx >= anchors.arraySize) return null;
        return anchors.GetArrayElementAtIndex(idx).objectReferenceValue as Transform;
    }

    // Scene-view authoring: place the tooltip and edit its candidate positions with handles.
    private void OnSceneGUI()
    {
        var controller = target as InteractableToolTipController;
        if (controller == null) return;

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

        // Script root (Base) — just a marker (the object's own transform tool moves it).
        DrawTargetMarker(controller, 1, basePos, "root", Color.cyan);

        var anchors = serializedObject.FindProperty("candidateAnchors");
        if (anchors == null || !anchors.isArray) return;

        for (int i = 0; i < anchors.arraySize; i++)
        {
            var anchor = anchors.GetArrayElementAtIndex(i).objectReferenceValue as Transform;
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
    private void DrawRangeGizmos(InteractableToolTipController controller)
    {
        Vector3 origin = controller.transform.position;
        float range = controller.MinimizedRange;

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
                serializedObject.Update();
                var p = serializedObject.FindProperty("minimizedRange");
                if (p != null)
                {
                    p.floatValue = Mathf.Max(0f, newRange);
                    serializedObject.ApplyModifiedProperties(); // updates the inspector + Undo
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
    private void DrawBillboardConstraintHandles(InteractableToolTipController controller)
    {
        if (controller.BillboardModeEditor == BillboardMode.Never) return; // not billboarding -> nothing to limit

        Transform anchorTf = _previewPos >= 2 ? GetPreviewAnchorTransform() : null;

        // Effective constraints for the previewed position, and the object that OWNS them (the candidate's
        // ToolTipAnchor when it overrides, else the controller) so handle drags write to the right place.
        var c = controller.BillboardConstraintsForEditor(anchorTf);
        Object owner = controller;
        if (anchorTf != null)
        {
            var a = anchorTf.GetComponent<ToolTipAnchor>();
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

    // Mirrors InteractableToolTipController.EvaluateBestAnchor: a colour-coded line to each candidate
    // (green = the one that would be chosen) and the scores appended to the screen-space panel.
    private void AppendCandidateScores(InteractableToolTipController controller, Vector3 eye,
        List<(string text, Color col)> lines)
    {
        var anchors = serializedObject.FindProperty("candidateAnchors");
        if (anchors == null || !anchors.isArray || anchors.arraySize == 0) return;

        float w = controller.DistanceWeight;
        Transform centerT = controller.LookTarget;
        Vector3 centerPos = centerT != null ? centerT.position : controller.transform.position;
        Vector3 dirCenterToEye = eye - centerPos;
        if (dirCenterToEye.sqrMagnitude > 1e-6f) dirCenterToEye.Normalize();

        var scores = new float[anchors.arraySize];
        var positions = new Vector3[anchors.arraySize];
        int bestIdx = -1;
        float bestScore = float.NegativeInfinity;

        for (int i = 0; i < anchors.arraySize; i++)
        {
            var t = anchors.GetArrayElementAtIndex(i).objectReferenceValue as Transform;
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
        for (int i = 0; i < anchors.arraySize; i++)
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
    private void DrawTargetMarker(InteractableToolTipController controller, int posIndex, Vector3 pos, string label, Color baseColor)
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
            if (Handles.Button(pos, Quaternion.identity, size * 0.08f, size * 0.12f, Handles.DotHandleCap))
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

    private void DrawCandidatePositions(InteractableToolTipController controller)
    {
        var anchors = serializedObject.FindProperty("candidateAnchors");
        if (anchors == null) return;

        EditorGUILayout.Space();
        _candidatesFoldout = EditorGUILayout.Foldout(_candidatesFoldout,
            $"Candidate positions (repositioning) — {anchors.arraySize}", true, EditorStyles.foldoutHeader);
        if (!_candidatesFoldout) return;

        int removeIndex = -1;
        for (int i = 0; i < anchors.arraySize; i++)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(anchors.GetArrayElementAtIndex(i), new GUIContent($"Position {i}"));
            if (GUILayout.Button("Remove", GUILayout.Width(70))) removeIndex = i;
            EditorGUILayout.EndHorizontal();
        }

        if (removeIndex >= 0)
        {
            var t = anchors.GetArrayElementAtIndex(removeIndex).objectReferenceValue as Transform;
            if (t != null && t.parent == controller.transform && t.name.StartsWith("TooltipPosition"))
                Undo.DestroyObjectImmediate(t.gameObject);
            if (anchors.GetArrayElementAtIndex(removeIndex).objectReferenceValue != null)
                anchors.DeleteArrayElementAtIndex(removeIndex);
            anchors.DeleteArrayElementAtIndex(removeIndex);
        }

        if (GUILayout.Button("Add candidate position"))
        {
            var go = new GameObject($"TooltipPosition {anchors.arraySize}");
            Undo.RegisterCreatedObjectUndo(go, "Add Tooltip Candidate Position");
            go.transform.SetParent(controller.transform, false);
            go.transform.localPosition = new Vector3(0f, 0.3f * (anchors.arraySize + 1), 0f);
            go.AddComponent<ToolTipAnchor>();

            int idx = anchors.arraySize;
            anchors.arraySize = idx + 1;
            anchors.GetArrayElementAtIndex(idx).objectReferenceValue = go.transform;
        }
    }

    // Legacy (non-pooled / no Action Content SO) references, tucked into a collapsed foldout at the very
    // bottom so they don't clutter the common pooled setup. Drawn explicitly (excluded from the main pass).
    private void DrawLegacyReferences()
    {
        EditorGUILayout.Space();
        _legacyFoldout = EditorGUILayout.Foldout(_legacyFoldout,
            "Legacy references (non-pooled / no Action Content SO)", true, EditorStyles.foldoutHeader);
        if (!_legacyFoldout) return;

        EditorGUI.indentLevel++;
        var prefab = serializedObject.FindProperty("tooltipGameObjectPrefab");
        var inputIcon = serializedObject.FindProperty("inputIconSo");
        var inputSo = serializedObject.FindProperty("interactableToolTipInputSo");
        if (prefab != null) EditorGUILayout.PropertyField(prefab);
        if (inputIcon != null) EditorGUILayout.PropertyField(inputIcon);
        if (inputSo != null) EditorGUILayout.PropertyField(inputSo);
        EditorGUI.indentLevel--;
    }

    private void BuildPreview(InteractableToolTipController controller, bool warnIfNoPool = true)
    {
        DestroyPreview();
        if (Application.isPlaying || _previewPos == 0) return;

        PooledTooltipView prefab = ResolveViewPrefab();
        if (prefab == null)
        {
            if (warnIfNoPool)
                Debug.LogWarning("[TooltipPreview] No PooledTooltipView prefab found (no ToolTipPoolManager in the scene and none in the project).", controller);
            return;
        }

        _preview = Instantiate(prefab.gameObject, controller.transform);
        _preview.name = PreviewName;
        _preview.hideFlags = HideFlags.HideAndDontSave;

        ConfigurePreview(controller); // _appliedPreviewPos == -1 here -> fresh build snaps
    }

    // Preview doesn't need a ToolTipPoolManager in the scene: prefer one if present (uses the exact prefab
    // the game will pool), else fall back to the PooledTooltipView prefab in the project so authoring works
    // in any scene.
    private static PooledTooltipView ResolveViewPrefab()
    {
        var pool = Object.FindFirstObjectByType<ToolTipPoolManager>();
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

    private void ConfigurePreview(InteractableToolTipController controller)
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

    // Effective billboard for the previewed position, mirroring the runtime decision: a ToolTipAnchor
    // override wins, otherwise the controller's mode (UseManagerDefault -> the scene pool's default, or on
    // if there's no pool yet).
    private bool GetPreviewBillboard(InteractableToolTipController controller)
    {
        if (_previewPos >= 2)
        {
            var a = GetPreviewAnchorTransform()?.GetComponent<ToolTipAnchor>();
            if (a != null && a.BillboardOverride.HasValue) return a.BillboardOverride.Value;
            // Candidate without override -> manager default (the general Auto-orient mode is self-only).
            var poolC = Object.FindFirstObjectByType<ToolTipPoolManager>();
            return poolC == null || poolC.BillboardDefault;
        }

        switch (controller.BillboardModeDefault)
        {
            case BillboardMode.Always: return true;
            case BillboardMode.Never: return false;
            default:
                var pool = Object.FindFirstObjectByType<ToolTipPoolManager>();
                return pool == null || pool.BillboardDefault;
        }
    }

    // Expanded-or-minimized for the current Preview state. Auto mirrors runtime: in range AND looking -> expanded.
    private bool ResolvePreviewExpanded(InteractableToolTipController controller)
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
    private static bool ComputePlayerProxyState(InteractableToolTipController controller, out bool inRange, out bool looking)
    {
        inRange = false;
        looking = false;

        var sv = SceneView.lastActiveSceneView;
        if (sv == null || sv.camera == null) return false;

        Vector3 eye = sv.camera.transform.position;
        Vector3 eyeFwd = sv.camera.transform.forward;
        Vector3 origin = controller.transform.position;

        float range = controller.MinimizedRange;
        inRange = range <= 0f || (origin - eye).magnitude <= range;

        Transform lookT = controller.LookTarget;
        Vector3 toTarget = lookT.position - eye;
        float dist = toTarget.magnitude;
        float gazeDot = dist > 1e-4f ? Vector3.Dot(eyeFwd, toTarget / dist) : 1f;
        looking = gazeDot > controller.FieldOfViewThreshold;
        return true;
    }

    // The candidate array index the runtime picker would choose for the Scene camera (mirrors EvaluateBestAnchor).
    private bool ComputeBestCandidate(InteractableToolTipController controller, out int bestArrayIdx)
    {
        bestArrayIdx = -1;

        var sv = SceneView.lastActiveSceneView;
        if (sv == null || sv.camera == null) return false;

        var anchors = serializedObject.FindProperty("candidateAnchors");
        if (anchors == null || !anchors.isArray || anchors.arraySize == 0) return false;

        Vector3 eye = sv.camera.transform.position;
        float w = controller.DistanceWeight;
        Transform centerT = controller.LookTarget;
        Vector3 centerPos = centerT != null ? centerT.position : controller.transform.position;
        Vector3 dirCenterToEye = eye - centerPos;
        if (dirCenterToEye.sqrMagnitude > 1e-6f) dirCenterToEye.Normalize();
        float bestScore = float.NegativeInfinity;

        for (int i = 0; i < anchors.arraySize; i++)
        {
            var t = anchors.GetArrayElementAtIndex(i).objectReferenceValue as Transform;
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

    private void GetPreviewTransform(InteractableToolTipController controller, out Vector3 worldPos, out bool iconOnRight)
    {
        worldPos = controller.transform.position;
        iconOnRight = controller.IconOnRightDefault;

        if (_previewPos < 2) return; // Base

        var anchor = GetPreviewAnchorTransform();
        if (anchor == null) return;

        worldPos = anchor.position;
        var a = anchor.GetComponent<ToolTipAnchor>();
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

        var controller = target as InteractableToolTipController;
        if (controller == null) return;
        for (int i = controller.transform.childCount - 1; i >= 0; i--)
        {
            var child = controller.transform.GetChild(i);
            if (child != null && child.name == PreviewName) DestroyImmediate(child.gameObject);
        }
    }
}
#endif
