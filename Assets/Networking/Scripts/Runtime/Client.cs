using System;
using JetBrains.Annotations;
using Unity.Collections;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Utilities;
using UnityEngine;

namespace Networking
{
    public class Client : IDisposable
    {
        public enum ClientState
        {
            Disconnected,
            Connecting,
            Connected,
            Debug
        }

        private const int MaxBytesPerMessage = 1300; // 1400 causes errors on receiving side

        private static Client _instance;
        public static Client Instance => _instance = _instance ?? new Client();
        public static bool IsConnected => Instance.State == ClientState.Connected;
        public static bool IsConnecting => Instance.State == ClientState.Connecting;
        public ClientState State { get; private set; } = ClientState.Disconnected;
        public static bool IsHost => Server.IsActive && IsConnected;
        public bool UseLocalhost { get; set; }

        public int ActorNumber { get; private set; } = -2;
        public event Action Connected;
        public event Action Disconnected;

        private ushort _port;
        private string _ip;
        private NetworkDriver _clientDriver;
        private NetworkConnection _clientToServerConnection;
        private float _timeout;
        private GameActionManager _gameActionManager;
        private float _averageServerTimeOffset;

        // pendingPing is a ping sent to the server which have not yet received a response.
        public float LastPongTime { get; private set; }

        // The ping stats are two integers, time for last ping and number of pings
        public float RoundTripTime { get; private set; }
        public float Latency { get; private set; }
        private bool _initialized;
        private NetworkPipeline _unreliablePipeline;
        private NetworkPipeline _reliablePipeline;

        public delegate void PingDelegate(float roundTripTime, float latency);

        public static event PingDelegate PingReceived;
        public static event Action<int> DataReceived;
        public static event Action<int> DataSent;

        public void Init(string ip, ushort port, bool useLocalhost, float timeout, bool useSimulationPipeline,
                         [NotNull] GameActionManager gameActionManager)
        {
            SetEndpoint(ip, port, useLocalhost);
            _timeout = timeout;
            _averageServerTimeOffset = 0;
            _gameActionManager = gameActionManager;
            if (_clientDriver.IsCreated) _clientDriver.Dispose();
            _clientDriver = new NetworkDriver(new UDPNetworkInterface(), new SimulatorUtility.Parameters
            {
                MaxPacketCount = 30,
                PacketDropPercentage = 5,
                MaxPacketSize = 256,
                PacketDelayMs = 50
            }, new ReliableUtility.Parameters {WindowSize = 32});
            _unreliablePipeline = useSimulationPipeline
                ? _clientDriver.CreatePipeline(typeof(SimulatorPipelineStage))
                : NetworkPipeline.Null;
            _reliablePipeline = useSimulationPipeline
                ? _clientDriver.CreatePipeline(typeof(ReliableSequencedPipelineStage), typeof(SimulatorPipelineStage))
                : _clientDriver.CreatePipeline(typeof(ReliableSequencedPipelineStage));
            _initialized = true;
        }

        public void SetEndpoint(string ip, ushort port, bool useLocalhost)
        {
            _ip = ip;
            _port = port;
            UseLocalhost = useLocalhost;
        }

        public void Connect()
        {
            LastPongTime = Time.time;
            if (State != ClientState.Disconnected)
            {
                Debug.LogWarning($"Cannot connect in client state {State}");
                return;
            }

            if (!_initialized)
            {
                Debug.LogWarning("Cannot connect: not initialized. You must call Init() first.");
                return;
            }

            Debug.Log("Connecting...");
            State = ClientState.Connecting;
            NetworkEndPoint endpoint = string.IsNullOrEmpty(_ip) || UseLocalhost
                ? NetworkEndPoint.LoopbackIpv4
                : NetworkEndPoint.Parse(_ip, _port);
            endpoint.Port = _port;
            _clientToServerConnection = _clientDriver.Connect(endpoint);
        }

