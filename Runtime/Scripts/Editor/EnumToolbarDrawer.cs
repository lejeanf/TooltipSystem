#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace jeanf.tooltip
{
    [CustomPropertyDrawer(typeof(EnumToolbarAttribute))]
    public class EnumToolbarDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.Enum)
            {
                EditorGUI.PropertyField(position, property, label);
                return;
            }

            EditorGUI.BeginProperty(position, label, property);
            Rect toolbarRect = EditorGUI.PrefixLabel(position, label);
            EditorGUI.BeginChangeCheck();
            int selected = GUI.Toolbar(toolbarRect, property.enumValueIndex, property.enumDisplayNames);
            if (EditorGUI.EndChangeCheck()) property.enumValueIndex = selected;
            EditorGUI.EndProperty();
        }
    }
}
#endif
