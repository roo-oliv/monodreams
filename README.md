MonoDreams
==========

<p align="center">
  <br>
   <img src="/Icon/monodreams-logo.png" width="128" alt="MonoDreams Logo" title="MonoDreams Logo" />
  <br>
</p>
<p align="center">
A code-first and data-driven opensource 2D game engine powered by MonoGame
</p>

![NuGet Version](https://img.shields.io/nuget/vpre/MonoDreams?link=https%3A%2F%2Fwww.nuget.org%2Fpackages%2FMonoDreams%2F)
![MIT License](https://img.shields.io/crates/l/mit?link=https%3A%2F%2Fgithub.com%2Froo-oliv%2Fmonodreams%2Fblob%2Fmain%2FLICENSE)

## Prerequisites

- .NET 8.0 SDK (or .NET 9+ with rollforward support)

## Setup & Build

### First-Time Setup

**On macOS:**
```bash
# Install .NET 8 SDK (if not already installed)
curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 8.0

# Add to PATH (add to ~/.zshrc or ~/.bash_profile for persistence)
export PATH="$HOME/.dotnet:$PATH"

# Clone and navigate to project
git clone https://github.com/roo-oliv/monodreams.git
cd monodreams
```

**On Windows:**
```powershell
# Install .NET 8 SDK from https://dotnet.microsoft.com/download/dotnet/8.0

# Clone and navigate to project
git clone https://github.com/roo-oliv/monodreams.git
cd monodreams
```

**Important:** When switching between Mac and Windows, you must update the reference paths in `MonoDreams.Examples/Content/Content.npl`:

- **Mac paths:** `/Users/[username]/.nuget/packages/...`
- **Windows paths:** `C:/Users/[username]/.nuget/packages/...`

### Build & Run

```bash
# Restore tools and packages
dotnet tool restore
dotnet restore

# Build the solution (compiles assets via nopipeline)
dotnet build

# Run the examples
dotnet run --project MonoDreams.Examples/MonoDreams.Examples.csproj
```

## About

This is a hobby project of mine.

With no roadmap commitment, this project's goal is to create an opensource 2D game engine on top of Monogame and DefaultECS loaded with common systems for 2D games such as input handling, HUD, dialogue system, camera movement, sprite renderer, level importer, gravity and jumping logics, AABB collision detection and resolution, and more.
MonoDreams is designed to be a code-first and data-driven engine, with a focus on ease of use and flexibility.

[You can follow my tentative roadmap here.](https://github.com/users/roo-oliv/projects/1/views/1)

# Special Thanks
This project is intended to support and enable the gamedev community and I hope one day it will be a good starting point for many people to create their own games.

But this is also a way to give back to this vibrant community. So I would like to thank the following people for their open contributions and for inspiring me to create this project:
 - [@MonoGame](https://github.com/MonoGame) (MonoGame Team) for their awesome work on [MonoGame](https://github.com/MonoGame/MonoGame)
 - [@craftworkgames](https://github.com/craftworkgames) (Craftwork Games) for their awesome work on [Monogame.Extended](https://github.com/craftworkgames/MonoGame.Extended)
 - [@prime31](https://github.com/prime31) (Prime31) for their awesome work on [Nez](https://github.com/prime31/Nez)
 - [@Doraku](https://github.com/Doraku) (Paillat Laszlo) for his awesome work on [DefaultECS](https://github.com/Doraku/DefaultEcs)
 - [@OneLoneCoder](https://github.com/OneLoneCoder) (Javidx9) for his [One Lone Coder Youtube Channel](https://www.youtube.com/channel/UC-yuWVUplUJZvieEligKBkA)
 - [@kyleschaub](https://github.com/kyleschaub) (Challacade) for his [Challacade Youtube Channel](https://www.youtube.com/@Challacade)
 - [@spavkov](https://github.com/spavkov) (Slobodan Pavkov) for his [My Public Interface blog](https://blog.roboblob.com/)
 - [@MaddyThorson](https://github.com/MaddyThorson) (Madeline Stephanie Thorson) for her [articles, codes, and tools](https://maddymakesgames.com/index.html#articles)
 - [@NoelFB](https://github.com/NoelFB) (Noel Berry) for his codes and [his blog](https://noelberry.ca/)
 - [@tkarras](https://github.com/tkarras) (Tero Karras) for his [NVIDIA Developer blog Posts](https://developer.nvidia.com/blog/author/tkarras/)
 - [@davidluzgouveia](https://github.com/davidluzgouveia) (David Gouveia) for his [contributions to GameDev StackExchange](https://gamedev.stackexchange.com/users/11686/david-gouveia)
 - [@BoardToBits](https://github.com/BoardToBits) for their [Board To Bits Games Youtube Channel](https://www.youtube.com/@BoardToBitsGames/featured)
 - [@deepnight](https://github.com/deepnight) (Sébastien Bénard) for his awesome work on [LDtk (Level Designer Toolkit)](https://ldtk.io/)
 - Mark Brown for his [Game Maker's Toolkit Youtube Channel](https://www.youtube.com/@GMTK/featured)
 - The Game Developers Conference for their [GDC Youtube Channel](https://www.youtube.com/@Gdconf)
 - My wife and my family for their support and patience ❤️