using CustomAvatar.Avatar;
using CustomAvatar.Player;
using IPA.Utilities.Async;
using MultiplayerAvatars.Providers.Abstractions;
using MultiplayerAvatars.Providers.Attributes;
using SiraUtil.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace MultiplayerAvatars.Providers
{
    // Avatar provider for local avatars
    [AvatarProviderPriority(100)]
    internal class CustomAvatarsProvider : IAvatarProvider
    {
        public string CustomAvatarsPath = PlayerAvatarManager.kCustomAvatarsPath;

        private readonly Task _updateAvatarHashes;
        private Dictionary<string, string> _cachedAvatarPaths = new();
        private Dictionary<string, string> _cachedAvatarHashes = new();

        private readonly AvatarLoader _avatarLoader;
        private readonly SiraLog _logger;

        public CustomAvatarsProvider(
            AvatarLoader avatarLoader,
            SiraLog logger)
        {
            _avatarLoader = avatarLoader;
            _logger = logger;

            _updateAvatarHashes = Task.Run(UpdateAvatarHashes);
        }

        public async Task<AvatarPrefab?> GetAvatarByHash(string hash, CancellationToken cancellationToken)
        {
            try
            {
                await _updateAvatarHashes;
                if (!_cachedAvatarPaths.TryGetValue(hash, out string path))
                    return null;

                // Asset bundles must be loaded from the main thread
                return await UnityMainThreadTaskScheduler.Factory.StartNew(() => _avatarLoader.LoadFromFileAsync(path, null, cancellationToken)).Unwrap();
            }
            catch (Exception ex)
            {
                _logger.Error($"Error loading avatar '{hash}': {ex.Message}");
                _logger.Debug(ex);
            }
            return null;
        }

        public async Task<string?> GetAvatarHash(string path)
        {
            await _updateAvatarHashes;
            if (!_cachedAvatarHashes.ContainsKey(path))
                return null;
            return _cachedAvatarHashes[path];
        }

        public void AddAvatar(string path, string hash)
        {
            _cachedAvatarHashes[path] = hash;
            if (_cachedAvatarPaths.ContainsKey(hash))
                return;
            _cachedAvatarPaths[hash] = path;
        }

        public void AddAvatar(string path)
        {
            using (var fs = File.OpenRead(path))
            {
                string hash = BitConverter.ToString(MD5.Create().ComputeHash(fs)).Replace("-", "");
                _cachedAvatarHashes[path] = hash;
                if (_cachedAvatarPaths.ContainsKey(hash))
                    return;
                _cachedAvatarPaths[hash] = path;
            }
        }

        public void UpdateAvatarHashes()
        {
            var avatarFiles = Directory.GetFiles(CustomAvatarsPath, "*.avatar");
            foreach(string avatarFile in avatarFiles)
            {
                try
                {
                    using (var fs = File.OpenRead(avatarFile))
                    {
                        string hash = BitConverter.ToString(MD5.Create().ComputeHash(fs)).Replace("-", "");
                        // Index every filename, but keep one load path for identical content.
                        _cachedAvatarHashes[avatarFile] = hash;
                        if (_cachedAvatarPaths.ContainsKey(hash))
                            continue;
                        _cachedAvatarPaths[hash] = avatarFile;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error($"Failed to hash avatar '{avatarFile}': {ex.Message}");
                    _logger.Debug(ex);
                }
            }
        }
    }
}
