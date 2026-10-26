using BeatSaber.AvatarCore;
using CustomAvatar.Avatar;
using IPA.Utilities;
using MultiplayerAvatars.Networking;
using MultiplayerAvatars.Providers;
using SiraUtil.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Zenject;

namespace MultiplayerAvatars.Avatars
{
    // Lives next to the game's AvatarController and swaps the base game avatar for the player's custom avatar.
    // Dependencies are resolved optionally: a failure here must never break lobby/game setup.
    internal class CustomAvatarController : MonoBehaviour
    {
        private static readonly FieldAccessor<AvatarController, Transform>.Accessor ParentingTransform
            = FieldAccessor<AvatarController, Transform>.GetAccessor("_parentingTransform");
        private static readonly FieldAccessor<AvatarController, IAvatarPoseDataProvider>.Accessor PoseDataProvider
            = FieldAccessor<AvatarController, IAvatarPoseDataProvider>.GetAccessor("_poseDataProvider");

        private CustomAvatarPacket _avatarPacket = new();
        // Tracked separately because the local player's packet is mutated in place
        private string? _requestedHash;
        private SpawnedAvatar? _spawnedAvatar;
        private MultiplayerAvatarInput? _avatarInput;
        private Renderer[]? _hiddenRenderers;
        private int _loadVersion;
        private bool _started;

        // In gameplay the avatar pose provider doesn't reliably update, so drive the avatar from the
        // head/saber transforms the game moves for this player instead (lobby avatars keep using pose data)
        private bool _usePoseControllerTransforms;

        private AvatarController _avatarController = null!;
        private AvatarSpawner _avatarSpawner = null!;
        private IConnectedPlayer _connectedPlayer = null!;
        private CustomAvatarManager _customAvatarManager = null!;
        private AvatarProviderService _avatarProvider = null!;
        private IAvatarPoseDataProvider _poseDataProvider = null!;
        private SiraLog? _logger;

        // SiraLog can't be resolved through DiContainer.TryResolve: SiraUtil names it after the injected type,
        // which only exists in a regular injection context
        [Inject]
        public void Construct(
            [InjectOptional] AvatarSpawner avatarSpawner,
            [InjectOptional] IConnectedPlayer connectedPlayer,
            [InjectOptional] CustomAvatarManager customAvatarManager,
            [InjectOptional] AvatarProviderService avatarProvider,
            [InjectOptional] IAvatarPoseDataProvider poseDataProvider,
            [InjectOptional] SiraLog logger)
        {
            _logger = logger;
            _avatarController = GetComponent<AvatarController>();
            _avatarSpawner = avatarSpawner;
            _connectedPlayer = connectedPlayer;
            _customAvatarManager = customAvatarManager;
            _avatarProvider = avatarProvider;
            _poseDataProvider = poseDataProvider;
            _usePoseControllerTransforms = GetComponent<LobbyAvatarInstaller>() == null && GetComponent<MultiplayerAvatarPoseController>() != null;

            if (_avatarController == null || _avatarSpawner == null || _connectedPlayer == null
                || _customAvatarManager == null || _avatarProvider == null || _poseDataProvider == null)
            {
                Log($"Missing dependencies on '{name}', custom avatar disabled " +
                    $"(controller={_avatarController != null}, spawner={_avatarSpawner != null}, player={_connectedPlayer != null}, " +
                    $"manager={_customAvatarManager != null}, provider={_avatarProvider != null}, pose={_poseDataProvider != null})");
                enabled = false;
            }
        }

        public void Start()
        {
            if (!enabled)
                return;
            _started = true;
            _customAvatarManager.avatarReceived += HandleAvatarReceived;
            HandleAvatarReceived(_connectedPlayer, _customAvatarManager.GetPlayerAvatarPacket(_connectedPlayer.userId));
        }

        public void Update()
        {
            // The base game avatar loads asynchronously, so keep it hidden while ours is shown
            if (_spawnedAvatar != null && _hiddenRenderers == null && _avatarController.avatar != null)
            {
                _hiddenRenderers = _avatarController.avatar.GetComponentsInChildren<Renderer>(true);
                SetBaseAvatarVisible(false);
            }
        }

        public void OnDestroy()
        {
            if (_started)
                _customAvatarManager.avatarReceived -= HandleAvatarReceived;
            _loadVersion++;
            DestroyCustomAvatar();
        }

