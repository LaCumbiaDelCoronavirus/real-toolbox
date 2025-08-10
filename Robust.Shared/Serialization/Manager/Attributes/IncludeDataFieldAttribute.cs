using System;
using JetBrains.Annotations;
using Robust.Shared.Network;

namespace Robust.Shared.Serialization.Manager.Attributes;

/// <summary>
/// Inlines the datafield instead of putting it into its own node.
/// </summary>
/// <remarks>
/// mapping:
///   data1: 0
///   data2: 0
/// Becomes
/// data1: 0
/// data2: 0
/// </remarks>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
[MeansImplicitAssignment]
[MeansImplicitUse(ImplicitUseKindFlags.Assign)]
public sealed class IncludeDataFieldAttribute : DataFieldBaseAttribute
{
    public IncludeDataFieldAttribute(bool readOnly = false, int priority = 1, NetworkSide networkSide = NetworkSide.Shared,
        Type? customTypeSerializer = null) : base(readOnly, priority, networkSide, customTypeSerializer)
    {
    }

    public override string ToString()
    {
        return "[INCLUDE]";
    }
}
