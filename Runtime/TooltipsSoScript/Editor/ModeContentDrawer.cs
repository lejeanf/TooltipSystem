#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace jeanf.tooltip
{
    /// <summary>
    /// Compact drawer for <see cref="TooltipActionContentSo.ModeContent"/>: the icon object field and a
    /// single-line text field stack on the left (the text is meant to be a short action phrase, not a
    /// paragraph), with a small sprite preview on the right so the icon is visible at a glance.
    /// </summary>
    [CustomPropertyDrawer(typeof(TooltipActionContentSo.ModeContent))]
    public class ModeContentDrawer : PropertyDrawer
    {
        private const float Gap = 4f;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float line = EditorGUIUtility.singleLineHeight;
            float pad = EditorGUIUtility.standardVerticalSpacing;
            return line * 3f + pad * 2f; // header + icon row + text row
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            float line = EditorGUIUtility.singleLineHeight;
            float pad = EditorGUIUtility.standardVerticalSpacing;
            var icon = property.FindPropertyRelative("icon");
            var text = property.FindPropertyRelative("text");

            var header = new Rect(position.x, position.y, position.width, line);
            EditorGUI.LabelField(header, label, EditorStyles.boldLabel);

            float y1 = header.yMax + pad;                 // icon row
            float y2 = y1 + line + pad;                   // text row
            float thumbH = (y2 + line) - y1;              // preview spans both rows
            float thumbW = thumbH;                        // square
            float leftW = Mathf.Max(60f, position.width - thumbW - Gap);

            EditorGUI.PropertyField(new Rect(position.x, y1, leftW, line), icon, new GUIContent("Icon"));
            EditorGUI.PropertyField(new Rect(position.x, y2, leftW, line), text, new GUIContent("Text"));

            DrawSpritePreview(new Rect(position.xMax - thumbW, y1, thumbW, thumbH), icon.objectReferenceValue as Sprite);

            EditorGUI.EndProperty();
        }

        // Draw the sprite (using its atlas UVs) into r, with a faint backing box so an empty slot still reads
        // as a preview area. Fit preserving the sprite's aspect ratio (letterbox / pillarbox), centered — the
        // box is square, so never stretch a non-square glyph.
        private static void DrawSpritePreview(Rect r, Sprite sprite)
        {
            EditorGUI.DrawRect(r, new Color(0f, 0f, 0f, 0.2f));
            if (sprite == null || sprite.texture == null) return;

            var tex = sprite.texture;
            var tr = sprite.textureRect;
            var uv = new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height);

            float aspect = tr.height > 0f ? tr.width / tr.height : 1f;
            Rect fit = r;
            if (aspect >= 1f) { fit.height = r.width / aspect; fit.y = r.y + (r.height - fit.height) * 0.5f; }
            else { fit.width = r.height * aspect; fit.x = r.x + (r.width - fit.width) * 0.5f; }

            GUI.DrawTextureWithTexCoords(fit, tex, uv, true);
        }
    }
}
#endif
