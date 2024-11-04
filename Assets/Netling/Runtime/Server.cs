using System;
using System.Collections.Generic;
using System.Linq;
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

        public static ClientID ServerClientID => ClientID.Server;
        private const int MaxBytesPerMessage = 1300; // 1400 causes errors on receiving side

        public static float Time =>
            IsActive ? UnityEngine.Time.time : Client.Instance.EstimateServerTime();

        public static bool IsActive { get; private set; }
        public ushort Port => _endPoint.Port;
        private bool _initialized;
        private NetworkDriver _serverDriver;
        private NativeList<NetworkConnection> _connections;
        private float _connectionTimeout;

        private readonly Dictionary<NetworkConnection, ClientID> _clientIDByConnection = new();
        private readonly Dictionary<ClientID, NetworkConnection> _connectionByClientID = new();
        private readonly HashSet<ClientID> _acceptedClients = new();
        private readonly Dictionary<NetworkConnection, float> _lastPingTimes = new();
        private ClientID _nextClientID;
        public ClientID[] AcceptedClients => _acceptedClients.ToArray();

        private NetworkEndpoint _endPoint;
        private NetworkPipeline _unreliablePipeline;
        private NetworkPipeline _reliablePipeline;
        private bool _acceptAllClients;
        private bool _useSimulationPipeline;
        private ushort[] _ports;

        public delegate void ConnectionDelegate(ClientID clientID);

        public event Action Started;
        public event Action Stopped;
        public event ConnectionDelegate ClientConnected;
        public event ConnectionDelegate ClientDisconnected;
        public event ConnectionDelegate ClientAccepted;

        public void Init(ushort[] ports, float connectionTimeout, bool acceptAllClients, bool useSimulationPipeline)
        {
            _connectionTimeout = connectionTimeout;
            _endPoint = NetworkEndpoint.AnyIpv4;
            _ports = ports;
            _acceptAllClients = acceptAllClients;
            _useSimulationPipeline = useSimulationPipeline;
            _initialized = true;
            _nextClientID = ClientID.Create(2);
            NetObjectManager.Instance.Init();
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
                _clientIDByConnection.Clear();
                _connectionByClientID.Clear();
                _acceptedClients.Clear();
                _nextClientID = ClientID.Create(2);
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
            _clientIDByConnection.Clear();
            _connectionByClientID.Clear();
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

                ClientID clientID = _nextClientID++;
                _connections.Add(connection);
                _clientIDByConnection[connection] = clientID;
                _connectionByClientID[clientID] = connection;
                _lastPingTimes[connection] = Time;
                Debug.Log($"Client connected. Assigning ID {clientID}.");
                _serverDriver.BeginSend(_reliablePipeline, connection, out DataStreamWriter writer);
                writer.WriteInt(Commands.AssignClientID);
                writer.WriteInt(clientID.Value);
                _serverDriver.EndSend(writer);
                ClientConnected?.Invoke(clientID);
            }

            // process open connections
            for (var i = 0; i < _connections.Length; ++i)
            {
                // check for timeout
                NetworkConnection connection = _connections[i];
                ClientID clientID = _clientIDByConnection[connection];
                if (_connectionTimeout > 0 && Time - _lastPingTimes[connection] > _connectionTimeout)
                {
                    connection.Disconnect(_serverDriver);
                    Debug.LogWarning($"Disconnecting client {clientID} due to timeout");
                    _connections.RemoveAtSwapBack(i);
                    _clientIDByConnection.Remove(connection);
                    _connectionByClientID.Remove(clientID);
                    _acceptedClients.Remove(clientID);
                    ClientDisconnected?.Invoke(clientID);
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
                        Debug.Log($"Client {clientID} disconnected");
                        _connections.RemoveAtSwapBack(i);
                        _clientIDByConnection.Remove(connection);
                        _connectionByClientID.Remove(clientID);
                        _acceptedClients.Remove(clientID);
                        ClientDisconnected?.Invoke(clientID);
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
                ClientID senderClientID = _clientIDByConnection[connection];
                int command = streamReader.ReadInt();
                switch (command)
                {
                    case Commands.AcknowledgeClientID:
                    {
                        if (_acceptAllClients) AcceptClient(senderClientID);
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
                            var netObjID = new NetObjectID(streamReader.ReadInt());
                            int size = streamReader.ReadInt();
                            NetObject netObject = NetObjectManager.Instance.Exists(netObjID)
                                ? NetObjectManager.Instance.Get(netObjID)
                                : null;

                            // ignore illegal updates and those from local host client
                            if (netObject == null
                                || netObject.OwnerClientID != senderClientID // cheater?
                                || Client.Instance.IsHost && Client.Instance.ID == senderClientID)
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
                        var clientID = ClientID.Create(streamReader.ReadInt());
                        float triggerTime = streamReader.ReadFloat();
                        try
                        {
                            GameAction gameAction = GameActionManager.Instance.Get(gameActionID);
                            GameAction.IParameters parameters =
                                gameAction.DeserializeParameters(ref streamReader);
                            gameAction.ReceiveOnServer(parameters, clientID, senderClientID,
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
                            { SentServerTime = sentServerTime, SenderClientID = senderClientID };
                        rpc.Invoke(messageInfo);
                        break;
                    }
                    case Commands.NetObjectRPC:
                    {
                        float sentServerTime = streamReader.ReadFloat();
                        var netObjectID = new NetObjectID(streamReader.ReadInt());
                        if (!NetObjectManager.Instance.Exists(netObjectID))
                        {
                            Debug.LogWarning("Ignoring received RPC, because NetObject was not found.");
                            break;
                        }

                        NetObject netObject = NetObjectManager.Instance.Get(netObjectID);
                        ushort netBehaviourIndex = streamReader.ReadUShort();
                        NetBehaviour netBehaviour = netObject.Get(netBehaviourIndex);
                        NetObjectManager.RPC rpc =
                            NetObjectManager.Instance.DeserializeRPC(ref streamReader, netBehaviour);
                        var messageInfo = new MessageInfo
                            { SentServerTime = sentServerTime, SenderClientID = senderClientID };
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

        public void AcceptClient(ClientID clientID)
        {
            if (!IsActive)
                throw new InvalidOperationException("Cannot accept client: Server not running");

            NetworkConnection connection = _connectionByClientID[clientID];
            Debug.Log($"Accepting client with ID {clientID}");
            var connections = new NativeList<NetworkConnection>(1, Allocator.Temp) { connection };
            _serverDriver.BeginSend(_reliablePipeline, connection, out DataStreamWriter writer);
            writer.WriteInt(Commands.AcceptClient);
            _serverDriver.EndSend(writer);
            SendNetAssetUpdate(true, connections);
            connections.Dispose();
            _acceptedClients.Add(clientID);
            ClientAccepted?.Invoke(clientID);
        }

        public void Kick(ClientID clientID)
        {
            if (!IsActive)
                throw new InvalidOperationException("Cannot kick client: Server not running");

            if (_connectionByClientID.TryGetValue(clientID, out NetworkConnection connection))
            {
                Debug.Log($"Kicking client {clientID}");
                connection.Disconnect(_serverDriver);
                _connections.RemoveAtSwapBack(_connections.IndexOf(connection));
                _connectionByClientID.Remove(clientID);
                _clientIDByConnection.Remove(connection);
                _acceptedClients.Remove(clientID);
                ClientDisconnected?.Invoke(clientID);
            }
            else
            {
                Debug.LogWarning($"Tried to kick client {clientID}, but did not find respective connection.");
            }
        }

        public void KickAll()
        {
            if (!IsActive)
                throw new InvalidOperationException("Cannot kick client: Server not running");

            ClientID[] clientIDs = _clientIDByConnection.Values.ToArray();
            foreach (NetworkConnection connection in _connections)
            {
                connection.Disconnect(_serverDriver);
            }

            _connections.Clear();
            _clientIDByConnection.Clear();
            _connectionByClientID.Clear();
            _acceptedClients.Clear();
            _lastPingTimes.Clear();

            foreach (ClientID clientID in clientIDs)
            {
                ClientDisconnected?.Invoke(clientID);
            }
        }

        public void SendSpawnMessage(params NetObject[] netObjects) => SendSpawnMessage(netObjects, _connections);

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
                        objectWriter.WriteInt(netObject.ID.Value);
                        objectWriter.WriteUShort(netObject.PrefabIndex);
                        objectWriter.WriteInt(netObject.OwnerClientID.Value);
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
        /// Spawn a new networked object with a specific network behaviour component on the server, owned by the server.
        /// Sends a spawn message to all clients.
        /// Returns the component of the newly created network object.
        /// </summary>
        /// <param name="netBehaviourPrefab">The prefab to instantiate</param>
        /// <param name="scene">The scene where the object will be moved to. Ignored if parent is set.</param>
        /// <param name="parent">The parent transform this object will be a child of. Null for root objects.</param>
        /// <param name="position">The global position where to instantiate the object.</param>
        /// <param name="rotation">The global rotation with which to instantiate the object.</param>
        /// <typeparam name="T">The type of the net behaviour component of the new object that is returned.</typeparam>
        /// <returns>The net behaviour component of the newly instantiated network object.</returns>
        /// <exception cref="InvalidOperationException">Thrown if server not running.</exception>
        public T SpawnNetObject<T>(T netBehaviourPrefab, Scene scene, Transform parent, Vector3 position,
            Quaternion rotation) where T : NetBehaviour =>
            SpawnNetObject(netBehaviourPrefab.NetObject, scene, parent, position, rotation, ClientID.Server)
                .GetComponent<T>();

        /// <summary>
        /// Spawn a new networked object with a specific network behaviour component on the server.
        /// Sends a spawn message to all clients.
        /// Returns the component of the newly created network object.
        /// </summary>
        /// <param name="prefab">The NetBehaviour prefab to instantiate</param>
        /// <param name="scene">The scene where the object will be moved to. Ignored if parent is set.</param>
        /// <param name="parent">The parent transform this object will be a child of. Null for root objects.</param>
        /// <param name="position">The global position where to instantiate the object.</param>
        /// <param name="rotation">The global rotation with which to instantiate the object.</param>
        /// <param name="clientID">The client ID of the owner of this object. -1 if server-owned.</param>
        /// <typeparam name="T">The type of the net behaviour component of the new object that is returned.</typeparam>
        /// <returns>The net behaviour component of the newly instantiated network object.</returns>
        /// <exception cref="InvalidOperationException">Thrown if server not running.</exception>
        public T SpawnNetObject<T>(T prefab, Scene scene, Transform parent, Vector3 position,
            Quaternion rotation, ClientID clientID) where T : NetBehaviour
        {
            if (!IsActive) throw new InvalidOperationException("Cannot spawn NetObject: Server not running");
            return NetObjectManager.Instance.SpawnOnServer(prefab, position, rotation, scene, parent, clientID);
        }

        /// <summary>
        /// Spawn a new networked object on the server, owned by the server.
        /// Sends a spawn message to all clients.
        /// Returns the newly created network object.
        /// </summary>
        /// <param name="prefab"></param>
        /// <param name="scene">The scene where the object will be moved to. Ignored if parent is set.</param>
        /// <param name="parent">The parent transform this object will be a child of. Null for root objects.</param>
        /// <param name="position">The global position where to instantiate the object.</param>
        /// <param name="rotation">The global rotation with which to instantiate the object.</param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException">Thrown if server not running.</exception>
        public NetObject SpawnNetObject(NetObject prefab, Scene scene, Transform parent, Vector3 position,
            Quaternion rotation) => SpawnNetObject(prefab, scene, parent, position, rotation, ClientID.Server);

        /// <summary>
        /// Spawn a new networked object on the server.
        /// Sends a spawn message to all clients.
        /// Returns the newly created network object.
        /// </summary>
        /// <param name="prefab">The NetObject prefab to instantiate.</param>
        /// <param name="scene">The scene where the object will be moved to. Ignored if parent is set.</param>
        /// <param name="parent">The parent transform this object will be a child of. Null for root objects.</param>
        /// <param name="position">The global position where to instantiate the object.</param>
        /// <param name="rotation">The global rotation with which to instantiate the object.</param>
        /// <param name="clientID">The client ID of the owner of this object. -1 if server-owned.</param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException">Thrown if server not running.</exception>
        public NetObject SpawnNetObject(NetObject prefab, Scene scene, Transform parent, Vector3 position,
            Quaternion rotation, ClientID clientID)
        {
            if (!IsActive) throw new InvalidOperationException("Cannot spawn NetObject: Server not running");
            return NetObjectManager.Instance.SpawnOnServer(prefab, position, rotation, scene, parent, clientID);
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
            streamWriter.WriteInt(netObject.ID.Value);
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
                    netObject.ID.Serialize(ref streamWriter);
                }

                SendBytes(streamWriter.AsNativeArray(), _reliablePipeline, _connections);
            }
        }

        public void SendGameAction(GameAction gameAction, GameAction.IParameters parameters, ClientID clientID,
            float triggerTime)
        {
            if (!IsActive)
                throw new InvalidOperationException($"Cannot send {gameAction}: Server not running");

            var streamWriter = new DataStreamWriter(MaxBytesPerMessage, Allocator.Temp);
            streamWriter.WriteInt(Commands.GameAction);
            streamWriter.WriteInt(GameActionManager.Instance.GetID(gameAction));
            streamWriter.WriteInt(clientID.Value);
            streamWriter.WriteFloat(triggerTime);
            streamWriter.WriteBool(true); // valid
            gameAction.SerializeParameters(ref streamWriter, parameters);

            SendBytes(streamWriter.AsNativeArray(), _reliablePipeline, _connections);
        }

        public void DenyGameAction(GameAction gameAction, GameAction.IParameters parameters, ClientID clientID,
            float triggerTime)
        {
            if (!IsActive)
                throw new InvalidOperationException($"Cannot deny {gameAction}: Server not running");

            var streamWriter = new DataStreamWriter(MaxBytesPerMessage, Allocator.Temp);
            streamWriter.WriteInt(Commands.GameAction);
            streamWriter.WriteInt(GameActionManager.Instance.GetID(gameAction));
            streamWriter.WriteInt(clientID.Value);
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
                netBehaviour.NetObject.ID.Serialize(ref streamWriter);
                streamWriter.WriteUShort(netBehaviour.NetBehaviourIndex);
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