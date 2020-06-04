using System;
using NUnit.Framework;

namespace Networking.Tests
{
    public class ServerTest
    {
        [Test]
        public void ShouldStartServer()
        {
            Server.Instance.Init(new ushort[] {9099}, 100, true, false);
            Server.Instance.Start();
            Assert.True(Server.IsActive);
        }

        [Test]
        public void ShouldNotStartUninitializedServer()
        {
            Assert.Throws<InvalidOperationException>(() => Server.Instance.Start());
        }
    }
}