#if UNITY_EDITOR
using jeanf.EventSystem;
using jeanf.tooltip;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class TestTooltipOnClick : MonoBehaviour
{
    [Header("Broadcasting On")]
    [SerializeField]
    private TooltipEventChannelSO testChannel;

    [Space(20)]
    [SerializeField] private TooltipSO tooltipSO;
    

    public void CallFunction()
    {
        testChannel.RaiseEvent(tooltipSO);
    }
}

[CustomEditor(typeof(TestTooltipOnClick))]
public class TestTooltipOnClickEditor: Editor
{
    override public void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var eventToSend = (TestTooltipOnClick)target;
        if (GUILayout.Button("Send Tooltip", GUILayout.Height(30))){
            eventToSend.CallFunction();
        }
        GUILayout.Space(10);
    }
}
#endif