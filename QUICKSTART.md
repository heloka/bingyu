# 并语 · Windows 快速开始

1. 在 [GitHub Releases](https://github.com/heloka/bingyu/releases) 下载 `Bingyu-win-x64-self-contained.zip`。它自带 .NET，适合首次在另一台 Windows 10/11 x64 电脑使用。若电脑已经安装 .NET 10 Desktop Runtime，也可以选择体积更小的 `Bingyu-win-x64.zip`。
2. 将 ZIP **完整解压**到一个普通文件夹，例如 `D:\Apps\Bingyu`，再双击其中的 `Bingyu.exe`。不要直接在压缩包预览窗口里运行。
3. 如果程序提示缺少 WebView2 Runtime，安装微软的 [WebView2 Evergreen Runtime](https://developer.microsoft.com/microsoft-edge/webview2/#download-section) 后重试。轻量版若提示缺少 .NET，请安装 [.NET 10 Desktop Runtime（Windows x64）](https://dotnet.microsoft.com/download/dotnet/10.0)。
4. 在各 AI 网站的网页里分别登录。新电脑不会自动带入旧电脑的网页登录状态。

应用配置保存在 `%LOCALAPPDATA%\Qiye\`；这是改名前保留的数据目录，发布包不含任何人的账号、Cookie 或聊天内容。以后更新时，退出程序，解压新版覆盖程序文件即可，配置仍会保留。需要彻底退出时，右键系统托盘中的并语图标，选择「退出并语」。

更多操作和快捷键见同目录的「使用说明.md」；在源码仓库中可查看 `README.md`。
