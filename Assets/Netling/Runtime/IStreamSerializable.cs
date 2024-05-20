

using Unity.Collections;

namespace Netling
{
    public interface IStreamSerializable
    {
        void Serialize(ref DataStreamWriter writer);
        void Deserialize(ref DataStreamReader reader);
    }
}