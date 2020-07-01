using UnityEngine;
using UnityEngine.SceneManagement;

namespace Netling.Samples
{
    public sealed class SampleIMGUI : MonoBehaviour
    {
        [SerializeField, Tooltip("Name of additive sub scene")]
        private string _subSceneName = "NetworkTestSub";
        [SerializeField, Tooltip("Button to toggle this UI")]
        private KeyCode _toggleActiveKey = KeyCode.F2;
        [SerializeField] private Rect _screenRect = new Rect(200, 0, 200, 500);
        [SerializeField] private bool _isActive = true;
        [SerializeField] private NetObject _netObjectPrefab;

        private void Update()
        {
            if (Input.GetKeyDown(_toggleActiveKey)) _isActive = !_isActive;
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(_screenRect);
            if (!_isActive) return;
            GUILayout.Label($"Sample IMGUI. Toggle with {_toggleActiveKey}");

            if (!SceneManager.GetSceneByName(_subSceneName).isLoaded && GUILayout.Button("Load Network Test Sub Scene"))
            {
                SceneManager.LoadSceneAsync(_subSceneName, LoadSceneMode.Additive);
            }

            if (SceneManager.GetSceneByName(_subSceneName).isLoaded &&
                GUILayout.Button("Unload Network Test Sub Scene"))
            {
                SceneManager.UnloadSceneAsync(_subSceneName);
            }

            if (Server.IsActive && SceneManager.GetSceneByName(_subSceneName).isLoaded &&
                GUILayout.Button("Spawn NetObject"))
            {
                Server.Instance.SpawnNetObject(_netObjectPrefab, Vector3.zero, Quaternion.identity);
            }

            GUILayout.EndArea();
        }
    }
}