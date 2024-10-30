using System;
using System.Collections.Generic;
using MufflonUtil;
using Unity.Collections;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Error;
using Unity.Networking.Transport.Utilities;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;

namespace Netling
{
    public class Server : IDisposable
    {
        private static Server _instance;
        public static Server Instance => _instance ??= new Server();

        public const int ServerActorNumber = -1;
        private const int MaxBytesPerMessage = 1300; // 1400 causes errors on receiving side

        public static float Time =>
            IsActive ? UnityEngine.Time.time : Client.Instance.EstimateServerTime();

        public static bool IsActive { get; private set; }
        public ushort Port => _endPoint.Port;
        private bool _initialized;
        private NetworkDriver _serverDriver;
        private NativeList<NetworkConnection> _connections;
        private float _connectionTimeout;

        private readonly Dictionary<NetworkConnection, int> _actorNumberByConnection = new();
        private readonly Dictionary<int, NetworkConnection> _connectionByActorNumber = new();
        private readonly Dictionary<NetworkConnection, float> _lastPingTimes = new();
        private int _nextActorNumber; // todo make this ulong and name clientId

        private NetworkEndpoint _endPoint;
        private NetworkPipeline _unreliablePipeline;
        private NetworkPipeline _reliablePipeline;
        private bool _acceptAllActors;
        private bool _useSimulationPipeline;
        private ushort[] _ports;

        public delegate void ConnectionDelegate(int actorNumber);

        public event Action Started;
        public event Action Stopped;
        public event ConnectionDelegate ClientConnected;
        public event ConnectionDelegate ClientDisconnected;
        public event ConnectionDelegate ActorAccepted;

        public void Init(ushort[] ports, float connectionTimeout, bool acceptAllActors, bool useSimulationPipeline)
        {
            _connectionTimeout = connectionTimeout;
            _endPoint = NetworkEndpoint.AnyIpv4;
            _ports = ports;
            _acceptAllActors = acceptAllActors;
            _useSimulationPipeline = useSimulationPipeline;
            _initialized = true;
        }

        public void Start(bool quitOnFail = false)
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
            Start(serverDriver, quitOnFail);
        }

