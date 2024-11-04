using Unity.Collections;

namespace Netling
{
    public record ClientID()
    {
        public int Value { get; }
        public bool IsServer => Value == 1;
        public bool IsValid => Value > 0;

        private ClientID(int value) : this()
        {
            Value = value;
        }

        public static ClientID Invalid => new(0);
        public static ClientID Server => new(1);
        public static ClientID FirstValidClient => new(2);

        public static ClientID operator ++(ClientID clientID)
        {
            return new ClientID(clientID.Value + 1);
        }

        public void Serialize(ref DataStreamWriter writer)
        {
            writer.WriteInt(Value);
        }

        public static ClientID Deserialize(ref DataStreamReader reader)
        {
            return new ClientID(reader.ReadInt());
        }
    }
}