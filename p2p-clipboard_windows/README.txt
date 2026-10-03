P2P Clipboard - Windows

启动：双击本目录的“P2P Clipboard.exe”或“启动 P2P Clipboard.lnk”。
打开后自动等待 NetBird 地址 100.64.79.70，并监听 100.64.79.70:55515。

状态窗口和系统托盘菜单显示：
连接状态、NetBird、本机监听地址、Mac 地址、Windows Peer ID、最近连接事件。
“等待 Mac 连接”表示本机正在监听，尚未收到 Mac 的连接成功事件。
“已连接 Mac”依据当前同步进程的 Connected to peer 日志事件显示；断开时自动更新。
软件不会把旧日志中的连接记录当作当前连接。

启动 / 重试：恢复同步或立即重试。
停止同步：暂停同步，状态窗口和托盘图标继续保留。
查看日志：打开本目录 logs 文件夹。
退出软件：退出界面并停止它启动的同步核心。
关闭窗口的 X：隐藏到系统托盘，同步继续；双击托盘图标可重新打开窗口。
重复双击软件会显示现有窗口，不会启动第二个实例。
核心异常退出后等待 5 秒自动重试；NetBird 未上线时自动等待。

Windows 软件启动后，在 Mac 打开 /Applications/P2P Clipboard.app。
Mac 应连接 Windows 100.64.79.70:55515，Windows Peer ID 为：
12D3KooWLF8kaRr1Gg84RFAiDhLnwHk23rM9ASkuyYcqqiBZU46r
Windows 识别的 Mac Peer ID 来自 P2PClipboard_mac 的运行日志：
12D3KooWBgMhRLxRLAcQPZjiJAjVJoLDY3ankxxN1jTXLok1hRtK
如果 Mac 的身份变化，连接会显示为“已连接其他设备”，最近事件会列出新 Peer ID。
连接后用一段纯文本在两台设备间分别复制粘贴，验证双向同步。

bin：Windows 核心程序。scripts：管理脚本、界面源代码和图标。
logs：普通/错误日志及实时状态。data：临时数据。
P2PClipboard_mac：用于参考的 Mac 版本。
status.cmd：命令行查看状态。stop.cmd：完全退出 Windows 软件和核心。
如果移动整个文件夹，双击“创建启动快捷方式.cmd”更新快捷方式。
所有项目文件、日志和运行数据保存在当前 F 盘项目目录。