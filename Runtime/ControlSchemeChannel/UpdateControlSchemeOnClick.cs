#if UNITY_EDITOR

using jeanf.vrplayer;
using UnityEditor;
using UnityEngine;

namespace jeanf.tooltip
{
    public class UpdateControlSchemeOnClick : MonoBehaviour
    {
        
        [Header("Broadcasting on:")] [SerializeField]
        private ControlSchemeChannelSo TestChannel;
        
        [Space(20)]
        [SerializeField] private BroadcastControlsStatus.ControlScheme valueToSend = BroadcastControlsStatus.ControlScheme.KeyboardMouse;
        
        public void CallFunction()
        {
            TestChannel.RaiseEvent(valueToSend);
        }
    }
    
    [CustomEditor(typeof(UpdateControlSchemeOnClick))]
    public class UpdateControlSchemeOnClickEditor : Editor {
        public override void  OnInspectorGUI () {
            DrawDefaultInspector();
            var eventToSend = (UpdateControlSchemeOnClick)target;
            if(GUILayout.Button("Send Control Scheme", GUILayout.Height(30))) {
                eventToSend.CallFunction(); // how do i call this?
            }
            GUILayout.Space(10);
        }
    }
}

#endif