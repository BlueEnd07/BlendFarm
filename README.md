# BlendFarm

BlendFarm is a stand-alone network renderer for Blender. It lets multiple machines on the same network help render one image, animation, or live preview without installing a Blender add-on.

## Maintainer Note

The original LogicReinc BlendFarm project is no longer actively maintained. This repository is being updated as a continuation effort, with new fixes and features being added on top of the original LogicReinc codebase.

![BlendFarm render preview](https://raw.githubusercontent.com/LogicReinc/LogicReinc.BlendFarm/master/.resources/example.gif)

## Features

- Distributed CPU and GPU rendering across render nodes
- Stand-alone Avalonia desktop client
- Headless render-node server
- Automatic Blender version download and management
- Image, animation, chunked, split, and live-update render workflows
- LAN node discovery over UDP, with manual IP entry when discovery is blocked
- Render queue support

## Downloads

Download the latest client and server builds from:

https://github.com/LogicReinc/LogicReinc.BlendFarm/releases

Use the GUI client on the machine where you manage projects. Use the server build on render-only machines.

## Quick Start

1. Download and extract the GUI client on your main machine.
2. Download and extract the server on each render node.
3. Start the render-node server.
4. Start the GUI client.
5. Allow BlendFarm through your firewall when prompted.
6. Open a `.blend` file in the GUI, choose the Blender version and render settings, then render.

Render nodes should appear automatically. If they do not, add them manually with:

```text
192.168.1.123:15000
```

Default ports:

- TCP `15000` for render-node communication
- UDP `16342` for LAN discovery

## Platform Notes

### Windows

- Run `LogicReinc.BlendFarm.exe` for the GUI.
- Run `LogicReinc.BlendFarm.Server.exe` for a render node.
- Allow the app through Windows Firewall.

### Linux

- Make the extracted files executable if needed.
- Allow TCP `15000` and UDP `16342` through your firewall.
- If rendering fails because Blender dependencies are missing, install your distro's Blender package to pull in the required system libraries.
- If image handling fails, install `libgdiplus` and `libc6-dev`.

### macOS

- Run the GUI package or command file from the extracted release.
- Allow TCP `15000` and UDP `16342` if your firewall blocks local network traffic.

## Build From Source

Requirements:

- .NET 8 SDK
- Blender only if you want to test local rendering outside the bundled/downloaded Blender flow

Build:

```bash
dotnet build LogicReinc.BlendFarm.sln
```

Run tests:

```bash
dotnet test LogicReinc.BlendFarm.sln
```

Publish examples:

```bash
dotnet publish LogicReinc.BlendFarm -f net8.0 -c Release -r win-x64 -p:PublishSingleFile=true --self-contained true
dotnet publish LogicReinc.BlendFarm.Server -f net8.0 -c Release -r linux-x64 -p:PublishSingleFile=true --self-contained true
```

## Repository Layout

- `LogicReinc.BlendFarm` - Avalonia GUI application
- `LogicReinc.BlendFarm.Server` - headless render-node server
- `LogicReinc.BlendFarm.Client` - client-side render management
- `LogicReinc.BlendFarm.Shared` - shared protocol and models
- `LogicReinc.BlendFarm.Tests` - unit tests and sample blend file
- `BlenderData` - Blender scripts and locally cached Blender builds

## Common Issues

**Nodes are not discovered automatically**

Check that the machines are on the same LAN and UDP `16342` is not blocked. If discovery still fails, add the node manually using `ip:port`.

**The render node will not connect**

Check the target machine's firewall and confirm TCP `15000` is open.

**Textures are missing**

Pack external assets into the `.blend` file with Blender's `File > External Data > Automatically Pack into .blend`.

**Rendering is slower than expected**

Use `Split` for final renders and `SplitChunked` for live previews. Chunked rendering has more overhead because Blender work is split into smaller pieces.

**Linux rendering fails**

Install Blender system dependencies. Installing Blender from your package manager is often enough, even if BlendFarm later downloads another Blender version.

## Notes

BlendFarm runs Blender in a controlled environment and does not execute scripts embedded in `.blend` files by default. This is intentional for safety.

The project is licensed under the GPL-3.0 license. See [LICENSE](LICENSE).
