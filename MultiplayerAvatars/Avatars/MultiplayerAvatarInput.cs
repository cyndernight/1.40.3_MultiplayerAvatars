using BeatSaber.AvatarCore;
using CustomAvatar.Avatar;
using CustomAvatar.Tracking;
using HarmonyLib;
using IPA.Utilities;
using System;
using UnityEngine;

namespace MultiplayerAvatars.Avatars
{
    // Feeds a connected player's synced head/hand poses to a custom avatar.
    // CustomAvatars uses the transforms we hand out as IK targets / constraint sources.
    internal class MultiplayerAvatarInput : IAvatarInput, IDisposable
    {
        private static readonly FieldAccessor<MultiplayerAvatarPoseController, Transform>.Accessor HeadTransform
            = FieldAccessor<MultiplayerAvatarPoseController, Transform>.GetAccessor("_headTransform");
        private static readonly FieldAccessor<MultiplayerAvatarPoseController, Transform>.Accessor LeftSaberTransform
            = FieldAccessor<MultiplayerAvatarPoseController, Transform>.GetAccessor("_leftSaberTransform");
        private static readonly FieldAccessor<MultiplayerAvatarPoseController, Transform>.Accessor RightSaberTransform
            = FieldAccessor<MultiplayerAvatarPoseController, Transform>.GetAccessor("_rightSaberTransform");

        // Per-avatar controller-to-hand offsets, internal in CustomAvatars
        private static readonly Func<AvatarPrefab, Pose>? GetLeftHandOffset = CreatePoseGetter("leftHandOffset");
        private static readonly Func<AvatarPrefab, Pose>? GetRightHandOffset = CreatePoseGetter("rightHandOffset");

        private readonly IAvatarPoseDataProvider? _poseDataProvider;
        private readonly Func<bool>? _swapHands;

        private readonly Transform? _root;
        private readonly Transform _head;
        private readonly Transform _leftController;
        private readonly Transform _rightController;
        private readonly Transform _leftHand;
        private readonly Transform _rightHand;
        private readonly Pose _leftHandOffset;
        private readonly Pose _rightHandOffset;

        // Keeps our own transforms in the space the base game avatar is posed in, updated from the pose data provider.
        // swapHands: reflected poses keep left/right as-is, so a humanoid avatar needs them swapped to look like a reflection
        internal MultiplayerAvatarInput(AvatarPrefab avatar, IAvatarPoseDataProvider poseDataProvider, Transform parent, Func<bool>? swapHands)
        {
            _poseDataProvider = poseDataProvider;
            _swapHands = swapHands;

            _root = new GameObject(nameof(MultiplayerAvatarInput)).transform;
            _root.SetParent(parent, false);
            _head = CreateNode("Head", _root);
            _leftController = CreateNode("LeftController", _root);
            _rightController = CreateNode("RightController", _root);
            (_leftHandOffset, _rightHandOffset) = GetHandOffsets(avatar);
            _leftHand = CreateNode("LeftHand", _leftController);
            _rightHand = CreateNode("RightHand", _rightController);
            SetScale(1f);

            HandlePoseDidChange(_poseDataProvider.currentPose);
            _poseDataProvider.poseDidChangeEvent += HandlePoseDidChange;
        }

        // Uses the head/saber transforms the game (or MultiplayerMirror) moves for this player
        private MultiplayerAvatarInput(AvatarPrefab avatar, Transform head, Transform leftController, Transform rightController)
        {
            _head = head;
            _leftController = leftController;
            _rightController = rightController;
            (_leftHandOffset, _rightHandOffset) = GetHandOffsets(avatar);
            _leftHand = CreateNode($"{nameof(MultiplayerAvatars)}LeftHand", _leftController);
            _rightHand = CreateNode($"{nameof(MultiplayerAvatars)}RightHand", _rightController);
            SetScale(1f);
        }

