import AppKit
import Darwin

final class AppDelegate: NSObject, NSApplicationDelegate, NSMenuDelegate {
    var statusItem: NSStatusItem!
    let stateItem = NSMenuItem(title: "连接状态：正在启动…", action: nil, keyEquivalent: "")
    let networkItem = NSMenuItem(title: "NetBird：检查中…", action: nil, keyEquivalent: "")
    let localItem = NSMenuItem(title: "本机监听：100.64.25.71:55516", action: nil, keyEquivalent: "")
    let remoteItem = NSMenuItem(title: "Windows 地址：100.64.79.70:55515", action: nil, keyEquivalent: "")
    let eventItem = NSMenuItem(title: "最近连接事件：暂无", action: nil, keyEquivalent: "")
    var worker: Process?
    var enabled = true
    var retryAfter = Date.distantPast
    var timer: Timer?
    var lockFD: Int32 = -1
    var logOffset: UInt64 = 0
    var peerConnected = false
    var lastEvent = "暂无"
    let peerID = "12D3KooWLF8kaRr1Gg84RFAiDhLnwHk23rM9ASkuyYcqqiBZU46r"
    let logs = FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Library/Logs/P2PClipboard")

    func applicationDidFinishLaunching(_ notification: Notification) {
        do { try FileManager.default.createDirectory(at: logs, withIntermediateDirectories: true) }
        catch { fail("无法创建日志目录：\(error.localizedDescription)"); return }
        lockFD = open(logs.appendingPathComponent("app.lock").path, O_CREAT | O_RDWR, 0o600)
        guard lockFD >= 0, flock(lockFD, LOCK_EX | LOCK_NB) == 0 else { NSApp.terminate(nil); return }
        _ = fcntl(lockFD, F_SETFD, FD_CLOEXEC)
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        statusItem.button?.image = NSImage(systemSymbolName: "doc.on.clipboard", accessibilityDescription: "P2P Clipboard")
        let menu = NSMenu()
        menu.addItem(NSMenuItem(title: "P2P Clipboard", action: nil, keyEquivalent: ""))
        menu.delegate = self
        menu.addItem(stateItem)
        menu.addItem(.separator())
        menu.addItem(networkItem)
        menu.addItem(localItem)
        menu.addItem(remoteItem)
        menu.addItem(eventItem)
        menu.addItem(.separator())
        for (title, action) in [("启动 / 重试", #selector(start)), ("停止同步", #selector(stop)), ("查看日志", #selector(showLogs)), ("退出", #selector(quit))] {
            let item = NSMenuItem(title: title, action: action, keyEquivalent: "")
            item.target = self
            menu.addItem(item)
        }
        statusItem.menu = menu
        tick()
        timer = Timer.scheduledTimer(withTimeInterval: 3, repeats: true) { [weak self] _ in self?.tick(); self?.refreshConnection() }
    }
    func menuWillOpen(_ menu: NSMenu) {
        if worker != nil { refreshConnection() }
        networkItem.title = networkReady() ? "NetBird：已就绪" : "NetBird：未就绪"
    }
    func setState(_ text: String, label: String = "未连接", color: NSColor = .systemOrange) {
        stateItem.title = "连接状态：" + text
        networkItem.title = networkReady() ? "NetBird：已就绪" : "NetBird：未就绪"
        statusItem.button?.toolTip = "P2P Clipboard：" + text
    }
    func refreshConnection() {
        guard let worker = worker, worker.isRunning else { return }
        guard networkReady() else {
            peerConnected = false
            setState("NetBird 地址不可用", label: "等待网络")
            return
        }
        let path = logs.appendingPathComponent("p2p-clipboard.log").path
        guard let file = try? FileHandle(forReadingFrom: URL(fileURLWithPath: path)) else { return }
        defer { try? file.close() }
        let end = (try? file.seekToEnd()) ?? logOffset
        if end < logOffset { logOffset = 0; peerConnected = false }
        guard end > logOffset else { return }
        do {
            try file.seek(toOffset: logOffset)
            let data = try file.read(upToCount: Int(min(end - logOffset, 1_048_576))) ?? Data()
            logOffset += UInt64(data.count)
            for line in String(decoding: data, as: UTF8.self).components(separatedBy: .newlines) {
                if line.contains("Connected to peer \(peerID)") {
                    peerConnected = true
                    lastEvent = "最近连接事件：已连接（\(DateFormatter.localizedString(from: Date(), dateStyle: .none, timeStyle: .medium))）"
                }
                if line.contains("Peer \(peerID) has disconnected") {
                    peerConnected = false
                    lastEvent = "最近连接事件：已断开（\(DateFormatter.localizedString(from: Date(), dateStyle: .none, timeStyle: .medium))）"
                }
            }
            eventItem.title = lastEvent
            setState(peerConnected ? "● 已连接 Windows" : "○ 未连接 Windows",
                     label: peerConnected ? "已连接" : "未连接",
                     color: peerConnected ? .systemGreen : .systemOrange)
        } catch { setState("无法读取连接日志") }
    }
    func networkReady() -> Bool {
        var head: UnsafeMutablePointer<ifaddrs>?
        guard getifaddrs(&head) == 0 else { return false }
        defer { freeifaddrs(head) }
        var cursor = head
        while let current = cursor {
            defer { cursor = current.pointee.ifa_next }
            guard let addr = current.pointee.ifa_addr, addr.pointee.sa_family == UInt8(AF_INET) else { continue }
            var host = [CChar](repeating: 0, count: Int(NI_MAXHOST))
            if getnameinfo(addr, socklen_t(addr.pointee.sa_len), &host, socklen_t(host.count), nil, 0, NI_NUMERICHOST) == 0,
               String(cString: host) == "100.64.25.71" { return true }
        }
        return false
    }
    func tick() {
        guard enabled, worker == nil, Date() >= retryAfter else { return }
        guard networkReady() else { setState("等待 NetBird 上线…"); return }
        // Avoid a second engine if the old terminal launcher is still running.
        let check = Process()
        check.executableURL = URL(fileURLWithPath: "/usr/bin/pgrep")
        check.arguments = ["-x", "p2p-clipboard"]
        check.standardOutput = FileHandle.nullDevice
        check.standardError = FileHandle.nullDevice
        do { try check.run(); check.waitUntilExit() } catch { setState("无法检查现有进程"); return }
        guard check.terminationStatus == 1 else {
            setState(check.terminationStatus == 0 ? "原脚本的同步进程正在运行" : "无法检查现有进程")
            return
        }
        let process = Process()
        process.executableURL = Bundle.main.url(forResource: "p2p-clipboard", withExtension: nil)
        process.currentDirectoryURL = FileManager.default.homeDirectoryForCurrentUser
        var environment = ProcessInfo.processInfo.environment
        environment["RUST_LOG"] = "info"
        process.environment = environment
        process.arguments = ["--listen", "100.64.25.71:55516", "--connect", "100.64.79.70:55515", "12D3KooWLF8kaRr1Gg84RFAiDhLnwHk23rM9ASkuyYcqqiBZU46r", "--no-mdns"]
        do {
            let output = try logHandle("p2p-clipboard.log")
            let errors = try logHandle("p2p-clipboard-error.log")
            defer { try? output.close(); try? errors.close() }
            process.standardOutput = output
            process.standardError = errors
            process.terminationHandler = { [weak self] exited in
                DispatchQueue.main.async {
                    guard let self = self, self.worker === exited else { return }
                    self.worker = nil
                    self.peerConnected = false
                    self.retryAfter = Date().addingTimeInterval(10)
                    self.setState(self.enabled ? "进程已退出（\(exited.terminationStatus)），10 秒后重试" : "已停止")
                }
            }
            logOffset = (try? FileManager.default.attributesOfItem(atPath: logs.appendingPathComponent("p2p-clipboard.log").path)[.size] as? UInt64) ?? 0
            peerConnected = false
            lastEvent = "暂无"
            eventItem.title = "最近连接事件：暂无"
            try process.run()
            worker = process
            setState("○ 未连接 Windows")
        } catch { retryAfter = Date().addingTimeInterval(10); setState("启动失败：\(error.localizedDescription)") }
    }
    func logHandle(_ name: String) throws -> FileHandle {
        let url = logs.appendingPathComponent(name)
        if !FileManager.default.fileExists(atPath: url.path) { FileManager.default.createFile(atPath: url.path, contents: nil) }
        let handle = try FileHandle(forWritingTo: url)
        try handle.seekToEnd()
        try handle.write(contentsOf: Data("\n[\(Date())] App 启动同步进程\n".utf8))
        return handle
    }
    @objc func start() { enabled = true; retryAfter = .distantPast; tick() }
    @objc func stop() { enabled = false; peerConnected = false; if let worker = worker, worker.isRunning { worker.terminate() }; setState("已停止 App 管理的同步进程", label: "已停止", color: .secondaryLabelColor) }
    @objc func showLogs() { NSWorkspace.shared.open(logs) }
    @objc func quit() { NSApp.terminate(nil) }
    func applicationWillTerminate(_ notification: Notification) { timer?.invalidate(); stop(); if lockFD >= 0 { close(lockFD) } }
    func fail(_ message: String) { let alert = NSAlert(); alert.messageText = message; alert.runModal(); NSApp.terminate(nil) }
}
let app = NSApplication.shared
let delegate = AppDelegate()
app.delegate = delegate
app.setActivationPolicy(.accessory)
app.run()
