using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;

namespace MdViewer
{
    /// <summary>
    /// 单实例：命名 Mutex 判断谁是第一个，命名管道把第二次启动的文件路径转给已有窗口。
    /// 管道只在本机、只传一行路径，不做别的事。
    /// </summary>
    internal static class SingleInstance
    {
        private const string MutexName = @"Local\MdViewer.SingleInstance";
        private static Mutex _mutex;
        private static Thread _thread;
        private static volatile bool _running;

        public static string PipeName
        {
            get { return "MdViewer.Pipe." + Environment.UserName; }
        }

        /// <summary>true 表示本进程是第一个实例。</summary>
        public static bool TryAcquire()
        {
            try
            {
                _mutex = new Mutex(true, MutexName, out bool created);
                return created;
            }
            catch (Exception)
            {
                return true;   // 拿不到 Mutex 就当作第一个实例，总比打不开好
            }
        }

        public static void StartServer(Action<string> onPath)
        {
            if (onPath == null) return;
            _running = true;
            _thread = new Thread(() => ServerLoop(onPath))
            {
                IsBackground = true,
                Name = "MdViewer.PipeServer"
            };
            _thread.Start();
        }

        private static void ServerLoop(Action<string> onPath)
        {
            while (_running)
            {
                try
                {
                    using (var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.None))
                    {
                        server.WaitForConnection();
                        using (var reader = new StreamReader(server, Encoding.UTF8))
                        {
                            string line = reader.ReadLine();
                            if (!string.IsNullOrWhiteSpace(line)) onPath(line.Trim());
                        }
                    }
                }
                catch (Exception)
                {
                    if (!_running) return;
                    Thread.Sleep(200);   // 管道被别人占着，等一下再来
                }
            }
        }

        /// <summary>把路径交给已经开着的窗口。true 表示交出去了。</summary>
        public static bool TrySend(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            try
            {
                using (var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out))
                {
                    client.Connect(1500);
                    using (var writer = new StreamWriter(client, new UTF8Encoding(false)))
                    {
                        writer.WriteLine(path);
                        writer.Flush();
                    }
                }
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static void Stop()
        {
            _running = false;
            try { _mutex?.ReleaseMutex(); } catch (Exception) { }
            try { _mutex?.Dispose(); } catch (Exception) { }
            _mutex = null;
        }
    }
}
