using System;
using Unity.Jobs;
using Unity.Networking.Transport;

namespace Netling.Tests
{
    /// <summary>
    /// See https://docs.unity3d.com/Packages/com.unity.transport@2.2/api/Unity.Networking.Transport.INetworkInterface.html
    /// </summary>
    public struct MockNetworkInterface : INetworkInterface
    {
        public int ReturnOnBind;
        
        public void Dispose()
        {
        }

        public int Initialize(ref NetworkSettings settings, ref int packetPadding)
        {
            return 0;
        }

        public JobHandle ScheduleReceive(ref ReceiveJobArguments arguments, JobHandle dep)
        {
            return dep;
        }

        public JobHandle ScheduleSend(ref SendJobArguments arguments, JobHandle dep)
        {
            return dep;
        }

        public int Bind(NetworkEndpoint endpoint)
        {
            return ReturnOnBind;
        }

        public int Listen()
        {
            return 0;
        }

        public NetworkEndpoint LocalEndpoint  => default;
    }
}