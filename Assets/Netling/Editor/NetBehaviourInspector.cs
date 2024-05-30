using UnityEditor;
using UnityEngine;

namespace Netling.Editor
{
    [CustomEditor(typeof(NetBehaviour), true)]
    public class NetBehaviourInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            var netBehaviour = (NetBehaviour)target;
            var netObject = netBehaviour.GetComponentInParent<NetObject>();
            if (netObject == null)
            {
                EditorGUILayout.HelpBox("NetObject component missing. Add a NetObject to this or a parent.",
                    MessageType.Warning);
                if (GUILayout.Button("Add here")) netBehaviour.gameObject.AddComponent<NetObject>();
            }
        }
    }
}