using System;

namespace Analyzer
{
    /// <summary>
    /// Marks a record / struct / class as “friend-only”.
    /// The type can be used — ctor call, member access, etc. — *only* from the
    /// types listed in <paramref name="friendTypes"/>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct,
        Inherited = false, AllowMultiple = false)]
    public sealed class FriendAttribute : Attribute
    {
        public FriendAttribute(params Type[] friendTypes)
            : this(FriendLevel.Error, friendTypes)
        {
        }

        public FriendAttribute(FriendLevel level, params Type[] friendTypes)
        {
            Level = level;
            FriendTypes = friendTypes;
        }

        public FriendLevel Level { get; }
        public Type[] FriendTypes { get; }
    }

    public enum FriendLevel
    {
        Error,
        Warning
    }
}