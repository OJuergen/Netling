using System;
using System.Linq;
using UnityEngine;

namespace Netling
{
    public class ServerRunner : MonoBehaviour
    {
        [SerializeField] private ushort _minPort;
        [SerializeField] private ushort _maxPort;
        [SerializeField] private float _clientConnectionTimeout = 30;
        [SerializeField] private bool _acceptAllPlayers;
        [SerializeField] private bool _useSimulationPipeline;
        [SerializeField] private float _netObjectUpdateRate = 20;
        [SerializeField] private float _netAssetUpdateRate = 20;
        [SerializeField] private bool _autoStart;

        private float _lastNetObjectUpdateTime;
        private float _lastNetAssetUpdateTime;

        private void Awake()
        {
            if (_maxPort < _minPort || _minPort < 1024)
                throw new InvalidOperationException("Illegal port range. Must be between 1024 and 65535");
            ushort[] ports = Enumerable.Range(_minPort, _maxPort - _minPort + 1)
                .Select(port => (ushort) port)
                .ToArray();
            Server.Instance.Init(ports, _clientConnectionTimeout, _acceptAllPlayers, _useSimulationPipeline);
            if (_autoStart) Server.Instance.Start();
        }

        private void OnDestroy()
        {
            if (Server.IsActive) Server.Instance.Stop();
            Server.Instance.Dispose();
        }

        private void Update()
        {
            if (!Server.IsActive)
                return;

            Server.Instance.Tick();
            if (Time.time > _lastNetObjectUpdateTime + 1 / _netObjectUpdateRate)
            {
                _lastNetObjectUpdateTime = Time.time;
                Server.Instance.SendNetObjectsUpdate();
            }
            if (Time.time > _lastNetAssetUpdateTime + 1 / _netAssetUpdateRate)
            {
                _lastNetAssetUpdateTime = Time.time;
                Server.Instance.SendNetAssetUpdate(false);
            }
        }

        private void OnApplicationQuit()
        {
            if (Server.IsActive) Server.Instance.Stop();
        }
    }
}