        public void Tick()
        {
            if (!_clientDriver.IsCreated) return;
            _clientDriver.ScheduleUpdate().Complete();

            if (_timeout > 0 && IsConnected && Time.time - LastPongTime > _timeout)
            {
                Debug.LogWarning("Disconnected due to timeout");
                Disconnect();
                return;
            }

            // listen for events
            NetworkEvent.Type eventType;
            while ((eventType = _clientToServerConnection.PopEvent(_clientDriver, out DataStreamReader streamReader))
                   != NetworkEvent.Type.Empty)
            {
                if (eventType == NetworkEvent.Type.Connect)
                {
                    Debug.Log("Connected!");
                    State = ClientState.Connected;
                    Connected?.Invoke();
                }
                else if (eventType == NetworkEvent.Type.Data)
                {
                    DataReceived?.Invoke(streamReader.Length);
                    int command = streamReader.ReadInt();
                    switch (command)
                    {
                        case Commands.AssignActorNumber:
                            ActorNumber = streamReader.ReadInt();
                            Debug.Log($"Got assigned actor number {ActorNumber}");
                            DataStreamWriter writer =
                                _clientDriver.BeginSend(_reliablePipeline, _clientToServerConnection);
                            writer.WriteInt(Commands.AcknowledgeActorNumber);
                            _clientDriver.EndSend(writer);
                            DataSent?.Invoke(writer.Length);
                            break;
                        case Commands.Ping:
                        {
                            LastPongTime = Time.time;
                            float sendLocalTime = streamReader.ReadFloat();
                            float serverTime = streamReader.ReadFloat();
                            float serverDeltaTime = streamReader.ReadFloat();
                            RoundTripTime = Time.time - sendLocalTime;
                            Latency = Mathf.Max(0, (RoundTripTime - serverDeltaTime / 2 - Time.deltaTime / 2) / 2);
                            // estimated server time NOW is received serverTime + latency for one trip + average frame wait on client side
                            float serverTimeOffset = serverTime - Time.time + Latency + Time.deltaTime / 2;
                            _averageServerTimeOffset = Mathf.Abs(_averageServerTimeOffset - serverTime) > 0.5f
                                ? serverTimeOffset
                                : 0.9f * _averageServerTimeOffset + 0.1f * serverTimeOffset;
                            PingReceived?.Invoke(RoundTripTime, Latency);
                            break;
                        }
                        case Commands.SpawnNetObjects:
                        {
                            if (IsHost) break;
                            int count = streamReader.ReadInt();

                            for (var i = 0; i < count; i++)
                            {
                                int netObjID = streamReader.ReadInt();
                                ushort prefabIndex = streamReader.ReadUShort();
                                int ownerActorNumber = streamReader.ReadInt();
                                Vector3 position = streamReader.ReadVector3();
                                Quaternion rotation = streamReader.ReadQuaternion();
                                int size = streamReader.ReadInt();
                                int bytesRead = streamReader.GetBytesRead();
                                NetObject netObject = NetObjectManager.Instance.SpawnOnClient(netObjID, prefabIndex,
                                    ownerActorNumber, position, rotation);
                                if (netObject != null)
                                    netObject.Deserialize(ref streamReader, behaviour => true);
                                else
                                    streamReader.DiscardBytes(size);
                                if (streamReader.GetBytesRead() - bytesRead != size)
                                    Debug.LogWarning("Did not deserialize properly!");
                            }

                            break;
                        }
                        case Commands.UpdateNetAssets:
                        {
                            if (IsHost) break;
                            int assetsInMessage = streamReader.ReadInt();
                            for (var i = 0; i < assetsInMessage; i++)
                            {
                                int assetNetID = streamReader.ReadInt();
                                int size = streamReader.ReadInt();
                                int bytesRead = streamReader.GetBytesRead();
                                try
                                {
                                    NetAsset netAsset = NetAssetManager.Instance.Get(assetNetID);
                                    netAsset.Deserialize(ref streamReader);
                                }
                                catch (Exception e)
                                {
                                    Debug.LogException(new NetException($"Failed to update net asset {assetNetID}", e));
                                    streamReader.DiscardBytes(size + bytesRead - streamReader.GetBytesRead());
                                }
                            }

                            break;
                        }
                        case Commands.UpdateNetObjects:
                        {
                            if (IsHost) break;
                            int objectsInMessage = streamReader.ReadInt();
                            for (var i = 0; i < objectsInMessage; i++)
                            {
                                int netObjID = streamReader.ReadInt();
                                int size = streamReader.ReadInt();
                                if (NetObjectManager.Instance.Exists(netObjID))
                                {
                                    NetObject netObject = NetObjectManager.Instance.Get(netObjID);
                                    int bytesRead = streamReader.GetBytesRead();
                                    try
                                    {
                                        netObject.Deserialize(ref streamReader, behaviour => !behaviour.HasAuthority);
                                    }
                                    catch (Exception e)
                                    {
                                        Debug.LogException(
                                            new NetException($"Failed to update net object {netObjID}", e));
                                        streamReader.DiscardBytes(size + bytesRead - streamReader.GetBytesRead());
                                    }
                                }
                                else
                                {
                                    streamReader.DiscardBytes(size);
                                }
                            }

                            break;
                        }
                        case Commands.UnspawnNetObjects:
                        {
                            if (IsHost) break;
                            int count = streamReader.ReadInt();
                            for (var i = 0; i < count; i++)
                            {
                                int netObjID = streamReader.ReadInt();
                                NetObjectManager.Instance.Unspawn(netObjID);
                            }

                            break;
                        }
                        case Commands.GameAction:
                        {
                            if (IsHost) break;
                            int gameActionID = streamReader.ReadInt();
                            int actorNumber = streamReader.ReadInt();
                            float triggerTime = streamReader.ReadFloat();
                            bool valid = streamReader.ReadBool();
                            try
                            {
                                GameAction gameAction = _gameActionManager.Get(gameActionID);
                                GameAction.IParameters parameters = gameAction.DeserializeParameters(ref streamReader);
                                gameAction.ReceiveOnClient(parameters, valid, actorNumber, triggerTime);
                                break;
                            }
                            catch (Exception e)
                            {
                                Debug.LogException(e);
                                break;
                            }
                        }
                        case Commands.NetAssetRPC:
                        {
                            if (IsHost) break;
                            float sentServerTime = streamReader.ReadFloat();
                            int netAssetID = streamReader.ReadInt();
                            NetAsset netAsset = NetAssetManager.Instance.Get(netAssetID);
                            NetAsset.RPC rpc = netAsset.DeserializeRPC(ref streamReader);
                            var messageInfo = new MessageInfo
                                {SentServerTime = sentServerTime, SenderActorNumber = Server.ServerActorNumber};
                            rpc.Invoke(messageInfo);
                            break;
                        }
                        case Commands.NetObjectRPC:
                        {
                            if (IsHost) break;
                            float sentServerTime = streamReader.ReadFloat();
                            int netObjectID = streamReader.ReadInt();
                            if (!NetObjectManager.Instance.Exists(netObjectID))
                            {
                                Debug.LogWarning("Ignoring received RPC, because NetObject was not found.");
                                break;
                            }

                            NetObject netObject = NetObjectManager.Instance.Get(netObjectID);
                            ushort netBehaviourID = streamReader.ReadUShort();
                            NetBehaviour netBehaviour = netObject.Get(netBehaviourID);
                            NetObjectManager.RPC rpc =
                                NetObjectManager.Instance.DeserializeRPC(ref streamReader, netBehaviour);
                            var messageInfo = new MessageInfo
                                {SentServerTime = sentServerTime, SenderActorNumber = Server.ServerActorNumber};
                            rpc.Invoke(messageInfo);
                            break;
                        }
                        default:
                            Debug.LogError($"Unknown event type {eventType}");
                            break;
                    }
                }
                else if (eventType == NetworkEvent.Type.Disconnect)
                {
                    Debug.Log("Disconnected!");
                    Disconnected?.Invoke();
                    State = ClientState.Disconnected;
                    _clientToServerConnection = default;
                }
            }
        }

