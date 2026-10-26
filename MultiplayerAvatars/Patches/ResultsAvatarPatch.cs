using BeatSaber.AvatarCore;
using HarmonyLib;
using MultiplayerAvatars.Avatars;
using System;

namespace MultiplayerAvatars.Patches
{
    // SiraUtil has no redecorator for the results pyramid, so add our controller to its avatar prefab directly
    [HarmonyPatch(typeof(MultiplayerObjectInstaller), nameof(MultiplayerObjectInstaller.InstallBindings))]
    internal static class ResultsAvatarPatch
    {
        private static readonly AccessTools.FieldRef<MultiplayerObjectInstaller, MultiplayerResultsPyramidViewAvatar> ResultsAvatarPrefab
            = AccessTools.FieldRefAccess<MultiplayerObjectInstaller, MultiplayerResultsPyramidViewAvatar>("_multiplayerResultsPyramidViewAvatarPrefab");

        private static void Prefix(MultiplayerObjectInstaller __instance)
        {
            try
            {
                var prefab = ResultsAvatarPrefab(__instance);
                if (prefab == null)
                    return;
                foreach (var avatarController in prefab.GetComponentsInChildren<AvatarController>(true))
                    if (avatarController.GetComponent<CustomAvatarController>() == null)
                        avatarController.gameObject.AddComponent<CustomAvatarController>();
            }
            catch (Exception ex)
            {
                Plugin.Logger.Error($"Failed to decorate results avatar: {ex}");
            }
        }
    }
}
