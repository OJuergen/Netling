using System;
using MufflonUtil;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Netling
{
    public sealed class NetObject : MonoBehaviour
    {
        [SerializeField, NotEditable] private NetBehaviour[] _netBehaviours;
        private int _ownerClientID = Server.ServerClientID;
        public bool IsInitialized { get; private set; }

        private int _id;
        public int ID
        {
            get
            {
                if (Application.isPlaying && !IsInitialized)
                    throw new InvalidOperationException("Cannot query ID of uninitialized NetObject");
                return _id;
            }
            private set => _id = value;
        }

        private ushort _prefabIndex;
        public ushort PrefabIndex
        {
            get
            {
                if (Application.isPlaying && !IsInitialized)
                    throw new InvalidOperationException("Cannot query prefab index of uninitialized NetObject");
                return _prefabIndex;
            }
            private set => _prefabIndex = value;
        }

        public int OwnerClientID
        {
            get
            {
                if (Application.isPlaying && !IsInitialized)
                    throw new InvalidOperationException("Cannot query owner client ID of uninitialized NetObject");
                return _ownerClientID;
            }
            private set => _ownerClientID = value;
        }

        public bool IsDirty { get; private set; }
        public bool IsMine => OwnerClientID == Client.Instance.ID
                              || Server.IsActive && OwnerClientID == Server.ServerClientID;
        public NetBehaviour[] NetBehaviours => _netBehaviours;

        private void OnValidate()
        {
            AssignIDs();
        }

        private void Init(int id, ushort prefabIndex, int ownerClientID)
        {
            ID = id;
            PrefabIndex = prefabIndex;
            OwnerClientID = ownerClientID;
            IsInitialized = true;
        }

        private void OnDestroy()
        {
            if (Server.IsActive && IsInitialized) Server.Instance.UnspawnNetObject(this);
            if(Client.Instance.IsConnected && IsInitialized) NetObjectManager.Instance.Unspawn(ID);
        }

        public void SetDirty()
        {
            IsDirty = true;
        }

        public void AssignIDs()
        {
            _netBehaviours = GetComponentsInChildren<NetBehaviour>();
            for (ushort i = 0; i < _netBehaviours.Length; i++)
            {
                if (_netBehaviours[i].NetBehaviourID == i) continue;
                _netBehaviours[i].NetBehaviourID = i;
#if UNITY_EDITOR
                EditorUtility.SetDirty(this);
#endif
            }
        }

        public NetBehaviour Get(ushort netBehaviourID)
        {
            if (netBehaviourID >= _netBehaviours.Length)
                throw new ArgumentException($"No net behaviour with index {netBehaviourID} found");
            return _netBehaviours[netBehaviourID];
        }

        public NetObject Create(int id, ushort prefabIndex, Scene scene, Transform parent, Vector3 position, Quaternion rotation, int ownerClientID)
        {
            NetObject netObject = Instantiate(this, position, rotation, parent);
            if(parent == null && scene.isLoaded) SceneManager.MoveGameObjectToScene(netObject.gameObject, scene);
            netObject.Init(id, prefabIndex, ownerClientID);
            return netObject;
        }

        #region Serialization

        public void Serialize(ref DataStreamWriter writer, bool fullLoad)
        {
            Serialize(ref writer, fullLoad, _ => true);
        }

        public void Serialize(ref DataStreamWriter writer, bool fullLoad, Predicate<NetBehaviour> behaviourFilter)
        {
            var behaviourCount = (ushort) 0;
            DataStreamWriter behaviourCountWriter = writer;
            writer.WriteUShort(0);
            if (!fullLoad && !IsDirty) return;
            foreach (NetBehaviour networkBehaviour in _netBehaviours)
            {
                if (!networkBehaviour.enabled) continue;
                if (!behaviourFilter(networkBehaviour)) continue;
                if (!fullLoad && !networkBehaviour.IsDirty()) continue;
                behaviourCount++;
                writer.WriteUShort(networkBehaviour.NetBehaviourID);
                if (Server.IsActive)
                    writer.WriteBool(networkBehaviour.ClientAuthoritative); // can be true due to override
                DataStreamWriter sizeWriter = writer;
                writer.WriteUShort(0);
                int length = writer.Length;
                networkBehaviour.Serialize(ref writer, fullLoad);
                sizeWriter.WriteUShort((ushort) (writer.Length - length));
            }

            behaviourCountWriter.WriteUShort(behaviourCount);

            IsDirty = false;
        }

        public void Deserialize(ref DataStreamReader reader, Predicate<NetBehaviour> behaviourFilter)
        {
            ushort behaviourCount = reader.ReadUShort();
            for (ushort i = 0; i < behaviourCount; i++)
            {
                ushort id = reader.ReadUShort();
                NetBehaviour netBehaviour = _netBehaviours[id];
                if (!Server.IsActive) netBehaviour.ClientAuthoritative = reader.ReadBool();
                ushort size = reader.ReadUShort();
                if (behaviourFilter(netBehaviour) && netBehaviour.enabled)
                    netBehaviour.Deserialize(ref reader);
                else
                    reader.DiscardBytes(size);
            }
        }

        #endregion
    }
}