using MufflonUtil;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Netling.Samples
{
    public sealed class SampleIMGUI : ToggleableIMGUI
    {
        [SerializeField, Tooltip("Name of additive sub scene")]
        private SceneReference _subScene;
        [SerializeField] private NetObject _netObjectPrefab;

        protected override void OnIMGUI()
        {
            if (!_subScene.Scene.isLoaded && GUILayout.Button("Load Network Test Sub Scene"))
            {
                SceneManager.LoadSceneAsync(_subScene.SceneName, LoadSceneMode.Additive);
            }

            if (_subScene.Scene.isLoaded && GUILayout.Button("Unload Network Test Sub Scene"))
            {
                SceneManager.UnloadSceneAsync(_subScene.Scene);
            }

            if (Server.IsActive && _subScene.Scene.isLoaded && GUILayout.Button("Spawn NetObject"))
            {
                Server.Instance.SpawnNetObject(_netObjectPrefab, _subScene, null, Vector3.zero, Quaternion.identity);
            }
        }
    }
}