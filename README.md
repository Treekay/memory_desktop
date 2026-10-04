# Memory Desktop · 记忆浮光

Windows 托盘式照片动态壁纸第一版。照片在深色留白中慢慢显现，轻微漂浮，然后与下一段记忆重叠消散。桌面模式是 Explorer 图标后方的原生子窗口，桌面图标仍可点击。没有主界面、账号、网络请求或照片上传。

## 运行

1. 解压 `MemoryDesktop-win-x64.zip` 到长期保留的本地目录，双击 `MemoryDesktop.exe`。无需管理员权限。
2. 在通知区域找到青绿色照片图标（可能在隐藏图标 `^` 内），右键 → **选择照片文件夹…**。第一次启动不读取照片、不显示主窗口，直到你选择文件夹。
3. 右键菜单提供 **暂停 / 继续**、**登录 Windows 时启动**、**退出**。登录启动默认关闭，只在你点击该项时修改当前用户的启动项。
4. 退出后原有 Windows 壁纸仍在；程序不修改 Windows 的静态壁纸设置。删除程序前先取消登录启动。

支持 JPG/JPEG、PNG、BMP、GIF、TIF/TIFF；GIF/TIFF 只使用第一帧。仅读取选定文件夹的直接文件，不含子目录。HEIC、RAW、视频暂不支持。不同宽高比完整缩放呈现，不做强制裁切。空文件夹会等待新增照片；损坏或过大的文件会跳过。文件夹消失时已有照片逐渐结束，可重新选择文件夹；移动盘恢复会在后台重试。

## 行为与资源边界

- 每张照片约 46 秒，12 秒显现、末段 14 秒消散；约每 18 秒出现下一张，最多同时三张。位移约 12 像素以内，缩放增量约 1.5%，保留大量留白。
- 每个屏幕独立构图；最多 8 个屏幕。所有屏幕共用一个解码队列，单张最长边最多 1600 像素，编码文件最多 64 MiB，原始像素最多 1.2 亿且任一边不超过 30000。
- 不缓存整个相册；最多保存 4096 个路径，大型文件夹用随机蓄水池取样。每个屏幕最多 3 张冻结位图，单个解码在途；不同编码器还可能使用短暂的内部解码内存。超大相册的第一次枚举仍需时间，但在后台进行。
- 解码后关闭文件流，允许图片移动、删除。切换文件夹会取消旧来源、丢弃迟到的解码结果，并清空旧照片；读取新文件夹失败则保留原来源。
- 正常约 30 FPS；手动暂停、锁屏、睡眠或前台窗口覆盖整屏时冻结时间线并降低调度频率。退出释放窗口、托盘、文件监视器。文件变动去抖 2 秒，另每约 2 分钟重新检查所选文件夹。
- 设置位于 `%LOCALAPPDATA%\MemoryDesktop\settings.xml`，包含本地路径和暂停/登录启动偏好。启动项位于当前用户 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\MemoryDesktop`；启动时只读取，不主动创建、修复或开启它。

## 兼容性与桌面宿主限制

目标：Windows 10 / 11 x64，.NET Framework 4.8（常见 Windows 10/11 系统已包含；不使用 .NET SDK 作为运行依赖）。本次实际验证环境是 **Windows 11 25H2，内部版本 26200，.NET Framework Release 533509**。Windows 注册表可能仍把 ProductName 显示为 Windows 10，应以内部版本/DisplayVersion 区分。

Explorer 的 `0x052C` 消息和 `WorkerW` 壁纸层是**未公开、非官方支持的宿主机制**。代码兼顾传统顶层 WorkerW 与 Windows 11 24H2 之后的 Progman 子层结构；Windows 更新、第三方桌面工具、远程桌面或不同 DPI 的跨进程重设可能影响挂载。挂载失败时不会退回覆盖桌面的顶层窗口，会提示并保留在托盘自动重试。

每个显示器使用物理屏幕边界定位，清单声明 PerMonitorV2；支持负坐标显示器和屏幕布局变化时重建。Explorer 重启通过 TaskbarCreated 和宿主存活检查尝试恢复。混合 DPI、多屏热插拔、Explorer 重启和真实图标后方会话的完整人工验证仍需进行；本次开发只运行不会连接桌面的临时预览，未修改用户壁纸，未重启 Explorer，未写入登录启动。

支持常见 JPEG/TIFF 的 EXIF 方向（旋转及镜像），并用带方向元数据的合成 JPEG 验证旋转。HDR/色彩管理、动态虚拟桌面差异、第三方 Shell 的适配暂未专项验证。前台覆盖判断是矩形近似；透明全屏窗口也会触发暂停。非常极端的图片在 Windows 内置编解码器内部仍可能产生临时内存峰值。

## 开发和验证

使用 Windows 内置 .NET Framework C# 编译器，不需要 NuGet 或外部依赖：

```powershell
./build.ps1 -Test
```

输出：`artifacts/MemoryDesktop-win-x64/MemoryDesktop.exe` 和对应 ZIP。也提供 `src/MemoryDesktop/MemoryDesktop.csproj`，安装 .NET Framework 4.8 开发目标包的 Visual Studio/MSBuild 可打开；本环境没有安装开发目标包，因此实际构建由 `build.ps1` 引用已安装运行时程序集完成。代码保持 C# 5 兼容。

测试使用程序生成的合成风景图，输出到忽略的 `test-results/`。涵盖损坏/空/缺失来源、取消、非递归选择、随机循环、解码上限、文件句柄释放、暂停、淡入淡出连续性、宽高比、三张上限和隔离设置持久化。测试不写启动项、不发送 Explorer 创建宿主消息。

非持久临时预览（会出现普通预览窗口，默认 40 秒后退出，不创建托盘、不保存设置、不挂载 Explorer）：

```powershell
./artifacts/MemoryDesktop-win-x64/MemoryDesktop.exe --preview ./test-results/fixtures --seconds 60 --capture ./test-results/preview
```

`--capture` 导出实际 WPF 渲染帧和资源计数；预览可通过关闭窗口提前结束。只读宿主诊断：

```powershell
./artifacts/MemoryDesktop-win-x64/MemoryDesktop.exe --diagnose-host ./test-results/host-diagnostics.txt
```

### 模块

`IPhotoEffect` / `MemoryEffect` 只管理照片构图与绘制；`PhotoSurface` 管理暂停时间线与请求；`PhotoSource` 负责有界扫描和解码；`NativeDesktop` 负责原生挂载；`WallpaperController` 负责托盘、切换、生命周期和后台恢复。以后新增效果可实现 `IPhotoEffect`，无需重写桌面宿主。多文件夹预设和其他平台不在本版本范围。

### 手工验收（用户同意启动桌面会话后）

检查桌面图标点击、右键、Win+D；暂停/继续无时间跳跃；切换空/正常文件夹；新增/删除图片；退出恢复原背景；选择性开启/关闭登录启动。另在 Windows 10、多屏混合缩放、Explorer 重启、锁屏恢复和全屏应用环境验收。开发中不主动运行这些会改变桌面或启动项的操作。

参考：[SetParent 官方说明](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setparent)（跨进程 DPI 注意事项）、[DPI 清单官方说明](https://learn.microsoft.com/en-us/windows/win32/hidpi/setting-the-default-dpi-awareness-for-a-process)。这些 API 文档并不保证 WorkerW 机制。
