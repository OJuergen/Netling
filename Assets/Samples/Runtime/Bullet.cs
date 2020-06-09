using System;
using UnityEngine;

namespace Netling.Samples
{
    public class Bullet : NetBehaviour
    {
        [SerializeField] private float _speed;
        [SerializeField] private float _lifetime;

        private void Start()
        {
            if (Server.IsActive) Destroy(gameObject, _lifetime);
        }

        private void Update()
        {
            if (Server.IsActive)
            {
                transform.position += transform.forward * (_speed * Time.deltaTime);
            }
        }

        public void InitOnServer(Vector3 muzzlePosition, Quaternion muzzleOrientation, float shotServerTime)
        {
            Server.AssertActive();
            transform.position = muzzlePosition +
                                 _speed * (Server.Time - shotServerTime) * (muzzleOrientation * transform.forward);
        }
    }
}