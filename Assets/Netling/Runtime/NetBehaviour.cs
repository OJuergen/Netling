using System;
using System.Linq;
using MufflonUtil;
using Unity.Collections;
using UnityEngine;

namespace Netling
{
    /// <summary>
    /// A networked <see cref="MonoBehaviour"/> with synchronized data and RPC capability.
    /// Must be accompanied by a <see cref="NetObject"/> component on the same GameObject.
    /// </summary>
    public abstract class NetBehaviour : MonoBehaviour, IDirtyMaskProvider
    {
        [SerializeField, NotEditable, Tooltip("ID of this NetworkBehaviour as a component of a NetObject. "
                                              + "Is assigned by the NetObject.")]
        private ushort _netBehaviourID;

        [SerializeField, Tooltip("Determines whether the owner of this NetObject, or only the server "
                                 + "has the authority to change synchronized fields on this NetworkBehaviour")]
        private bool _clientAuthoritative;

        /// <summary>
        /// Identifier for this behaviour on a specific <see cref="NetObject"/>.
        /// </summary>
        public ushort NetBehaviourID { get => _netBehaviourID; set => _netBehaviourID = value; }

        /// <summary>
        /// True, if owning client has authority over the state of this behaviour.
        /// Synchronized from server to clients.
        /// Can be overriden on server temporarily for the next update.
        /// </summary>
        public bool ClientAuthoritative
        {
            get => _clientAuthoritative && !_authorityOverride;
            set
            {
                if (value == _clientAuthoritative) return;
                _authorityDirty = true;
                NetObject.SetDirty();
                _clientAuthoritative = value;
            }
        }

        /// <summary>
        /// Flags whether state is sent or received on this client/server.
        /// </summary>
        public bool HasAuthority => IsLocal && ClientAuthoritative || Server.IsActive && !ClientAuthoritative;

        /// <summary>
        /// Flags whether the local client owns this <see cref="NetObject"/>.
        /// Do not confuse with <see cref="HasAuthority"/>!
        /// </summary>
        public bool IsLocal => NetObject.IsMine;

        private NetObject _netObject;

        /// <summary>
        /// The associated <see cref="NetObject"/>.
        /// </summary>
        public NetObject NetObject => _netObject != null
            ? _netObject
            : _netObject = GetComponentsInParent<NetObject>(true).SingleOrDefault();

        /// <summary>
        /// The actor number of the owning client.
        /// </summary>
        public int OwnerActorNumber => NetObject.OwnerActorNumber;

        private byte _dirtyMask = 255;
        private bool _authorityDirty; // client-authority flag needs sync
        private bool _authorityOverride; // overriding client-authority for next update

        private void OnValidate()
        {
            NetObject?.AssignIDs();
        }

        /// <summary>
        /// Sends an RPC request to call the method with the given <paramref name="methodName"/> on this behaviour.
        /// If server, RPC is sent to all clients. If client, RPC is sent to server.
        /// </summary>
        /// <param name="methodName"></param>
        /// <param name="args"></param>
        protected void SendRPC(string methodName, params object[] args)
        {
            if (Server.IsActive)
                Server.Instance.SendRPC(this, methodName, args);
            else
                Client.Instance.SendRPC(this, methodName, args);
        }

        public void SetAuthorityOverride()
        {
            Server.AssertActive();
            _authorityOverride = true;
        }

        /// <summary>
        /// Write dirty data to <paramref name="writer"/>.
        /// First writes the dirty mask itself and then all data according to it.
        /// When <paramref name="fullLoad"/>, sets all bits dirty first.
        /// </summary>
        /// <param name="writer">Stream to write data to</param>
        /// <param name="fullLoad">When true, all bits are set dirty first</param>
        public void Serialize(ref DataStreamWriter writer, bool fullLoad)
        {
            if (fullLoad) _dirtyMask = 255;
            writer.WriteByte(_dirtyMask);
            Serialize(ref writer, _dirtyMask);
            _dirtyMask = 0;
            _authorityDirty = false;
            _authorityOverride = false;
        }

        /// <summary>
        /// Write data marked as dirty by <paramref name="dirtyMask"/> to <paramref name="writer"/>.
        /// <br/><br/>
        /// <b>Warning:</b> The implementation of this must complement that of
        /// <see cref="Deserialize(ref DataStreamReader, byte)"/>
        /// </summary>
        /// <param name="writer">Stream to write data to</param>
        /// <param name="dirtyMask">Bit mask defining what data to write</param>
        protected virtual void Serialize(ref DataStreamWriter writer, byte dirtyMask)
        { }

        /// <summary>
        /// Read and apply data from <paramref name="reader"/>.
        /// First reads the dirty bit mask and handles the following data accordingly.
        /// </summary>
        /// <param name="reader">Stream containing the data</param>
        public void Deserialize(ref DataStreamReader reader)
        {
            byte dirtyMask = reader.ReadByte();
            Deserialize(ref reader, dirtyMask);
            SetDirty(dirtyMask);
        }

        /// <summary>
        /// Use the given <paramref name="reader"/> to parse and handle data for this <see cref="NetBehaviour"/>.
        /// Reads data according to the <paramref name="dirtyMask"/> bit mask.
        /// <br/><br/>
        /// <b>Warning: </b>The implementation of this must complement that of <see cref="Serialize(ref DataStreamWriter, byte)"/>.
        /// </summary>
        /// <param name="reader">Stream containing the data</param>
        /// <param name="dirtyMask">Dirty bit mask defining what data to expect</param>
        protected virtual void Deserialize(ref DataStreamReader reader, byte dirtyMask)
        { }

        /// <summary>
        /// Sets all bits dirty.
        /// Marks the parenting <see cref="NetObject"/> as dirty.
        /// </summary>
        public void SetDirty()
        {
            _dirtyMask = 255;
            NetObject.SetDirty();
        }

        /// <summary>
        /// Sets the bit at the given <paramref name="bitPosition"/> dirty.
        /// Marks the parenting <see cref="NetObject"/> as dirty.
        /// </summary>
        /// <param name="bitPosition">Dirty bit position between 0 and 7</param>
        public void SetDirtyAt(byte bitPosition)
        {
            if (bitPosition > 7) throw new IndexOutOfRangeException("Bit position cannot be larger than 7");
            _dirtyMask |= (byte) (1 << bitPosition);
            NetObject.SetDirty();
        }

        /// <summary>
        /// Sets the bits given by the passed bit mask dirty.
        /// Marks the parenting <see cref="NetObject"/> as dirty.
        /// </summary>
        /// <param name="bitMask"></param>
        protected void SetDirty(byte bitMask)
        {
            _dirtyMask |= bitMask;
            NetObject.SetDirty();
        }

        /// <summary>
        /// Check whether state needs to be synchronized.
        /// </summary>
        /// <returns>True, iff any bit or the authority flag is dirty</returns>
        public bool IsDirty()
        {
            return _dirtyMask != 0 || Server.IsActive && !_authorityDirty;
        }

        /// <summary>
        /// True if the bit at <paramref name="bitPosition"/> is currently set dirty.
        /// </summary>
        public bool IsDirtyAt(byte bitPosition)
        {
            return IsDirtyAt(_dirtyMask, bitPosition);
        }

        /// <summary>
        /// True if the bit at <paramref name="bitPosition"/> is set in the <paramref name="dirtyMask"/>.
        /// </summary>
        public bool IsDirtyAt(byte dirtyMask, byte bitPosition)
        {
            return (dirtyMask & (1 << bitPosition)) == 1 << bitPosition;
        }
    }
}