        internal static MultiplayerAvatarInput? FromPoseController(AvatarPrefab avatar, MultiplayerAvatarPoseController? poseController, bool swapHands)
        {
            if (poseController == null)
                return null;
            var head = HeadTransform(ref poseController);
            var leftSaber = LeftSaberTransform(ref poseController);
            var rightSaber = RightSaberTransform(ref poseController);
            if (head == null || leftSaber == null || rightSaber == null)
                return null;
            return swapHands
                ? new MultiplayerAvatarInput(avatar, head, rightSaber, leftSaber)
                : new MultiplayerAvatarInput(avatar, head, leftSaber, rightSaber);
        }

        public bool allowMaintainPelvisPosition => true;

        // Our transforms never change identity, so there is nothing to notify about
        public event Action? inputChanged { add { } remove { } }

        // Same as CustomAvatars' TrackingRig: hand offsets scale with the avatar
        public void SetScale(float scale)
        {
            _leftHand.SetLocalPositionAndRotation(_leftHandOffset.position * scale, _leftHandOffset.rotation);
            _rightHand.SetLocalPositionAndRotation(_rightHandOffset.position * scale, _rightHandOffset.rotation);
        }

        public void Dispose()
        {
            if (_poseDataProvider != null)
                _poseDataProvider.poseDidChangeEvent -= HandlePoseDidChange;
            if (_root != null)
            {
                UnityEngine.Object.Destroy(_root.gameObject);
                return;
            }
            // Hand nodes were attached to game-owned transforms
            if (_leftHand != null)
                UnityEngine.Object.Destroy(_leftHand.gameObject);
            if (_rightHand != null)
                UnityEngine.Object.Destroy(_rightHand.gameObject);
        }

        public bool TryGetTransform(DeviceUse use, out Transform transform)
        {
            switch (use)
            {
                case DeviceUse.Head:
                    transform = _head;
                    return true;
                case DeviceUse.LeftHand:
                    transform = _leftHand;
                    return true;
                case DeviceUse.RightHand:
                    transform = _rightHand;
                    return true;
                default:
                    transform = null!;
                    return false;
            }
        }

        public bool TryGetFingerCurl(DeviceUse use, out FingerCurl curl)
        {
            curl = new FingerCurl(0f, 0f, 0f, 0f, 0f);
            return false;
        }

        private static Transform CreateNode(string name, Transform parent)
        {
            var node = new GameObject(name).transform;
            node.SetParent(parent, false);
            return node;
        }

        private static (Pose left, Pose right) GetHandOffsets(AvatarPrefab avatar)
            => (GetLeftHandOffset?.Invoke(avatar) ?? Pose.identity, GetRightHandOffset?.Invoke(avatar) ?? Pose.identity);

        private static Func<AvatarPrefab, Pose>? CreatePoseGetter(string propertyName)
        {
            var getter = AccessTools.PropertyGetter(typeof(AvatarPrefab), propertyName);
            if (getter == null || getter.ReturnType != typeof(Pose))
            {
                Plugin.Logger.Warn($"Could not find {nameof(AvatarPrefab)}.{propertyName}, hand offsets will not be applied");
                return null;
            }
            return (Func<AvatarPrefab, Pose>)Delegate.CreateDelegate(typeof(Func<AvatarPrefab, Pose>), getter);
        }

        private void HandlePoseDidChange(AvatarPoseData pose)
        {
            if (_root == null)
                return;
            _head.SetLocalPositionAndRotation(pose.headPose.position, Normalized(pose.headPose.rotation));
            var swap = _swapHands != null && _swapHands();
            var leftHandPose = swap ? pose.rightHandPose : pose.leftHandPose;
            var rightHandPose = swap ? pose.leftHandPose : pose.rightHandPose;
            _leftController.SetLocalPositionAndRotation(leftHandPose.position, Normalized(leftHandPose.rotation));
            _rightController.SetLocalPositionAndRotation(rightHandPose.position, Normalized(rightHandPose.rotation));
        }

        // Poses default to an all-zero quaternion before the first sync state arrives
        private static Quaternion Normalized(Quaternion rotation)
            => rotation == new Quaternion(0f, 0f, 0f, 0f) ? Quaternion.identity : rotation;
    }
}
