using UnityEngine;

namespace Netling
{
    public abstract class ToggleableIMGUI : MonoBehaviour
    {
        [SerializeField, Tooltip("Button to toggle this UI")]
        private KeyCode _toggleActiveKey = KeyCode.F1;
        [SerializeField] private string _name;
        [SerializeField] private Rect _rect = new Rect(200, 0, 200, 500);
        [SerializeField] private bool _isActive = true;
        [SerializeField] private float _padding = 5;

        private bool _isResizingX;
        private bool _isResizingY;
        private Vector2 _lastMousePos;
        private bool _isDragging;

        protected virtual void Update()
        {
            if (Input.GetKeyDown(_toggleActiveKey)) _isActive = !_isActive;
        }

        protected abstract void OnIMGUI();

        private void OnGUI()
        {
            if (!_isActive) return;

            GUI.Box(_rect, ""); // background

            // IMGUI content
            GUILayout.BeginArea(new Rect(_rect.x + _padding, _rect.y + _padding,
                _rect.width - 2 * _padding, _rect.height - 2 * _padding));
            GUILayout.Label($"{_name} | Toggle with {_toggleActiveKey}");
            OnIMGUI();
            GUILayout.EndArea();

            // Get the current mouse position
            Vector2 mousePos = Event.current.mousePosition;

            // Determine if the mouse is near the edges of the rect for resizing
            bool mouseNearRightEdge = Mathf.Abs(mousePos.x - _rect.xMax) < 10
                                      && mousePos.y > _rect.yMin && mousePos.y < _rect.yMax;
            bool mouseNearBottomEdge = Mathf.Abs(mousePos.y - _rect.yMax) < 10
                                       && mousePos.x > _rect.xMin && mousePos.x < _rect.xMax;
            bool mouseNearEdge = mouseNearRightEdge || mouseNearBottomEdge;

            // Change the cursor if near the edges
            if (mouseNearEdge && !_isResizingX && !_isResizingY)
            {
                if (mouseNearRightEdge && mouseNearBottomEdge)
                {
                    Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto); // Change to a resize cursor if available
                }
                else if (mouseNearRightEdge)
                {
                    Cursor.SetCursor(null, Vector2.zero,
                        CursorMode.Auto); // Change to a horizontal resize cursor if available
                }
                else if (mouseNearBottomEdge)
                {
                    Cursor.SetCursor(null, Vector2.zero,
                        CursorMode.Auto); // Change to a vertical resize cursor if available
                }
            }

            // Handle mouse events for resizing
            if (Event.current.type == EventType.MouseDown && mouseNearEdge)
            {
                _isResizingY = mouseNearBottomEdge;
                _isResizingX = mouseNearRightEdge;
                _lastMousePos = mousePos;
                Event.current.Use();
            }

            if (Event.current.type == EventType.MouseDown && _rect.Contains(mousePos))
            {
                _isDragging = true;
                _lastMousePos = mousePos;
                Event.current.Use();
            }

            if (Event.current.type == EventType.MouseDrag && (_isResizingX || _isResizingY))
            {
                if (_isResizingX) _rect.width += mousePos.x - _lastMousePos.x;
                if (_isResizingY) _rect.height += mousePos.y - _lastMousePos.y;
                _rect.size = new Vector2(Mathf.Max(_rect.width, 50), Mathf.Max(_rect.height, 50));
                _lastMousePos = mousePos;
                Event.current.Use();
            }

            if (Event.current.type == EventType.MouseDrag && _isDragging)
            {
                _rect.position += mousePos - _lastMousePos;
                _lastMousePos = mousePos;
                Event.current.Use();
            }

            if (Event.current.type == EventType.MouseUp)
            {
                _isResizingX = false;
                _isResizingY = false;
                _isDragging = false;
            }
        }
    }
}