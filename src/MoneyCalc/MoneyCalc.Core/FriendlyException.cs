using System;

namespace MoneyCalc.Core
{
    /// <summary>能直接显示给用户看的错误（"汇率不能是 0" 这种）。</summary>
    public sealed class FriendlyException : Exception
    {
        public FriendlyException(string message) : base(message) { }
        public FriendlyException(string message, Exception inner) : base(message, inner) { }
    }
}
