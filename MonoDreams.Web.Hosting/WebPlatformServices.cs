using System;
using System.IO;
using MonoDreams.Platform;

namespace MonoDreams.Web.Hosting
{
    /// <summary>
    /// Web (Blazor/WASM) implementation of <see cref="IPlatformServices"/>, shared by every web
    /// head. There is no writable host filesystem and no process environment in the browser
    /// sandbox, so:
    ///   - reads of game CONTENT (XNB assets, and raw /copy: files like the native <c>.mdscene</c> levels)
    ///     go through MonoGame's <c>ContentManager</c> / <c>TitleContainer</c> (served over HTTP),
    ///     never through here — so they work on web;
    ///   - reads of USER DATA / dev files that used <see cref="File"/> (settings, the debug
    ///     input-replay plan) route through here and return empty/no-op (no readable disk);
    ///   - the log sink is the browser console (a <see cref="TextWriter"/> over
    ///     <see cref="Console"/>, which Blazor maps to the dev console);
    ///   - background work runs inline — WASM is single-threaded;
    ///   - file writes (screenshots, settings save) are no-ops.
    /// <see cref="WebHost.RunAsync"/> installs this via <c>PlatformServices.Current</c> before any
    /// engine construction (Logger, systems), as required by the foundation portability premise.
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

        public string ExportScene(string suggestedFileName, string contents)
        {
            // Minimal web export path (issue: full browser download is deferred — see the Wave 3
            // handoff). There is no writable host filesystem in the browser, so a desktop-style
            // File.Write is impossible. Until a JS-interop blob download / clipboard copy is wired
            // through GameCanvas's IJSRuntime, surface the scene to the dev console so it is not
            // silently lost, and warn loudly that the export was not downloaded.
            Console.WriteLine(
                $"[level-editor] WebPlatformServices.ExportScene: browser download is not yet wired; " +
                $"echoing '{suggestedFileName}' to the console. Copy it from here to save it.\n{contents}");
            // null => delivered out-of-band (here: only to the console, pending a real download).
            return null;
        }

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
