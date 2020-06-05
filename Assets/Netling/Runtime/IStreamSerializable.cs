using Unity.Networking.Transport;

namespace Netling
{
    public interface IStreamSerializable
    {
        void Serialize(ref DataStreamWriter writer);
        void Deserialize(ref DataStreamReader reader);
    }
}