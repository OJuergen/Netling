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
            Server.Instance.ClientAccepted += OnClientAccepted;
            Server.Instance.ClientDisconnected += OnClientDisconnected;
        }

        ~PlayerManager()
        {
            Server.Instance.ClientAccepted -= OnClientAccepted;
            Server.Instance.ClientDisconnected -= OnClientDisconnected;
        }

        private void OnClientAccepted(int clientID)
        {
            if (_players.ContainsKey(clientID))
            {
                Debug.LogWarning($"Player with ID {clientID} is already connected");
                return;
            }

            Server.Instance.SpawnNetObject(_playerPrefab, default, null, Vector3.zero, Quaternion.identity,
                clientID);
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
            if (!_players.TryAdd(player.OwnerClientID, player))
            {
                Debug.LogWarning($"Player with ID {player.OwnerClientID} is already connected");
            }
        }

        public void Unregister(Player player)
        {
            if (!_players.ContainsKey(player.OwnerClientID))
            {
                Debug.LogWarning($"Player with unknown client ID {player.OwnerClientID} disconnected");
                return;
            }

            _players.Remove(player.OwnerClientID);
        }

        public Player Get(int clientID)
        {
            return _players.GetValueOrDefault(clientID);
        }
    }
}