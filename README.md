# Memory Desktop · 记忆浮光

Windows 托盘式照片动态壁纸。照片在深色留白中轻柔出现、微微漂移、独立消散。每屏两张较大的照片，位置和大小有变化，互不重叠，各自交替切换。运行在 Explorer 桌面图标后方，无主界面、账号、网络请求或照片上传。

## 使用

1. 解压 `MemoryDesktop-win-x64.zip` 到长期保留的本地目录，双击 `MemoryDesktop.exe`，无需管理员权限。
2. 右键通知区域的青绿色照片图标（可能在 `^` 内），选择 **选择照片文件夹…**。首次启动等待你选定目录，不扫描其他个人文件夹。
3. 菜单提供 **暂停 / 继续**、**背景 → 深色留白 / 梦境星空**、**停留时间 → 6–8 秒 / 9–12 秒 / 11–17 秒**、**登录 Windows 时启动**、**退出**。
4. 登录启动默认关闭，只有点击该菜单项才修改当前用户启动项。退出后原 Windows 壁纸仍在；删除或移动程序前先取消登录启动。

梦境星空使用程序生成的稀疏星点、淡淡银河雾光和缓慢极光漂移。背景选择保存在本机，随照片一起暂停，可随时切回原背景。照片始终是视觉主体。

支持 JPG/JPEG、PNG、BMP、GIF、TIF/TIFF；GIF/TIFF 只使用第一帧。只读取所选目录的直接文件，不递归。HEIC、RAW、视频暂不支持。照片完整保留宽高比，不强制裁切。损坏、过大图片会跳过；空目录等待新增照片，移动盘恢复后后台重试。

## 动画与资源

- 默认清晰停留随机 6–8 秒，可在托盘选择 9–12 秒或 11–17 秒；不包含约 2.8 秒淡入、3.8 秒淡出。选项会打勾并保存在本机。更改后已显示的照片按原节奏完成，各自下一张采用新档位。每张独立随机计时，初始错开约 7–9 秒，到期点至少约 4 秒间隔；必要时延后下一张出现，不额外延长已选择的停留时间。
- 每屏两块宽敞的构图区域，随机位置和比例；区域包含漂移的完整边界和留白间隔，直到照片完全消失才释放。轻微漂移约 12 像素以内，固定采样尺寸避免缩放闪烁。
- 桌面照片按显示器物理像素直接绘制，不再通过 960 像素中间画布放大。图片只在到达时缩放一次，逐帧用 Windows 原生 AlphaBlend 合成；只有外缘羽化，中央不加模糊。源图本身分辨率低时仍受其限制。
- 目标 30 FPS，实际取决于显示器数量、分辨率及系统负载。背景纹理缓存；极光只在实际位置或透明度变化时更新。暂停、锁屏、睡眠或被前台全屏窗口覆盖时冻结时间线，降低调度。
- 最多 8 屏，各自构图，共用一个解码队列。源图最长边最多 1600 像素、文件最多 64 MiB、源像素最多 1.2 亿且任意边不超过 30000。每屏只保留两张活动照片及本屏绘制缓冲，内存随屏幕物理面积增加，不缓存整个相册。
- 最多存储 4096 个文件路径，大目录随机蓄水池取样。解码后释放文件句柄。切换目录取消旧任务，拒绝迟到的解码结果；新目录读取失败则保留原来源。文件监视去抖约 2 秒，另约每 2 分钟重查所选目录。
- 设置：`%LOCALAPPDATA%\MemoryDesktop\settings.xml`。启动项：`HKCU\Software\Microsoft\Windows\CurrentVersion\Run\MemoryDesktop`，启动时只读，不自动创建或修复。
- 有界诊断日志：`%LOCALAPPDATA%\MemoryDesktop\diagnostics.log`，记录计数、窗口几何和绘制耗时，不记录照片内容、文件名或目录路径。

## 兼容性

目标 Windows 10 / 11 x64，运行依赖 .NET Framework 4.8。实际测试系统为 **Windows 11 25H2，内部版本 26200，Framework Release 533509**；不能将 Windows 10 兼容目标当成已验证系统。

