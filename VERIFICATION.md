# 并语 0.1.10 · 验证记录

验证环境：Windows 11 x64；轻量版使用已安装的 .NET 10 Desktop Runtime 与 Microsoft Edge WebView2 Runtime。发布版另提供自带 .NET 的 Windows x64 包，两种包都不捆绑 WebView2。

- `dotnet run --project tests/Bingyu.Core.Tests -c Release`：16 项通过，包括 10,000 次随机标签与分屏操作。
- `scripts/Test-Desktop.ps1`：19 项通过。分别用轻量版、自带 .NET 版的发布程序运行过；检查原生首页、2/3/4 分屏、WebView2 加载、共享 Cookie、独立 DOM、通知权限、专注模式与最大化边界、拖动分屏、暂停恢复、弹窗、热键和配置持久化。
- 工程、命名空间、图标资源和构建脚本统一为 Bingyu；新装使用新的数据目录，已有旧版数据继续沿用。

桌面测试使用隔离数据目录和本地 HTTP 网页，不包含真实 AI 网站账号。真实网站的登录限制和网页布局可能随服务商更新。发布 ZIP 不包含用户配置、Cookie 或聊天内容。
