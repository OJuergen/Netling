using UnityEngine;

namespace Netling
{
    public sealed class DebugGUI : ToggleableIMGUI
    {
        private float _rtt;
        private float _latency;

        private void OnEnable()
        {
            Client.Instance.PingReceived += OnPingReceived;
        }

        private void OnDisable()
        {
            Client.Instance.PingReceived -= OnPingReceived;
        }

        private void OnPingReceived(float roundTripTime, float latency)
        {
            _rtt = roundTripTime;
            _latency = latency;
        }

        protected override void OnIMGUI()
        {
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