// Polyfills so AgentSim.Core compiles for netstandard2.1 (Unity Mono runtime). The C#
// compiler emits references to these types when we use `init` accessors and `required`
// members, but the netstandard2.1 BCL doesn't ship them. Conditionally compile only when
// targeting netstandard2.1.

#if NETSTANDARD2_1

namespace System.Runtime.CompilerServices
{
    /// <summary>Required for `init`-only setters.</summary>
    internal static class IsExternalInit { }

    /// <summary>Required for `required` members.</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    internal sealed class RequiredMemberAttribute : Attribute { }

    /// <summary>Required for `required` members (compiler-feature attribute).</summary>
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = false)]
    internal sealed class CompilerFeatureRequiredAttribute : Attribute
    {
        public CompilerFeatureRequiredAttribute(string featureName) { FeatureName = featureName; }
        public string FeatureName { get; }
        public bool IsOptional { get; init; }
    }
}

namespace System.Diagnostics.CodeAnalysis
{
    /// <summary>Tells the compiler that constructor satisfies all `required` members.</summary>
    [AttributeUsage(AttributeTargets.Constructor, AllowMultiple = false, Inherited = false)]
    internal sealed class SetsRequiredMembersAttribute : Attribute { }
}

#endif
