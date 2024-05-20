using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Error;
using Unity.Networking.Transport.Utilities;
using UnityEngine;
using UnityEngine.Profiling;

namespace Netling
{
    public class Server : IDisposable
    {
        private enum ServerState
        {
            Stopped,
            Started,
            Debug
        }

        public const int ServerActorNumber = -1;
        private const int MaxBytesPerMessage = 1300; // 1400 causes errors on receiving side

        private static Server _instance;
        public static Server Instance => _instance = _instance ?? new Server();
        private ServerState State { get; set; }

        public static float Time =>
            IsActive ? UnityEngine.Time.time : Client.Instance.EstimateServerTime();

        public static bool IsActive => Instance.State == ServerState.Started || Instance.State == ServerState.Debug;
        public ushort Port => _endPoint.Port;
        private bool _initialized;
        private NetworkDriver _serverDriver;
        private NativeList<NetworkConnection> _connections;
        private float _clientConnectionTimeout;

        private readonly Dictionary<NetworkConnection, float> _lastPingTimes =
            new Dictionary<NetworkConnection, float>();

        private NetworkEndPoint _endPoint;
        private NetworkPipeline _unreliablePipeline;
        private NetworkPipeline _reliablePipeline;
        private bool _acceptAllPlayers;
        private bool _useSimulationPipeline;
        private ushort[] _ports;

        public delegate void ConnectionDelegate(int actorNumber);

        public delegate void PlayerDataDelegate(int actorNumber, string playerData);

        public event Action Stopped;
        public event ConnectionDelegate ClientConnected;
        public event ConnectionDelegate ClientDisconnected;
        public event ConnectionDelegate PlayerAccepted;
        public event PlayerDataDelegate PlayerDataReceived;

        public void Init(ushort[] ports, float clientConnectionTimeout, bool acceptAllPlayers,
            bool useSimulationPipeline)
        {
            _clientConnectionTimeout = clientConnectionTimeout;
            _endPoint = NetworkEndPoint.AnyIpv4;
            _ports = ports;
            _acceptAllPlayers = acceptAllPlayers;
            _useSimulationPipeline = useSimulationPipeline;
            _initialized = true;
        }

        public void Start()
        {
            var networkSettings = new NetworkSettings();
            var simulationParameters = new SimulatorUtility.Parameters
            {
                MaxPacketCount = 30,
                PacketDropPercentage = 5,
                MaxPacketSize = 256,
                PacketDelayMs = 50
            };
            networkSettings.AddRawParameterStruct(ref simulationParameters);
            var serverDriver = NetworkDriver.Create(networkSettings);
            Start(serverDriver);
        }

        public void Start(NetworkDriver serverDriver)
        {
            if (IsActive)
            {
                serverDriver.Dispose();
                throw new InvalidOperationException("Cannot start server: is already active");
            }

            if (!_initialized)
            {
                serverDriver.Dispose();
                throw new InvalidOperationException("Cannot start server: not initialized. You must call Init() first");
            }

            if (_serverDriver.IsCreated) _serverDriver.Dispose();
            _serverDriver = serverDriver;
            _unreliablePipeline = _useSimulationPipeline
                ? _serverDriver.CreatePipeline(typeof(SimulatorPipelineStage))
                : NetworkPipeline.Null;
            _reliablePipeline = _useSimulationPipeline
                ? _serverDriver.CreatePipeline(typeof(ReliableSequencedPipelineStage), typeof(SimulatorPipelineStage))
                : _serverDriver.CreatePipeline(typeof(ReliableSequencedPipelineStage));
            _connections = new NativeList<NetworkConnection>(16, Allocator.Persistent);

            foreach (ushort port in _ports)
            {
                _endPoint.Port = port;
                if (_serverDriver.Bind(_endPoint) != 0) continue;
                _serverDriver.Listen();
                State = ServerState.Started;
                Debug.Log($"Started server on port {_endPoint.Port}");
                break;
            }

            if (State != ServerState.Started)
            {
                _connections.Dispose();
                _serverDriver.Dispose();
                Application.Quit(-1);
                throw new NetException("Failed to bind to any port");
            }
        }