        public void SendPlayerData(string playerData)
        {
            if (State == ClientState.Debug) return;
            if (State != ClientState.Connected)
                throw new InvalidOperationException("Cannot send player data: client not connected");

            DataStreamWriter writer = _clientDriver.BeginSend(_reliablePipeline, _clientToServerConnection);
            writer.WriteInt(Commands.PlayerData);
            writer.WriteManagedString(playerData);
            _clientDriver.EndSend(writer);
            DataSent?.Invoke(writer.Length);
        }

        public void SendBatchedNetObjectsUpdate()
        {
            if (State == ClientState.Debug) return;
            if (State != ClientState.Connected)
            {
                Debug.LogWarning($"Cannot send messages in client state {State}");
                return;
            }

            if (IsHost) return;

            NetObject[] netObjects = NetObjectManager.Instance.NetObjects;

            const int headerSizeInBytes = 8;
            var streamWriter = new DataStreamWriter(MaxBytesPerMessage, Allocator.Temp);
            var objectWriter = new DataStreamWriter(MaxBytesPerMessage - headerSizeInBytes, Allocator.Temp);
            var objectIndex = 0;
            // compose new message if objects left to send or serialize
            while (objectIndex < netObjects.Length || objectWriter.Length > 0)
            {
                // header
                streamWriter.Clear();
                streamWriter.WriteInt(Commands.UpdateNetObjects);
                DataStreamWriter objectCountWriter = streamWriter;
                streamWriter.WriteInt(0);

                // add items as long as they fit
                var objectsInMessage = 0;
                while (streamWriter.Length + objectWriter.Length <= MaxBytesPerMessage)
                {
                    if (objectWriter.Length > 0)
                    {
                        streamWriter.WriteBytes(objectWriter.AsNativeArray());
                        objectWriter.Clear();
                        objectsInMessage++;
                    }

                    // next object. Write if dirty and controlled by this client
                    if (objectIndex < netObjects.Length)
                    {
                        NetObject netObject = netObjects[objectIndex++];
                        if (netObject.IsDirty && netObject.IsMine)
                            WriteNetObject(netObject, ref objectWriter);
                    }
                    else break;
                }

                objectCountWriter.WriteInt(objectsInMessage);

                // message complete. Send if payload exists
                if (objectsInMessage > 0)
                {
                    DataStreamWriter writer = _clientDriver.BeginSend(_reliablePipeline, _clientToServerConnection);
                    writer.WriteBytes(streamWriter.AsNativeArray());
                    _clientDriver.EndSend(writer);
                    DataSent?.Invoke(writer.Length);
                }
            }
        }

