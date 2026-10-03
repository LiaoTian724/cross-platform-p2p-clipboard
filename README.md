# P2P Clipboard：Mac ↔ Windows 文字剪贴板同步

这是基于 [gnattu/p2p-clipboard](https://github.com/gnattu/p2p-clipboard) 核心程序做的**个人场景二次封装**：增加了 macOS 菜单栏 App、Windows 状态窗口/托盘 App、启动与停止脚本，以及一套已经验证过的跨网络连接参数。剪贴板同步核心仍来自原项目；本仓库没有修改其 Rust 源码，也不是原项目的官方发行版。

原项目主要介绍同一局域网内通过 mDNS 自动发现的用法。在我的 Mac 与 Windows 处于不同网络的场景里，仅分别运行核心程序不能让它们互相发现，因此曾出现“程序在运行，但没有同步”的情况。本封装改为**先用 NetBird 建立两台设备间可访问的虚拟网络**，再用固定的 NetBird IP、监听端口和 Windows Peer ID 手动连接，并关闭 mDNS。原项目本身并非必须使用 NetBird；**本仓库当前这套写死地址的配置必须使用 NetBird**。换了设备或 NetBird 地址，需要修改配置后重新构建。

## 能做什么

- Mac 和 Windows 双向同步**纯文字**，包括普通文本、网址和代码；富文本格式不会保留。
- 不传输文件、图片或 ZIP 压缩包的内容。上游核心的单条网络载荷上限约为**压缩后 64 KB**。[上游限制说明](https://github.com/gnattu/p2p-clipboard#limitation)
- 两台设备可连接不同 Wi-Fi；只要 NetBird 中的两端能互通即可。NetBird 可能通过中继建立通道，不保证始终直连。
- Mac 菜单栏和 Windows 状态窗口显示核心进程及 peer 连接日志的状态，并提供启动、停止与日志入口。

## 仓库结构

```text
P2PClipboard/
├── macos/                 # macOS 菜单栏封装、构建脚本和核心程序
│   ├── bin/
│   ├── scripts/
│   └── src/
├── windows/               # Windows 窗口/托盘封装、脚本和核心程序
│   ├── bin/
│   └── scripts/
├── THIRD_PARTY_LICENSES/  # 上游核心程序的许可证
└── README.md
```

`macos/` 与 `windows/` 是同一套同步方案的两个客户端封装，放在同一个 GitHub 仓库中；它们不是互相嵌套的项目。

## 已验证的连接配置

| 角色 | NetBird 地址与端口 | 启动方式 |
| --- | --- | --- |
| Windows（监听） | `100.64.79.70:55515` | `--listen 100.64.79.70:55515 --no-mdns` |
| Mac（主动连接） | `100.64.25.71:55516` | `--listen 100.64.25.71:55516 --connect 100.64.79.70:55515 <WINDOWS_PEER_ID> --no-mdns` |

Mac 源码中配置了当前 Windows 的 Peer ID。这个 ID 由 Windows 核心程序报告，**不是任意 Windows 电脑通用的 ID**。Windows 状态窗口会显示本机 Peer ID；如果不同，须更新 [`macos/src/main.swift`](macos/src/main.swift) 并重新构建。Windows 界面中识别 Mac 的 Peer ID 写在 [`windows/scripts/P2PClipboardApp.cs`](windows/scripts/P2PClipboardApp.cs) 中；更换 Mac 时需同步修改并重新构建 Windows 界面。

这些地址与 Peer ID 是当前两台机器的示例配置。公开仓库中的它们并不是密码，但不能直接用于其他人的设备。

## 准备工作

1. 在 Mac 和 Windows 安装 NetBird，并让两台设备进入**同一个 NetBird 网络**。用两端的 `netbird status -d` 检查能看到对方。管理和信令服务显示 Connected，只表示已连到 NetBird 服务；还要看对方 peer 是否 Connected。
2. 确认各自的 NetBird IP 与上表一致。Windows 需要允许 Mac 通过 NetBird 访问 TCP `55515`（包括 NetBird 策略和 Windows 防火墙）。
3. 确认两端的 `p2p-clipboard` 核心程序都存在。本仓库的 `macos/bin/p2p-clipboard` 是 Mac 核心，`windows/bin/p2p-clipboard.exe` 是 Windows 核心；图形启动器只是调用它们。

### Windows

把 [`windows`](windows) 整个文件夹放在 Windows 本地磁盘，双击其中的 `P2P Clipboard.exe`。它会等待 Windows NetBird 地址出现，然后监听 `100.64.79.70:55515`。窗口右上角关闭按钮会隐藏到托盘，**不会停止同步**；用窗口或托盘菜单中的“退出”才会停止。`status.cmd` 可查看状态，`stop.cmd` 可退出程序，日志在该文件夹的 `logs/` 中。

Windows 图形启动器源码在 [`windows/scripts/P2PClipboardApp.cs`](windows/scripts/P2PClipboardApp.cs)。若修改了 Windows 地址、端口或 Mac Peer ID，可在 Windows PowerShell 中运行 `windows\scripts\build-app.ps1` 重新构建 `P2P Clipboard.exe`；该脚本使用 Windows 的 .NET Framework 4 编译器。移动文件夹不会影响直接双击 `P2P Clipboard.exe` 启动。

### Mac

先启动 Windows 监听端。Mac 上运行 [`macos/scripts/build-macos-app.sh`](macos/scripts/build-macos-app.sh) 构建 `macos/app/P2P Clipboard.app`，然后把该 App 复制到 `/Applications` 并双击启动。需要 macOS Command Line Tools。App 常驻菜单栏，点击剪贴板图标查看“已连接 Windows / 未连接 Windows”、NetBird 地址状态、两端地址与最近连接事件；也可停止、启动或打开日志。Mac App 日志在 `~/Library/Logs/P2PClipboard/`。

Mac 启动器的地址和 Peer ID 位于 [`macos/src/main.swift`](macos/src/main.swift)，旧的命令行启动脚本在 [`macos/scripts/start-p2p-clipboard.sh`](macos/scripts/start-p2p-clipboard.sh)。修改任一配置后需重新构建并替换 `/Applications/P2P Clipboard.app`。构建脚本使用本机临时签名；App 没有 Developer ID 签名或公证。

## 连接慢或无法同步时

1. 两端先看 `netbird status -d` 中对方 peer 的状态。若长时间停在 `Connecting`、没有 WireGuard 握手，先排查 NetBird 的网络通道和 relay。`0/1 Connected` 在 Lazy connection 模式下单独不足以判断故障。
2. 确认 Windows 状态窗口显示“正在监听”；在 Mac 运行 `nc -vz 100.64.79.70 55515`。端口超时，先检查 NetBird、Windows 防火墙及监听程序。
3. 端口可达但 App 仍未连接，核对 Windows 窗口显示的 Peer ID 与 Mac 配置是否一致，并查看两端日志是否出现 `Connected to peer`。Mac App 的“NetBird 已就绪”目前只检查本机 NetBird IP，**不保证对方 peer 已接通**。
4. `Query finished with no closest peers` 或复制时的 `NoPeersSubscribedToTopic` 可以在对端尚未连接时出现；仅凭这两行不能判定程序损坏。

在已有一次实测中，Mac 的 NetBird relay 曾超时，peer 保持 `Connecting`；重新连接 NetBird 后，两端先建立了 NetBird 中继连接，随后核心程序才记录 `Connected to peer`。**两端先能互通，再看剪贴板程序**，可避免把网络故障误判为 App 故障。

## 目录与来源

- `macos/src/`、`macos/scripts/`：Mac 菜单栏封装源码与构建/调试脚本。
- `windows/scripts/`：Windows 图形封装源码、状态与停止脚本、构建脚本。
- `macos/bin/p2p-clipboard`、`windows/bin/p2p-clipboard.exe`：来自上游项目的核心程序；`windows/P2P Clipboard.exe` 是本仓库 Windows 图形封装的构建产物。
- `app/`、`.build/`、`logs/`、`data/`、`*.lnk`：本机构建产物或运行数据，已由 `.gitignore` 排除。运行日志可能含有剪贴板内容，不应上传。

上游核心项目使用 MIT 许可证；其原始版权声明见 [`THIRD_PARTY_LICENSES/gnattu-p2p-clipboard-LICENSE`](THIRD_PARTY_LICENSES/gnattu-p2p-clipboard-LICENSE)。本仓库的 Mac/Windows 封装不是上游官方客户端。