        public static void AssertActive()
        {
            if (!IsActive) throw new InvalidOperationException("Assertion failed: Server not active");
        }

        public void StartDebugMode()
        {
            State = ServerState.Debug;
        }

        public void Stop()
        {
            AssertActive();
            Debug.Log("Stopping server...");
            UnspawnNetObjects(NetObjectManager.Instance.NetObjects);
            foreach (NetworkConnection networkConnection in _connections)
            {
                networkConnection.Disconnect(_serverDriver);
            }

            if (_serverDriver.IsCreated) _serverDriver.Dispose();
            if (_connections.IsCreated) _connections.Dispose();
            State = ServerState.Stopped;
            Stopped?.Invoke();
        }

        public void Tick()
        {
            if (!_serverDriver.IsCreated) return;
            _serverDriver.ScheduleUpdate().Complete();

            // Accept all new connections
            while (true)
            {
                NetworkConnection connection = _serverDriver.Accept();
                if (!connection.IsCreated)
                    break;

                _connections.Add(connection);
                _lastPingTimes[connection] = Time;
                Debug.Log($"Client connected with internal Id {connection.InternalId}. Assigning actor number.");
                _serverDriver.BeginSend(_reliablePipeline, connection, out DataStreamWriter writer);
                writer.WriteInt(Commands.AssignActorNumber);
                writer.WriteInt(connection.InternalId);
                _serverDriver.EndSend(writer);
                ClientConnected?.Invoke(connection.InternalId);
            }

            // process open connections
            for (var i = 0; i < _connections.Length; ++i)
            {
                // check for timeout
                NetworkConnection connection = _connections[i];
                if (_clientConnectionTimeout > 0 && Time - _lastPingTimes[connection] > _clientConnectionTimeout)
                {
                    connection.Disconnect(_serverDriver);
                    Debug.LogWarning($"Disconnecting client {connection.InternalId} due to timeout");
                    ClientDisconnected?.Invoke(connection.InternalId);
                    _connections.RemoveAtSwapBack(i);
                    continue;
                }

                // pop events
                NetworkEvent.Type eventType;
                while ((eventType = _serverDriver.PopEventForConnection(
                           connection, out DataStreamReader streamReader)) != NetworkEvent.Type.Empty)
                {
                    if (eventType == NetworkEvent.Type.Data)
                    {
                        ReadDataEvent(connection, streamReader);
                    }
                    else if (eventType == NetworkEvent.Type.Disconnect)
                    {
                        Debug.Log($"Client {connection.InternalId} disconnected");
                        ClientDisconnected?.Invoke(connection.InternalId);
                        _connections.RemoveAtSwapBack(i);
                        if (i >= _connections.Length)
                            break;
                    }
                }
            }
        }

