using UnityEngine;

namespace Netling
{
    public abstract class ToggleableIMGUI : MonoBehaviour
    {
        [SerializeField, Tooltip("Button to toggle this UI")]
        private KeyCode _toggleActiveKey = KeyCode.F1;
        [SerializeField] private string _name;
        [SerializeField] private Rect _screenRect = new Rect(200, 0, 200, 500);
        [SerializeField] private bool _isActive = true;

        protected virtual void Update()
        {
            if (Input.GetKeyDown(_toggleActiveKey)) _isActive = !_isActive;
        }

        protected abstract void OnIMGUI();

        private void OnGUI()
        {
            if (!_isActive) return;
            GUILayout.BeginArea(_screenRect);
            GUILayout.Label($"{_name} | Toggle with {_toggleActiveKey}");
            OnIMGUI();
            GUILayout.EndArea();
        }
    }
}