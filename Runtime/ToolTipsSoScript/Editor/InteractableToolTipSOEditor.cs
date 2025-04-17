#if UNITY_EDITOR
using jeanf.vrplayer;
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
                $"{interactableToolTipInputSo.GetBindingName(BroadcastControlsStatus.ControlScheme.XR)} {interactableToolTipInputSo.followingMessage}" 
                );
            EditorGUILayout.LabelField("Gamepad : ", 
                $"{interactableToolTipInputSo.GetBindingName(BroadcastControlsStatus.ControlScheme.Gamepad)} {interactableToolTipInputSo.followingMessage}");
            EditorGUILayout.LabelField("Keyboard & Mouse : ", 
                $"{interactableToolTipInputSo.GetBindingName(BroadcastControlsStatus.ControlScheme.KeyboardMouse)} {interactableToolTipInputSo.followingMessage}");
        }
        
    }
}

#endif