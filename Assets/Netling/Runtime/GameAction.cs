using System;
using Unity.Collections;
using UnityEngine;

namespace Netling
{
    public abstract class GameAction : NetAsset
    {
        [SerializeField] private bool _optimistic;
        protected bool Optimistic => _optimistic;

        public interface IParameters
        { }

        public abstract void SerializeParameters(ref DataStreamWriter writer, IParameters parameters);
        public abstract IParameters DeserializeParameters(ref DataStreamReader reader);

        public abstract void ReceiveOnClient(IParameters parameters, bool valid, ClientID clientID, float triggerTime);

        public abstract void ReceiveOnServer(IParameters parameters, ClientID clientID, ClientID senderClientID,
            float triggerTime);
    }

    public abstract class GameAction<T> : GameAction where T : struct, GameAction.IParameters
    {
        public override void SerializeParameters(ref DataStreamWriter writer, IParameters parameters)
        {
            if (!(parameters is T t))
                throw new ArgumentException($"Cannot serialize parameters of wrong type {parameters.GetType()}");
            SerializeParams(ref writer, t);
        }

        protected abstract void SerializeParams(ref DataStreamWriter writer, T parameters);

        public sealed override IParameters DeserializeParameters(ref DataStreamReader reader)
        {
            return DeserializeParams(ref reader);
        }

        protected abstract T DeserializeParams(ref DataStreamReader reader);

        protected void Trigger(T parameters)
        {
            if (Server.IsActive)
            {
                ClientID clientID = Client.Instance.IsHost ? Client.Instance.ID : Server.ServerClientID;
                TriggerOnServer(parameters, clientID);
            }
            else
                TriggerOnClient(parameters);
        }

        protected void Trigger(T parameters, ClientID clientID)
        {
            if (Server.IsActive) TriggerOnServer(parameters, clientID);
            else TriggerOnClient(parameters);
        }

        private void TriggerOnServer(T parameters, ClientID clientID)
        {
            Server.AssertActive();
            if (IsValid(parameters, clientID, Server.Time))
            {
                Execute(parameters, clientID, Server.Time);
                Server.Instance.SendGameAction(this, parameters, clientID, Server.Time);
            }
            else
            {
                Deny(parameters, clientID, Server.Time);
            }
        }

        private void TriggerOnClient(T parameters)
        {
            if (IsValid(parameters, Client.Instance.ID, Server.Time))
            {
                if (Optimistic) Execute(parameters, Client.Instance.ID, Server.Time);

                if (Client.Instance.IsConnected) Client.Instance.SendGameAction(this, parameters);
                else Debug.LogWarning("Cannot trigger game action: client not connected");
            }
            else Deny(parameters, Client.Instance.ID, Server.Time);
        }

        public override void ReceiveOnClient(IParameters parameters, bool valid, ClientID clientID, float triggerTime)
        {
            if (!(parameters is T tParameter))
                throw new ArgumentException(
                    $"Received parameters of wrong type {parameters.GetType()} and expected {typeof(T)}");

            if (valid)
            {
                if (!Optimistic || Client.Instance.ID != clientID)
                    Execute(tParameter, clientID, triggerTime);
            }
            else
            {
                if (Optimistic) Rollback(tParameter, clientID, triggerTime);
                else Deny(tParameter, clientID, triggerTime);
            }
        }

        public override void ReceiveOnServer(IParameters parameters, ClientID clientID, ClientID senderClientID,
            float triggerTime)
        {
            if (!(parameters is T tParameter))
                throw new ArgumentException(
                    $"Received parameters of wrong type {parameters.GetType()} and expected {typeof(T)}");

            if (!Server.IsActive)
            {
                Debug.LogWarning($"Cannot handle event {parameters} by {senderClientID}: Server inactive");
                return;
            }

            bool valid = IsValid(tParameter, clientID, triggerTime);
            // todo maybe kick sender, if sender not client or trigger time in the future?
            if (valid && senderClientID == clientID && triggerTime <= Server.Time)
            {
                if (!Client.Instance.IsConnected || Client.Instance.ID != senderClientID || !Optimistic)
                    Execute(tParameter, clientID, triggerTime);
                Server.Instance.SendGameAction(this, parameters, clientID, triggerTime);
            }
            else Server.Instance.DenyGameAction(this, parameters, senderClientID, triggerTime);
        }

        protected abstract bool IsValid(T parameters, ClientID clientID, float triggerTime);
        protected abstract void Execute(T parameters, ClientID clientID, float triggerTime);
        protected abstract void Deny(T parameters, ClientID clientID, float triggerTime);
        protected abstract void Rollback(T parameters, ClientID clientID, float triggerTime);
    }
}