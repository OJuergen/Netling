using System;
using Unity.Networking.Transport;
using UnityEngine;

namespace Netling
{
    /// <summary>
    /// Behaviour for synchronizing position and rotation over the network.
    /// </summary>
    public class SyncTransform : NetBehaviour
    {
        [Header("Client Side Prediction")]
        [SerializeField] private Mode _mode = Mode.PredictedLerp;

        [SerializeField] private float _lerpFactor = 10;
        [SerializeField] private float _jumpThreshold = 1;

        private Vector3 _latestPosition;
        private Quaternion _latestRotation;
        private readonly PredictedVector3 _predictedPosition = new PredictedVector3();
        private readonly PredictedQuaternion _predictedRotation = new PredictedQuaternion();
        private float _lastUpdateTime;

        /// <summary>
        /// If true, deserialized values will not be applied to the transform.
        /// </summary>
        public bool Hold { private get; set; }

        public delegate void SyncTransformDelegate(SyncTransform syncTransform);

        public event SyncTransformDelegate Updated;

        [Flags]
        private enum DirtyMask : byte
        {
            Position = 1,
            Rotation = 2
        }

        private enum Mode
        {
            Direct,
            Predicted,
            PredictedLerp,
            Lerp
        }

        private void Awake()
        {
            Transform syncTransform = transform;
            _latestPosition = syncTransform.position;
            _latestRotation = syncTransform.rotation;
        }

        private void Update()
        {
            Transform t = transform;

            // submit
            if (HasAuthority)
            {
                _lastUpdateTime = Server.Time;
                _latestPosition = t.position;
                _latestRotation = t.rotation;
                _predictedPosition.ReceiveValue(_lastUpdateTime, _latestPosition);
                _predictedRotation.ReceiveValue(_lastUpdateTime, _latestRotation);
                SetDirty((byte) DirtyMask.Position);
                SetDirty((byte) DirtyMask.Rotation);
            }
            // receive
            else if (!Hold) UpdateTransform();
        }

        private void UpdateTransform()
        {
            Transform t = transform;
            switch (_mode)
            {
                case Mode.Direct:
                {
                    t.position = _latestPosition;
                    t.rotation = _latestRotation;
                    break;
                }
                case Mode.Lerp:
                    t.position = Vector3.Lerp(t.position, _latestPosition, _lerpFactor * Time.deltaTime);
                    t.rotation = Quaternion.Lerp(t.rotation, _latestRotation, _lerpFactor * Time.deltaTime);

                    if (Vector3.Distance(t.position, _latestPosition) > _jumpThreshold)
                    {
                        t.position = _latestPosition;
                        t.rotation = _latestRotation;
                    }

                    break;
                case Mode.Predicted:
                {
                    t.position = _predictedPosition.Get(Server.Time);
                    t.rotation = _predictedRotation.Get(Server.Time);
                    break;
                }
                case Mode.PredictedLerp:
                {
                    if (!_predictedPosition.ReceivedOnce) break;
                    Vector3 predictedPosition = _predictedPosition.Get(Server.Time);
                    Quaternion predictedRotation = _predictedRotation.Get(Server.Time);
                    if (!_predictedPosition.ReceivedTwice
                        || Vector3.Distance(t.position, predictedPosition) > _jumpThreshold)
                    {
                        t.position = predictedPosition;
                        t.rotation = predictedRotation;
                        break;
                    }

                    t.position = Vector3.Lerp(t.position, predictedPosition, _lerpFactor * Time.deltaTime);
                    t.rotation = Quaternion.Lerp(t.rotation, predictedRotation, _lerpFactor * Time.deltaTime);
                    break;
                }
                default:
                    enabled = false;
                    throw new Exception($"Unknown client side prediction mode {_mode}. Disabled script.");
            }

            Updated?.Invoke(this);
        }

        /// <summary>
        /// Directly sets the position of the object with no interpolated movement.
        /// </summary>
        /// <param name="position"></param>
        public void SetPosition(Vector3 position)
        {
            if (HasAuthority || Server.IsActive)
            {
                SetPositionRPC(position);
                SendRPC(nameof(SetPositionRPC), position);
            }
        }

        [NetlingRPC]
        private void SetPositionRPC(Vector3 position)
        {
            if (Server.IsActive) SendRPC(nameof(SetPositionRPC), position);
            _predictedPosition.Clear();
            _latestPosition = position;
            _predictedPosition.ReceiveValue(Server.Time, position);
            UpdateTransform();
        }

        protected override void Deserialize(ref DataStreamReader reader, byte dirtyMask)
        {
            var mask = (DirtyMask) dirtyMask;
            if (mask != 0)
                _lastUpdateTime = reader.ReadFloat();
            if (mask.HasFlag(DirtyMask.Position))
            {
                _latestPosition = reader.ReadVector3();
                _predictedPosition.ReceiveValue(_lastUpdateTime, _latestPosition);
            }

            if (mask.HasFlag(DirtyMask.Rotation))
            {
                _latestRotation = reader.ReadQuaternion();
                _predictedRotation.ReceiveValue(_lastUpdateTime, _latestRotation);
            }
        }

        protected override void Serialize(ref DataStreamWriter writer, byte dirtyMask)
        {
            var mask = (DirtyMask) dirtyMask;
            if (dirtyMask != 0)
                writer.WriteFloat(_lastUpdateTime);
            if (mask.HasFlag(DirtyMask.Position))
            {
                writer.WriteVector3(_latestPosition);
            }

            if (mask.HasFlag(DirtyMask.Rotation))
            {
                writer.WriteQuaternion(_latestRotation);
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (Server.IsActive || !Application.isPlaying) return;
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(_latestPosition, 0.1f);
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(_predictedPosition.Get(Server.Time), 0.1f);
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, 0.1f);
        }
    }
}