        private static void WriteNetObject(NetObject netObject, ref DataStreamWriter streamWriter)
        {
            if (!netObject.IsMine) return;
            streamWriter.WriteInt(netObject.ID);
            DataStreamWriter sizeWriter = streamWriter;
            streamWriter.WriteInt(0);
            int length = streamWriter.Length;
            netObject.Serialize(ref streamWriter, false, b => b.HasAuthority);
            sizeWriter.WriteInt(streamWriter.Length - length);
        }

        public void SendPing()
        {
            if (State == ClientState.Debug) return;
            if (State != ClientState.Connected)
            {
                Debug.LogWarning($"Cannot send messages in client state {State}");
                return;
            }

            DataStreamWriter writer = _clientDriver.BeginSend(_unreliablePipeline, _clientToServerConnection);
            writer.WriteInt(Commands.Ping);
            writer.WriteFloat(Time.time);
            _clientDriver.EndSend(writer);
            DataSent?.Invoke(writer.Length);
        }

        public float EstimateServerTime()
        {
            return Time.time + _averageServerTimeOffset;
        }

        public void SendGameAction(GameAction gameAction, GameAction.IParameters parameters)
        {
            if (State == ClientState.Debug) return;
            if (State != ClientState.Connected)
                throw new InvalidOperationException($"Cannot send game action {gameAction}: not connected");

            DataStreamWriter streamWriter = _clientDriver.BeginSend(_reliablePipeline, _clientToServerConnection);
            streamWriter.WriteInt(Commands.GameAction);
            streamWriter.WriteInt(_gameActionManager.GetID(gameAction));
            streamWriter.WriteInt(ActorNumber);
            streamWriter.WriteFloat(Server.Time);
            gameAction.SerializeParameters(ref streamWriter, parameters);
            _clientDriver.EndSend(streamWriter);
            DataSent?.Invoke(streamWriter.Length);
        }

        public void SendRPC(NetAsset netAsset, string methodName, object[] args)
        {
            if (State == ClientState.Debug) return;
            if (State != ClientState.Connected)
                throw new InvalidOperationException($"Cannot send rpc {methodName}: not connected");

            DataStreamWriter streamWriter = _clientDriver.BeginSend(_reliablePipeline, _clientToServerConnection);
            streamWriter.WriteInt(Commands.NetAssetRPC);
            streamWriter.WriteFloat(Server.Time);
            streamWriter.WriteInt(netAsset.NetID);
            netAsset.SerializeRPC(ref streamWriter, methodName, args);
            _clientDriver.EndSend(streamWriter);
            DataSent?.Invoke(streamWriter.Length);
        }

        public void SendRPC(NetBehaviour netBehaviour, string methodName, object[] args)
        {
            if (State == ClientState.Debug) return;
            if (State != ClientState.Connected)
                throw new InvalidOperationException($"Cannot send rpc {methodName}: not connected");

            DataStreamWriter streamWriter = _clientDriver.BeginSend(_reliablePipeline, _clientToServerConnection);
            streamWriter.WriteInt(Commands.NetObjectRPC);
            streamWriter.WriteFloat(Server.Time);
            streamWriter.WriteInt(netBehaviour.NetObject.ID);
            streamWriter.WriteUShort(netBehaviour.NetBehaviourID);
            NetObjectManager.Instance.SerializeRPC(ref streamWriter, netBehaviour, methodName, args);
            _clientDriver.EndSend(streamWriter);
            DataSent?.Invoke(streamWriter.Length);
        }

        public void Disconnect()
        {
            if (State != ClientState.Connected && State != ClientState.Connecting)
            {
                Debug.LogWarning($"Cannot disconnect in client state {State}");
                return;
            }

            if (_clientDriver.IsCreated)
            {
                _clientDriver.Disconnect(_clientToServerConnection);
                Disconnected?.Invoke();
            }

            State = ClientState.Disconnected;
            _clientToServerConnection = default;
            Debug.Log("Disconnected");
        }

#if UNITY_EDITOR
        public void StartDebugMode(int actorNumber)
        {
            ActorNumber = actorNumber;
            State = ClientState.Debug;
        }
#endif

        public void Dispose()
        {
            if (_clientDriver.IsCreated) _clientDriver.Dispose();
            State = ClientState.Disconnected;
        }
    }
}