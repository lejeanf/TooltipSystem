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
            interactableToolTipController.InstantiateTooltip();
        }
        if (GUILayout.Button("Destroy Tooltip"))
        {
            interactableToolTipController.DestroyInstantiateToolTip();
        }
        GUILayout.EndHorizontal();
        
        DrawDefaultInspector();
    }
}
#endif
