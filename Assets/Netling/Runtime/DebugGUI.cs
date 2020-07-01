using UnityEngine;

namespace Netling
{
    public sealed class DebugGUI : MonoBehaviour
    {
        private float _rtt;
        private float _latency;
        [SerializeField, Tooltip("Button to toggle this UI")]
        private KeyCode _toggleActiveKey = KeyCode.F1;
        [SerializeField] private bool _isActive = true;

        private void OnEnable()
        {
            Client.PingReceived += OnPingReceived;
        }

        private void OnDisable()
        {
            Client.PingReceived -= OnPingReceived;
        }

        private void Update()
        {
            if (Input.GetKeyDown(_toggleActiveKey)) _isActive = !_isActive;
        }

        private void OnPingReceived(float roundTripTime, float latency)
        {
            _rtt = roundTripTime;
            _latency = latency;
        }

        private void OnGUI()
        {
            if (!_isActive) return;
            GUILayout.Label($"Debug GUI. Toggle with {_toggleActiveKey}");

            if (!Server.IsActive && GUILayout.Button("Start Server"))
            {
                Server.Instance.Start();
            }

            if (Server.IsActive && GUILayout.Button("Stop Server"))
            {
                Server.Instance.Stop();
            }

            if (Client.Instance.State == Client.ClientState.Disconnected && GUILayout.Button("Connect Client"))
            {
                Client.Instance.Connect();
            }

            if (Client.Instance.State == Client.ClientState.Connecting && GUILayout.Button("Cancel"))
            {
                Client.Instance.Disconnect();
            }

            if (Client.Instance.State == Client.ClientState.Connected && GUILayout.Button("Disconnect Client"))
            {
                Client.Instance.Disconnect();
            }

            GUILayout.TextArea($"RTT: {_rtt * 1000:0.00} ms\nLAT: {_latency * 1000:0.00} ms");
        }
    }
}