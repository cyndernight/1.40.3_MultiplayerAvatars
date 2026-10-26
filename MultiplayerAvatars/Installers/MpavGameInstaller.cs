using BeatSaber.AvatarCore;
using MultiplayerAvatars.Avatars;
using SiraUtil.Extras;
using SiraUtil.Logging;
using SiraUtil.Objects.Multiplayer;
using System;
using Zenject;

namespace MultiplayerAvatars.Installers
{
    internal class MpavGameInstaller : Installer
    {
        [InjectOptional]
        private readonly SiraLog? _logger = null;

        public override void InstallBindings()
        {
            Container.RegisterRedecorator(new ConnectedPlayerRegistration(DecorateConnectedPlayerFacade));
            Container.RegisterRedecorator(new ConnectedPlayerDuelRegistration(DecorateConnectedPlayerFacade));
        }

        private MultiplayerConnectedPlayerFacade DecorateConnectedPlayerFacade(MultiplayerConnectedPlayerFacade original)
        {
            try
            {
                var avatarControllers = original.GetComponentsInChildren<AvatarController>(true);
                if (avatarControllers.Length == 0)
                    _logger?.Warn($"No {nameof(AvatarController)} found on connected player '{original.name}'");
                foreach (var avatarController in avatarControllers)
                    avatarController.gameObject.AddComponent<CustomAvatarController>();
            }
            catch (Exception ex)
            {
                _logger?.Error($"Failed to decorate connected player: {ex}");
            }
            return original;
        }
    }
}
