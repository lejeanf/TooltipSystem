#if UNITY_EDITOR
using jeanf.universalplayer;
using UnityEditor;

namespace jeanf.tooltip
{
    [CustomEditor(typeof(InteractableToolTipInputSo))]
    public class InteractableToolTipSoEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            
            InteractableToolTipInputSo interactableToolTipInputSo = (InteractableToolTipInputSo)target;
            
            DrawDefaultInspector();
            EditorGUILayout.Space();
            
            EditorGUILayout.LabelField("XR : ", 
                $"{interactableToolTipInputSo.GetBindingName(BroadcastControlsStatus.ControlScheme.XR)}" 
                );
            EditorGUILayout.LabelField("Gamepad : ", 
                $"{interactableToolTipInputSo.GetBindingName(BroadcastControlsStatus.ControlScheme.Gamepad)}");
            EditorGUILayout.LabelField("Keyboard & Mouse : ", 
                $"{interactableToolTipInputSo.GetBindingName(BroadcastControlsStatus.ControlScheme.KeyboardMouse)}");
        }
        
    }
}

#endif