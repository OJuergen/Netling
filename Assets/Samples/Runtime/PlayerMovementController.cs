using UnityEngine;

namespace Netling.Samples
{
    [RequireComponent(typeof(Player))]
    public sealed class PlayerMovementController : MonoBehaviour
    {
        [SerializeField] private float _speed;
        [SerializeField] private float _rotationalSpeed;
        private Player _player;

        private void Awake()
        {
            _player = GetComponent<Player>();
        }

        private void Update()
        {
            if (!_player.IsLocal) return;
            Transform t = transform;
            if (Input.GetKey(KeyCode.W))
                t.position += _speed * Time.deltaTime * t.forward;
            if (Input.GetKey(KeyCode.S))
                t.position -= _speed * Time.deltaTime * t.forward;
            if (Input.GetKey(KeyCode.A))
                t.position -= _speed * Time.deltaTime * t.right;
            if (Input.GetKey(KeyCode.D))
                t.position += _speed * Time.deltaTime * t.right;
            if (Input.GetKey(KeyCode.E))
                t.Rotate(Vector3.up, _rotationalSpeed * Time.deltaTime);
            if (Input.GetKey(KeyCode.Q))
                t.Rotate(Vector3.up, -_rotationalSpeed * Time.deltaTime);
        }
    }
}