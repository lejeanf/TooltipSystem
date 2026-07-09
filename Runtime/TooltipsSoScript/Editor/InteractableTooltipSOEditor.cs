#if UNITY_EDITOR
using jeanf.universalplayer;
using UnityEditor;

namespace jeanf.tooltip
{
    [CustomEditor(typeof(InteractableTooltipInputSo))]
    public class InteractableTooltipSoEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            
            InteractableTooltipInputSo interactableTooltipInputSo = (InteractableTooltipInputSo)target;
            
            DrawDefaultInspector();
            EditorGUILayout.Space();
            
            EditorGUILayout.LabelField("XR : ", 
                $"{interactableTooltipInputSo.GetBindingName(BroadcastControlsStatus.ControlScheme.XR)}" 
                );
            EditorGUILayout.LabelField("Gamepad : ", 
                $"{interactableTooltipInputSo.GetBindingName(BroadcastControlsStatus.ControlScheme.Gamepad)}");
            EditorGUILayout.LabelField("Keyboard & Mouse : ", 
                $"{interactableTooltipInputSo.GetBindingName(BroadcastControlsStatus.ControlScheme.KeyboardMouse)}");
        }
        
    }
}

#endif