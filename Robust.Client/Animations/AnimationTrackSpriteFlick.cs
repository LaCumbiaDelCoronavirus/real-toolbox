using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Animations;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Network;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.Utility;

namespace Robust.Client.Animations
{
    /// <summary>
    ///     An animation track that plays RSI state animations manually, so they can be precisely controlled etc.
    /// </summary>
    [UsedImplicitly, SerializedType(nameof(AnimationTrackSpriteFlick))]
    public sealed partial class AnimationTrackSpriteFlick : AnimationTrack
    {
        /// <summary>
        ///     A list of key frames for when to fire flicks.
        /// </summary>
        [DataField(networkSide: NetworkSide.Client)]
        public List<KeyFrame> KeyFrames { get; private set; } = new();

        // TODO: Should this layer key be per keyframe maybe?
        /// <summary>
        ///     The layer key of the layer to flick on.
        /// </summary>
        [DataField(networkSide: NetworkSide.Client)]
        public object? LayerKey { get; set; }

        public override (int KeyFrameIndex, float FramePlayingTime) InitPlayback()
        {
            if (LayerKey == null)
            {
                throw new InvalidOperationException("Must set LayerKey.");
            }

            return (-1, 0);
        }

        public override (int KeyFrameIndex, float FramePlayingTime)
            AdvancePlayback(object context, int prevKeyFrameIndex, float prevPlayingTime, float frameTime)
        {
            DebugTools.AssertNotNull(LayerKey);

            var entity = (EntityUid) context;
            var sprite = IoCManager.Resolve<IEntityManager>().GetComponent<SpriteComponent>(entity);

            var playingTime = prevPlayingTime + frameTime;
            var keyFrameIndex = prevKeyFrameIndex;
            // Advance to the correct key frame.
            while (keyFrameIndex != KeyFrames.Count - 1 && KeyFrames[keyFrameIndex + 1].KeyTime < playingTime)
            {
                playingTime -= KeyFrames[keyFrameIndex + 1].KeyTime;
                keyFrameIndex += 1;
            }

            if (keyFrameIndex >= 0)
            {
                var keyFrame = KeyFrames[keyFrameIndex];
                // Advance animation on current key frame.
                var rsi = sprite.LayerGetActualRSI(LayerKey!);
                if (rsi != null && rsi.TryGetState(keyFrame.State, out var state))
                {
                    var animationTime = Math.Min(state.AnimationLength - 0.01f, playingTime);
                    sprite.LayerSetAutoAnimated(LayerKey!, false);
                    // TODO: Doesn't setting the state explicitly reset the animation
                    // so it's slightly more inefficient?
                    sprite.LayerSetState(LayerKey!, keyFrame.State);
                    sprite.LayerSetAnimationTime(LayerKey!, animationTime);
                }
            }

            return (keyFrameIndex, playingTime);
        }

        [DataDefinition]
        public partial struct KeyFrame
        {
            /// <summary>
            ///     The RSI state to play when this keyframe gets triggered.
            /// </summary>
            [DataField(networkSide: NetworkSide.Client)]
            public RSI.StateId State;

            /// <summary>
            ///     The time between this keyframe and the last.
            /// </summary>
            [DataField(networkSide: NetworkSide.Client)]
            public float KeyTime;

            public KeyFrame(RSI.StateId state, float keyTime)
            {
                State = state;
                KeyTime = keyTime;
            }
        }
    }
}
