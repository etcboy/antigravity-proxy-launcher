# Antigravity 代理启动器

一个轻量的 Windows 工具，为 Antigravity 及其子进程设置代理环境变量。配置一次，以后通过桌面图标静默启动；不需要让启动器驻留后台。

A lightweight Windows launcher that starts Antigravity with per-process proxy environment variables. Configure once, then launch silently from a desktop shortcut.

[下载最新版本](https://github.com/etcboy/antigravity-proxy-launcher/releases/latest) · [使用说明](docs/usage.md) · [MIT 许可证](LICENSE)

![设置界面](docs/screenshot.png)

## 功能

- 支持提供 **HTTP / mixed 代理端口**的软件，不绑定特定代理客户端。
- 创建桌面“Antigravity（代理）”图标，日常点击后静默启动并退出。
- 在开始菜单创建“Antigravity 代理设置”，随时自行修改配置。
- 配置保存在当前用户 AppData，下载的 exe 旁边不生成配置文件。
- 测试 Google 登录接口连通性；启动失败时自动显示设置界面。
- 不修改 Antigravity 安装文件、原有快捷方式或系统代理设置。

## 快速开始

适用于 Windows 10 / 11，需要 .NET Framework 4.8 和已安装的 Antigravity。

1. 从 [Releases](https://github.com/etcboy/antigravity-proxy-launcher/releases/latest) 下载 `Antigravity-Proxy-Launcher.exe`。
2. 打开工具，选择 Antigravity 主程序，填写代理软件的 HTTP / mixed 地址和端口。
3. 可先点击“测试连接”，再点击“配置启动图标”。
4. 完全退出已运行的 Antigravity，以后使用桌面的 **“Antigravity（代理）”** 图标打开。

使用期间保持代理软件运行。浏览器登录也需要可用的网络连接，例如使用系统代理。

以后需要修改地址、端口或程序位置，在开始菜单搜索 **“Antigravity 代理设置”**，修改后点击“保存设置”。如果只更换代理软件，但地址和端口没变，不需要重新设置。

> 此工具自身不提供代理节点，也不强制接管程序流量。它依赖当前版本的 Antigravity 遵循 `HTTP_PROXY` / `HTTPS_PROXY` 等环境变量；是否适用于你的版本，应通过实际登录和 AI 对话验证。

## 工作原理

启动子进程时设置 `HTTP_PROXY`、`HTTPS_PROXY`、`ALL_PROXY`，并通过 `NO_PROXY=localhost,127.0.0.1,::1` 排除本机内部连接。

“配置启动图标”会把启动组件和设置保存到 `%LOCALAPPDATA%\AntigravityProxyLauncher\`。桌面图标指向该组件的 `--launch` 模式，开始菜单设置入口使用 `--settings`。完成配置后，移动最初下载的工具不会影响已生成的图标。

环境变量只传给启动的 Antigravity 及其子进程，不永久修改 Windows 用户或系统环境变量。本工具不读取账号密码或登录令牌。

## 从源码构建

在 Windows PowerShell 中运行：

```powershell
.\build.ps1
.\tests\run-tests.ps1
.\package.ps1 -SkipBuild
```

构建使用系统 .NET Framework 的 C# 编译器，无第三方运行时依赖。产物位于 `dist/`，其中 exe 可单独运行。测试使用本地模拟端口、子进程和临时快捷方式，不需要账号、代理订阅或 Google 连接，不会修改真实桌面或开始菜单。

推送 `v*` 标签会通过 GitHub Actions 构建、测试并发布 exe、便携压缩包和 SHA-256 校验文件。

## 限制

- 仅支持无需用户名和密码的 HTTP 代理端口，包括 mixed 端口；不能填写仅支持 SOCKS 的端口。
- 已经运行的 Antigravity 不会被重新设置环境，请先保存工作并退出。
- 连接测试仅验证网络路径，不验证账号资格、服务地区或 AI 功能。
- 软件未进行商业代码签名。

这是独立开源项目，与 Google 或 Antigravity 官方无关联。
