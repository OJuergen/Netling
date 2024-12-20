using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using JetBrains.Annotations;
using MufflonUtil;
using Unity.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Netling
{
    [CreateAssetMenu(menuName = "Netling/Network Object Manager")]
    public sealed class NetObjectManager : ScriptableObjectSingleton<NetObjectManager>
    {
        [SerializeField, NotEditable, UsedImplicitly]
        private int _objectCount;

        [SerializeField] private List<NetObject> _netObjectPrefabs;
        public NetObject[] NetObjectPrefabs => _netObjectPrefabs.ToArray();
        private readonly Dictionary<NetObjectID, NetObject> _objectsById = new();
        private NetObjectID _nextId;

        public NetObject[] NetObjects => _objectsById.Values.ToArray();

        private delegate void RPCDelegate(NetBehaviour netBehaviour, MessageInfo messageInfo, params object[] args);

        public delegate void RPC(MessageInfo messageInfo);

        public delegate void NetObjectDelegate(NetObject netObject);

        [SerializeField, NotEditable] private List<RPCInfo> _rpcInfo = new();

        private readonly Dictionary<(ushort PrefabIndex, ushort NetBehaviourIndex, ushort MethodIndex), RPCDelegate>
            _rpcDelegates = new();

        private readonly Dictionary<(ushort PrefabIndex, ushort NetBehaviourIndex, ushort MethodIndex), Type[]>
            _argumentTypes = new();

        private readonly
            Dictionary<(ushort PrefabIndex, ushort NetBehaviourIndex, string methodName), (ushort prefabIndex, ushort
                netBehaviourIndex, ushort methodIndex)> _rpcNameToID = new();

        public event NetObjectDelegate NetObjectAdded;
        public event NetObjectDelegate NetObjectRemoved;

        [Serializable]
        public struct RPCInfo
        {
            [field: SerializeField] public NetObject Prefab { get; set; }
            [field: SerializeField] public NetBehaviour NetBehaviour { get; set; }
            [field: SerializeField] public string MethodName { get; set; }
        }

        private class RPCInfoComparer : IComparer<RPCInfo>
        {
            public int Compare(RPCInfo x, RPCInfo y)
            {
                int prefabIndexX = Instance._netObjectPrefabs.IndexOf(x.Prefab);
                int prefabIndexY = Instance._netObjectPrefabs.IndexOf(y.Prefab);
                if (prefabIndexX > prefabIndexY) return 1;
                if (prefabIndexX < prefabIndexY) return -1;
                if (x.NetBehaviour.NetBehaviourIndex > y.NetBehaviour.NetBehaviourIndex) return 1;
                if (x.NetBehaviour.NetBehaviourIndex < y.NetBehaviour.NetBehaviourIndex) return -1;
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
            _rpcInfo.Clear();
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
                _rpcInfo.AddRange(rpcMethods.Select(info => new RPCInfo
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
            Init();
            Client.Instance.StateChanged += OnClientStateChanged;
            Server.Instance.ClientDisconnected += OnClientDisconnected;
#if UNITY_EDITOR
            AssetPostProcessor.ImportedPrefab += OnImportedPrefab;
            AssetPostProcessor.DeletedAsset += OnDeletedAsset;
#endif
        }

        private new void OnDisable()
        {
            base.OnDisable();
            Client.Instance.StateChanged -= OnClientStateChanged;
            Server.Instance.ClientDisconnected -= OnClientDisconnected;
#if UNITY_EDITOR
            AssetPostProcessor.ImportedPrefab -= OnImportedPrefab;
            AssetPostProcessor.DeletedAsset -= OnDeletedAsset;
#endif
        }

        public void Init()
        {
            _objectsById.Clear();
            _objectCount = 0;
            _nextId = new NetObjectID(0);
            PrepareRPCDelegates();
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
            if (NetObjects.Length != prefabCount) UnityEditor.EditorUtility.SetDirty(this);
        }

        [ContextMenu("Find Prefabs")]
        public void FindPrefabs()
        {
            _netObjectPrefabs = UnityEditor.AssetDatabase.FindAssets("t:GameObject")
                .Select(UnityEditor.AssetDatabase.GUIDToAssetPath)
                .Where(path => path.StartsWith("Assets/")) // exclude package assets
                .Select(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>)
                .Where(go => go != null)
                .Select(go => go.GetComponent<NetObject>())
                .Where(netObject => netObject != null)
                .OrderBy(p => p.name)
                .ToList();
            CollectRPCInfo();
        }
#endif

        private Type[] GetBaseTypes(Type type)
        {
            if (type == null) return Type.EmptyTypes;
            Type[] types = { type };
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
                NetObject prefab = _netObjectPrefabs[prefabIndex];
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
                        var methodIndex = (ushort)_rpcInfo.IndexOf(rpcInfo);
                        (ushort PrefabIndex, ushort NetBehaviourIndex, ushort MethodIndex) rpcID =
                            (prefabIndex, netBehaviour.NetBehaviourIndex, methodIndex);
                        ParameterInfo[] parameterInfos = rpcMethodInfo.GetParameters();
                        _rpcDelegates[rpcID] = GetRPCDelegate(rpcMethodInfo);
                        _argumentTypes[rpcID] = parameterInfos
                            .Select(p => p.ParameterType)
                            .Where(type => type != typeof(MessageInfo))
                            .ToArray();
                        _rpcNameToID[(prefabIndex, netBehaviour.NetBehaviourIndex, rpcMethodInfo.Name)] = rpcID;
                    }
                }
            }
        }

        public void SerializeRPC(ref DataStreamWriter writer, NetBehaviour netBehaviour, string methodName,
            params object[] args)
        {
            (ushort PrefabIndex, ushort NetBehaviourIndex, string methodName) rpcName =
                (netBehaviour.NetObject.PrefabIndex, netBehaviour.NetBehaviourIndex, methodName);
            (ushort PrefabIndex, ushort NetBehaviourIndex, ushort MethodIndex) rpcID = _rpcNameToID[rpcName];
            Type[] argumentTypes = _argumentTypes[rpcID];

            writer.WriteUShort(rpcID.MethodIndex);
            writer.WriteObjects(args, argumentTypes);
        }

        public RPC DeserializeRPC(ref DataStreamReader reader, NetBehaviour netBehaviour)
        {
            ushort methodIndex = reader.ReadUShort();
            (ushort PrefabIndex, ushort NetBehaviourIndex, ushort MethodIndex) rpcID =
                (netBehaviour.NetObject.PrefabIndex, netBehaviour.NetBehaviourIndex, methodIndex);

            if (!_argumentTypes.TryGetValue(rpcID, out Type[] argumentTypes))
                throw new NetException($"Cannot deserialize rpc with index {rpcID}");

            object[] arguments = reader.ReadObjects(argumentTypes);

            return messageInfo => _rpcDelegates[rpcID].Invoke(netBehaviour, messageInfo, arguments);
        }

        private void OnClientDisconnected(ClientID clientID)
        {
            Server.Instance.UnspawnNetObjects(NetObjects
                .Where(netObject => netObject.OwnerClientID == clientID)
                .ToArray());
        }

        private void OnClientStateChanged(Client.ClientState clientState)
        {
            if (clientState == Client.ClientState.Disconnected)
            {
                if (Server.IsActive)
                    return; // keep hosting

                foreach (NetObject netObject in NetObjects)
                {
                    Unspawn(netObject.ID);
                }
            }
        }

        public T SpawnOnServer<T>(T prefab, Vector3 position, Quaternion rotation, Scene scene, Transform parent,
            ClientID ownerClientID) where T : NetBehaviour
        {
            Server.AssertActive();
            NetObject netObjectPrefab = prefab.NetObject;
            return SpawnOnServer(netObjectPrefab, position, rotation, scene, parent, ownerClientID)
                .GetComponent<T>();
        }

        public NetObject SpawnOnServer(NetObject prefab, Vector3 position, Quaternion rotation, Scene scene,
            Transform parent, ClientID ownerClientID)
        {
            Server.AssertActive();
            if (!_netObjectPrefabs.Contains(prefab))
            {
                throw new Exception($"Failed to instantiate {prefab}: Prefab not registered");
            }

            var prefabIndex = (ushort)_netObjectPrefabs.IndexOf(prefab);
            NetObjectID id = _nextId++;
            NetObject netObject = Instantiate(prefab, position, rotation, parent);
            if (parent == null && scene.isLoaded) SceneManager.MoveGameObjectToScene(netObject.gameObject, scene);
            netObject.Init(id, prefabIndex, ownerClientID);

            _objectsById.Add(id, netObject);
            _objectCount = _objectsById.Count;
            Server.Instance.SendSpawnMessage(netObject);
            NetObjectAdded?.Invoke(netObject);

            return netObject;
        }

        public NetObject SpawnOnClient(NetObjectID id, ushort prefabIndex, Scene scene, Transform parent,
            Vector3 position, Quaternion rotation, ClientID ownerClientID)
        {
            if (_netObjectPrefabs.Count < prefabIndex + 1)
                throw new Exception($"Cannot instantiate network object with prefab index {prefabIndex}");

            if (!_objectsById.ContainsKey(id))
            {
                if (!scene.isLoaded) throw new ArgumentException($"Scene {scene} not loaded");
                NetObject netObject = Instantiate(_netObjectPrefabs[prefabIndex], position, rotation, parent);
                netObject.Init(id, prefabIndex, ownerClientID);
                _objectsById.Add(id, netObject);
                _objectCount = _objectsById.Count;
                NetObjectAdded?.Invoke(netObject);
            }

            return _objectsById[id];
        }

        public void Unspawn(NetObjectID netObjectID)
        {
            if (_objectsById.TryGetValue(netObjectID, out NetObject netObject))
            {
                if (netObject != null && netObject.gameObject != null && Application.isPlaying)
                    Destroy(netObject.gameObject);
                if (netObject != null && netObject.gameObject != null && !Application.isPlaying)
                    DestroyImmediate(netObject.gameObject);
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
        public bool Exists(NetObjectID netObjectID)
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
        public NetObject Get(NetObjectID netObjectID)
        {
            if (_objectsById.TryGetValue(netObjectID, out NetObject netObject))
                return netObject;
            throw new ArgumentException($"Net object with id {netObjectID} not found. " +
                                        "Not spawned yet or already destroyed?");
        }

        /// <summary>
        /// Creates a delegate from method info (<see cref="MethodInfo"/>).
        /// The delegate will take as parameters: a NetBehaviour (<see cref="NetBehaviour"/>), the message network info
        /// (<see cref="MessageInfo"/>) and the parameters of the method that are <i>not</i> of type MessageInfo.
        /// The delegate will invoke the method on the network behaviour using the message info to populate any
        /// arguments of that type.
        ///
        /// (NetBehaviour nb, MessageInfo mi, object[] args) => methodInfo.Invoke(nb, argsWithMessageInfo)
        /// </summary>
        /// <param name="methodInfo">The method to create the delegate for.</param>
        /// <returns>A delegate to invoke the method on a specific network behaviour with given message info and arguments.</returns>
        /// <exception cref="ArgumentNullException">Thrown when passed method info is null.</exception>
        /// <exception cref="ArgumentException">Thrown when passed method info is not suited for delegate preparation, e.g., static methods.</exception>
        private static RPCDelegate GetRPCDelegate(MethodInfo methodInfo)
        {
            if (methodInfo == null) throw new ArgumentNullException(nameof(methodInfo));
            if (methodInfo.DeclaringType == null)
                throw new ArgumentException($"Cannot prepare rpc delegate for static method {methodInfo.Name}");

            ParameterExpression argsExpression = Expression.Parameter(typeof(object[]), "args");
            ParameterExpression messageInfoExpression = Expression.Parameter(typeof(MessageInfo), "messageInfo");

            var argsIndex = 0;
            var methodArgumentExpressions = new List<Expression>();
            foreach (ParameterInfo parameterInfo in methodInfo.GetParameters())
            {
                Type paramType = parameterInfo.ParameterType;
                if (paramType == typeof(MessageInfo)) methodArgumentExpressions.Add(messageInfoExpression);
                else
                {
                    ConstantExpression indexExpression = Expression.Constant(argsIndex++);
                    IndexExpression arrayAccessExpression = Expression.ArrayAccess(argsExpression, indexExpression);
                    methodArgumentExpressions.Add(Expression.Convert(arrayAccessExpression, paramType));
                }
            }

            ParameterExpression targetExpression = Expression.Parameter(typeof(object), "target");
            UnaryExpression typedTargetExpression = Expression.Convert(targetExpression, methodInfo.DeclaringType);
            MethodCallExpression callExpression =
                Expression.Call(typedTargetExpression, methodInfo, methodArgumentExpressions);
            Expression<RPCDelegate> lambda =
                Expression.Lambda<RPCDelegate>(callExpression, targetExpression, messageInfoExpression, argsExpression);

            return
                lambda.Compile(); // (NetBehaviour nb, MessageInfo mi, object[] args) => methodInfo.Invoke(nb, (T[]) args) .
            // args are cast to respective parameter type or replaced with MessageInfo mi.
        }
    }
}