        private void ReadDataEvent(NetworkConnection connection, DataStreamReader streamReader)
        {
            try
            {
                int senderActorNumber = connection.InternalId;
                int command = streamReader.ReadInt();
                switch (command)
                {
                    case Commands.AcknowledgeActorNumber:
                    {
                        if (_acceptAllPlayers) AcceptPlayer(senderActorNumber);
                        break;
                    }
                    case Commands.PlayerData:
                    {
                        string playerData = streamReader.ReadManagedString();
                        PlayerDataReceived?.Invoke(senderActorNumber, playerData);
                        break;
                    }
                    case Commands.Ping:
                        _lastPingTimes[connection] = Time;
                        float sendLocalTime = streamReader.ReadFloat();
                        _serverDriver.BeginSend(_unreliablePipeline, connection, out DataStreamWriter writer);
                        writer.WriteInt(Commands.Ping);
                        writer.WriteFloat(sendLocalTime);
                        writer.WriteFloat(Time);
                        writer.WriteFloat(UnityEngine.Time.deltaTime);
                        _serverDriver.EndSend(writer);
                        break;
                    case Commands.RequestSpawnMessage:
                    {
                        var connections = new NativeList<NetworkConnection>(1, Allocator.Temp) { connection };
                        SendSpawnMessage(NetObjectManager.Instance.NetObjects, connections);
                        connections.Dispose();
                        break;
                    }
                    case Commands.UpdateNetObjects:
                    {
                        int objectsInMessage = streamReader.ReadInt();
                        for (var obj = 0; obj < objectsInMessage; obj++)
                        {
                            int netObjID = streamReader.ReadInt();
                            int size = streamReader.ReadInt();
                            NetObject netObject = NetObjectManager.Instance.Exists(netObjID)
                                ? NetObjectManager.Instance.Get(netObjID)
                                : null;

                            // ignore illegal updates and those from local host client
                            if (netObject == null
                                || netObject.OwnerActorNumber != senderActorNumber // cheater?
                                || Client.IsHost && Client.Instance.ActorNumber == senderActorNumber)
                            {
                                streamReader.DiscardBytes(size);
                            }
                            else
                            {
                                int bytesRead = streamReader.GetBytesRead();
                                try
                                {
                                    netObject.Deserialize(ref streamReader, b => b.ClientAuthoritative);
                                }
                                catch (Exception e)
                                {
                                    Debug.LogException(e);
                                    int remainingBytes = size + bytesRead - streamReader.GetBytesRead();
                                    streamReader.DiscardBytes(remainingBytes);
                                }
                            }
                        }

                        break;
                    }
                    case Commands.GameAction:
                    {
                        int gameActionID = streamReader.ReadInt();
                        int actorNumber = streamReader.ReadInt();
                        float triggerTime = streamReader.ReadFloat();
                        try
                        {
                            GameAction gameAction = GameActionManager.Instance.Get(gameActionID);
                            GameAction.IParameters parameters =
                                gameAction.DeserializeParameters(ref streamReader);
                            gameAction.ReceiveOnServer(parameters, actorNumber, senderActorNumber,
                                triggerTime);
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
                        float sentServerTime = streamReader.ReadFloat();
                        int netAssetID = streamReader.ReadInt();
                        NetAsset netAsset = NetAssetManager.Instance.Get(netAssetID);
                        NetAsset.RPC rpc = netAsset.DeserializeRPC(ref streamReader);
                        var messageInfo = new MessageInfo
                            { SentServerTime = sentServerTime, SenderActorNumber = senderActorNumber };
                        rpc.Invoke(messageInfo);
                        break;
                    }
                    case Commands.NetObjectRPC:
                    {
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
                            { SentServerTime = sentServerTime, SenderActorNumber = senderActorNumber };
                        rpc.Invoke(messageInfo);
                        break;
                    }
                    default:
                        Debug.LogException(new NetException($"Unknown command {command}"));
                        break;
                }
            }
            catch (Exception e)
            {
                Debug.LogException(new NetException("Failed to handle data event", e));
            }
        }

        public void AcceptPlayer(int actorNumber)
        {
            if (State == ServerState.Debug) return;
            if (State != ServerState.Started)
                throw new InvalidOperationException("Cannot accept player: Server not running");

            foreach (NetworkConnection connection in _connections)
            {
                if (connection.InternalId != actorNumber) continue;
                Debug.Log($"Accepting player of client {actorNumber}");
                var connections = new NativeList<NetworkConnection>(1, Allocator.Temp) { connection };
                _serverDriver.BeginSend(_reliablePipeline, connection, out DataStreamWriter writer);
                writer.WriteInt(Commands.AcceptPlayer);
                _serverDriver.EndSend(writer);
                SendNetAssetUpdate(true, connections);
                connections.Dispose();
                PlayerAccepted?.Invoke(actorNumber);
                return;
            }

            Debug.LogWarning($"Cannot accept player of client {actorNumber}: No connection found");
        }

        public void Kick(int actorNumber)
        {
            if (State == ServerState.Debug) return;
            if (State != ServerState.Started)
                throw new InvalidOperationException("Cannot kick player: Server not running");

            for (var i = 0; i < _connections.Length; i++)
            {
                NetworkConnection connection = _connections[i];
                if (connection.InternalId != actorNumber) continue;
                Debug.Log($"Kicking client {actorNumber}");
                ClientDisconnected?.Invoke(actorNumber);
                connection.Disconnect(_serverDriver);
                _connections.RemoveAtSwapBack(i);
                return;
            }

            Debug.LogWarning($"Tried to kick client {actorNumber}, but did not find respective connection.");
        }

        public void KickAll()
        {
            if (State == ServerState.Debug) return;
            if (State != ServerState.Started)
                throw new InvalidOperationException("Cannot kick player: Server not running");

            foreach (NetworkConnection connection in _connections)
            {
                connection.Disconnect(_serverDriver);
                ClientDisconnected?.Invoke(connection.InternalId);
            }

            _connections.Clear();
        }

        private void SendSpawnMessage(NetObject[] netObjects, NativeList<NetworkConnection> connections)
        {
            if (connections.Length == 0) return;
            AssertActive();
            const int headerSizeInBytes = 8;
            var streamWriter = new DataStreamWriter(MaxBytesPerMessage, Allocator.Temp);
            var objectWriter = new DataStreamWriter(MaxBytesPerMessage - headerSizeInBytes, Allocator.Temp);
            var objectIndex = 0;

            // compose new message if objects left to send or copy to message stream
            while (objectIndex < netObjects.Length || objectWriter.Length > 0)
            {
                streamWriter.Clear();

                // write header
                streamWriter.WriteInt(Commands.SpawnNetObjects);
                DataStreamWriter objectCountWriter = streamWriter;
                streamWriter.WriteInt(0);

                // copy data over to message stream and write to object stream
                var objectsInMessage = 0;
                while (streamWriter.Length + objectWriter.Length <= MaxBytesPerMessage)
                {
                    if (objectWriter.Length > 0)
                    {
                        streamWriter.WriteBytes(objectWriter.AsNativeArray());
                        objectWriter.Clear();
                        objectsInMessage++;
                    }

                    if (objectIndex < netObjects.Length)
                    {
                        NetObject netObject = netObjects[objectIndex++];
                        objectWriter.WriteInt(netObject.ID);
                        objectWriter.WriteUShort(netObject.PrefabIndex);
                        objectWriter.WriteInt(netObject.OwnerActorNumber);
                        objectWriter.WriteVector3(netObject.transform.position);
                        objectWriter.WriteQuaternion(netObject.transform.rotation);
                        objectWriter.WriteInt(netObject.gameObject.scene.buildIndex);
                        DataStreamWriter objectSizeWriter = objectWriter;
                        objectWriter.WriteInt(0);
                        int length = objectWriter.Length;
                        netObject.Serialize(ref objectWriter, true);
                        objectSizeWriter.WriteInt(objectWriter.Length - length);
                    }
                    else break;
                }

                objectCountWriter.WriteInt(objectsInMessage);

                // message complete. Send if payload present
                if (objectsInMessage == 0) return;
                SendBytes(streamWriter.AsNativeArray(), _reliablePipeline, connections);
            }
        }

        public T SpawnNetObject<T>(T netBehaviourPrefab, Vector3 position, Quaternion rotation,
            string sceneName = null, int actorNumber = ServerActorNumber)
            where T : NetBehaviour
        {
            return SpawnNetObject(netBehaviourPrefab.NetObject, position, rotation, sceneName, actorNumber)
                .GetComponent<T>();
        }

        public NetObject SpawnNetObject(NetObject netObjectPrefab, Vector3 position, Quaternion rotation,
            string sceneName = null, int actorNumber = ServerActorNumber)
        {
            if (State != ServerState.Started && State != ServerState.Debug)
                throw new InvalidOperationException("Cannot spawn NetObject: Server not running");

            NetObject netObject =
                NetObjectManager.Instance.SpawnOnServer(netObjectPrefab, position, rotation, sceneName, actorNumber);
            if (State == ServerState.Started) SendSpawnMessage(new[] { netObject }, _connections);
            return netObject;
        }

        public void SendNetAssetUpdate(bool fullLoad)
        {
            SendNetAssetUpdate(fullLoad, _connections);
        }

        private void SendNetAssetUpdate(bool fullLoad, NativeList<NetworkConnection> connections)
        {
            if (State == ServerState.Debug) return;
            if (State != ServerState.Started)
                throw new InvalidOperationException("Cannot send NetAsset update: Server not running");
            Profiler.BeginSample("NetAsset Update");

            NetAsset[] netAssets = NetAssetManager.Instance.GetAll();
            const int headerSizeInBytes = 8;
            var streamWriter = new DataStreamWriter(MaxBytesPerMessage, Allocator.Temp);
            var assetWriter = new DataStreamWriter(MaxBytesPerMessage - headerSizeInBytes, Allocator.Temp);
            var assetIndex = 0;

            // compose new message if assets left to send or serialize
            while (assetIndex < netAssets.Length || assetWriter.Length > 0)
            {
                streamWriter.Clear();

                // write header
                streamWriter.WriteInt(Commands.UpdateNetAssets);
                DataStreamWriter netAssetCountWriter = streamWriter;
                streamWriter.WriteInt(0);

                // add assets as long as they fit
                var assetsInMessage = 0;
                while (streamWriter.Length + assetWriter.Length <= MaxBytesPerMessage)
                {
                    if (assetWriter.Length > 0)
                    {
                        streamWriter.WriteBytes(assetWriter.AsNativeArray());
                        assetWriter.Clear();
                        assetsInMessage++;
                    }

                    // next asset. Serialize if dirty
                    if (assetIndex < netAssets.Length)
                    {
                        NetAsset netAsset = netAssets[assetIndex++];
                        if (fullLoad || netAsset.IsDirty()) SerializeNetAsset(netAsset, ref assetWriter, fullLoad);
                    }
                    else break;
                }

                netAssetCountWriter.WriteInt(assetsInMessage);

                // message complete. Send if payload exists
                if (assetsInMessage == 0) break;
                SendBytes(streamWriter.AsNativeArray(), _reliablePipeline, connections);
            }

            Profiler.EndSample();
        }

        private static void SerializeNetAsset(NetAsset netAsset, ref DataStreamWriter streamWriter, bool fullLoad)
        {
            streamWriter.WriteInt(netAsset.NetID);
            DataStreamWriter sizeWriter = streamWriter;
            streamWriter.WriteInt(0);
            int length = streamWriter.Length;
            netAsset.Serialize(ref streamWriter, fullLoad);
            sizeWriter.WriteInt(streamWriter.Length - length);
        }

        public void SendNetObjectsUpdate()
        {
            if (State == ServerState.Debug) return;
            if (State != ServerState.Started)
                throw new InvalidOperationException("Cannot set NetObject update: Server not running");

            Profiler.BeginSample("NetObject Update");
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

                    // next object. Write if dirty
                    if (objectIndex < netObjects.Length)
                    {
                        NetObject netObject = netObjects[objectIndex++];
                        if (netObject.IsDirty) WriteNetObject(netObject, ref objectWriter);
                    }
                    else break;
                }

                objectCountWriter.WriteInt(objectsInMessage);

                // message complete. Send if payload exists
                if (objectsInMessage == 0) break;
                SendBytes(streamWriter.AsNativeArray(), _reliablePipeline, _connections);
            }

