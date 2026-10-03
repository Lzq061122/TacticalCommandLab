// 编译期 polyfill：让 Core / Telemetry 能被 netstandard2.0（Bannerlord 的 net472 宿主）消费。
// 只在非 net5+ 目标生效；net10.0 下本文件整体为空，绝不影响既有仿真行为与 replay_hash。

#if !NET5_0_OR_GREATER

namespace System.Runtime.CompilerServices
{
    /// <summary>init 访问器的标记属性（net5+ 内置）。</summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Field |
                    AttributeTargets.Property | AttributeTargets.Parameter, Inherited = false)]
    internal sealed class IsExternalInit : Attribute { }

    /// <summary>required 成员的标记属性（net7+ 内置）。</summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Field |
                    AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
    internal sealed class RequiredMemberAttribute : Attribute { }

    /// <summary>required 成员的编译器特性标记（net7+ 内置）。</summary>
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = false)]
    internal sealed class CompilerFeatureRequiredAttribute : Attribute
    {
        public CompilerFeatureRequiredAttribute(string featureName) => FeatureName = featureName;

        public string FeatureName { get; }

        public bool IsOptional { get; init; }

        public const string RefStructInterfaces = nameof(RefStructInterfaces);
    }

    /// <summary>标记"本构造器会写入所有 required 成员"（net7+ 内置）。</summary>
    [AttributeUsage(AttributeTargets.Constructor, AllowMultiple = false, Inherited = false)]
    internal sealed class SetsRequiredMembersAttribute : Attribute { }
}

#endif