        public void Start(NetworkDriver serverDriver, bool quitOnFail = false)
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
                IsActive = true;
                Debug.Log($"Started server on port {_endPoint.Port}");
                Started?.Invoke();
                break;
            }

            if (!IsActive)
            {
                _connections.Dispose();
                _serverDriver.Dispose();
                _actorNumberByConnection.Clear();
                _connectionByActorNumber.Clear();
                _lastPingTimes.Clear();
                if (quitOnFail) Application.Quit(-1);
                throw new NetException("Failed to bind to any port");
            }
        }

        public static void AssertActive()
        {
            if (!IsActive) throw new InvalidOperationException("Assertion failed: Server not active");
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

            _serverDriver.ScheduleUpdate().Complete(); // send disconnection events
            _actorNumberByConnection.Clear();
            _connectionByActorNumber.Clear();
            if (_serverDriver.IsCreated) _serverDriver.Dispose();
            if (_connections.IsCreated) _connections.Dispose();
            IsActive = false;
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
                {
                    break;
                }

                int actorNumber = _nextActorNumber++;
                _connections.Add(connection);
                _actorNumberByConnection[connection] = actorNumber;
                _connectionByActorNumber[actorNumber] = connection;
                _lastPingTimes[connection] = Time;
                Debug.Log($"Client connected. Assigning actor number {actorNumber}.");
                _serverDriver.BeginSend(_reliablePipeline, connection, out DataStreamWriter writer);
                writer.WriteInt(Commands.AssignActorNumber);
                writer.WriteInt(actorNumber);
                _serverDriver.EndSend(writer);
                ClientConnected?.Invoke(actorNumber);
            }

            // process open connections
            for (var i = 0; i < _connections.Length; ++i)
            {
                // check for timeout
                NetworkConnection connection = _connections[i];
                int actorNumber = _actorNumberByConnection[connection];
                if (_connectionTimeout > 0 && Time - _lastPingTimes[connection] > _connectionTimeout)
                {
                    connection.Disconnect(_serverDriver);
                    Debug.LogWarning($"Disconnecting client {actorNumber} due to timeout");
                    ClientDisconnected?.Invoke(actorNumber);
                    _connections.RemoveAtSwapBack(i);
                    _actorNumberByConnection.Remove(connection);
                    _connectionByActorNumber.Remove(actorNumber);
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
                        Debug.Log($"Client {actorNumber} disconnected");
                        ClientDisconnected?.Invoke(actorNumber);
                        _connections.RemoveAtSwapBack(i);
                        _actorNumberByConnection.Remove(connection);
                        _connectionByActorNumber.Remove(actorNumber);
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
                int senderActorNumber = _actorNumberByConnection[connection];
                int command = streamReader.ReadInt();
                switch (command)
                {
                    case Commands.AcknowledgeActorNumber:
                    {
                        if (_acceptAllActors) AcceptPlayer(senderActorNumber);
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
                                || Client.Instance.IsHost && Client.Instance.ActorNumber == senderActorNumber)
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
            if (!IsActive)
                throw new InvalidOperationException("Cannot accept player: Server not running");

            NetworkConnection connection = _connectionByActorNumber[actorNumber];
            Debug.Log($"Accepting player of client {actorNumber}");
            var connections = new NativeList<NetworkConnection>(1, Allocator.Temp) { connection };
            _serverDriver.BeginSend(_reliablePipeline, connection, out DataStreamWriter writer);
            writer.WriteInt(Commands.AcceptActor);
            _serverDriver.EndSend(writer);
            SendNetAssetUpdate(true, connections);
            connections.Dispose();
            ActorAccepted?.Invoke(actorNumber);
        }

        public void Kick(int actorNumber)
        {
            if (!IsActive)
                throw new InvalidOperationException("Cannot kick player: Server not running");

            if (_connectionByActorNumber.TryGetValue(actorNumber, out NetworkConnection connection))
            {
                Debug.Log($"Kicking client {actorNumber}");
                ClientDisconnected?.Invoke(actorNumber);
                connection.Disconnect(_serverDriver);
                _connections.RemoveAtSwapBack(_connections.IndexOf(connection));
                _connectionByActorNumber.Remove(actorNumber);
                _actorNumberByConnection.Remove(connection);
            }
            else
            {
                Debug.LogWarning($"Tried to kick client {actorNumber}, but did not find respective connection.");
            }
        }

        public void KickAll()
        {
            if (!IsActive)
                throw new InvalidOperationException("Cannot kick player: Server not running");

            foreach (NetworkConnection connection in _connections)
            {
                connection.Disconnect(_serverDriver);
                ClientDisconnected?.Invoke(_actorNumberByConnection[connection]);
            }

            _connections.Clear();
            _actorNumberByConnection.Clear();
            _connectionByActorNumber.Clear();
            _lastPingTimes.Clear();
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
                        objectWriter.WriteVector3(netObject.transform.localPosition);
                        objectWriter.WriteQuaternion(netObject.transform.localRotation);
                        objectWriter.WriteInt(netObject.gameObject.scene.buildIndex);
                        Transform parent = netObject.transform.parent;
                        objectWriter.WriteManagedString(parent == null ? "/" : parent.GetFullPath());
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

        /// <summary>
        /// Spawn a new networked object with a specific network behaviour component on the server.
        /// Sends a spawn message to all clients.
        /// Returns the component of the newly created network object.
        /// </summary>
        /// <param name="netBehaviourPrefab">The prefab to instantiate</param>
        /// <param name="scene">The scene where the object will be moved to. Ignored if parent is set.</param>
        /// <param name="parent">The parent transform this object will be a child of. Null for root objects.</param>
        /// <param name="position">The global position where to instantiate the object.</param>
        /// <param name="rotation">The global rotation with which to instantiate the object.</param>
        /// <param name="actorNumber">The actor number of the owner of this object.</param>
        /// <typeparam name="T">The type of the net behaviour component of the new object that is returned.</typeparam>
        /// <returns>The net behaviour component of the newly instantiated network object.</returns>
        /// <exception cref="InvalidOperationException">Thrown if server not running.</exception>
        public T SpawnNetObject<T>(T netBehaviourPrefab, Scene scene, Transform parent, Vector3 position,
            Quaternion rotation, int actorNumber = ServerActorNumber) where T : NetBehaviour
        {
            return SpawnNetObject(netBehaviourPrefab.NetObject, scene, parent, position, rotation, actorNumber)
                .GetComponent<T>();
        }

        /// <summary>
        /// Spawn a new networked object on the server.
        /// Sends a spawn message to all clients.
        /// Returns the newly created network object.
        /// </summary>
        /// <param name="netObjectPrefab"></param>
        /// <param name="scene">The scene where the object will be moved to. Ignored if parent is set.</param>
        /// <param name="parent">The parent transform this object will be a child of. Null for root objects.</param>
        /// <param name="position">The global position where to instantiate the object.</param>
        /// <param name="rotation">The global rotation with which to instantiate the object.</param>
        /// <param name="actorNumber">The actor number of the owner of this object.</param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException">Thrown if server not running.</exception>
        public NetObject SpawnNetObject(NetObject netObjectPrefab, Scene scene, Transform parent, Vector3 position,
            Quaternion rotation, int actorNumber = ServerActorNumber)
        {
            if (!IsActive)
                throw new InvalidOperationException("Cannot spawn NetObject: Server not running");

            NetObject netObject =
                NetObjectManager.Instance.SpawnOnServer(netObjectPrefab, position, rotation, scene, parent,
                    actorNumber);
            SendSpawnMessage(new[] { netObject }, _connections);
            return netObject;
        }

        public void SendNetAssetUpdate(bool fullLoad)
        {
            SendNetAssetUpdate(fullLoad, _connections);
        }

        private void SendNetAssetUpdate(bool fullLoad, NativeList<NetworkConnection> connections)
        {
            if (!IsActive)
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
            if (!IsActive)
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
            if (!IsActive)
                throw new InvalidOperationException("Cannot unspawn NetObjects: Server not running");
            foreach (NetObject netObject in netObjects)
            {
                NetObjectManager.Instance.Unspawn(netObject.ID);
            }

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
            if (!IsActive)
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
            if (!IsActive)
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
            if (!IsActive)
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
            if (!IsActive)
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