Explorer 的 `0x052C` 消息和 WorkerW 桌面挂载属于**未公开、非官方支持的机制**。传统桌面连接 WorkerW；新版 Windows 11 raised desktop 在 Progman 下创建分层子窗口，位于图标 DefView 后、系统壁纸 WorkerW 前。子窗口在创建时即指定桌面父窗口，避免重新挂载造成合成器不显示。失败时提示一次并后台重试，不显示覆盖桌面的顶层壁纸窗口。

清单声明 PerMonitorV2，每屏使用物理边界，支持负坐标；显示布局变化时重建。Explorer 重启通过 TaskbarCreated 与宿主存活检查尝试恢复。当前检查了 2240×1400、150% 主屏和位于负 Y 的 1920×1080、100% 副屏；主屏实际图标后方显示已观察。Windows 10、多屏热插拔、Explorer 重启、锁屏和睡眠恢复仍需专项验收。本次未重启 Explorer、未修改静态壁纸设置、未启用登录启动。

支持常见 JPEG/TIFF EXIF 旋转与镜像。HDR、动态虚拟桌面、第三方 Shell 和远程桌面未专项验证；全屏覆盖判断采用矩形近似，透明全屏也可能暂停。极端图片的 Windows 编解码器仍可能产生临时内存峰值。

## 构建与验证

无需 NuGet 或新 SDK，使用 Windows 内置 Framework C# 编译器：

```powershell
./build.ps1 -Test
```

输出 `artifacts/MemoryDesktop-win-x64/MemoryDesktop.exe` 与同名 ZIP。运行中更新时先右键退出，也可使用 `MemoryDesktop.exe --exit`。提供 `src/MemoryDesktop/MemoryDesktop.csproj` 供安装了 Framework 4.8 开发目标包的 Visual Studio/MSBuild 使用；当前实际构建由脚本引用系统已安装的运行时程序集完成，代码保持 C# 5 兼容。

合成图测试与截图均在被 Git 忽略的 `test-results/`，个人照片和截图不提交。测试覆盖来源边界、损坏图片、EXIF、文件句柄、取消、暂停、独立调度、运动不碰撞、清晰中心像素、背景切换、设置持久化；不写登录启动项，不调用 Explorer 创建宿主消息。

临时预览（普通窗口，默认 40 秒自动退出，不保存设置或连接桌面）：

```powershell
./artifacts/MemoryDesktop-win-x64/MemoryDesktop.exe --preview ./test-results/fixtures --seconds 60 --dreamy --capture ./test-results/preview
```

只读诊断：

```powershell
./artifacts/MemoryDesktop-win-x64/MemoryDesktop.exe --diagnose-host report.txt
./artifacts/MemoryDesktop-win-x64/MemoryDesktop.exe --diagnose-selected report.txt
```

第二项只检查用户已选目录并输出聚合数量。实际验证及未测项目见 `docs/VERIFICATION.md`。

## 模块

`IPhotoEffect` / `MemoryEffect` 管理布局、独立生命周期和绘制；`PhotoSurface` 管理暂停时钟与请求；`BackgroundScene` 管理可切换背景；`PhotoSource` 管理有界扫描与解码；`NativeDesktop` 管理宿主；`DesktopPresenter` / `NativeImage` 管理原生呈现与缓冲释放；`WallpaperController` 管理托盘、来源切换及系统生命周期。临时预览保留 WPF 绘制，桌面逐帧采用原生 GDI。新效果可实现接口；多文件夹预设和其他平台不在本版范围。

参考：[SetParent 文档](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setparent)、[DPI 清单文档](https://learn.microsoft.com/en-us/windows/win32/hidpi/setting-the-default-dpi-awareness-for-a-process)、[Lively 桌面宿主实现](https://github.com/lively-community/lively/blob/core-separation/src/Lively/Lively/Core/WinDesktopCore.cs)。视觉节奏参考 [React Bits Fade Content](https://reactbits.dev/animations/fade-content) 的无模糊透明度变化，以及 [Aurora](https://reactbits.dev/backgrounds/aurora) 的缓慢光流原则；未引入 React 或 WebGL 依赖。
