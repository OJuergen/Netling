using UnityEngine;

namespace Netling.Samples
{
    public class Bullet : NetBehaviour
    {
        [SerializeField] private float _speed;
        [SerializeField] private float _lifetime;
        private Transform _transform;

        private void Start()
        {
            if (Server.IsActive) Destroy(gameObject, _lifetime);
            _transform = transform;
        }

        private void Update()
        {
            if (Server.IsActive)
            {
                _transform.position += _transform.forward * (_speed * Time.deltaTime);
            }
        }

        public void InitOnServer(Vector3 muzzlePosition, Quaternion muzzleOrientation, float shotServerTime)
        {
            Server.AssertActive();
            _transform.position = muzzlePosition +
                                  _speed * (Server.Time - shotServerTime) * (muzzleOrientation * _transform.forward);
        }
    }
}