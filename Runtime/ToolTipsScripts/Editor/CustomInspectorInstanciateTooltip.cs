#if UNITY_EDITOR
using jeanf.tooltip;
using UnityEditor;
using UnityEngine;


[CustomEditor(typeof(InteractableToolTipController))]
public class CustomInspectorInstanciateTooltip : Editor
{
    public override void OnInspectorGUI()
    {
        var interactableToolTipController = (InteractableToolTipController)target;
        
        GUILayout.Label("Preview tooltip :");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Instantiate Tooltip"))
        {
            interactableToolTipController.InstanciateTooltip();
        }
        if (GUILayout.Button("Destroy Tooltip"))
        {
            interactableToolTipController.DestroyInstanciateToolTip();
        }
        GUILayout.EndHorizontal();
        
        DrawDefaultInspector();
    }
}
#endif
