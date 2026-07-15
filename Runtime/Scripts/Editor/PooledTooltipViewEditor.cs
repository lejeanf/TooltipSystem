#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace jeanf.tooltip
{
    /// <summary>
    /// Inspector for <see cref="PooledTooltipView"/>. The pooled prefab is set up once, so day-to-day you
    /// only touch the two per-state size sliders (and the colours). Every granular sizing / animation /
    /// wiring field is tucked into a collapsed <b>Advanced</b> foldout; the editor-preview authoring aids
    /// get their own foldout.
    /// </summary>
    [CustomEditor(typeof(PooledTooltipView))]
    public class PooledTooltipViewEditor : Editor
    {
        private static bool _advanced;
        private static bool _preview = true;

        // Shown OUTSIDE Advanced, in this order (the two size sliders + the colours).
        private static readonly string[] Primary = { "minimizedScale", "expandedScale", "color", "contentColor" };
        // Editor-preview authoring aids — their own foldout, not Advanced.
        private static readonly string[] Preview = { "previewExpandedInEditor", "previewContentSo", "previewMode" };

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));

            EditorGUILayout.LabelField("Size & colour", EditorStyles.boldLabel);
            DrawProps(Primary);

            EditorGUILayout.Space();
            _preview = EditorGUILayout.Foldout(_preview, "Editor preview", true, EditorStyles.foldoutHeader);
            if (_preview)
            {
                EditorGUI.indentLevel++;
                DrawProps(Preview);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();
            _advanced = EditorGUILayout.Foldout(_advanced, "Advanced (granular sizing, animation, wiring)", true, EditorStyles.foldoutHeader);
            if (_advanced)
            {
                EditorGUI.indentLevel++;
                DrawRemaining();
                EditorGUI.indentLevel--;
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawProps(string[] names)
        {
            foreach (var n in names)
            {
                var p = serializedObject.FindProperty(n);
                if (p != null) EditorGUILayout.PropertyField(p, true);
            }
        }

        // Everything not primary / preview / script -> the Advanced foldout, in declaration order.
        private void DrawRemaining()
        {
            var shown = new HashSet<string>(Primary) { "m_Script" };
            foreach (var n in Preview) shown.Add(n);

            var it = serializedObject.GetIterator();
            bool enterChildren = true;
            while (it.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (shown.Contains(it.name)) continue;
                EditorGUILayout.PropertyField(it, true);
            }
        }
    }
}
#endif
