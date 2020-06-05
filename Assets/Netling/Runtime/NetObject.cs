using System;
using MufflonUtil;
using Unity.Networking.Transport;
using UnityEngine;

namespace Netling
{
    public sealed class NetObject : MonoBehaviour
    {
        [SerializeField, NotEditable] private int _id;
        [SerializeField, NotEditable] private ushort _prefabIndex;
        [SerializeField, NotEditable] private NetBehaviour[] _netBehaviours;
        private int _ownerActorNumber = Server.ServerActorNumber;
        public bool IsInitialized { get; private set; }

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

        public ushort PrefabIndex
        {
            get => _prefabIndex;
            set
            {
                _prefabIndex = value;
#if UNITY_EDITOR
                UnityEditor.EditorUtility.SetDirty(this);
#endif
            }
        }

        public int OwnerActorNumber
        {
            get
            {
                if (Application.isPlaying && !IsInitialized)
                    throw new InvalidOperationException("Cannot query OwnerActorNumber of uninitialized NetObject");
                return _ownerActorNumber;
            }
            private set => _ownerActorNumber = value;
        }

        public bool IsDirty { get; private set; }
        public bool IsMine => OwnerActorNumber == Client.Instance.ActorNumber
                              || Server.IsActive && OwnerActorNumber == Server.ServerActorNumber;
        public NetBehaviour[] NetBehaviours => _netBehaviours;

        private void OnValidate()
        {
            AssignIDs();
        }

        private void Init(int id, ushort prefabIndex, int ownerActorNumber)
        {
            ID = id;
            PrefabIndex = prefabIndex;
            OwnerActorNumber = ownerActorNumber;
            IsInitialized = true;
        }

        private void OnDestroy()
        {
            if (Server.IsActive && IsInitialized) Server.Instance.UnspawnNetObject(this);
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
                _netBehaviours[i].NetBehaviourID = i;
            }
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        public NetBehaviour Get(ushort netBehaviourID)
        {
            if (netBehaviourID >= _netBehaviours.Length)
                throw new ArgumentException($"No net behaviour with index {netBehaviourID} found");
            return _netBehaviours[netBehaviourID];
        }

        public NetObject Create(int id, ushort prefabIndex, int ownerActorNumber, Vector3 position,
                                Quaternion rotation)
        {
            NetObject netObject = Instantiate(this, position, rotation);
            netObject.Init(id, prefabIndex, ownerActorNumber);
            return netObject;
        }

        #region Serialization

        public void Serialize(ref DataStreamWriter writer, bool fullLoad)
        {
            Serialize(ref writer, fullLoad, networkBehaviour => true);
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