            Profiler.EndSample();
        }

        private static void WriteNetObject(NetObject netObject, ref DataStreamWriter streamWriter)
        {
            streamWriter.WriteInt(netObject.ID);
            DataStreamWriter sizeWriter = streamWriter;
            streamWriter.WriteInt(0);
            int length = streamWriter.Length;
            netObject.Serialize(ref streamWriter, false);
            sizeWriter.WriteInt(streamWriter.Length - length);
        }

        public void UnspawnNetObject(NetObject netObject)
        {
            UnspawnNetObjects(new[] { netObject });
        }

        public void UnspawnNetObjects(NetObject[] netObjects)
        {
            if (State != ServerState.Started && State != ServerState.Debug)
                throw new InvalidOperationException("Cannot unspawn NetObjects: Server not running");
            foreach (NetObject netObject in netObjects)
            {
                NetObjectManager.Instance.Unspawn(netObject.ID);
            }

            if (State == ServerState.Debug) return;
            int size = 8 // header
                       + netObjects.Length * 4; // payload
            var streamWriter = new DataStreamWriter(size, Allocator.Temp);
            {
                streamWriter.WriteInt(Commands.UnspawnNetObjects);
                streamWriter.WriteInt(netObjects.Length);
                foreach (NetObject netObject in netObjects)
                {
                    streamWriter.WriteInt(netObject.ID);
                }

                SendBytes(streamWriter.AsNativeArray(), _reliablePipeline, _connections);
            }
        }

