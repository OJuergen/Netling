using System;
using System.Linq;
using MufflonUtil;
using NUnit.Framework;
using Unity.Networking.Transport;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Netling.Editor.Tests
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
            var networkInterface = new MockNetworkInterface();
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
            NetworkConnection networkConnection = clientDriver.Connect(serverDriver.GetLocalEndpoint());
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
            var networkInterface = new MockNetworkInterface();
            networkInterface.ReturnOnBind = -1;
            Server.Instance.Init(new ushort[] {9099}, 100, true, false);
            var serverDriver = NetworkDriver.Create(networkInterface);
            Assert.Throws<NetException>(() => Server.Instance.Start(serverDriver));
        }

        [Test]
        public void ShouldNotStartServerTwice()
        {
            var networkInterface = new MockNetworkInterface();
            Server.Instance.Init(new ushort[] {9099}, 100, true, false);
            var serverDriver = NetworkDriver.Create(networkInterface);
            Server.Instance.Start(serverDriver);
            Assert.Throws<InvalidOperationException>(() => Server.Instance.Start());
        }

        [Test]
        public void ShouldStopServer()
        {
            var networkInterface = new MockNetworkInterface();
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

        [Test]
        public void ShouldCreateNetObject()
        {
            var networkInterface = new MockNetworkInterface();
            Server.Instance.Init(new ushort[] {9099}, 100, true, false);
            var serverDriver = NetworkDriver.Create(networkInterface);
            Server.Instance.Start(serverDriver);
            NetObject[] netObjectPrefabs = NetObjectManager.Instance.NetObjectPrefabs;
            NetObject netObject = Server.Instance.SpawnNetObject(netObjectPrefabs[1], default, null, Vector3.zero, Quaternion.identity);
            Assert.NotNull(netObject);
            Assert.AreEqual(0, netObject.ID);
            Assert.AreEqual(1, netObject.PrefabIndex);
            Assert.AreEqual(ClientID.Server, netObject.OwnerClientID);
            Object.DestroyImmediate(netObject.gameObject);
        }
    }
}