using System;
using System.Diagnostics;
using System.Text;

namespace MusicBeePlugin.Common
{
    public static class CurlHelper
    {
        public const string DefaultUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36";

        public static string Get(string url, string userAgent = DefaultUserAgent, int timeoutMs = 8000)
        {
            try
            {
                var safeUrl = EscapeCliArg(url);
                var safeUa = EscapeCliArg(userAgent);

                var psi = new ProcessStartInfo
                {
                    FileName = "curl.exe",
                    Arguments = $"-s --fail -A \"{safeUa}\" \"{safeUrl}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };

                using (var process = new Process { StartInfo = psi })
                {
                    var outputBuilder = new StringBuilder();
                    process.OutputDataReceived += (sender, args) =>
                    {
                        if (args.Data != null)
                        {
                            outputBuilder.AppendLine(args.Data);
                        }
                    };

                    if (!process.Start())
                    {
                        return null;
                    }

                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    if (!process.WaitForExit(timeoutMs))
                    {
                        try { process.Kill(); } catch { }
                        return null;
                    }

                    return process.ExitCode == 0 ? outputBuilder.ToString() : null;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string EscapeCliArg(string arg)
        {
            if (string.IsNullOrEmpty(arg)) return string.Empty;
            return arg.Replace("\"", "\\\"");
        }
    }
}
