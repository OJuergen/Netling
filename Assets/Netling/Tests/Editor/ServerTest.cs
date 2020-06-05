using System;
using NSubstitute;
using NUnit.Framework;
using Unity.Networking.Transport;

namespace Networking.Tests
{
    public class ServerTest
    {
        [TearDown]
        public void TearDown()
        {
            if (Server.IsActive) Server.Instance.Stop();
            Server.Instance.Dispose();
        }

        [Test]
        public void ShouldStartServer()
        {
            var networkInterface = Substitute.For<INetworkInterface>();
            Server.Instance.Init(new ushort[] {9099}, 100, true, false);
            var serverDriver = new NetworkDriver(networkInterface, new NetworkDataStreamParameter {size = 64});
            Server.Instance.Start(serverDriver);
            Assert.True(Server.IsActive);
            Assert.True(serverDriver.Listening);
        }

        [Test]
        public void ShouldAcceptConnection()
        {
            Server.Instance.Init(new ushort[] {9099}, 100, true, false);
            var serverDriver = new NetworkDriver(new IPCNetworkInterface(), new NetworkDataStreamParameter {size = 64});
            Server.Instance.Start(serverDriver);

            // send out connection request
            var clientDriver = new NetworkDriver(new IPCNetworkInterface(), new NetworkDataStreamParameter {size = 64});
            NetworkConnection networkConnection = clientDriver.Connect(serverDriver.LocalEndPoint());
            Assert.True(networkConnection != default);
            clientDriver.ScheduleUpdate().Complete();

            var numberOfConnectedClients = 0;
            var onClientConnected = new Server.ConnectionDelegate(id => numberOfConnectedClients++);
            Server.Instance.ClientConnected += onClientConnected;
            Server.Instance.Tick();
            Assert.AreEqual(1, numberOfConnectedClients);
            Server.Instance.ClientConnected -= onClientConnected;

            if (clientDriver.IsCreated) clientDriver.Dispose();
        }
        
        [Test]
        public void ShouldHandleDisconnection()
        {
            // start server
            Server.Instance.Init(new ushort[] {9099}, 100, true, false);
            Server.Instance.Start();

            // connect
            Client.Instance.Init(null, 9099, true, 100, false);
            Client.Instance.Connect();
            Server.Instance.Tick();
            Client.Instance.Tick();
            
            // disconnect
            Client.Instance.Disconnect();
            Server.Instance.Tick();
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
            var networkInterface = Substitute.For<INetworkInterface>();
            Server.Instance.Init(new ushort[] {9099}, 100, true, false);
            var serverDriver = new NetworkDriver(networkInterface, new NetworkDataStreamParameter {size = 64});
            Server.Instance.Start(serverDriver);
            Assert.Throws<InvalidOperationException>(() => Server.Instance.Start());
        }

        [Test]
        public void ShouldStopServer()
        {
            var networkInterface = Substitute.For<INetworkInterface>();
            Server.Instance.Init(new ushort[] {9099}, 100, true, false);
            var serverDriver = new NetworkDriver(networkInterface, new NetworkDataStreamParameter {size = 64});
            Server.Instance.Start(serverDriver);
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