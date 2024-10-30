using Unity.Collections;
using UnityEngine;

namespace Netling.Samples
{
    [CreateAssetMenu(menuName = "Netling/Sample Game Action")]
    public class ShootGameAction : GameAction<ShootGameAction.Parameters>
    {
        public readonly struct Parameters : IParameters
        {
            public Vector3 MuzzlePosition { get; }
            public Quaternion MuzzleOrientation { get; }

            public Parameters(Vector3 muzzlePosition, Quaternion muzzleOrientation)
            {
                MuzzlePosition = muzzlePosition;
                MuzzleOrientation = muzzleOrientation;
            }
        }

        protected override void SerializeParams(ref DataStreamWriter writer, Parameters parameters)
        {
            writer.WriteVector3(parameters.MuzzlePosition);
            writer.WriteQuaternion(parameters.MuzzleOrientation);
        }

        protected override Parameters DeserializeParams(ref DataStreamReader reader)
        {
            Vector3 muzzlePosition = reader.ReadVector3();
            Quaternion muzzleOrientation = reader.ReadQuaternion();
            return new Parameters(muzzlePosition, muzzleOrientation);
        }

        public void Trigger(Vector3 muzzlePosition, Quaternion muzzleOrientation)
        {
            Trigger(new Parameters(muzzlePosition, muzzleOrientation));
        }

        protected override bool IsValid(Parameters parameters, int clientID, float triggerTime)
        {
            Player player = PlayerManager.Instance.Get(clientID);
            if (player == null) return false;
            var gunController = player.GetComponent<GunController>();
            if (gunController == null) return false;
            return gunController.Ammo > 0;
        }

        protected override void Execute(Parameters parameters, int clientID, float triggerTime)
        {
            Player player = PlayerManager.Instance.Get(clientID);
            var gunController = player.GetComponent<GunController>();
            GameObject bulletContainer = GameObject.Find("BulletContainer") ?? new GameObject("BulletContainer");
            if (Server.IsActive)
            {
                Bullet bullet = Server.Instance.SpawnNetObject(gunController.BulletPrefab, default,
                    bulletContainer.transform, parameters.MuzzlePosition, parameters.MuzzleOrientation);
                // initialize position to compensate for latency
                bullet.InitOnServer(parameters.MuzzlePosition, parameters.MuzzleOrientation, triggerTime);
            }

            gunController.Ammo--;
            // todo play shot sound
        }

        protected override void Deny(Parameters parameters, int clientID, float triggerTime)
        {
            // todo play click sound
        }

        protected override void Rollback(Parameters parameters, int clientID, float triggerTime)
        {
            Player player = PlayerManager.Instance.Get(clientID);
            if (player == null) return;
            var gunController = player.GetComponent<GunController>();
            if (gunController == null) return;
            gunController.Ammo++;
            // todo play click sound
        }
    }
}