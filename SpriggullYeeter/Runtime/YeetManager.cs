using System;
using System.Collections.Generic;
using Alta.Character;
using Alta.Networking;
using Alta.Networking.Scripts.Player;
using MelonLoader;
using RagdollLib.API;
using SpriggullYeeter.Config;
using SpriggullYeeter.Core.Config;
using UnityEngine;

namespace SpriggullYeeter.Runtime
{
    /// <summary>
    /// Coordinates active <see cref="PlayerYeetHandler"/>s, airborne Spriggull collision trackers,
    /// RagdollLib recovery subscriptions, and scene/entity lifecycle cleanup.
    /// </summary>
    internal sealed class YeetManager
    {
        public static YeetManager Instance { get; private set; }

        private readonly Dictionary<int, PlayerYeetHandler> _players = new Dictionary<int, PlayerYeetHandler>();
        private readonly Dictionary<int, AirborneSpriggullTracker> _airborneTrackers = new Dictionary<int, AirborneSpriggullTracker>();
        private readonly HashSet<int> _heldEntityIds = new HashSet<int>();
        private readonly List<int> _pruneBuffer = new List<int>();

        private readonly YeetConfig _config;
        private readonly MelonLogger.Instance _log;

        public YeetManager(YeetConfig config, MelonLogger.Instance log)
        {
            _config = config;
            _log = log;
            Instance = this;
        }

        public void Initialize()
        {
            RagdollAPI.OnRagdollRecovered += OnRagdollRecovered;
        }

        public void Shutdown()
        {
            RagdollAPI.OnRagdollRecovered -= OnRagdollRecovered;
            ClearAll();
        }

        public void LogError(string message) => _log?.Error(message);

        public void ReconcilePlayers()
        {
            try
            {
                _pruneBuffer.Clear();
                foreach (KeyValuePair<int, PlayerYeetHandler> kvp in _players)
                {
                    if (kvp.Value.Player == null || !kvp.Value.Player.gameObject.activeInHierarchy)
                        _pruneBuffer.Add(kvp.Key);
                }

                for (int i = 0; i < _pruneBuffer.Count; i++)
                {
                    int key = _pruneBuffer[i];
                    if (_players.TryGetValue(key, out PlayerYeetHandler handler))
                    {
                        handler.AbortHoldIfActive();
                        _players.Remove(key);
                    }
                }

                if (!NetworkSceneManager.IsServer)
                {
                    PlayerController local = PlayerController.Current;
                    if (local != null)
                        RegisterPlayer(local);
                    return;
                }

                HashSet<IPlayer> allPlayers = Player.AllPlayers;
                if (allPlayers == null)
                    return;

                foreach (IPlayer iplayer in allPlayers)
                {
                    if (iplayer is Player p && p != null && p.PlayerController != null)
                        RegisterPlayer(p.PlayerController);
                }
            }
            catch (Exception ex)
            {
                _log?.Warning($"[SpriggullYeeter] Error reconciling players: {ex.Message}");
            }
        }

        public void RegisterPlayer(PlayerController pc)
        {
            if (pc == null || !(pc is PlayerCharacter))
                return;

            int id = pc.GetInstanceID();
            if (_players.ContainsKey(id))
                return;

            _players[id] = new PlayerYeetHandler(pc, this, _log);
        }

        public void UnregisterPlayer(Player player)
        {
            if (player == null)
                return;

            if (player.PlayerController != null)
            {
                int id = player.PlayerController.GetInstanceID();
                if (_players.TryGetValue(id, out PlayerYeetHandler handler))
                {
                    handler.AbortHoldIfActive();
                    _players.Remove(id);
                }
            }
        }

        public void TickAll()
        {
            YeetConfigSnapshot cfg = _config.Snapshot;

            foreach (KeyValuePair<int, PlayerYeetHandler> kvp in _players)
            {
                try
                {
                    kvp.Value.Tick(cfg);
                }
                catch (Exception ex)
                {
                    _log?.Error($"[SpriggullYeeter] Error ticking PlayerYeetHandler: {ex}");
                }
            }

            if (_airborneTrackers.Count > 0)
            {
                _pruneBuffer.Clear();
                foreach (KeyValuePair<int, AirborneSpriggullTracker> kvp in _airborneTrackers)
                {
                    if (kvp.Value.ShouldCompleteCleanup())
                        _pruneBuffer.Add(kvp.Key);
                }

                for (int i = 0; i < _pruneBuffer.Count; i++)
                {
                    int id = _pruneBuffer[i];
                    if (_airborneTrackers.TryGetValue(id, out AirborneSpriggullTracker tracker))
                    {
                        tracker.Cleanup();
                        _airborneTrackers.Remove(id);
                    }
                }
            }
        }

