using System;
using System.Collections.Generic;
using Robust.Shared.Animations;
using Robust.Shared.Network;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Robust.Shared.Animations;

/// <summary>
///     A animation represents a way to animate something, using keyframes and such.
/// </summary>
/// <remarks>
///     An animation is a collection of <see cref="AnimationTracks"/>, which are all executed in sync.
/// </remarks>
/// <seealso cref="AnimationPlayerComponent"/>
[DataDefinition, SerializedType(nameof(Animation))]
public sealed partial class Animation
{
    [DataField(networkSide: NetworkSide.Client)]
    public List<AnimationTrack> AnimationTracks { get; private set; } = new();

    [DataField]
    public TimeSpan Length { get; set; }
}
