using UnityEngine;

namespace Networking
{
    public class ClientRunner : MonoBehaviour
    {
        [SerializeField] private string _ip;
        [SerializeField] private bool _useLocalhost;
        [SerializeField] private bool _useSimulationPipeline;
        [SerializeField] private ushort _port = 9099;
        [SerializeField] private bool _autoConnect;
        [SerializeField] private float _timeout = 30;
        [SerializeField] private float _netObjectUpdateRate = 20;
        [SerializeField] private float _pingRate = 10;
        [SerializeField] private GameActionManager _gameActionManager;

        private float _lastNetObjectUpdateTime;
        private float _lastPingTime;

        private void Awake()
        {
            _lastPingTime = Time.time;
            _lastNetObjectUpdateTime = Time.time;
            Client.Instance.Init(_ip, _port, _useLocalhost, _timeout, _useSimulationPipeline, _gameActionManager);
            if (_autoConnect) Client.Instance.Connect();
        }

        private void OnDestroy()
        {
            if (Client.IsConnected) Client.Instance.Disconnect();
            Client.Instance.Dispose();
        }

        private void Update()
        {
            float time = Time.time;
            Client.Instance.Tick();
            if (Client.IsConnected && time > _lastNetObjectUpdateTime + 1 / _netObjectUpdateRate)
            {
                _lastNetObjectUpdateTime = time;
                Client.Instance.SendBatchedNetObjectsUpdate();
            }
            if (Client.IsConnected && time > _lastPingTime + 1 / _pingRate)
            {
                _lastPingTime = time;
                Client.Instance.SendPing();
            }
        }

        [ContextMenu("Disconnect")]
        private void Disconnect()
        {
            Client.Instance.Disconnect();
        }

        private void OnApplicationQuit()
        {
            if (Client.IsConnected) Client.Instance.Disconnect();
        }
    }
}