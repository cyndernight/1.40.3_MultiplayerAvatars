using BeatSaber.AvatarCore;
using System;
using System.Reflection;
using UnityEngine;

namespace MultiplayerAvatars.Avatars
{
    // Soft integration with MultiplayerMirror's mirrored pose sources, without referencing the mod
    internal static class MirrorPoseDataProvider
    {
        private const string ProviderTypeName = "MultiplayerMirror.Core.Scripts.MirrorAvatarPoseDataProvider";
        private const string PoseControllerTypeName = "MultiplayerMirror.Core.Scripts.MirrorAvatarPoseController";

        // Returns whether hands should currently be swapped, or null if the provider isn't a mirror
        public static Func<bool>? GetSwapHands(IAvatarPoseDataProvider? provider)
            => provider != null && provider.GetType().FullName == ProviderTypeName ? GetEnableMirror(provider) : null;

        // Whether a MultiplayerMirror pose controller on this object (the self hologram) currently mirrors poses
        public static bool IsMirroring(GameObject gameObject)
        {
            foreach (var component in gameObject.GetComponents<MonoBehaviour>())
                if (component != null && component.GetType().FullName == PoseControllerTypeName)
                    return GetEnableMirror(component)?.Invoke() ?? false;
            return false;
        }

        private static Func<bool>? GetEnableMirror(object target)
        {
            var getter = target.GetType().GetProperty("EnableMirror", BindingFlags.Public | BindingFlags.Instance)?.GetGetMethod();
            if (getter == null || getter.ReturnType != typeof(bool))
                return null;
            return (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), target, getter);
        }
    }
}
