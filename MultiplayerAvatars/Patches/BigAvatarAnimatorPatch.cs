using HarmonyLib;
using Tweening;
using UnityEngine;

namespace MultiplayerAvatars.Patches
{
    // Same problem as ScaleAnimatorPatch, for the big hologram avatar in 3+ player lobbies:
    // it is scaled to and from 0, which breaks the custom avatar's IK for good (only the sabers stay visible).
    // Keep it at a small scale instead; it is deactivated while hidden anyway.
    [HarmonyPatch(typeof(MultiplayerBigAvatarAnimator))]
    internal static class BigAvatarAnimatorPatch
    {
        private const float MinScale = 0.05f;

        private static readonly AccessTools.FieldRef<MultiplayerBigAvatarAnimator, Transform> AvatarTransform
            = AccessTools.FieldRefAccess<MultiplayerBigAvatarAnimator, Transform>("_avatarTransform");
        private static readonly AccessTools.FieldRef<MultiplayerBigAvatarAnimator, Tween<float>> ScaleDownTween
            = AccessTools.FieldRefAccess<MultiplayerBigAvatarAnimator, Tween<float>>("_scaleDownTween");

        [HarmonyPostfix]
        [HarmonyPatch("InitIfNeeded")]
        private static void InitIfNeededPostfix(MultiplayerBigAvatarAnimator __instance)
        {
            var scaleDownTween = ScaleDownTween(__instance);
            if (scaleDownTween != null)
                scaleDownTween.toValue = MinScale;
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(MultiplayerBigAvatarAnimator.HideInstant))]
        private static void HideInstantPostfix(MultiplayerBigAvatarAnimator __instance)
            => ClampScale(__instance);

        // Before: the scale up tween starts from the current scale. After: an instant hide sets it to 0.
        [HarmonyPrefix]
        [HarmonyPatch(nameof(MultiplayerBigAvatarAnimator.Animate))]
        private static void AnimatePrefix(MultiplayerBigAvatarAnimator __instance)
            => ClampScale(__instance);

        [HarmonyPostfix]
        [HarmonyPatch(nameof(MultiplayerBigAvatarAnimator.Animate))]
        private static void AnimatePostfix(MultiplayerBigAvatarAnimator __instance)
            => ClampScale(__instance);

        private static void ClampScale(MultiplayerBigAvatarAnimator animator)
        {
            var avatarTransform = AvatarTransform(animator);
            if (avatarTransform != null && avatarTransform.localScale.x < MinScale)
                avatarTransform.localScale = Vector3.one * MinScale;
        }
    }
}