        public void SendGameAction(GameAction gameAction, GameAction.IParameters parameters, int actorNumber,
            float triggerTime)
        {
            if (State == ServerState.Debug) return;
            if (State != ServerState.Started)
                throw new InvalidOperationException($"Cannot send {gameAction}: Server not running");

            var streamWriter = new DataStreamWriter(MaxBytesPerMessage, Allocator.Temp);
            streamWriter.WriteInt(Commands.GameAction);
            streamWriter.WriteInt(GameActionManager.Instance.GetID(gameAction));
            streamWriter.WriteInt(actorNumber);
            streamWriter.WriteFloat(triggerTime);
            streamWriter.WriteBool(true); // valid
            gameAction.SerializeParameters(ref streamWriter, parameters);

            SendBytes(streamWriter.AsNativeArray(), _reliablePipeline, _connections);
        }

        public void DenyGameAction(GameAction gameAction, GameAction.IParameters parameters, int actorNumber,
            float triggerTime)
        {
            if (State == ServerState.Debug) return;
            if (State != ServerState.Started)
                throw new InvalidOperationException($"Cannot deny {gameAction}: Server not running");

            var streamWriter = new DataStreamWriter(MaxBytesPerMessage, Allocator.Temp);
            streamWriter.WriteInt(Commands.GameAction);
            streamWriter.WriteInt(GameActionManager.Instance.GetID(gameAction));
            streamWriter.WriteInt(actorNumber);
            streamWriter.WriteFloat(triggerTime);
            streamWriter.WriteBool(false); // invalid
            gameAction.SerializeParameters(ref streamWriter, parameters);

            SendBytes(streamWriter.AsNativeArray(), _reliablePipeline, _connections);
        }

