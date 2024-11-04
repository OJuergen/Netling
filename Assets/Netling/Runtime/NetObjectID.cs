using Unity.Collections;

namespace Netling
{
    /// <summary>
    /// Typesafe int wrapper for net object identifiers.
    /// </summary>
    /// <param name="Value">The wrapped int value.</param>
    public record NetObjectID(int Value)
    {
        public void Serialize(ref DataStreamWriter writer)
        {
            writer.WriteInt(Value);
        }

        public static NetObjectID Deserialize(ref DataStreamReader reader)
        {
            return new NetObjectID(reader.ReadInt());
        }

        public static NetObjectID operator ++(NetObjectID netObjectID)
        {
            return new NetObjectID(netObjectID.Value + 1);
        }
    }
}