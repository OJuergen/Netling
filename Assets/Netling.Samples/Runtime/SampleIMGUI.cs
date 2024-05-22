using UnityEngine;
using UnityEngine.SceneManagement;

namespace Netling.Samples
{
    public sealed class SampleIMGUI : ToggleableIMGUI
    {
        [SerializeField, Tooltip("Name of additive sub scene")]
        private string _subSceneName = "NetworkTestSub";
        [SerializeField] private NetObject _netObjectPrefab;

        protected override void OnIMGUI()
        {
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
                Server.Instance.SpawnNetObject(_netObjectPrefab, Vector3.zero, Quaternion.identity, _subSceneName);
            }
        }
    }
}