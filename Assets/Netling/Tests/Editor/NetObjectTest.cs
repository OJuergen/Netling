﻿using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Netling.Tests
{
    public class NetObjectTest
    {
        [Test]
        public void ShouldCreateNetObjectInstance()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<NetObject>("Assets/Netling/Tests/Editor/NetObject.prefab");
            NetObject instance = prefab.Create(0, 0, 0, Vector3.zero, Quaternion.identity);
            Assert.NotNull(instance);
        }

        [Test]
        public void ShouldInitializeNetObjectInstance()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<NetObject>("Assets/Netling/Tests/Editor/NetObject.prefab");
            const int netID = 7;
            const int prefabIndex = 3;
            const int ownerActorNumber = 9;
            NetObject instance = prefab.Create(netID, prefabIndex, ownerActorNumber, Vector3.zero, Quaternion.identity);
            Assert.NotNull(instance);
            Assert.AreEqual(netID, instance.ID);
            Assert.AreEqual(prefabIndex, instance.PrefabIndex);
            Assert.AreEqual(ownerActorNumber, instance.OwnerActorNumber);
        }
    }
}