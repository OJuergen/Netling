using System;
using System.Linq;
using Unity.Collections;
using Unity.Networking.Transport;
using UnityEngine;

namespace Networking
{
    [RequireComponent(typeof(Animator))]
    public class SyncAnimator : NetBehaviour
    {
        private Animator _animator;
        private byte[] _paramBytes;
        private AnimatorControllerParameter[] _animatorControllerParameters;
        private int _currentStateHash;
        private bool _isInTransition;
        private float _currentEnterTime;

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            _animatorControllerParameters = _animator.parameters;
            _paramBytes = SerializeParams();
        }

        private void Update()
        {
            if (!HasAuthority) return;
            byte[] paramBytes = SerializeParams();
            if (_paramBytes == null || !paramBytes.SequenceEqual(_paramBytes))
            {
                _paramBytes = paramBytes;
                SetDirtyAt(0);
            }

            AnimatorStateInfo currentStateInfo = _animator.GetCurrentAnimatorStateInfo(0);
            if (currentStateInfo.fullPathHash != _currentStateHash)
            {
                _currentStateHash = currentStateInfo.fullPathHash;
                _currentEnterTime = Server.Time - currentStateInfo.normalizedTime * currentStateInfo.length;
                SetDirtyAt(1);
                SendRPC(nameof(ChangeCurrentStateRPC), _currentStateHash, _currentEnterTime, SerializeParams());
            }

            bool isInTransition = _animator.IsInTransition(0);
            if (isInTransition != _isInTransition)
            {
                _isInTransition = isInTransition;
                if (_isInTransition)
                {
                    AnimatorStateInfo nextStateInfo = _animator.GetNextAnimatorStateInfo(0);
                    AnimatorTransitionInfo animatorTransitionInfo = _animator.GetAnimatorTransitionInfo(0);
                    float nextEnterTime = Server.Time - nextStateInfo.normalizedTime * nextStateInfo.length;
                    float transitionDuration = animatorTransitionInfo.durationUnit == DurationUnit.Fixed
                        ? animatorTransitionInfo.duration
                        : animatorTransitionInfo.duration * currentStateInfo.length;
                    SendRPC(nameof(TransitionRPC), nextStateInfo.fullPathHash, nextEnterTime, transitionDuration,
                        SerializeParams());
                }
            }
        }

        [MufflonRPC]
        private void ChangeCurrentStateRPC(int stateHash, float enterServerTime, byte[] paramBytes)
        {
            if (HasAuthority) return;
            if (stateHash == _animator.GetCurrentAnimatorStateInfo(0).fullPathHash)
                _animator.PlayInFixedTime(0, 0, Server.Time - enterServerTime);
            else
                _animator.PlayInFixedTime(stateHash, 0, Server.Time - enterServerTime);
            DeserializeParameters(paramBytes);
        }

        [MufflonRPC]
        private void TransitionRPC(int stateHash, float enterServerTime, float transitionDuration, byte[] paramBytes)
        {
            if (HasAuthority) return;
            if (stateHash != _animator.GetCurrentAnimatorStateInfo(0).fullPathHash)
                _animator.CrossFadeInFixedTime(stateHash, transitionDuration, 0, Server.Time - enterServerTime);
            else
                _animator.CrossFadeInFixedTime(0, transitionDuration, 0, Server.Time - enterServerTime);

            DeserializeParameters(paramBytes);
        }
        
        
        private byte[] SerializeParams()
        {
            var writer = new DataStreamWriter(1000, Allocator.Temp);
            for (var i = 0; i < _animator.parameterCount; i++)
            {
                switch (_animatorControllerParameters[i].type)
                {
                    case AnimatorControllerParameterType.Float:
                        writer.WriteFloat(_animator.GetFloat(_animatorControllerParameters[i].nameHash));
                        break;
                    case AnimatorControllerParameterType.Int:
                        writer.WriteInt(_animator.GetInteger(_animatorControllerParameters[i].nameHash));
                        break;
                    case AnimatorControllerParameterType.Bool:
                    case AnimatorControllerParameterType.Trigger:
                        writer.WriteBool(_animator.GetBool(_animatorControllerParameters[i].nameHash));
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            return writer.AsNativeArray().ToArray();
        }
        
        private void DeserializeParameters(byte[] paramBytes)
        {
            var index = 0;
            for (var i = 0; i < _animator.parameterCount; i++)
            {
                switch (_animatorControllerParameters[i].type)
                {
                    case AnimatorControllerParameterType.Float:
                        _animator.SetFloat(_animatorControllerParameters[i].nameHash,
                            BitConverter.ToSingle(paramBytes, index));
                        index += 4;
                        break;
                    case AnimatorControllerParameterType.Int:
                        _animator.SetInteger(_animatorControllerParameters[i].nameHash,
                            BitConverter.ToInt32(paramBytes, index));
                        index += 4;
                        break;
                    case AnimatorControllerParameterType.Bool:
                    case AnimatorControllerParameterType.Trigger:
                        _animator.SetBool(_animatorControllerParameters[i].nameHash, paramBytes[index] != 0);
                        index += 1;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        protected override void Serialize(ref DataStreamWriter writer, byte dirtyMask)
        {
            if (IsDirtyAt(dirtyMask, 0))
            {
                writer.WriteInt(_paramBytes.Length);
                writer.WriteBytes(new NativeArray<byte>(_paramBytes, Allocator.Temp));
            }
            if (IsDirtyAt(dirtyMask, 1))
            {
                writer.WriteInt(_currentStateHash);
                writer.WriteFloat(_currentEnterTime);
            }
        }

        protected override void Deserialize(ref DataStreamReader reader, byte dirtyMask)
        {
            if (IsDirtyAt(dirtyMask, 0))
            {
                int byteCount = reader.ReadInt();
                var bytes = new NativeArray<byte>(byteCount, Allocator.Temp);
                reader.ReadBytes(bytes);
                _paramBytes = bytes.ToArray();
                bytes.Dispose();
                DeserializeParameters(_paramBytes);
            }

            if (IsDirtyAt(dirtyMask, 1))
            {
                _currentStateHash = reader.ReadInt();
                _currentEnterTime = reader.ReadFloat();
                if (_currentStateHash == _animator.GetCurrentAnimatorStateInfo(0).fullPathHash)
                    _animator.PlayInFixedTime(0, 0, Server.Time - _currentEnterTime);
                else
                    _animator.PlayInFixedTime(_currentStateHash, 0, Server.Time - _currentEnterTime);
            }
        }
    }
}