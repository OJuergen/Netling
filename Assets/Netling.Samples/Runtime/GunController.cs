using UnityEngine;

namespace Netling.Samples
{
    public class GunController : NetBehaviour
    {
        [SerializeField] private Player _player;
        [SerializeField] private ShootGameAction _shootGameAction;
        [SerializeField] private int _maximumAmmo;
        [SerializeField] private Transform _muzzle;
        [SerializeField] private Bullet _bulletPrefab;
        public int Ammo { get; set; }
        public Bullet BulletPrefab => _bulletPrefab;

        private void Start()
        {
            Ammo = _maximumAmmo;
        }

        private void Update()
        {
            if (_player.IsLocal && Input.GetKeyDown(KeyCode.Space))
            {
                _shootGameAction.Trigger(_muzzle.position, _muzzle.rotation);
            }

            if (_player.IsLocal && Input.GetKeyDown(KeyCode.R))
            {
                Reload();
                SendRPC(nameof(Reload));
            }
        }

        [NetlingRPC]
        private void Reload()
        {
            Ammo = _maximumAmmo;
        }
    }
}