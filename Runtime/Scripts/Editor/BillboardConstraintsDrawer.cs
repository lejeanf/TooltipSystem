#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace jeanf.tooltip
{
    /// <summary>
    /// Compact inspector for <see cref="BillboardConstraints"/>: one row per axis (Yaw / Pitch / Roll) with an
    /// enable toggle, and — when enabled — an optional clamp with a min/max range (slider + fields). Disabled
    /// rows collapse so the panel only shows what's relevant. Pairs with the scene-view arc handles drawn by
    /// the controller's editor.
    /// </summary>
    [CustomPropertyDrawer(typeof(BillboardConstraints))]
    public class BillboardConstraintsDrawer : PropertyDrawer
    {
        private const float Pad = 2f;
        private static float Line => EditorGUIUtility.singleLineHeight;

        private struct AxisDef
        {
            public string toggle, clamp, center, min, max, label, hint;
            public float lo, hi;
            public System.Func<Color> color; // Unity axis colour (resolved live so it follows editor prefs)
        }

        // Yaw turns around up (Y = green), pitch around right (X = red), roll around forward (Z = blue) —
        // matching Unity's rotation gizmo so the title colour tells you which axis you're constraining.
        private static readonly AxisDef[] AxisDefs =
        {
            new AxisDef { toggle = "yawAxis",   clamp = "clampYaw",   center = "yawCenter",   min = "yawMin",   max = "yawMax",
                          label = "Yaw (horizontal)", hint = "Turn left/right around the rest up axis (Y) to face the camera.", lo = -180f, hi = 180f, color = () => Handles.yAxisColor },
            new AxisDef { toggle = "pitchAxis", clamp = "clampPitch", center = "pitchCenter", min = "pitchMin", max = "pitchMax",
                          label = "Pitch (vertical)",  hint = "Tilt up/down around the rest right axis (X) to face the camera.", lo = -90f, hi = 90f, color = () => Handles.xAxisColor },
            new AxisDef { toggle = "rollAxis",  clamp = "clampRoll",  center = "rollCenter",  min = "rollMin",  max = "rollMax",
                          label = "Roll (camera tilt)", hint = "Lean around the view axis (Z) to follow the camera's tilt. Off = always upright (classic billboard).", lo = -180f, hi = 180f, color = () => Handles.zAxisColor },
        };

        private static float Wrap180(float a) => Mathf.Repeat(a + 180f, 360f) - 180f;

        public override void OnGUI(Rect pos, SerializedProperty prop, GUIContent label)
        {
            EditorGUI.BeginProperty(pos, label, prop);

            var r = new Rect(pos.x, pos.y, pos.width, Line);
            prop.isExpanded = EditorGUI.Foldout(r, prop.isExpanded, label, true);

            if (prop.isExpanded)
            {
                EditorGUI.indentLevel++;
                r.y += Line + Pad;

                var ease = prop.FindPropertyRelative("limitEaseDegrees");
                EditorGUI.PropertyField(r, ease, new GUIContent("Limit ease (°)",
                    "Soften the approach to every clamp: over this many degrees before each limit the billboard eases to a stop. 0 = hard clamp."));
                r.y += Line + Pad;

                foreach (var ax in AxisDefs) r = DrawAxis(r, prop, ax);
                EditorGUI.indentLevel--;
            }

            EditorGUI.EndProperty();
        }

        private static Rect DrawAxis(Rect r, SerializedProperty prop, AxisDef ax)
        {
            var on = prop.FindPropertyRelative(ax.toggle);

            // Axis enable as a bold, axis-coloured toggle title (red/green/blue = X/Y/Z).
            var lbl = new GUIContent(ax.label, ax.hint);
            var style = new GUIStyle(EditorStyles.label) { fontStyle = FontStyle.Bold };
            style.normal.textColor = style.onNormal.textColor = ax.color();
            Rect ir = EditorGUI.IndentedRect(r);
            int toggleIndent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0; // we indented manually via IndentedRect
            EditorGUI.BeginProperty(ir, lbl, on);
            EditorGUI.BeginChangeCheck();
            bool v = EditorGUI.ToggleLeft(ir, lbl, on.boolValue, style);
            if (EditorGUI.EndChangeCheck()) on.boolValue = v;
            EditorGUI.EndProperty();
            EditorGUI.indentLevel = toggleIndent;
            r.y += Line + Pad;

            if (!on.boolValue) return r;

            EditorGUI.indentLevel++;
            var clamp = prop.FindPropertyRelative(ax.clamp);
            EditorGUI.PropertyField(r, clamp,
                new GUIContent("Clamp range", "Limit this axis to a degree range relative to the rest forward."));
            r.y += Line + Pad;

            if (clamp.boolValue)
            {
                var center = prop.FindPropertyRelative(ax.center);
                var min = prop.FindPropertyRelative(ax.min);
                var max = prop.FindPropertyRelative(ax.max);

                EditorGUI.PropertyField(r, center, new GUIContent("Center (°)",
                    "Rotate the whole allowed band around the circle. The Range below is relative to this; move it to place the band anywhere, even across ±180°."));
                r.y += Line + Pad;

                Rect row = EditorGUI.IndentedRect(r);
                int prevIndent = EditorGUI.indentLevel;
                EditorGUI.indentLevel = 0; // manual layout: don't let FloatField add its own indent

                const float fieldW = 46f, gap = 4f;
                var minR = new Rect(row.x, row.y, fieldW, Line);
                var maxR = new Rect(row.xMax - fieldW, row.y, fieldW, Line);
                var sliderR = new Rect(minR.xMax + gap, row.y, maxR.xMin - minR.xMax - 2f * gap, Line);

                float origLo = min.floatValue, origHi = max.floatValue;
                float lo = origLo, hi = origHi;
                EditorGUI.BeginChangeCheck();
                lo = EditorGUI.FloatField(minR, lo);
                EditorGUI.MinMaxSlider(sliderR, ref lo, ref hi, ax.lo, ax.hi);
                hi = EditorGUI.FloatField(maxR, hi);
                if (EditorGUI.EndChangeCheck())
                {
                    // Alt = mirror: editing one end sets the other to its negative (symmetric band about the
                    // centre), matching the scene-view handles. Mirror around the end that moved MORE — the one
                    // the user is actually dragging — so it stays robust even when the two-thumb slider nudges the
                    // other thumb to stop them crossing (using XOR there would skip a frame and read as jitter).
                    bool changed = !Mathf.Approximately(lo, origLo) || !Mathf.Approximately(hi, origHi);
                    bool mirror = Event.current != null && Event.current.alt && changed;
                    if (mirror)
                    {
                        if (Mathf.Abs(lo - origLo) >= Mathf.Abs(hi - origHi)) hi = -lo;
                        else lo = -hi;
                    }

                    lo = Mathf.Clamp(lo, ax.lo, ax.hi);
                    hi = Mathf.Clamp(hi, ax.lo, ax.hi);
                    if (mirror)
                    {
                        min.floatValue = Mathf.Min(lo, hi);
                        max.floatValue = Mathf.Max(lo, hi);
                    }
                    else
                    {
                        if (lo > hi) hi = lo;
                        min.floatValue = lo;
                        max.floatValue = hi;
                    }
                }

                EditorGUI.indentLevel = prevIndent;
                r.y += Line + Pad;

                // Effective band after wrapping (so the user sees the across-the-seam result of the center),
                // plus the Alt-mirror hint (highlighted while Alt is held — matches the scene handles).
                float a = Wrap180(center.floatValue + min.floatValue);
                float b = Wrap180(center.floatValue + max.floatValue);
                bool mirroring = Event.current != null && Event.current.alt;
                var readout = new GUIStyle(EditorStyles.miniLabel);
                if (mirroring) readout.normal.textColor = new Color(1f, 0.78f, 0.25f);
                EditorGUI.LabelField(EditorGUI.IndentedRect(r),
                    $"covers {a:0}° -> {b:0}°      {(mirroring ? "Alt: mirroring min/max" : "Alt-drag: mirror")}", readout);
                r.y += Line + Pad;
            }

            EditorGUI.indentLevel--;
            return r;
        }

        public override float GetPropertyHeight(SerializedProperty prop, GUIContent label)
        {
            if (!prop.isExpanded) return Line;

            float h = Line + Pad; // foldout
            h += Line + Pad;      // limit ease field
            foreach (var ax in AxisDefs)
            {
                h += Line + Pad; // axis toggle
                if (prop.FindPropertyRelative(ax.toggle).boolValue)
                {
                    h += Line + Pad; // clamp toggle
                    if (prop.FindPropertyRelative(ax.clamp).boolValue)
                        h += 3f * (Line + Pad); // center + range + wrapped-readout rows
                }
            }
            return h;
        }
    }
}
#endif