        public void FixedTickAll()
        {
            foreach (KeyValuePair<int, PlayerYeetHandler> kvp in _players)
            {
                try
                {
                    kvp.Value.FixedTick();
                }
                catch (Exception ex)
                {
                    _log?.Error($"[SpriggullYeeter] Error in FixedTickAll: {ex}");
                }
            }
        }

        public void LateTickAll()
        {
            foreach (KeyValuePair<int, PlayerYeetHandler> kvp in _players)
            {
                try
                {
                    kvp.Value.LateTick();
                }
                catch (Exception ex)
                {
                    _log?.Error($"[SpriggullYeeter] Error in LateTickAll: {ex}");
                }
            }
        }

        public void NotifyHoldStarted(int rootInstanceId)
        {
            if (rootInstanceId != 0)
                _heldEntityIds.Add(rootInstanceId);
        }

        public void NotifyHoldEnded(int rootInstanceId)
        {
            if (rootInstanceId != 0)
                _heldEntityIds.Remove(rootInstanceId);
        }

        public bool IsSpriggullHeld(int rootInstanceId)
        {
            return _heldEntityIds.Contains(rootInstanceId);
        }

        public bool IsComponentEntityHeld(Component comp)
        {
            if (_heldEntityIds.Count == 0 || comp == null)
                return false;

            int directId = comp.gameObject.GetInstanceID();
            if (_heldEntityIds.Contains(directId))
                return true;

            NetworkEntity net = comp as NetworkEntity ?? comp.GetComponentInParent<NetworkEntity>();
            if (net != null)
            {
                NetworkEntity prefabRoot = net.PrefabRoot != null ? net.PrefabRoot : net;
                if (prefabRoot != null && _heldEntityIds.Contains(prefabRoot.gameObject.GetInstanceID()))
                    return true;
            }

            Transform root = comp.transform.root;
            return root != null && _heldEntityIds.Contains(root.gameObject.GetInstanceID());
        }

        public void RegisterAirborneLaunch(GameObject spriggullRoot, PlayerController thrower, YeetConfigSnapshot cfg)
        {
            if (spriggullRoot == null)
                return;

            int id = spriggullRoot.GetInstanceID();
            if (_airborneTrackers.TryGetValue(id, out AirborneSpriggullTracker existing))
            {
                existing.Cleanup();
                _airborneTrackers.Remove(id);
            }

            _airborneTrackers[id] = new AirborneSpriggullTracker(spriggullRoot, thrower, cfg, _log);
        }

        public void HandleEntityKilled(HealthObject health)
        {
            if (health == null)
                return;

            NetworkEntity net = health.Entity != null ? health.Entity : health.GetComponentInParent<NetworkEntity>();
            GameObject root = net != null && net.PrefabRoot != null ? net.PrefabRoot.gameObject : health.gameObject;
            if (root == null)
                return;

            AbortHoldsForEntity(root.GetInstanceID());
        }

        public void HandleEntityDespawn(Component comp)
        {
            if (comp == null)
                return;

            NetworkEntity net = comp as NetworkEntity ?? comp.GetComponentInParent<NetworkEntity>();
            GameObject root = net != null && net.PrefabRoot != null ? net.PrefabRoot.gameObject : comp.gameObject;
            if (root == null)
                return;

            int id = root.GetInstanceID();
            AbortHoldsForEntity(id);

            if (_airborneTrackers.TryGetValue(id, out AirborneSpriggullTracker tracker))
            {
                tracker.Cleanup();
                _airborneTrackers.Remove(id);
            }
        }

        public void ClearAll()
        {
            foreach (KeyValuePair<int, PlayerYeetHandler> kvp in _players)
                kvp.Value.AbortHoldIfActive();
            _players.Clear();

            foreach (KeyValuePair<int, AirborneSpriggullTracker> kvp in _airborneTrackers)
                kvp.Value.Cleanup();
            _airborneTrackers.Clear();

            _heldEntityIds.Clear();
            _pruneBuffer.Clear();
        }

        private void AbortHoldsForEntity(int rootInstanceId)
        {
            foreach (KeyValuePair<int, PlayerYeetHandler> kvp in _players)
                kvp.Value.AbortIfHoldingEntity(rootInstanceId);
        }

        private void OnRagdollRecovered(RagdollEventArgs args)
        {
            if (args == null || args.TargetRoot == null)
                return;

            int id = args.TargetRoot.GetInstanceID();
            if (_airborneTrackers.TryGetValue(id, out AirborneSpriggullTracker tracker))
            {
                tracker.Cleanup();
                _airborneTrackers.Remove(id);

                if (_config.Snapshot.EnableDebugLogging)
                {
                    _log?.Msg($"[SpriggullYeeter] Spriggull '{args.TargetRoot.name}' recovered from RagdollLib flight.");
                }
            }
        }
    }
}
