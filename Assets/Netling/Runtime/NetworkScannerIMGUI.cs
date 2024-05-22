using UnityEngine;

namespace Netling
{
    public class NetworkScannerIMGUI : ToggleableIMGUI
    {
        [SerializeField] private ushort _port;
        private readonly NetworkScanner _networkScanner = new();

        protected override void OnIMGUI()
        {
            if (GUILayout.Button("Scan Network"))
            {
                ScanNetwork();
            }

            if (GUILayout.Button("Probe Network"))
            {
                ProbeNetwork();
            }
        }

        private async void ScanNetwork()
        {
            string[] activeIPs = await _networkScanner.ScanNetwork();
            if (activeIPs.Length == 0) Debug.Log("No Active IPs found");
            foreach (string ip in activeIPs)
            {
                Debug.Log($"Active IP {ip}");
            }
        }

        private async void ProbeNetwork()
        {
            string[] serverIPs = await _networkScanner.ProbeNetwork(_port);
            if (serverIPs.Length == 0) Debug.Log("No Servers found");
            foreach (string ip in serverIPs)
            {
                Debug.Log($"Active Server under IP {ip}");
            }
        }
    }
}