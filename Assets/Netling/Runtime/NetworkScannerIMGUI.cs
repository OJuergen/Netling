using UnityEngine;

namespace Netling
{
    public class NetworkScannerIMGUI : ToggleableIMGUI
    {
        [SerializeField] private ushort _port;
        private readonly NetworkScanner _networkScanner = new();

        protected override void OnIMGUI()
        {
            if (_networkScanner.IsScanning)
            {
                GUILayout.Label("Scanning...");
            }
            else if (GUILayout.Button("Scan Network"))
            {
                ScanNetwork();
            }
        }

        private async void ScanNetwork()
        {
            string[] activeIPs = await _networkScanner.ScanNetwork(_port);
            if (activeIPs.Length == 0) Debug.Log("No Active IPs found");
            foreach (string ip in activeIPs)
            {
                Debug.Log($"Active IP {ip}");
            }
        }
    }
}