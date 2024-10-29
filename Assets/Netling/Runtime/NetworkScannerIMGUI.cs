using UnityEngine;

namespace Netling
{
    public class NetworkScannerIMGUI : ToggleableIMGUI
    {
        [SerializeField] private ushort _port;
        [SerializeField] private int _batchSize;
        [SerializeField] private int _timeoutMilliSeconds = 100;
        private readonly NetworkScanner _networkScanner = new();

        protected override void OnIMGUI()
        {
            _networkScanner.TimeoutMilliseconds = _timeoutMilliSeconds;
            if (_networkScanner.IsScanning)
            {
                GUILayout.Label("Scanning...");
            }
            else if (GUILayout.Button("Scan Network"))
            {
                ScanNetwork();
            }

            if (GUILayout.Button("Connect via Broadcast"))
            {
                _networkScanner.BroadcastConnect(_port);
            }
        }

        private async void ScanNetwork()
        {
            string[] activeIPs = await _networkScanner.ScanLocalNetwork(_port, _batchSize);
            if (activeIPs.Length == 0) Debug.Log("No Active IPs found");
            foreach (string ip in activeIPs)
            {
                Debug.Log($"Active IP {ip}");
            }
        }
    }
}