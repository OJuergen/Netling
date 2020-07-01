using System.Collections;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Netling.Samples
{
    public class RandomWalker : NetBehaviour
    {
        [SerializeField] private float _maximumDistanceFromSpawnPoint = 1;
        [SerializeField] private float _speed = 1;
        [SerializeField] private float _targetSelectionPeriod = 1;
        private Vector3 _spawnPoint;

        private void Start()
        {
            if (!HasAuthority) return;
            _spawnPoint = transform.position;
            InvokeRepeating(nameof(WalkToRandomTarget), 0, _targetSelectionPeriod);
        }

        private void WalkToRandomTarget()
        {
            Vector2 offset = _maximumDistanceFromSpawnPoint * Random.insideUnitCircle;
            Vector3 targetPosition = _spawnPoint + offset.x * Vector3.right + offset.y * Vector3.forward;
            StopAllCoroutines();
            StartCoroutine(WalkToTargetCoroutine(targetPosition));
        }

        private IEnumerator WalkToTargetCoroutine(Vector3 target)
        {
            Vector3 start = transform.position;
            transform.LookAt(target);
            float time = Vector3.Distance(start, target) / _speed;
            float progress = 0;
            while (progress < 1)
            {
                progress += Time.deltaTime / time;
                transform.position = Vector3.Lerp(start, target, progress);
                yield return null;
            }
        }
    }
}