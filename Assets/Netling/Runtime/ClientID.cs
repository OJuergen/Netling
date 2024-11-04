using System;

namespace Netling
{
    public record ClientID()
    {
        public int Value { get; }
        public bool IsServer => Value == 1;
        public bool IsValid => Value > 0;

        internal ClientID(int value) : this()
        {
            Value = value;
        }

        public static ClientID Invalid => new(0);
        public static ClientID Server => new(1);

        /// <summary>
        /// Create a ClientID for an actual connected client from a value larger than 1.
        /// 0 is reserved for invalid/disconnected, and 1 is reserved for server.
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        public static ClientID Create(int id)
        {
            if (id <= 1) throw new ArgumentException("client id must be larger than 1");
            return new ClientID(id);
        }

        public static ClientID operator ++(ClientID clientID)
        {
            return new ClientID(clientID.Value + 1);
        }
    }
}