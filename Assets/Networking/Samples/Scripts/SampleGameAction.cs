using System.Diagnostics;
using Unity.Collections;
using Unity.Networking.Transport;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Networking.Samples
{
    [CreateAssetMenu(menuName = "Networking/Sample Game Action")]
    public class SampleGameAction : GameAction<SampleGameAction.Parameters>
    {
        [SerializeField] private int _number;
        [SerializeField] private int _actorNumber;

        public readonly struct Parameters : IParameters
        {
            public int Number { get; }

            public Parameters(int number)
            {
                Number = number;
            }
        }

        protected override void SerializeParams(ref DataStreamWriter writer, Parameters parameters)
        {
            writer.WriteInt(parameters.Number);
        }

        protected override Parameters DeserializeParams(ref DataStreamReader reader)
        {
            return new Parameters(reader.ReadInt());
        }

        public void Trigger()
        {
            Trigger(new Parameters(_number), _actorNumber);
        }

        public void Trigger(int number)
        {
            Trigger(new Parameters(number), _actorNumber);
        }

        protected override bool IsValid(Parameters parameters, int actorNumber, float triggerTime)
        {
            return parameters.Number % 2 == 0;
        }

        protected override void Execute(Parameters parameters, int actorNumber, float triggerTime)
        {
            Debug.Log($"Execute {parameters.Number} for actor {actorNumber}");
        }

        protected override void Deny(Parameters parameters, int actorNumber, float triggerTime)
        {
            Debug.Log($"Deny {parameters.Number} for actor {actorNumber}");
        }

        protected override void Rollback(Parameters parameters, int actorNumber, float triggerTime)
        {
            Debug.Log($"Rollback {parameters.Number} for actor {actorNumber}");
        }

        [MufflonRPC]
        private void TestRPC(int i, string s)
        {
            Debug.Log($"{Time.time}: RPC invoked {i} | {s}");
        }

        [ContextMenu("RPC[0](5, \"test\")")]
        public void TestRPCSerialization()
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            var dataStreamWriter = new DataStreamWriter(1400, Allocator.Temp);
            SerializeRPC(ref dataStreamWriter, nameof(TestRPC), 5, "test");
            var reader = new DataStreamReader(dataStreamWriter.AsNativeArray());
            RPC rpc = DeserializeRPC(ref reader);
            stopwatch.Stop();
            Debug.Log($"Serialization + Deserialization Took {stopwatch.ElapsedMilliseconds} milliseconds.");
            rpc(MessageInfo.ServerNow);
        }

        [ContextMenu("Send TestRPC(5, \"test\")")]
        public void SendRPC()
        {
            SendRPC(nameof(TestRPC), 5, "test");
        }
    }
}