<p align="center">
  <img src="src/PerDeviceMixer.App/Assets/PerDeviceMixer.png" width="96" height="96" alt="PerDeviceMixer 图标">
</p>

<h1 align="center">PerDeviceMixer</h1>

<p align="center">
  按输出设备自动记忆并恢复主音量、应用音量和静音状态的 Windows 本地音量管理器。
</p>

> 当前源码版本：`0.4.0-preview.1`；最新公开版本：`0.4.0-preview.1`。项目仍处于预览阶段，建议首次使用时保留 Windows 原生声音设置作为备用入口。

## 项目解决什么问题

Windows 通常会记住不同输出设备的主音量，但音量合成器中的应用音量并不会自然形成一套清晰、可管理的“每设备配置”。例如，你可能希望：

- 使用笔记本扬声器时，主音量为 26%，浏览器为 30%；
- 插入有线耳机后，主音量降到 8%，浏览器提高到 55%；
- 连接蓝牙耳机后，再使用另一套独立音量。

PerDeviceMixer 将输出设备本身视为配置。你正常调整音量后，程序会在本机记录当前设备的主音量、应用音量和静音状态；以后切换回该设备时，再自动恢复对应状态，不需要手动创建或套用 Profile。

项目完全在本机运行，不需要账号，也不会上传音量配置。

## 主要功能

### 每个输出设备拥有独立音量

- 自动识别当前默认输出设备；
- 分设备保存主音量和主静音状态；
- 分设备保存各应用的音量和静音状态；
- 监听在 PerDeviceMixer、Windows 或其他音量工具中做出的调整并自动学习；
- 切换输出设备时自动恢复已经保存的状态；
- 新的应用音频会话出现时，恢复该应用在当前设备上的历史音量。

### 音量合成器

- 在首页下拉框中直接选择当前系统输出设备；
- 调整主音量和每应用音量；
- 点击喇叭图标切换静音；
- 按程序聚合音频会话，并显示程序自身图标；
- 使用接近 Windows 11 的滑杆、悬停反馈和深色滚动条。

### 声音设备

- 查看当前可用的输出设备和输入设备；
- 设置默认输出设备和默认输入设备；
- 调整当前默认输入设备的实时音量和静音状态；
- 打开 Windows 设备属性、蓝牙设备、单声道音频等设置入口；
- 从设备属性入口继续管理由 Windows 或驱动提供的格式、音频增强和空间音效。

> 输出设备的主音量和应用音量会进入 PerDeviceMixer 配置。当前版本不会为输入设备保存独立的应用音量配置。

### 后台与设置

- 关闭到托盘后销毁完整窗口和页面树，仅保留音频监听、托盘、自动保存和更新调度；空闲约 2 秒后会执行一次内存整理并归还工作集；
- 使用与主界面一致的深色 WPF 托盘菜单，可打开窗口、切换主静音、切换输出设备、检查更新、反馈问题、访问项目主页或退出；
- 单实例运行：重复启动只会唤醒已有窗口；
- 可选择点击关闭按钮后直接退出，或最小化到托盘；
- 可选择登录 Windows 后自动在托盘中启动；
- 可调整自动学习、自动恢复、新会话恢复、静音状态保存和保存延迟；
- 所有设置和音量变化都会自动异步保存，设置页会显示保存状态；退出和更新安装交接前会刷新剩余更改；
- 可手动检查 GitHub Releases 更新，也可设置每 6 小时、1 天、3 天或 7 天自动检查；
- 安装版可校验 `SHA256SUMS.txt` 后打开可见安装向导，便携版会打开 Release 下载页；
- 配置以 schema v2 JSON 形式保存在 `%LocalAppData%\PerDeviceMixer\profiles.json`，旧配置会自动迁移。

## 安装方法

PerDeviceMixer 提供 EXE 安装程序和免安装便携版。两种官方发布包都包含运行所需的
.NET 组件，不要求目标电脑另行安装 .NET Runtime。

### 方法一：EXE 安装程序（推荐）

