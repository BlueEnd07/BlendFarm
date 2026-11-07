# BlendFarm

BlendFarm is a network render farm for Blender. Use the spare computers on your local network to help render one image, an animation or a live viewport preview. Nothing needs to be installed in Blender, and there's no installer: unzip and run.

![BlendFarm rendering one image across several machines](.resources/example.gif)

## What it does

- **Splits renders across machines.** One image is cut into strips or tiles, and each computer (a "render node") renders its own share. Animations are shared out frame by frame.
- **Faster machines take more work.** Nodes ask for the next piece when they finish, so a fast GPU box isn't left waiting on a slow laptop. Failed pieces are retried, and idle nodes pick up leftover work near the end.
- **CPU and GPU.** Choose CPU, CUDA/OptiX, OpenCL, HIP, Metal or oneAPI for each node, or benchmark nodes to compare their speed.
- **Manages Blender for you.** Choose a Blender version and every node downloads it automatically. You can also use your own custom Blender build.
- **Finds nodes on its own.** Render nodes on the same network appear in the client automatically. If they don't, add them by IP address.
- **Live preview.** See the image come together as tiles finish, and re-render quickly while you tweak the scene.
- **Render queue.** Line up several jobs and let them run one after another. Pause, resume, schedule a start time, edit queued jobs and retry failed ones.
- **Animation tools.** Render every Nth frame or a frame list like `1-50, 75, 100-120`. See which frames failed and re-render only those. Optionally encode an MP4 with ffmpeg when the animation is done.
- **Preview and history.** Zoom and pan the result, compare before/after with a slider, and browse past renders with thumbnails.
- **Comfort features.** Drag a `.blend` onto the window, keyboard shortcuts (F1 lists them), presets, a filterable activity log, desktop notifications, and the PC stays awake while rendering.
- **Node control.** Turn a node off without removing it, act on several nodes at once, and nodes reconnect automatically after a dropout, with a plain reason when they can't connect.
- **Portable, no admin needed.** Runs from any folder without installing. Settings and Blender downloads live next to the app (or in `%LOCALAPPDATA%\BlendFarm` if that folder is read-only).

## How it works

There are two programs:

| Program | Run it on | What it does |
| --- | --- | --- |
| **BlendFarm** (desktop app) | Your main computer | Open a `.blend` file, choose settings, start renders and see the results. It also has a built-in render node, so it can render on its own. |
| **BlendFarm Server** (render node) | Every other computer that should help | Runs in a console window, waits for work from the desktop app and renders it with Blender. |

The desktop app sends the `.blend` file to each node, and every node renders its part and sends the pixels back. The app then puts the final image together.

## Install

