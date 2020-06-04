using System;

namespace Networking
{
    /// <summary>
    /// Exception thrown for errors during Network communication.
    /// </summary>
    internal class NetException : Exception
    {
        public NetException(string message) : base(message)
        { }

        public NetException(string message, Exception innerException) : base(message, innerException)
        { }
    }
}