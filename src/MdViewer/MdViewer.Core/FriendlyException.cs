using System;

namespace MdViewer.Core
{
    /// <summary>
    /// 可以直接展示给用户看的异常。消息本身就是人话，不需要再加前缀。
    /// 界面捕获它时原样弹出，其他异常才打日志 + 「出错了」。
    /// </summary>
    public sealed class FriendlyException : Exception
    {
        public FriendlyException(string message) : base(message) { }
        public FriendlyException(string message, Exception inner) : base(message, inner) { }
    }
}
