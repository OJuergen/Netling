using UnityEngine;

namespace Netling
{
    public abstract class PredictedValue<T>
    {
        protected (float time, T value) Latest { get; private set; }
        private (float time, T value) _previous;
        protected T ValuePerSecond { get; private set; }
        public bool ReceivedOnce { get; private set; }
        public bool ReceivedTwice { get; private set; }

        public void Clear()
        {
            Latest = default;
            _previous = default;
            ValuePerSecond = default;
            ReceivedOnce = false;
            ReceivedTwice = false;
        }

        public void ReceiveValue(float time, T value)
        {
            if (ReceivedOnce) ReceivedTwice = true;
            ReceivedOnce = true;
            _previous = Latest;
            Latest = (time, value);
            if (time <= _previous.time) return;
            ValuePerSecond = ReceivedTwice
                ? GetSpeed(time, value, _previous.time, _previous.value)
                : default;
        }

        protected abstract T GetSpeed(float time, T value, float previousTime, T previousValue);

        public T Get(float time)
        {
            if (!ReceivedTwice) return Latest.value;
            return ExtrapolateValue(time);
        }

        protected abstract T ExtrapolateValue(float time);
    }

    public class PredictedFloat : PredictedValue<float>
    {
        protected override float GetSpeed(float time, float value, float previousTime, float previousValue)
        {
            return (value - previousValue) / (time - previousTime);
        }

        protected override float ExtrapolateValue(float time)
        {
            return Latest.value + (time - Latest.time) * ValuePerSecond;
        }
    }

    public class PredictedVector3 : PredictedValue<Vector3>
    {
        protected override Vector3 GetSpeed(float time, Vector3 value, float previousTime,
                                            Vector3 previousValue)
        {
            return (value - previousValue) / (time - previousTime);
        }

        protected override Vector3 ExtrapolateValue(float time)
        {
            return Latest.value + (time - Latest.time) * ValuePerSecond;
        }
    }

    public class PredictedQuaternion : PredictedValue<Quaternion>
    {
        private Vector3 _axis;
        private float _angularSpeed;

        protected override Quaternion GetSpeed(float time, Quaternion value, float previousTime,
                                               Quaternion previousValue)
        {
            (value * Quaternion.Inverse(previousValue)).ToAngleAxis(out float angle, out _axis);
            if (angle > 180)
            {
                _axis = -_axis;
                angle = 360 - angle;
            }
            _angularSpeed = angle / (time - previousTime);
            return Quaternion.AngleAxis(_angularSpeed, _axis);
        }

        protected override Quaternion ExtrapolateValue(float time)
        {
            return Quaternion.AngleAxis(_angularSpeed * (time - Latest.time), _axis) * Latest.value;
        }
    }
}