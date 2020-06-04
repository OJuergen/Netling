using UnityEngine;

namespace Networking.Samples
{
    public class SampleRunner : MonoBehaviour
    {
        [SerializeField] private Player _playerPrefab;
        [SerializeField] private NetObjectManager _netObjectManager;
        [SerializeField] private int _targetFrameRate = 200;

        private void Start()
        {
            Application.targetFrameRate = _targetFrameRate;
            if (_playerPrefab == null)
            {
                Debug.LogError("Player prefab cannot be null!");
                Application.Quit(-1);
            }

            PlayerManager.Instance.Init(_playerPrefab);
        }
    }
}