1. 准备 Windows 10/11 x64 系统。
2. 从本项目的 [Releases](https://github.com/NEVERRULES/PerDeviceMixer/releases) 页面下载文件名以 `Setup.exe` 结尾的安装程序。
3. 运行安装程序，可按需勾选桌面快捷方式。
4. 从开始菜单或桌面启动 PerDeviceMixer。

安装程序按当前 Windows 用户安装，不需要管理员权限，并会在“设置 → 应用 →
已安装的应用”中提供标准卸载入口。卸载程序默认保留 `%LocalAppData%\PerDeviceMixer`
中的音量配置，重新安装后可以继续使用；如需彻底清除，可在卸载后手动删除该目录。

> 当前预览版尚未使用代码签名证书，Windows 可能显示“未知发布者”。请确认下载地址属于
> 本项目，并用 Release 中的 `SHA256SUMS.txt` 校验文件完整性。

### 方法二：免安装便携版

1. 从 [Releases](https://github.com/NEVERRULES/PerDeviceMixer/releases) 下载文件名以 `Portable.zip` 结尾的压缩包。
2. 将压缩包完整解压到一个固定目录。
3. 双击 `PerDeviceMixer.App.exe`。

便携版不会创建安装和卸载记录，也不会自动创建快捷方式。程序配置仍保存在
`%LocalAppData%\PerDeviceMixer`，因此更换程序目录不会丢失已有音量配置。

如果 Releases 页面暂时没有可下载文件，可以按下面的方法从源码构建。

### 方法三：从源码构建

需要 Windows 和 `.NET SDK 10.0.303`，或同一功能带的兼容补丁版本。

```powershell
cd PerDeviceMixer
dotnet restore
dotnet build PerDeviceMixer.slnx -c Release
dotnet test PerDeviceMixer.slnx -c Release --no-build
dotnet run --project src\PerDeviceMixer.App\PerDeviceMixer.App.csproj -c Release
```

生成供开发验证的 Windows x64 发布目录：

```powershell
.\build-release.ps1
```

脚本会先执行还原、Release 构建和测试，再将 Windows x64 发布目录生成到
`artifacts\PerDeviceMixer-win-x64`。默认输出需要目标电脑安装 .NET 10 Desktop Runtime。

需要生成不依赖预装 .NET Runtime 的自包含版本时：

```powershell
.\build-release.ps1 -SelfContained
```

生成与 GitHub Release 相同的自包含便携 ZIP、EXE 安装程序和 SHA-256 校验文件：

```powershell
.\build-distribution.ps1
```

该命令还需要本机安装 Inno Setup 6；脚本会在找不到编译器时给出明确提示。

校验下载文件：

```powershell
Get-FileHash .\PerDeviceMixer-0.4.0-preview.1-win-x64-Setup.exe -Algorithm SHA256
```

将结果与 `SHA256SUMS.txt` 中对应文件的值比较。

## 使用方法

### 1. 设置当前输出设备

打开“音量合成器”首页，在“当前输出”下拉框中选择设备。PerDeviceMixer 会把它设置为 Windows 默认输出，并刷新该设备的主音量与音频会话。

也可以进入“声音设备”页，通过“设为默认”切换输出或输入设备。

### 2. 调整并保存音量

直接拖动主音量或应用音量滑杆。程序会先立即更新 Windows 音量，再按设置页中的保存延迟写入本地配置。

点击滑杆左侧的喇叭图标可以静音或取消静音。应用当前没有音频会话时，不会出现在合成器中；它下一次创建音频会话后，PerDeviceMixer 会尝试恢复已保存的音量。

### 3. 切换设备并自动恢复

切换到另一台输出设备后，程序会读取该设备已经保存的配置并自动恢复。首次使用的新设备没有历史配置，PerDeviceMixer 会先记录它的当前状态，不会套用其他设备的音量。

### 4. 设置托盘与开机启动

进入“设置”页，可以选择：

- 是否开机自启动；
- 点击关闭按钮时退出，还是最小化到托盘；
- 是否自动学习音量变化；
- 是否在切换设备时自动恢复；
- 是否恢复稍后启动的应用；
- 是否保存静音状态。

默认情况下，点击关闭按钮会最小化到托盘。要彻底结束程序，请在托盘菜单中选择“退出”，或先在设置页关闭“关闭时最小化到托盘”。

进入托盘后，任务管理器显示的内存会在短暂延迟后明显下降。由于窗口和运行库页面需要重新载入，再次打开主界面可能比普通切换页面多出短暂等待；后台音频监听和自动保存不会中断。

## 输入/输出示例

### 示例一：扬声器切换到耳机

假设程序已经学习到以下配置：

| 输出设备 | 主音量 | 系统声音 | 浏览器 | 播放器 |
| --- | ---: | ---: | ---: | ---: |
| 笔记本扬声器 | 26% | 15% | 30% | 45% |
| 有线耳机 | 8% | 5% | 55% | 22% |

**输入操作：** 在首页“当前输出”下拉框中选择“有线耳机”。

**输出结果：**

- Windows 默认输出切换为有线耳机；
- 主音量恢复为 8%；
- 已存在的系统声音、浏览器和播放器音频会话分别恢复为 5%、55% 和 22%；
- 后续启动的同一应用创建音频会话时，继续使用当前设备保存的音量。

### 示例二：在当前设备上修改应用音量

**输入操作：** 当前使用有线耳机，将浏览器音量从 55% 拖到 40%，然后点击浏览器左侧的喇叭图标。

**输出结果：**

- Windows 中该浏览器的音频会话立即变为 40% 并静音；
- 本地配置中的“有线耳机 → 浏览器”记录更新为 40% 和静音；
- 扬声器和其他输出设备保存的浏览器音量不受影响。

### 示例三：切换默认输入设备

**输入操作：** 在“声音设备”页将 USB 麦克风设为默认输入，并把输入音量调到 80%。

**输出结果：** Windows 默认输入设备切换到 USB 麦克风，其实时输入音量调整为 80%。当前版本不会把输入设备保存为一套独立的应用音量 Profile。

## 当前限制

- 只显示当前活动输出端点上的音频会话；没有创建音频会话的应用不会显示。
- 如果某个应用在 Windows 中被固定路由到其他输出设备，它可能不会出现在当前默认设备的合成器中。
- 应用图标从可执行文件中读取；受权限或打包方式限制时会使用通用图标。
- 音频格式、增强、空间音效等功能由 Windows 和设备驱动提供，PerDeviceMixer 当前负责打开对应设置入口。
- 项目仍处于预览阶段，安装程序尚未提供数字签名。

## 数据与隐私

- 不提供账号、遥测、远程同步或后台服务；仅在启用更新检查、打开项目链接或提交反馈时访问 GitHub；
- 更新检查不会上传音量配置、设备 ID、应用列表或日志；
- 音量配置只写入当前用户的本地应用数据目录；
- 配置文件损坏时，原文件会被重命名为带时间戳的 `.corrupt-*` 文件，然后创建新配置。

## 项目结构

```text
src/
├─ PerDeviceMixer.App/    WPF 界面、托盘、设置与启动逻辑
├─ PerDeviceMixer.Audio/  Windows Core Audio 访问与设备通知
├─ PerDeviceMixer.Core/   配置、恢复策略与核心模型
└─ PerDeviceMixer.Probe/  音频状态诊断与开发测试工具

tests/
├─ PerDeviceMixer.Core.Tests/
└─ PerDeviceMixer.App.Tests/
```

## 项目文档

- [项目总手册](docs/PROJECT_GUIDE.md)：产品原则、架构、数据模型和关键运行流程；
- [开发状态](docs/DEVELOPMENT_STATUS.md)：当前完成度、验证基线、已知限制和路线图；
- [开发与贡献指南](CONTRIBUTING.md)：环境、工作流、回归测试和发布清单；
- [V1 原始规格](specs/per-device-mixer/requirements.md)：需求、设计和实施任务；
- [AGENTS.md](AGENTS.md)：供新对话和自动化开发工具快速接手项目。

## 开发与测试

```powershell
dotnet format PerDeviceMixer.slnx --verify-no-changes
dotnet build PerDeviceMixer.slnx -c Release
dotnet test PerDeviceMixer.slnx -c Release --no-build
```

`PerDeviceMixer.Probe` 默认只读取当前音频状态，不会修改音量：

```powershell
dotnet run --project src\PerDeviceMixer.Probe\PerDeviceMixer.Probe.csproj -c Release
```

## 许可证

本项目使用 [MIT License](LICENSE)。音频互操作基于同样采用 MIT 许可证的 NAudio，
详见 [第三方软件声明](THIRD-PARTY-NOTICES.md)。发布输出会自动携带这两份文件。
