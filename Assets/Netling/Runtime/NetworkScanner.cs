using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Unity.Collections;
using Unity.Networking.Transport;
using UnityEngine;

namespace Netling
{
    public class NetworkScanner
    {
        private NetworkDriver _networkDriver;
        public int TimeoutMilliseconds { get; set; } = 100;

        public bool IsScanning { get; private set; }
        public float Progress { get; private set; }
        public event Action<bool> ScanStateChanged;
        public event Action<float> ProgressChanged;
        private const string LocalBaseIP = "192.168.0.";

        public void BroadcastConnect(ushort port)
        {
            Client.Instance.SetEndpoint(IPAddress.Broadcast.ToString(), port, false);
            Client.Instance.Connect();
        }

        public async Task<string[]> ScanLocalNetwork(ushort port, int batchSize = 254)
        {
            if (IsScanning)
            {
                throw new InvalidOperationException("Cannot initiate scan: already scanning.");
            }

            if (batchSize <= 0)
            {
                throw new ArgumentException($"Batch size must be positive, but was {batchSize}");
            }

            Progress = 0;
            ProgressChanged?.Invoke(Progress);

            var activeServerIPs = new List<string>();
            var startIP = 1;
            while (startIP <= 255)
            {
                int endIP = Mathf.Min(startIP + batchSize - 1, 255);
                string[] ips = Enumerable.Range(startIP, endIP - startIP + 1).Select(id => LocalBaseIP + id).ToArray();
                activeServerIPs.AddRange(await Scan(ips, port));
                startIP = endIP + 1;
                Progress = endIP / 255f;
                ProgressChanged?.Invoke(Progress);
            }

            return activeServerIPs.ToArray();
        }

        public async Task<string[]> Scan([NotNull] string[] ips, ushort port)
        {
            if (ips == null) throw new ArgumentNullException(nameof(ips));

            IsScanning = true;
            ScanStateChanged?.Invoke(IsScanning);
            var activeServerIPs = new List<string>();
            Dictionary<string, NetworkConnection> pendingConnections = new();
            if (!_networkDriver.IsCreated)
            {
                _networkDriver = NetworkDriver.Create();
                _networkDriver.CreatePipeline(typeof(ReliableSequencedPipelineStage));
            }

            foreach (string ipAddress in ips)
            {
                pendingConnections[ipAddress] = _networkDriver.Connect(NetworkEndpoint.Parse(ipAddress, port));
            }

            float startTime = Time.time;
            while (Time.time < startTime + TimeoutMilliseconds / 1000f && pendingConnections.Count > 0)
            {
                _networkDriver.ScheduleUpdate().Complete();
                foreach ((string ipAddress, NetworkConnection connection) in pendingConnections)
                {
                    NetworkEvent.Type eventType;
                    while ((eventType = _networkDriver.PopEventForConnection(connection, out DataStreamReader _)) !=
                           NetworkEvent.Type.Empty)
                    {
                        if (eventType == NetworkEvent.Type.Connect)
                        {
                            activeServerIPs.Add(ipAddress);
                            connection.Disconnect(_networkDriver);
                        }
                    }
                }

                foreach (string activeServerIP in activeServerIPs)
                {
                    pendingConnections.Remove(activeServerIP); // already successfully checked
                }

                await Task.Yield();
            }

            foreach ((string _, NetworkConnection connection) in pendingConnections)
            {
                connection.Disconnect(_networkDriver);
            }

            IsScanning = false;
            ScanStateChanged?.Invoke(IsScanning);
            return activeServerIPs.ToArray();
        }

        public string GetLocalIPAddress()
        {
            return (from netInterface in NetworkInterface.GetAllNetworkInterfaces()
                where netInterface.OperationalStatus == OperationalStatus.Up
                from addrInfo in netInterface.GetIPProperties().UnicastAddresses
                where addrInfo.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                select addrInfo.Address.ToString()).FirstOrDefault();
        }
    }
}