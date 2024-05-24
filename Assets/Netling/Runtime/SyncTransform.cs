using System;
using Unity.Collections;
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
        [SerializeField] private Space _space = Space.World;

        private Transform _transform;
        private Vector3 _latestPosition;
        private Quaternion _latestRotation;
        private readonly PredictedVector3 _predictedPosition = new();
        private readonly PredictedQuaternion _predictedRotation = new();
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
            _transform = transform;
            _latestPosition = _space == Space.World ? _transform.position : _transform.localPosition;
            _latestRotation = _space == Space.World ? _transform.rotation : _transform.localRotation;
        }

        private void Update()
        {

            // submit
            if (HasAuthority)
            {
                _lastUpdateTime = Server.Time;
                _latestPosition = _space == Space.World ? _transform.position : _transform.localPosition;
                _latestRotation = _space == Space.World ? _transform.rotation : _transform.localRotation;
                _predictedPosition.ReceiveValue(_lastUpdateTime, _latestPosition);
                _predictedRotation.ReceiveValue(_lastUpdateTime, _latestRotation);
                SetDirty((byte)DirtyMask.Position);
                SetDirty((byte)DirtyMask.Rotation);
            }
            // receive
            else if (!Hold) UpdateTransform();
        }

        private void UpdateTransform()
        {
            Vector3 position = _space == Space.World ? _transform.position : _transform.localPosition;
            Quaternion rotation = _space == Space.World ? _transform.rotation : _transform.localRotation;
            switch (_mode)
            {
                case Mode.Direct:
                {
                    position = _latestPosition;
                    rotation = _latestRotation;
                    break;
                }
                case Mode.Lerp:
                    position = Vector3.Lerp(position, _latestPosition, _lerpFactor * Time.deltaTime);
                    rotation = Quaternion.Lerp(rotation, _latestRotation, _lerpFactor * Time.deltaTime);

                    if (Vector3.Distance(position, _latestPosition) > _jumpThreshold)
                    {
                        position = _latestPosition;
                        rotation = _latestRotation;
                    }

                    break;
                case Mode.Predicted:
                {
                    position = _predictedPosition.Get(Server.Time);
                    rotation = _predictedRotation.Get(Server.Time);
                    break;
                }
                case Mode.PredictedLerp:
                {
                    if (!_predictedPosition.ReceivedOnce) break;
                    Vector3 predictedPosition = _predictedPosition.Get(Server.Time);
                    Quaternion predictedRotation = _predictedRotation.Get(Server.Time);
                    if (!_predictedPosition.ReceivedTwice
                        || Vector3.Distance(position, predictedPosition) > _jumpThreshold)
                    {
                        position = predictedPosition;
                        rotation = predictedRotation;
                        break;
                    }

                    position = Vector3.Lerp(position, predictedPosition, _lerpFactor * Time.deltaTime);
                    rotation = Quaternion.Lerp(rotation, predictedRotation, _lerpFactor * Time.deltaTime);
                    break;
                }
                default:
                    enabled = false;
                    throw new Exception($"Unknown client side prediction mode {_mode}. Disabled script.");
            }

            if (_space == Space.World)
            {
                _transform.position = position;
                _transform.rotation = rotation;
            }
            else
            {
                _transform.localPosition = position;
                _transform.localRotation = rotation;
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
            var mask = (DirtyMask)dirtyMask;
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
            var mask = (DirtyMask)dirtyMask;
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

            Vector3 latestPosition = _space == Space.World || _transform.parent == null
                ? _latestPosition
                : _transform.parent.TransformPoint(_latestPosition);
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(latestPosition, 0.1f);
            
            Vector3 predictedPosition = _space == Space.World || _transform.parent == null
                ? _predictedPosition.Get(Server.Time)
                : _transform.parent.TransformPoint(_predictedPosition.Get(Server.Time));
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(predictedPosition, 0.1f);
            
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, 0.1f);
        }
    }
}