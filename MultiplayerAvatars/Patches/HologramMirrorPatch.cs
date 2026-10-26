using HarmonyLib;
using MultiplayerAvatars.Avatars;
using System;
using System.Reflection;
using UnityEngine;
using Zenject;

namespace MultiplayerAvatars.Patches
{
    // MultiplayerMirror's self hologram instantiates the undecorated connected player prefab,
    // so attach our controller to its big avatar once it has been configured
    [HarmonyPatch]
    internal static class HologramMirrorPatch
    {
        private static readonly Type? HologramMirrorType = AccessTools.TypeByName("MultiplayerMirror.Core.HologramMirror");

        private static bool Prepare() => HologramMirrorType != null && TargetMethod() != null;

        private static MethodBase? TargetMethod()
            => HologramMirrorType == null ? null : AccessTools.Method(HologramMirrorType, "ConfigureBigAvatar");

        private static void Postfix(object __instance)
        {
            try
            {
                var bigAvatar = Traverse.Create(__instance).Field<GameObject?>("_mirrorBigAvatarGO").Value;
                if (bigAvatar == null || bigAvatar.GetComponent<CustomAvatarController>() != null)
                    return;
                var context = bigAvatar.GetComponent<GameObjectContext>();
                if (context == null)
                {
                    Plugin.Logger.Warn("MultiplayerMirror hologram has no GameObjectContext, custom avatar disabled");
                    return;
                }
                context.Container.Inject(bigAvatar.AddComponent<CustomAvatarController>());
            }
            catch (Exception ex)
            {
                Plugin.Logger.Error($"Failed to decorate MultiplayerMirror hologram: {ex}");
            }
        }
    }
}