Download the latest release from the [Releases page](https://github.com/BlueEnd07/BlendFarm/releases):

- `BlendFarm-<version>-win-x64-portable.zip` / `-linux-x64.zip`: the desktop app
- `BlendFarm-Server-<version>-win-x64-portable.zip` / `-linux-x64.zip`: the render node

> Use the same BlendFarm version on the desktop app and on every render node.

### Windows

The Windows zips are portable: no installer, no subfolder inside the zip. Unzip into a folder and run.

1. Unzip **BlendFarm** on your main computer and run `LogicReinc.BlendFarm.exe`. It never asks for firewall or admin access.
2. Unzip **BlendFarm Server** on each helper computer and run `LogicReinc.BlendFarm.Server.exe`.
3. Each render node needs **TCP port 15000** open for incoming connections, because the desktop app connects to it. Windows asks for this the first time an exe runs from a new folder. Either:
   - click **Allow** (needs an admin password once), or
   - have an admin double-click **`Allow BlendFarm through Firewall.bat`** (in the server zip) once. The rule is for the ports, not for one exe, so it keeps working after updates and from any folder.

**Updating:** unzip the new version **into the same folder** as the old one and overwrite. The exe keeps its path, so Windows keeps the permission it already gave and doesn't ask again.

If Windows SmartScreen warns about an unknown app, click **More info → Run anyway**.

### Linux

```bash
unzip BlendFarm-Server-<version>-linux-x64.zip
cd BlendFarm-Server-<version>-linux-x64
chmod +x LogicReinc.BlendFarm.Server
./LogicReinc.BlendFarm.Server
```

Do the same for the desktop app (`LogicReinc.BlendFarm`). Open TCP `15000` in your firewall (for example `sudo ufw allow 15000/tcp`).

If Blender fails to start on a node, it's usually missing system libraries. Installing your distro's `blender` package once pulls them all in.

### macOS

Build from source (see below), then allow TCP `15000` if your firewall asks. The macOS build is less tested than Windows and Linux.

## Your first render

1. Start **BlendFarm Server** on every helper computer.
2. Start **BlendFarm** on your main computer.
3. Click **Select File** and choose a `.blend` file. If you just want to see it work, use the small test scene at [`LogicReinc.BlendFarm.Tests/BlendFarmDemo.blend`](LogicReinc.BlendFarm.Tests/BlendFarmDemo.blend).
4. Choose a Blender version and click **Load Project**. The first time, each node downloads that Blender version, which can take a few minutes.
5. Check the node list. Your render nodes should show up. If one is missing, open the **Add node** tab and enter its address, for example `192.168.1.50:15000`.
6. Click **Connect All**, then **Sync All**, so every node gets the file.
7. Click **Render Now** for a still image, **Render Animation** for a frame range, or **Start Live Render** for a live preview.

### Render strategies (still images)

| Strategy | Best for |
| --- | --- |
| **SplitHorizontal** / **SplitVertical** | Final images. Each node renders whole strips of the frame. Fastest overall. |
| **SplitChunked** | Watching it render. Smaller tiles come back sooner, with a little more overhead. |
| **Chunked** | Very uneven machines. Many small tiles that any free node can take, but the most overhead. |

**Render Animation** always shares whole frames between the nodes.

## Settings

These files sit next to the app:

- `ClientSettings`: desktop app settings. Set `"ShareLocalNode": true` to let other computers use the desktop app's built-in node too. This needs TCP `15000` open on that computer.
- `ServerSettings`: render node settings, such as the port.

| Port | Used for |
| --- | --- |
| TCP `15000` | Desktop app ↔ render nodes |
| UDP `16342` | Finding render nodes on the network |

## Troubleshooting

**A render node doesn't show up.** Check that both computers are on the same network, then add the node by hand with `ip:15000`. Some networks (guest Wi-Fi, VPNs) block automatic discovery.

**A node shows up but won't connect.** TCP `15000` is blocked on that node. Open it in the firewall.

**Textures are missing in the render.** Pack them into the file in Blender with **File → External Data → Pack Resources**, then save and sync again.

**Rendering is slower than on one machine.** Network transfer and Blender start-up cost time. BlendFarm pays off on heavier scenes. For tiny test renders, a single machine is often faster.

**Linux node fails to render.** Install your distro's `blender` package to get the missing libraries, even though BlendFarm uses its own Blender download.

## Build from source

You need the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
git clone https://github.com/BlueEnd07/BlendFarm.git
cd BlendFarm
dotnet build LogicReinc.BlendFarm.sln
dotnet test LogicReinc.BlendFarm.sln
```

Make release builds (change `-r` to `linux-x64`, `osx-x64` or `osx-arm64` as needed):

```bash
dotnet publish LogicReinc.BlendFarm        -c Release -r win-x64 -p:PublishSingleFile=true --self-contained true
dotnet publish LogicReinc.BlendFarm.Server -c Release -r win-x64 -p:PublishSingleFile=true --self-contained true
```

Or build all the release zips (portable Windows zips and Linux zips) into `Deploy\Release` on Windows:

```powershell
.\build-release.ps1 -Version 2.1.0
```

### Project layout

| Folder | Contents |
| --- | --- |
| `LogicReinc.BlendFarm` | Desktop app (Avalonia) |
| `LogicReinc.BlendFarm.Server` | Render node |
| `LogicReinc.BlendFarm.Client` | Render scheduling and node management used by the app |
| `LogicReinc.BlendFarm.Shared` | Network protocol and shared models |
| `LogicReinc.BlendFarm.Tests` | Tests and the small demo scene |

## Safety

BlendFarm doesn't run Python scripts embedded in `.blend` files. Only open and render files you trust on machines you control.

## License

GPL-3.0. See [LICENSE](LICENSE). BlendFarm began as a fork of the open-source LogicReinc BlendFarm project and has since been extensively rewritten.
