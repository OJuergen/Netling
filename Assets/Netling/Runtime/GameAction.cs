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

        public abstract void ReceiveOnClient(IParameters parameters, bool valid, int actorNumber, float triggerTime);

        public abstract void ReceiveOnServer(IParameters parameters, int actorNumber, int senderActorNumber,
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
                TriggerOnServer(parameters, Client.IsHost ? Client.Instance.ActorNumber : Server.ServerActorNumber);
            else
                TriggerOnClient(parameters);
        }

        protected void Trigger(T parameters, int actorNumber)
        {
            if (Server.IsActive) TriggerOnServer(parameters, actorNumber);
            else TriggerOnClient(parameters);
        }

        private void TriggerOnServer(T parameters, int actorNumber)
        {
            Server.AssertActive();
            if (IsValid(parameters, actorNumber, Server.Time))
            {
                Execute(parameters, actorNumber, Server.Time);
                Server.Instance.SendGameAction(this, parameters, actorNumber, Server.Time);
            }
            else
            {
                Deny(parameters, actorNumber, Server.Time);
            }
        }

        private void TriggerOnClient(T parameters)
        {
            if (IsValid(parameters, Client.Instance.ActorNumber, Server.Time))
            {
                if (Optimistic) Execute(parameters, Client.Instance.ActorNumber, Server.Time);

                if (Client.IsConnected) Client.Instance.SendGameAction(this, parameters);
                else Debug.LogWarning("Cannot trigger game action: client not connected");
            }
            else Deny(parameters, Client.Instance.ActorNumber, Server.Time);
        }

        public override void ReceiveOnClient(IParameters parameters, bool valid, int actorNumber, float triggerTime)
        {
            if (!(parameters is T tParameter))
                throw new ArgumentException(
                    $"Received parameters of wrong type {parameters.GetType()} and expected {typeof(T)}");

            if (valid)
            {
                if (!Optimistic || Client.Instance.ActorNumber != actorNumber)
                    Execute(tParameter, actorNumber, triggerTime);
            }
            else
            {
                if (Optimistic) Rollback(tParameter, actorNumber, triggerTime);
                else Deny(tParameter, actorNumber, triggerTime);
            }
        }

        public override void ReceiveOnServer(IParameters parameters, int actorNumber, int senderActorNumber,
            float triggerTime)
        {
            if (!(parameters is T tParameter))
                throw new ArgumentException(
                    $"Received parameters of wrong type {parameters.GetType()} and expected {typeof(T)}");

            if (!Server.IsActive)
            {
                Debug.LogWarning($"Cannot handle event {parameters} by {senderActorNumber}: Server inactive");
                return;
            }

            bool valid = IsValid(tParameter, actorNumber, triggerTime);
            // todo maybe kick sender, if sender not actor or trigger time in the future?
            if (valid && senderActorNumber == actorNumber && triggerTime <= Server.Time)
            {
                if (!Client.IsConnected || Client.Instance.ActorNumber != senderActorNumber || !Optimistic)
                    Execute(tParameter, actorNumber, triggerTime);
                Server.Instance.SendGameAction(this, parameters, actorNumber, triggerTime);
            }
            else Server.Instance.DenyGameAction(this, parameters, senderActorNumber, triggerTime);
        }

        protected abstract bool IsValid(T parameters, int actorNumber, float triggerTime);
        protected abstract void Execute(T parameters, int actorNumber, float triggerTime);
        protected abstract void Deny(T parameters, int actorNumber, float triggerTime);
        protected abstract void Rollback(T parameters, int actorNumber, float triggerTime);
    }
}