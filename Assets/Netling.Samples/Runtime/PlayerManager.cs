using System.Collections.Generic;
using JetBrains.Annotations;
using UnityEngine;

namespace Netling.Samples
{
    public class PlayerManager
    {
        private static PlayerManager _instance;
        [NotNull] public static PlayerManager Instance => _instance ??= new PlayerManager();
        private readonly Dictionary<int, Player> _players = new();
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

            Server.Instance.SpawnNetObject(_playerPrefab, default, null, Vector3.zero, Quaternion.identity,
                actorNumber);
        }

        private void OnClientDisconnected(int id)
        {
            if (!_players.TryGetValue(id, out Player player))
            {
                Debug.Log($"Player with unknown ID {id} disconnected");
                return;
            }

            Object.Destroy(player.gameObject);
        }

        public void Register(Player player)
        {
            if (!_players.TryAdd(player.OwnerActorNumber, player))
            {
                Debug.LogWarning($"Player with ID {player.OwnerActorNumber} is already connected");
            }
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
            return _players.GetValueOrDefault(actorNumber);
        }
    }
}