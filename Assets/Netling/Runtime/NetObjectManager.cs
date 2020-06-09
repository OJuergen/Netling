using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using JetBrains.Annotations;
using MufflonUtil;
using Unity.Networking.Transport;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Netling
{
    [CreateAssetMenu(menuName = "Network Object Manager")]
    public sealed class NetObjectManager : ScriptableObjectSingleton<NetObjectManager>
    {
        [SerializeField, Tooltip("If provided, all network objects are moved to this scene.")]
        private string _sceneName;

        [SerializeField, NotEditable, UsedImplicitly]
        private int _objectCount;

        [SerializeField] private List<NetObject> _netObjectPrefabs;
        private readonly Dictionary<int, NetObject> _objectsById = new Dictionary<int, NetObject>();
        private int _nextId;

        public NetObject[] NetObjects => _objectsById.Values.ToArray();

        private delegate void RPCDelegate(NetBehaviour netBehaviour, MessageInfo messageInfo, params object[] args);

        public delegate void RPC(MessageInfo messageInfo);

        public delegate void NetObjectDelegate(NetObject netObject);

        [SerializeField, NotEditable] private List<RPCInfo> _RPCInfo = new List<RPCInfo>();

        private readonly Dictionary<(ushort prefabIndex, ushort netBehaviourID, ushort methodIndex), RPCDelegate>
            _rpcDelegates =
                new Dictionary<(ushort prefabIndex, ushort netBehaviourID, ushort methodIndex), RPCDelegate>();

        private readonly Dictionary<(ushort prefabIndex, ushort netBehaviourID, ushort methodIndex), Type[]>
            _argumentTypes = new Dictionary<(ushort prefabIndex, ushort netBehaviourID, ushort methodIndex), Type[]>();

        private readonly Dictionary<(ushort prefabIndex, ushort netBehaviourID, string methodName),
            (ushort prefabIndex, ushort netBehaviourID, ushort methodIndex)> _rpcNameToID
            = new Dictionary<(ushort prefabIndex, ushort netBehaviourID, string methodName),
                (ushort prefabIndex, ushort netBehaviourID, ushort methodIndex)>();

        public event NetObjectDelegate NetObjectAdded;
        public event NetObjectDelegate NetObjectRemoved;

        [Serializable]
        public struct RPCInfo
        {
            [UsedImplicitly] public NetObject Prefab;
            [UsedImplicitly] public NetBehaviour NetBehaviour;
            [UsedImplicitly] public string MethodName;
        }

        private class RPCInfoComparer : IComparer<RPCInfo>
        {
            public int Compare(RPCInfo x, RPCInfo y)
            {
                int prefabIndexX = Instance._netObjectPrefabs.IndexOf(x.Prefab);
                int prefabIndexY = Instance._netObjectPrefabs.IndexOf(y.Prefab);
                if (prefabIndexX > prefabIndexY) return 1;
                if (prefabIndexX < prefabIndexY) return -1;
                if (x.NetBehaviour.NetBehaviourID > y.NetBehaviour.NetBehaviourID) return 1;
                if (x.NetBehaviour.NetBehaviourID < y.NetBehaviour.NetBehaviourID) return -1;
                return StringComparer.InvariantCulture.Compare(x.MethodName, y.MethodName);
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            UnityEditor.EditorApplication.delayCall += FindPrefabs; // delay call to avoid warning about SendMessage
        }
#endif

        private void CollectRPCInfo()
        {
            _RPCInfo.Clear();
            for (ushort i = 0; i < _netObjectPrefabs.Count; i++)
            {
                if (_netObjectPrefabs[i] != null) AddRPCInfo(_netObjectPrefabs[i]);
            }
#if UNITY_EDITOR
            if (this != null) UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        private void AddRPCInfo([NotNull] NetObject prefab)
        {
            if (prefab == null) throw new ArgumentNullException(nameof(prefab));
            if (prefab.NetBehaviours == null) return;
            foreach (NetBehaviour netBehaviour in prefab.NetBehaviours)
            {
                if (netBehaviour == null) continue;
                MethodInfo[] rpcMethods =
                    GetBaseTypes(netBehaviour.GetType())
                        .SelectMany(t =>
                            t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                        .Where(info => info.GetCustomAttributes(typeof(NetlingRPCAttribute), false).Length > 0)
                        .ToArray();
                _RPCInfo.AddRange(rpcMethods.Select(info => new RPCInfo
                {
                    Prefab = netBehaviour.NetObject,
                    NetBehaviour = netBehaviour,
                    MethodName = info.Name
                }).OrderBy(x => x, new RPCInfoComparer()));
            }
        }

        private new void OnEnable()
        {
            base.OnEnable();
            _objectsById.Clear();
            _objectCount = 0;
            _nextId = 0;
            Client.Instance.Disconnected += OnDisconnectedFromServer;
            Server.Instance.ClientDisconnected += OnClientDisconnected;
            PrepareRPCDelegates();
#if UNITY_EDITOR
            AssetPostProcessor.ImportedPrefab += OnImportedPrefab;
            AssetPostProcessor.DeletedAsset += OnDeletedAsset;
#endif
        }

        private new void OnDisable()
        {
            base.OnDisable();
            Client.Instance.Disconnected -= OnDisconnectedFromServer;
            Server.Instance.ClientDisconnected -= OnClientDisconnected;
#if UNITY_EDITOR
            AssetPostProcessor.ImportedPrefab -= OnImportedPrefab;
            AssetPostProcessor.DeletedAsset -= OnDeletedAsset;
#endif
        }

#if UNITY_EDITOR
        private void OnImportedPrefab([NotNull] GameObject prefab)
        {
            var netObject = prefab.GetComponent<NetObject>();
            if (netObject == null || _netObjectPrefabs.Contains(netObject)) return;
            _netObjectPrefabs.Add(netObject);
            FindPrefabs();
            UnityEditor.EditorUtility.SetDirty(this);
        }

        private void OnDeletedAsset(string assetPath)
        {
            int prefabCount = NetObjects.Length;
            FindPrefabs();
            if(NetObjects.Length != prefabCount) UnityEditor.EditorUtility.SetDirty(this);
        }

        [ContextMenu("Find Prefabs")]
        public void FindPrefabs()
        {
            _netObjectPrefabs = UnityEditor.AssetDatabase.FindAssets("t:GameObject")
                .Select(UnityEditor.AssetDatabase.GUIDToAssetPath)
                .Select(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>)
                .Where(go => go != null)
                .Select(go => go.GetComponent<NetObject>())
                .Where(netObject => netObject != null)
                .OrderBy(p => p.name)
                .ToList();
            CollectRPCInfo();
            Debug.Log($"FindPrefabs {_netObjectPrefabs.Count} prefabs");
        }
#endif

        private Type[] GetBaseTypes(Type type)
        {
            if (type == null) return new Type[0];
            var types = new[] {type};
            if (type.BaseType == null) return types;
            Type t = type;
            while (t.BaseType != null)
            {
                t = t.BaseType;
                var newTypes = new Type[types.Length + 1];
                Array.Copy(types, newTypes, types.Length);
                newTypes[types.Length] = t;
                types = newTypes;
                if (t == typeof(NetBehaviour)) break;
            }

            return types;
        }

        [ContextMenu("Prepare RPC delegates")]
        private void PrepareRPCDelegates()
        {
            _rpcDelegates.Clear();
            _argumentTypes.Clear();
            for (ushort prefabIndex = 0; prefabIndex < _netObjectPrefabs.Count; prefabIndex++)
            {
                var prefab = _netObjectPrefabs[prefabIndex];
                if (prefab == null) continue;
                foreach (NetBehaviour netBehaviour in prefab.NetBehaviours)
                {
                    MethodInfo[] unsortedRPCMethodInfos = GetBaseTypes(netBehaviour.GetType())
                        .SelectMany(type =>
                            type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                        .Where(info => info.GetCustomAttributes(typeof(NetlingRPCAttribute), false).Length > 0)
                        .ToArray();
                    foreach (MethodInfo rpcMethodInfo in unsortedRPCMethodInfos)
                    {
                        var rpcInfo = new RPCInfo
                        {
                            Prefab = prefab,
                            NetBehaviour = netBehaviour,
                            MethodName = rpcMethodInfo.Name
                        };
                        var methodIndex = (ushort) _RPCInfo.IndexOf(rpcInfo);
                        (ushort PrefabIndex, ushort NetBehaviourID, ushort MethodIndex) rpcID =
                            (prefabIndex, netBehaviour.NetBehaviourID, methodIndex);
                        ParameterInfo[] parameterInfos = rpcMethodInfo.GetParameters();
                        _rpcDelegates[rpcID] = GetRPCDelegate(rpcMethodInfo);
                        _argumentTypes[rpcID] = parameterInfos
                            .Select(p => p.ParameterType)
                            .Where(type => type != typeof(MessageInfo))
                            .ToArray();
                        _rpcNameToID[(prefabIndex, netBehaviour.NetBehaviourID, rpcMethodInfo.Name)] = rpcID;
                    }
                }
            }
        }

        public void SerializeRPC(ref DataStreamWriter writer, NetBehaviour netBehaviour, string methodName,
                                 params object[] args)
        {
            (ushort PrefabIndex, ushort NetBehaviourID, string methodName) rpcName =
                (netBehaviour.NetObject.PrefabIndex, netBehaviour.NetBehaviourID, methodName);
            (ushort PrefabIndex, ushort NetBehaviourID, ushort MethodIndex) rpcID = _rpcNameToID[rpcName];
            Type[] argumentTypes = _argumentTypes[rpcID];

            writer.WriteUShort(rpcID.MethodIndex);
            writer.WriteObjects(args, argumentTypes);
        }

        public RPC DeserializeRPC(ref DataStreamReader reader, NetBehaviour netBehaviour)
        {
            ushort methodIndex = reader.ReadUShort();
            (ushort PrefabIndex, ushort NetBehaviourID, ushort MethodIndex) rpcid =
                (netBehaviour.NetObject.PrefabIndex, netBehaviour.NetBehaviourID, methodIndex);

            if (!_argumentTypes.ContainsKey(rpcid))
                throw new NetException($"Cannot deserialize rpc with index {rpcid}");

            Type[] argumentTypes = _argumentTypes[rpcid];
            object[] arguments = reader.ReadObjects(argumentTypes);

            return messageInfo => _rpcDelegates[rpcid].Invoke(netBehaviour, messageInfo, arguments);
        }

        private void OnClientDisconnected(int actorNumber)
        {
            Server.Instance.UnspawnNetObjects(NetObjects
                .Where(netObject => netObject.OwnerActorNumber == actorNumber)
                .ToArray());
        }

        private void OnDisconnectedFromServer()
        {
            if (Server.IsActive)
                return; // keep hosting

            foreach (NetObject netObject in NetObjects)
            {
                Unspawn(netObject.ID);
            }
        }

        public T SpawnOnServer<T>(T networkBehaviourPrefab, Vector3 position, Quaternion rotation,
                                  int ownerActorNumber = Server.ServerActorNumber) where T : NetBehaviour
        {
            Server.AssertActive();
            NetObject netObjectPrefab = networkBehaviourPrefab.NetObject;
            return SpawnOnServer(netObjectPrefab, position, rotation, ownerActorNumber).GetComponent<T>();
        }

        public NetObject SpawnOnServer(NetObject netObjectPrefab, Vector3 position, Quaternion rotation,
                                       int ownerActorNumber = Server.ServerActorNumber)
        {
            Server.AssertActive();
            if (!_netObjectPrefabs.Contains(netObjectPrefab))
            {
                throw new Exception($"Failed to instantiate {netObjectPrefab}: Prefab not registered");
            }

            var prefabIndex = (ushort) _netObjectPrefabs.IndexOf(netObjectPrefab);
            int id = _nextId++;
            NetObject netObject = netObjectPrefab.Create(id, prefabIndex, ownerActorNumber, position, rotation);
            if (!string.IsNullOrEmpty(_sceneName))
            {
                Scene scene = SceneManager.GetSceneByName(_sceneName);
                if (scene.isLoaded) SceneManager.MoveGameObjectToScene(netObject.gameObject, scene);
            }

            _objectsById.Add(id, netObject);
            _objectCount = _objectsById.Count;
            NetObjectAdded?.Invoke(netObject);

            return netObject;
        }

        public NetObject SpawnOnClient(int id, ushort prefabIndex, int ownerActorNumber, Vector3 position,
                                       Quaternion rotation)
        {
            if (_netObjectPrefabs.Count < prefabIndex + 1)
                throw new Exception($"Cannot instantiate network object with prefab index {prefabIndex}");

            if (!_objectsById.ContainsKey(id))
            {
                NetObject netObject =
                    _netObjectPrefabs[prefabIndex].Create(id, prefabIndex, ownerActorNumber, position, rotation);
                _objectsById.Add(id, netObject);
                _objectCount = _objectsById.Count;
                if (!string.IsNullOrEmpty(_sceneName) && SceneManager.GetSceneByName(_sceneName).isLoaded)
                    SceneManager.MoveGameObjectToScene(netObject.gameObject, SceneManager.GetSceneByName(_sceneName));
                NetObjectAdded?.Invoke(netObject);
            }

            return _objectsById[id];
        }

        public void Unspawn(int netObjectID)
        {
            if (_objectsById.TryGetValue(netObjectID, out NetObject netObject))
            {
                if (netObject != null && netObject.gameObject != null)
                    Destroy(netObject.gameObject);
                _objectsById.Remove(netObjectID);
                _objectCount = _objectsById.Count;
                NetObjectRemoved?.Invoke(netObject);
            }
        }

        /// <summary>
        /// Check whether a <see cref="NetObject"/> with the <paramref name="netObjectID"/> is registered.
        /// </summary>
        /// <param name="netObjectID">The ID of the <see cref="NetObject"/> in question</param>
        /// <returns>true, iff the <see cref="NetObject"/> is registered</returns>
        public bool Exists(int netObjectID)
        {
            return _objectsById.ContainsKey(netObjectID);
        }

        /// <summary>
        /// Retrieves the <see cref="NetObject"/> with the given <paramref name="netObjectID"/>.
        /// Use <see cref="Exists"/> to check, if an object with the ID is registered.
        /// </summary>
        /// <param name="netObjectID">The ID with which the <see cref="NetObject"/> is identified across the network</param>
        /// <returns>The <see cref="NetObject"/> with the given <paramref name="netObjectID"/></returns>
        /// <exception cref="ArgumentException">Thrown, if no <see cref="NetObject"/> with the given <paramref name="netObjectID"/> is registered</exception>
        public NetObject Get(int netObjectID)
        {
            if (_objectsById.TryGetValue(netObjectID, out NetObject netObject))
                return netObject;
            throw new ArgumentException($"Net object with id {netObjectID} not found. " +
                                        "Not spawned yet or already destroyed?");
        }

        private static RPCDelegate GetRPCDelegate(MethodInfo methodInfo)
        {
            ParameterExpression args = Expression.Parameter(typeof(object[]), "args");
            ParameterExpression messageInfo = Expression.Parameter(typeof(MessageInfo), "messageInfo");
            Expression[] methodArgs = methodInfo.GetParameters()
                .Select((paramInfo, i) => paramInfo.ParameterType == typeof(MessageInfo)
                    ? messageInfo as Expression
                    : Expression.Convert(Expression.ArrayAccess(args, Expression.Constant(i)), paramInfo.ParameterType))
                .ToArray();
            ParameterExpression target = Expression.Parameter(typeof(object), "target");
            if (methodInfo.DeclaringType == null)
                throw new ArgumentException($"Cannot prepare rpc delegate for static method {methodInfo.Name}");
            UnaryExpression convertedTarget = Expression.Convert(target, methodInfo.DeclaringType);
            MethodCallExpression call = Expression.Call(convertedTarget, methodInfo, methodArgs);
            Expression<RPCDelegate> lambda = Expression.Lambda<RPCDelegate>(call, target, messageInfo, args);

            return
                lambda.Compile(); // (NetBehaviour nb, MessageInfo mi, object[] args) => methodInfo.Invoke(nb, (T[]) args) .
            // args are cast to respective parameter type or replaced with MessageInfo mi.
        }
    }
}