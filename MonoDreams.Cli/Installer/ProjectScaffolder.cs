namespace MonoDreams.Cli.Installer;

internal static class ProjectScaffolder
{
    public static void Scaffold(string projectDir, string projectName)
    {
        Directory.CreateDirectory(projectDir);
        WriteCsproj(projectDir, projectName);
        WriteProgram(projectDir, projectName);
        WriteAppManifest(projectDir);
        WriteGitignore(projectDir);
    }

    private static void WriteCsproj(string projectDir, string projectName)
    {
        var path = Path.Combine(projectDir, $"{projectName}.csproj");
        File.WriteAllText(path, $"""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <RollForward>Major</RollForward>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <PublishReadyToRun>false</PublishReadyToRun>
    <TieredCompilation>false</TieredCompilation>
  </PropertyGroup>
</Project>

""");
    }

    private static void WriteProgram(string projectDir, string projectName)
    {
        var path = Path.Combine(projectDir, "Program.cs");
        File.WriteAllText(path, $$"""
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace {{projectName}};

public class {{projectName}}Game : Game
{
    private readonly GraphicsDeviceManager _graphics;

    public {{projectName}}Game()
    {
        _graphics = new GraphicsDeviceManager(this);
        IsMouseVisible = true;
    }

    protected override void Update(GameTime gameTime)
    {
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.CornflowerBlue);
        base.Draw(gameTime);
    }

    public static void Main()
    {
        using var game = new {{projectName}}Game();
        game.Run();
    }
}

""");
    }

    private static void WriteAppManifest(string projectDir)
    {
        var path = Path.Combine(projectDir, "app.manifest");
        File.WriteAllText(path, """
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <assemblyIdentity version="1.0.0.0" name="MonoGame.App"/>
</assembly>

""");
    }

    private static void WriteGitignore(string projectDir)
    {
        var path = Path.Combine(projectDir, ".gitignore");
        if (File.Exists(path)) return;
        File.WriteAllText(path, """
bin/
obj/
debug/
*.user
""");
    }
}
