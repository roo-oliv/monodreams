using MonoDreams.Examples;

// Before Game1: SDL initializes in the Game base ctor, and on macOS the focus grab happens at app
// activation during SDL init — a headless run must never yank focus from whatever the user is typing in.
if (args.Contains("--headless")) MonoDreams.Debug.HeadlessWindow.PreventFocusSteal();
using var game = new Game1(args);
game.Run();
