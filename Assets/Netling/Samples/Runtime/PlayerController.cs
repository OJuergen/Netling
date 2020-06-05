using UnityEngine;

namespace Netling.Samples
{
    [RequireComponent(typeof(Player))]
    public sealed class PlayerController : MonoBehaviour
    {
        [SerializeField] private float _speed;
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
        }
    }
}