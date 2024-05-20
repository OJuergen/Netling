using System;
using System.Linq;
using MufflonUtil;
using NSubstitute;
using NUnit.Framework;
using Unity.Networking.Transport;
using UnityEditor;

namespace Netling.Tests
{
    public class ServerTest
    {
        [TearDown]
        public void TearDown()
        {
            foreach (string path in AssetDatabase.FindAssets("t:ScriptableObjectSingleton")
                .Select(AssetDatabase.GUIDToAssetPath))
            {
                AssetDatabase.LoadAssetAtPath<ScriptableObjectSingleton>(path);
            }
            if (Server.IsActive) Server.Instance.Stop();
            Server.Instance.Dispose();
            Client.Instance.Dispose();
        }

        [Test]
        public void ShouldStartServer()
        {
            var networkInterface = Substitute.For<INetworkInterface>();
            Server.Instance.Init(new ushort[] {9099}, 100, true, false);
            var serverDriver = NetworkDriver.Create(networkInterface);
            Server.Instance.Start(serverDriver);
            Assert.True(Server.IsActive);
            Assert.True(serverDriver.Listening);
        }

        [Test]
        public void ShouldAcceptConnection()
        {
            Server.Instance.Init(new ushort[] {9099}, 100, true, false);
            var serverDriver = NetworkDriver.Create(new IPCNetworkInterface());
            Server.Instance.Start(serverDriver);

            // send out connection request
            var clientDriver = NetworkDriver.Create(new IPCNetworkInterface());
            NetworkConnection networkConnection = clientDriver.Connect(serverDriver.LocalEndPoint());
            Assert.True(networkConnection != default);
            clientDriver.ScheduleUpdate().Complete();

            var numberOfConnectedClients = 0;
            var onClientConnected = new Server.ConnectionDelegate(_ => numberOfConnectedClients++);
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
            var serverDriver = NetworkDriver.Create(networkInterface);
            Assert.Throws<NetException>(() => Server.Instance.Start(serverDriver));
        }

        [Test]
        public void ShouldNotStartServerTwice()
        {
            var networkInterface = Substitute.For<INetworkInterface>();
            Server.Instance.Init(new ushort[] {9099}, 100, true, false);
            var serverDriver = NetworkDriver.Create(networkInterface);
            Server.Instance.Start(serverDriver);
            Assert.Throws<InvalidOperationException>(() => Server.Instance.Start());
        }

        [Test]
        public void ShouldStopServer()
        {
            var networkInterface = Substitute.For<INetworkInterface>();
            Server.Instance.Init(new ushort[] {9099}, 100, true, false);
            var serverDriver = NetworkDriver.Create(networkInterface);
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