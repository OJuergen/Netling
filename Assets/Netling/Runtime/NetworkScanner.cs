using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Networking.Transport;
using UnityEngine;
using Ping = System.Net.NetworkInformation.Ping;

namespace Netling
{
    public class NetworkScanner
    {
        private readonly List<string> _activeServers = new();


        private NetworkDriver _networkDriver;
        public NetworkScannerState State { get; private set; }
        public int TimeoutMilliseconds { get; set; } = 100;

        public enum NetworkScannerState
        {
            Idle,
            Scanning,
            Probing
        }

        public async Task<string[]> ProbeNetwork(ushort port)
        {
            if (State != NetworkScannerState.Idle)
            {
                Debug.LogError($"Cannot start probing when in state {State}");
                return Array.Empty<string>();
            }

            string[] activeIPs = await ScanNetwork();

            State = NetworkScannerState.Probing;
            _activeServers.Clear();
            Dictionary<string, NetworkConnection> connections = new();
            if (_networkDriver.IsCreated) _networkDriver.Dispose();
            _networkDriver = NetworkDriver.Create();
            _networkDriver.CreatePipeline(typeof(ReliableSequencedPipelineStage));

            foreach (string ipAddress in activeIPs)
            {
                connections[ipAddress] = _networkDriver.Connect(NetworkEndpoint.Parse(ipAddress, port));
            }

            float startTime = Time.time;
            while (Time.time < startTime + TimeoutMilliseconds / 1000f)
            {
                _networkDriver.ScheduleUpdate().Complete();
                foreach ((string ipAddress, NetworkConnection connection) in connections)
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
                    connections.Remove(activeServer); // already successfully checked
                }

                await Task.Yield();
            }

            foreach ((string _, NetworkConnection connection) in connections)
            {
                connection.Disconnect(_networkDriver);
            }

            State = NetworkScannerState.Idle;
            return _activeServers.ToArray();
        }

        public async Task<string[]> ScanNetwork()
        {
            if (State != NetworkScannerState.Idle)
            {
                Debug.LogError($"Cannot start scanning when in state {State}");
                return Array.Empty<string>();
            }

            State = NetworkScannerState.Scanning;

            string localIP = GetLocalIPAddress();
            if (localIP == null)
            {
                Debug.LogError("Unable to get local IP address.");
                State = NetworkScannerState.Idle;
                return Array.Empty<string>();
            }

            string baseIP = localIP[..(localIP.LastIndexOf('.') + 1)];

            string[] activeIPs =
                (await Task.WhenAll(Enumerable.Range(1, 254)
                    .Select(id => baseIP + id)
                    .Select(async ip => (ip, isActive: await PingAddress(ip)))))
                .Where(result => result.isActive)
                .Select(result => result.ip)
                .ToArray();
            State = NetworkScannerState.Idle;
            return activeIPs;
        }

        private async Task<bool> PingAddress(string ip)
        {
            var ping = new Ping();
            try
            {
                PingReply reply = await Task.Run(() => ping.Send(ip, TimeoutMilliseconds));
                bool isActive = reply is { Status: IPStatus.Success };
                return isActive;
            }
            catch (Exception)
            {
                return false;
            }
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