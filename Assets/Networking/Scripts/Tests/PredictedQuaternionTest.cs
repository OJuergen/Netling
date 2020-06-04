using NUnit.Framework;
using UnityEngine;

namespace Networking.Tests
{
    public class PredictedQuaternionTest
    {
        [Test]
        public void ShouldPredictRotation()
        {
            RunTest(100, Quaternion.identity,
                200, Quaternion.LookRotation(Vector3.right),
                300, Quaternion.LookRotation(Vector3.back));

            RunTest(100, Quaternion.identity,
                200, Quaternion.LookRotation(Vector3.right),
                500, Quaternion.identity);

            RunTest(100, Quaternion.identity,
                200, Quaternion.LookRotation(Vector3.up, Vector3.back),
                300, Quaternion.LookRotation(Vector3.back, Vector3.down));

            RunTest(100, Quaternion.identity,
                200, Quaternion.LookRotation(Vector3.up),
                500, Quaternion.identity);

            RunTest(100, Quaternion.LookRotation(Vector3.right),
                200, Quaternion.LookRotation(Vector3.up, Vector3.left),
                300, Quaternion.LookRotation(Vector3.left, Vector3.down));

            RunTest(100, Quaternion.LookRotation(Vector3.right),
                200, Quaternion.LookRotation(Vector3.up, Vector3.left),
                900, Quaternion.LookRotation(Vector3.right));

            RunTest(1, Quaternion.identity,
                2, Quaternion.LookRotation(Vector3.forward, Vector3.right),
                3, Quaternion.LookRotation(Vector3.forward, Vector3.down));
        }

        private static void RunTest(ulong t1, Quaternion rot1, ulong t2, Quaternion rot2, ulong t3,
                                    Quaternion expectation)
        {
            var predictedQuaternion = new PredictedQuaternion();
            predictedQuaternion.ReceiveValue(t1, rot1);
            predictedQuaternion.ReceiveValue(t2, rot2);
            Assert.AreEqual(0, Quaternion.Angle(expectation, predictedQuaternion.Get(t3)));
        }
    }
}