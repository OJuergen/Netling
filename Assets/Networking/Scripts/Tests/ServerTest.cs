using System;
using NSubstitute;
using NUnit.Framework;
using Unity.Networking.Transport;

namespace Networking.Tests
{
    public class ServerTest
    {
        [SetUp]
        public void SetUp()
        {
            if (Server.IsActive) Server.Instance.Stop();
            Server.Instance.Dispose();
        }

        [TearDown]
        public void TearDown()
        {
            if (Server.IsActive) Server.Instance.Stop();
            Server.Instance.Dispose();
        }

        private static void StartTestServer()
        {
            var networkInterface = Substitute.For<INetworkInterface>();
            Server.Instance.Init(new ushort[] {9099}, 100, true, false);
            var serverDriver = new NetworkDriver(networkInterface, new NetworkDataStreamParameter {size = 64});
            Server.Instance.Start(serverDriver);
        }

        [Test]
        public void ShouldStartServer()
        {
            StartTestServer();
            Assert.True(Server.IsActive);
        }

        [Test]
        public void ShouldFailToBindToPort()
        {
            var networkInterface = Substitute.For<INetworkInterface>();
            networkInterface.Bind(Arg.Any<NetworkInterfaceEndPoint>()).Returns(-1);
            Server.Instance.Init(new ushort[] {9099}, 100, true, false);
            var serverDriver = new NetworkDriver(networkInterface, new NetworkDataStreamParameter {size = 64});
            Assert.Throws<NetException>(() => Server.Instance.Start(serverDriver));
        }

        [Test]
        public void ShouldNotStartServerTwice()
        {
            StartTestServer();
            Assert.Throws<InvalidOperationException>(() => Server.Instance.Start());
        }

        [Test]
        public void ShouldStopServer()
        {
            StartTestServer();
            Server.Instance.Stop();
            Assert.False(Server.IsActive);
        }

        [Test]
        public void ShouldNotStopStoppedServer()
        {
            Assert.False(Server.IsActive);
            Assert.Throws<InvalidOperationException>(() => Server.Instance.Stop());
        }

        [Test]
        public void ShouldNotStartUninitializedServer()
        {
            Assert.Throws<InvalidOperationException>(() => Server.Instance.Start());
        }
    }
}