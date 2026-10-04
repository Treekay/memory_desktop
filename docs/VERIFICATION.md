# 第一版验证记录

环境：Windows 11 25H2，10.0.26200.0，x64，.NET Framework Release 533509。验证日期：2026-10-04 UTC（桌面本地日期可能是 10 月 5 日）。

- 使用已有 `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe` 编译，开启优化、warning level 4 和 warnings as errors；无需安装依赖。
- `build.ps1 -Test`：47 项隔离合成测试全部通过，覆盖带 EXIF 旋转元数据的 JPEG。报告：`test-results/test-report.txt`。测试不访问用户相册，不写登录启动、不挂载桌面。
- `--preview` 运行 70 秒的真实 WPF 窗口并自动退出，输出 `test-results/preview/` 的渲染帧。人工检查 16 秒与 40 秒帧：照片宽高比完整，边缘柔化，两个片段交叠，四周留白。计数在 4/16/28/40/52/64 秒分别为 1/1/2/3/2/3；工作集约 128–164 MiB，句柄约 877–885。一次短预览不能证明长期内存稳定。
- `--diagnose-host` 为只读检查：本环境当时没有现成符合规则的 WorkerW。没有发送创建宿主消息，因此没有验证真实桌面挂载是否成功。
- 没有运行正常桌面模式、设置系统壁纸、重启 Explorer、写登录启动、上传照片或执行 Git push。

尚需在用户授权启动桌面会话后验证：图标后方层级与鼠标穿透、托盘菜单/文件夹对话框、当前用户登录启动切换、Explorer 重启恢复。Windows 10、多屏混合 DPI、热插拔、锁屏和全屏自动暂停尚待实机验收。

Git 权限证据：SSH `ls-remote` 与空仓库 clone 成功；`ssh -T` 返回已认证为 `Treekay`，与仓库所有者一致。没有测试远端写入，也没有 dry-run push。GitHub App 连接器对此私有仓库返回 404，连接器可见性与本机 SSH 认证是两条独立路径。
