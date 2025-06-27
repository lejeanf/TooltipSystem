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
        if (GUILayout.Button("Instanciate Tooltip"))
        {
            interactableToolTipController.InstanciateTooltip();
        }
        if (GUILayout.Button("Instanciate Tooltip"))
        {
            interactableToolTipController.DestroyInstanciateToolTip();
        }
        GUILayout.EndHorizontal();
        
        DrawDefaultInspector();
    }
}
