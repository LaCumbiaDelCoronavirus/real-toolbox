using System;
#if !ROBUST_ANALYZERS_TEST
using JetBrains.Annotations;
using Robust.Shared.Network;
#endif

namespace Robust.Shared.Serialization.Manager.Attributes
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
#if !ROBUST_ANALYZERS_TEST
    [MeansImplicitAssignment]
    [MeansImplicitUse(ImplicitUseKindFlags.Assign)]
    [Virtual]
#endif
    public class DataFieldAttribute : DataFieldBaseAttribute
    {
        /// <summary>
        ///     The name of this field in YAML.
        ///     If null, the name of the C# field will be used instead, with the first letter lowercased.
        /// </summary>
        public string? Tag { get; internal set; }

        /// <summary>
        ///     Whether or not this field being mapped is required for the component to function.
        ///     This will not guarantee that the field is mapped when the program is run,
        ///     it is meant to be used as metadata information.
        /// </summary>
        public readonly bool Required;

        public DataFieldAttribute(string? tag = null, bool readOnly = false, int priority = 1, bool required = false, NetworkSide networkSide = NetworkSide.Shared, Type? customTypeSerializer = null) : base(readOnly, priority, networkSide, customTypeSerializer)
        {
            Tag = tag;
            Required = required;
        }

        public override string? ToString()
        {
            return Tag;
        }
    }

    public abstract class DataFieldBaseAttribute : Attribute
    {
        public readonly int Priority;
        public readonly Type? CustomTypeSerializer;
        public readonly bool ReadOnly;

        /// <summary>
        ///     Specifies whether this datafield should only be replicated to
        ///     server, client, or both (shared).
        /// </summary>
        /// <seealso cref="NetworkSide"/>
        public readonly NetworkSide NetworkSide;

        protected DataFieldBaseAttribute(bool readOnly = false, int priority = 1, NetworkSide networkSide = NetworkSide.Shared, Type? customTypeSerializer = null)
        {
            ReadOnly = readOnly;
            Priority = priority;
            CustomTypeSerializer = customTypeSerializer;
            NetworkSide = networkSide;
        }
    }
}
