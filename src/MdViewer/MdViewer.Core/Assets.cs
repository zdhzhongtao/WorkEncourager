using System;
using System.IO;
using System.Reflection;

namespace MdViewer.Core
{
    /// <summary>内嵌资源（CSS / JS）。禁止 CDN，也禁止读程序目录下的散文件。</summary>
    public static class Assets
    {
        private static string _css;
        private static string _js;

        public static string StyleCss => _css ?? (_css = Load("style.css"));
        public static string AppJs => _js ?? (_js = Load("app.js"));

        private static string Load(string name)
        {
            var assembly = typeof(Assets).GetTypeInfo().Assembly;
            string resource = null;
            foreach (var candidate in assembly.GetManifestResourceNames())
            {
                if (candidate.EndsWith("." + name, StringComparison.OrdinalIgnoreCase)) { resource = candidate; break; }
            }
            if (resource == null) return string.Empty;

            using (var stream = assembly.GetManifestResourceStream(resource))
            {
                if (stream == null) return string.Empty;
                using (var reader = new StreamReader(stream, System.Text.Encoding.UTF8))
                    return reader.ReadToEnd();
            }
        }
    }
}
