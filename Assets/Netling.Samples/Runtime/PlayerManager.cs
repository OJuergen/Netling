using System.Collections.Generic;
using JetBrains.Annotations;
using UnityEngine;

namespace Netling.Samples
{
    public class PlayerManager
    {
        private static PlayerManager _instance;
        [NotNull] public static PlayerManager Instance => _instance = _instance ?? new PlayerManager();
        private readonly Dictionary<int, Player> _players = new Dictionary<int, Player>();
        private Player _playerPrefab;

        public void Init([NotNull] Player playerPrefab)
        {
            _playerPrefab = playerPrefab;
            Server.Instance.PlayerAccepted += OnPlayerAccepted;
            Server.Instance.ClientDisconnected += OnClientDisconnected;
        }

        ~PlayerManager()
        {
            Server.Instance.PlayerAccepted -= OnPlayerAccepted;
            Server.Instance.ClientDisconnected -= OnClientDisconnected;
        }

        private void OnPlayerAccepted(int actorNumber)
        {
            if (_players.ContainsKey(actorNumber))
            {
                Debug.LogWarning($"Player with ID {actorNumber} is already connected");
                return;
            }

            Server.Instance.SpawnNetObject(_playerPrefab, Vector3.zero, Quaternion.identity, null, actorNumber);
        }

        private void OnClientDisconnected(int id)
        {
            if (!_players.ContainsKey(id))
            {
                Debug.LogWarning($"Player with unknown ID {id} disconnected");
                return;
            }

            Object.Destroy(_players[id].gameObject);
        }

        public void Register(Player player)
        {
            if (_players.ContainsKey(player.OwnerActorNumber))
            {
                Debug.LogWarning($"Player with ID {player.OwnerActorNumber} is already connected");
                return;
            }

            _players.Add(player.OwnerActorNumber, player);
        }

        public void Unregister(Player player)
        {
            if (!_players.ContainsKey(player.OwnerActorNumber))
            {
                Debug.LogWarning($"Player with unknown ID {player.OwnerActorNumber} disconnected");
                return;
            }

            _players.Remove(player.OwnerActorNumber);
        }

        public Player Get(int actorNumber)
        {
            return _players.TryGetValue(actorNumber, out Player player) ? player : null;
        }
    }
}