        public void SendRPC(NetAsset netAsset, string methodName, object[] args)
        {
            if (State == ServerState.Debug) return;
            if (State != ServerState.Started)
                throw new InvalidOperationException($"Cannot send rpc {methodName}: Server not running");

            var streamWriter = new DataStreamWriter(MaxBytesPerMessage, Allocator.Temp);
            streamWriter.WriteInt(Commands.NetAssetRPC);
            streamWriter.WriteFloat(Time);
            streamWriter.WriteInt(netAsset.NetID);
            netAsset.SerializeRPC(ref streamWriter, methodName, args);

            SendBytes(streamWriter.AsNativeArray(), _reliablePipeline, _connections);
        }

        public void SendRPC(NetBehaviour netBehaviour, string methodName, object[] args)
        {
            if (State == ServerState.Debug) return;
            if (State != ServerState.Started)
                throw new InvalidOperationException($"Cannot send rpc {methodName}: Server not running");

            var streamWriter = new DataStreamWriter(MaxBytesPerMessage, Allocator.Temp);
            {
                streamWriter.WriteInt(Commands.NetObjectRPC);
                streamWriter.WriteFloat(Time);
                streamWriter.WriteInt(netBehaviour.NetObject.ID);
                streamWriter.WriteUShort(netBehaviour.NetBehaviourID);
                NetObjectManager.Instance.SerializeRPC(ref streamWriter, netBehaviour, methodName, args);

                SendBytes(streamWriter.AsNativeArray(), _reliablePipeline, _connections);
            }
        }

        private void SendBytes(NativeArray<byte> bytes, NetworkPipeline pipeline,
            NativeList<NetworkConnection> connections)
        {
            foreach (NetworkConnection connection in connections)
            {
                var result = (StatusCode)_serverDriver.BeginSend(pipeline, connection, out DataStreamWriter writer);
                if (result == StatusCode.Success)
                {
                    writer.WriteBytes(bytes);
                    _serverDriver.EndSend(writer);
                }
            }
        }

        private void ReleaseUnmanagedResources()
        {
            if (_serverDriver.IsCreated) _serverDriver.Dispose();
            if (_connections.IsCreated) _connections.Dispose();
        }

        public void Dispose()
        {
            ReleaseUnmanagedResources();
            GC.SuppressFinalize(this);
            _initialized = false;
            _instance = null;
        }

        ~Server()
        {
            ReleaseUnmanagedResources();
        }
    }
}