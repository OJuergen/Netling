using Unity.Networking.Transport;

namespace Networking
{
    public interface IStreamSerializable
    {
        void Serialize(ref DataStreamWriter writer);
        void Deserialize(ref DataStreamReader reader);
    }
}