using System;
using NUnit.Framework;

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
            Server.Instance.Init(new ushort[] {9099}, 100, true, false);
            Server.Instance.Start();
        }

        [Test]
        public void ShouldStartServer()
        {
            StartTestServer();
            Assert.True(Server.IsActive);
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