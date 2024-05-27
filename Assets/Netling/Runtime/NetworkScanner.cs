using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Networking.Transport;
using UnityEngine;

namespace Netling
{
    public class NetworkScanner
    {
        private readonly List<string> _activeServers = new();
        private NetworkDriver _networkDriver;
        public int TimeoutMilliseconds { get; set; } = 100;

        public bool IsScanning { get; private set; }

        public async Task<string[]> ScanNetwork(ushort port)
        {
            if (IsScanning)
            {
                Debug.LogError($"Cannot start scan: already scanning. Wait until finished.");
                return Array.Empty<string>();
            }

            IsScanning = true;
            _activeServers.Clear();
            
            string localIP = GetLocalIPAddress();
            string baseIP = localIP[..(localIP.LastIndexOf('.') + 1)];
            string[] localNetworkIPs = Enumerable.Range(1, 254).Select(id => baseIP + id).ToArray();
            Dictionary<string, NetworkConnection> pendingConnections = new();
            if (_networkDriver.IsCreated) _networkDriver.Dispose();
            _networkDriver = NetworkDriver.Create();
            _networkDriver.CreatePipeline(typeof(ReliableSequencedPipelineStage));

            foreach (string ipAddress in localNetworkIPs)
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
                            _activeServers.Add(ipAddress);
                            connection.Disconnect(_networkDriver);
                        }
                    }
                }

                foreach (string activeServer in _activeServers)
                {
                    pendingConnections.Remove(activeServer); // already successfully checked
                }

                await Task.Yield();
            }

            foreach ((string _, NetworkConnection connection) in pendingConnections)
            {
                connection.Disconnect(_networkDriver);
            }

            IsScanning = false;
            return _activeServers.ToArray();
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