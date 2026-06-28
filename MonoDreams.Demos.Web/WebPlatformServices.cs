#nullable disable
// The IPlatformServices interface lives in the nullable-oblivious engine; matching that
// context here keeps the no-op web implementation (which legitimately returns null) warning-free.
using System;
using System.IO;
using MonoDreams.Platform;

namespace MonoDreams.Demos.Web
{
    /// <summary>
    /// Web (Blazor/WASM) implementation of <see cref="IPlatformServices"/> for the demos head.
    /// Identical in behaviour to Examples.Web's: there is no writable host filesystem and no
    /// process environment in the browser sandbox, so game content goes through MonoGame's
    /// <c>ContentManager</c> (XNB over HTTP) rather than <see cref="File"/>, the log sink is the
    /// browser console, background work runs inline (WASM is single-threaded), and file writes
    /// (screenshots) are no-ops. Installed via <c>PlatformServices.Current</c> before any engine
    /// construction (Logger, systems), as required by the foundation portability premise.
    /// </summary>
    public sealed class WebPlatformServices : IPlatformServices
    {
        public string BaseDirectory => "/";

        public string GetEnvironmentVariable(string name) => null;

        public string CombinePath(params string[] paths) => string.Join("/", paths);

        public bool FileExists(string path) => false;

        public string ReadAllText(string path) => string.Empty;

        public void WriteAllText(string path, string contents) { /* no writable FS on web */ }

        public void WriteAllBytes(string path, byte[] bytes) { /* no writable FS on web */ }

        public void CreateDirectory(string path) { /* no-op on web */ }

        public TextWriter OpenLogWriter(string directory, string fileName)
            => new ConsoleLogWriter();

        public void WriteLineToConsole(string line) => Console.WriteLine(line);

        // WASM is single-threaded (DefaultParallelRunner(1) already enforces this engine-side):
        // run fire-and-forget work inline so it still happens, just synchronously.
        public void RunBackground(Action work) => work?.Invoke();

        /// <summary>A <see cref="TextWriter"/> that forwards each completed line to the
        /// browser console. The Logger writes line-buffered, so flushing on newline is enough.</summary>
        private sealed class ConsoleLogWriter : TextWriter
        {
            private readonly global::System.Text.StringBuilder _buffer = new();
            public override global::System.Text.Encoding Encoding => global::System.Text.Encoding.UTF8;

            public override void Write(char value)
            {
                if (value == '\n')
                {
                    Console.WriteLine(_buffer.ToString());
                    _buffer.Clear();
                }
                else if (value != '\r')
                {
                    _buffer.Append(value);
                }
            }

            public override void Write(string value)
            {
                if (string.IsNullOrEmpty(value)) return;
                foreach (var c in value) Write(c);
            }
        }
    }
}
