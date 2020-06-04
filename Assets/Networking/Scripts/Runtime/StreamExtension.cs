using System;
using JetBrains.Annotations;
using Unity.Collections;
using Unity.Networking.Transport;
using UnityEngine;

namespace Networking
{
    public static class StreamExtension
    {
        public static void WriteBool(ref this DataStreamWriter writer, bool b)
        {
            writer.WriteByte((byte) (b ? 1 : 0));
        }

        public static void WriteManagedString(ref this DataStreamWriter writer, [NotNull] string str)
        {
            if (str == null) throw new ArgumentNullException(nameof(str));
            writer.WriteInt(str.Length);
            foreach (char c in str)
            {
                writer.WriteShort((short) c);
            }
        }

        public static void WriteVector3(ref this DataStreamWriter writer, Vector3 vector3)
        {
            writer.WriteFloat(vector3.x);
            writer.WriteFloat(vector3.y);
            writer.WriteFloat(vector3.z);
        }

        public static void WriteQuaternion(ref this DataStreamWriter writer, Quaternion quaternion)
        {
            writer.WriteFloat(quaternion.x);
            writer.WriteFloat(quaternion.y);
            writer.WriteFloat(quaternion.z);
            writer.WriteFloat(quaternion.w);
        }

        public static void WriteObjects(ref this DataStreamWriter writer, object[] objects, Type[] types)
        {
            if (types.Length != objects.Length)
                throw new NetException("Cannot serialize objects: wrong number of arguments");
            for (var i = 0; i < types.Length; i++)
            {
                Type type = types[i];
                if (type == typeof(int)) writer.WriteInt((int) objects[i]);
                else if (type == typeof(uint)) writer.WriteUInt((uint) objects[i]);
                else if (type == typeof(bool)) writer.WriteBool((bool) objects[i]);
                else if (type == typeof(byte)) writer.WriteByte((byte) objects[i]);
                else if (type == typeof(byte[]))
                {
                    writer.WriteInt(((byte[]) objects[i]).Length);
                    var bytes = new NativeArray<byte>((byte[]) objects[i], Allocator.Temp);
                    writer.WriteBytes(bytes);
                    bytes.Dispose();
                }
                else if (type == typeof(short)) writer.WriteShort((short) objects[i]);
                else if (type == typeof(ushort)) writer.WriteUShort((ushort) objects[i]);
                else if (type == typeof(char)) writer.WriteShort((short) objects[i]);
                else if (type == typeof(float)) writer.WriteFloat((float) objects[i]);
                else if (type == typeof(string)) writer.WriteManagedString((string) objects[i]);
                else if (type == typeof(Vector3)) writer.WriteVector3((Vector3) objects[i]);
                else if (type == typeof(Quaternion)) writer.WriteQuaternion((Quaternion) objects[i]);
                else if (typeof(IStreamSerializable).IsAssignableFrom(type))
                    ((IStreamSerializable) objects[i]).Serialize(ref writer);
                else throw new NetException($"Cannot serialize rpc argument of type {type}");
            }
        }

        public static bool ReadBool(ref this DataStreamReader reader)
        {
            return reader.ReadByte() != 0;
        }

        /// <summary>
        /// Reads a <see cref="string"/> from the stream, corresponding to
        /// <see cref="WriteManagedString"/>.
        /// <br/><br/>
        /// As opposed to <see cref="DataStreamReader.ReadString()"/>, this reads
        /// a <see cref="string"/> (maximum length <see cref="int.MaxValue"/>) and not a <see cref="NativeString64"/>.
        /// </summary>
        public static string ReadManagedString(ref this DataStreamReader reader)
        {
            int length = reader.ReadInt();
            var chars = new char[length];
            for (var i = 0; i < length; i++)
                chars[i] = (char) reader.ReadShort();
            return new string(chars);
        }

        public static Vector3 ReadVector3(ref this DataStreamReader reader)
        {
            return new Vector3(
                reader.ReadFloat(),
                reader.ReadFloat(),
                reader.ReadFloat());
        }

        public static Quaternion ReadQuaternion(ref this DataStreamReader reader)
        {
            return new Quaternion(
                reader.ReadFloat(),
                reader.ReadFloat(),
                reader.ReadFloat(),
                reader.ReadFloat());
        }

        public static object[] ReadObjects(ref this DataStreamReader reader, Type[] types)
        {
            var objects = new object[types.Length];
            for (var i = 0; i < types.Length; i++)
            {
                Type type = types[i];
                if (type == typeof(int)) objects[i] = reader.ReadInt();
                else if (type == typeof(bool)) objects[i] = reader.ReadBool();
                else if (type == typeof(uint)) objects[i] = reader.ReadUInt();
                else if (type == typeof(byte)) objects[i] = reader.ReadByte();
                else if (type == typeof(byte[]))
                {
                    var bytes = new NativeArray<byte>(reader.ReadInt(), Allocator.Temp);
                    reader.ReadBytes(bytes);
                    objects[i] = bytes.ToArray();
                    bytes.Dispose();
                }
                else if (type == typeof(short)) objects[i] = reader.ReadShort();
                else if (type == typeof(ushort)) objects[i] = reader.ReadUShort();
                else if (type == typeof(char)) objects[i] = (char) reader.ReadUShort();
                else if (type == typeof(float)) objects[i] = reader.ReadFloat();
                else if (type == typeof(string)) objects[i] = reader.ReadManagedString();
                else if (type == typeof(Vector3)) objects[i] = reader.ReadVector3();
                else if (type == typeof(Quaternion)) objects[i] = reader.ReadQuaternion();
                else if (typeof(IStreamSerializable).IsAssignableFrom(type))
                {
                    objects[i] = Activator.CreateInstance(type);
                    ((IStreamSerializable) objects[i]).Deserialize(ref reader);
                }
                else throw new NetException($"Cannot deserialize object of type {type}");
            }

            return objects;
        }

        public static void DiscardBytes(ref this DataStreamReader reader, int byteCount)
        {
            var bytes = new NativeArray<byte>(byteCount, Allocator.Temp);
            reader.ReadBytes(bytes);
            bytes.Dispose();
        }
    }
}