        private void HandleAvatarReceived(IConnectedPlayer player, CustomAvatarPacket packet)
        {
            if (CustomAvatarManager.ResolveUserId(player.userId) != CustomAvatarManager.ResolveUserId(_connectedPlayer.userId))
                return;

            _avatarPacket = packet;

            if (packet.Hash == CustomAvatarPacket.NoAvatarHash)
            {
                _requestedHash = null;
                _loadVersion++;
                DestroyCustomAvatar();
                return;
            }

            if (_requestedHash == packet.Hash)
            {
                ApplyScale();
                return;
            }

            _requestedHash = packet.Hash;
            _ = LoadAvatar(packet.Hash, ++_loadVersion);
        }

        private async Task LoadAvatar(string hash, int version)
        {
            try
            {
                var avatarPrefab = await _avatarProvider.GetAvatarByHash(hash, CancellationToken.None);
                if (version != _loadVersion || this == null)
                    return;
                if (avatarPrefab == null)
                {
                    _logger?.Warn($"Could not find avatar '{hash}' for player '{_connectedPlayer.userName}'");
                    return;
                }

                CreateAvatar(avatarPrefab);
            }
            catch (Exception ex)
            {
                _logger?.Error($"Failed to load avatar '{hash}' for player '{_connectedPlayer.userName}': {ex.Message}");
                _logger?.Debug(ex);
                DestroyCustomAvatar();
            }
        }

        private void CreateAvatar(AvatarPrefab avatar)
        {
            DestroyCustomAvatar();

            // In gameplay the parenting transform carries a timeline-driven Animator that breaks the custom avatar's IK,
            // so spawn next to it instead; the IK targets are world-space transforms either way.
            // Elsewhere (e.g. the big hologram avatar, which scales its container) stay inside it.
            var parent = ParentingTransform(ref _avatarController);
            if (parent == null || parent.GetComponent<Animator>() != null)
                parent = _avatarController.transform;
            _avatarInput = _usePoseControllerTransforms
                ? MultiplayerAvatarInput.FromPoseController(avatar, GetComponent<MultiplayerAvatarPoseController>(), MirrorPoseDataProvider.IsMirroring(gameObject))
                : null;
            if (_usePoseControllerTransforms && _avatarInput == null)
                _logger?.Warn($"No usable {nameof(MultiplayerAvatarPoseController)} on '{name}', falling back to pose data");
            if (_avatarInput == null)
            {
                // MultiplayerMirror swaps the controller's pose provider after creation, so use whatever it holds now
                var poseDataProvider = PoseDataProvider(ref _avatarController) ?? _poseDataProvider;
                _avatarInput = new MultiplayerAvatarInput(avatar, poseDataProvider, parent, MirrorPoseDataProvider.GetSwapHands(poseDataProvider));
            }
            _spawnedAvatar = _avatarSpawner.SpawnAvatar(avatar, _avatarInput, parent);
            var avatarIk = _spawnedAvatar.GetComponent<AvatarIK>();
            if (avatarIk != null)
                avatarIk.isLocomotionEnabled = true;
            ApplyScale();
            _logger?.Debug($"Spawned avatar '{avatar.descriptor.name}' for player '{_connectedPlayer.userName}' " +
                $"into '{parent.name}' (poseControllerTransforms={_usePoseControllerTransforms})");
        }

        private void ApplyScale()
        {
            var scale = _avatarPacket.Scale > 0f ? _avatarPacket.Scale : 1f;
            if (_spawnedAvatar != null)
                _spawnedAvatar.transform.localScale = Vector3.one * scale;
            _avatarInput?.SetScale(scale);
        }

        private void DestroyCustomAvatar()
        {
            if (_spawnedAvatar != null)
                Destroy(_spawnedAvatar.gameObject);
            _spawnedAvatar = null;
            _avatarInput?.Dispose();
            _avatarInput = null;
            SetBaseAvatarVisible(true);
            _hiddenRenderers = null;
        }

        private void SetBaseAvatarVisible(bool visible)
        {
            if (_hiddenRenderers == null)
                return;
            foreach (var renderer in _hiddenRenderers)
                if (renderer != null)
                    renderer.enabled = visible;
        }

        private void Log(string message)
        {
            if (_logger != null)
                _logger.Warn(message);
            else
                Plugin.Logger.Warn(message);
        }
    }
}
