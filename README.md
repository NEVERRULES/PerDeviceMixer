<p align="center">
  <img src="src/PerDeviceMixer.App/Assets/PerDeviceMixer.png" width="96" height="96" alt="PerDeviceMixer 图标">
</p>

<h1 align="center">PerDeviceMixer</h1>

<p align="center">
  按输出设备自动记忆并恢复主音量、应用音量和静音状态的 Windows 本地音量管理器。
</p>

> 当前版本：`0.3.2-preview.1`。项目仍处于预览阶段，建议首次使用时保留 Windows 原生声音设置作为备用入口。

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

- 系统托盘菜单：打开窗口、切换主静音、切换输出设备或退出；
- 单实例运行：重复启动只会唤醒已有窗口；
- 可选择点击关闭按钮后直接退出，或最小化到托盘；
- 可选择登录 Windows 后自动在托盘中启动；
- 可调整自动学习、自动恢复、新会话恢复、静音状态保存和保存延迟；
- 配置以 JSON 形式保存在 `%LocalAppData%\PerDeviceMixer\profiles.json`。

## 安装方法

PerDeviceMixer 当前提供免安装的便携版，没有 MSI 或 EXE 安装向导。

### 方法一：使用发布包

1. 准备 Windows 10/11 x64 系统。
2. 安装 x64 版 **.NET 10 Desktop Runtime**。
3. 从本项目的 [Releases](../../releases/latest) 页面下载 Windows x64 发布包。
4. 将压缩包完整解压到一个固定目录。
5. 双击 `PerDeviceMixer.App.exe`。

如果 Releases 页面暂时没有可下载文件，可以按下面的方法从源码构建。

### 方法二：从源码构建

需要 Windows 和 `.NET SDK 10.0.303`，或同一功能带的兼容补丁版本。

```powershell
cd PerDeviceMixer
dotnet restore
dotnet build PerDeviceMixer.slnx -c Release
dotnet test PerDeviceMixer.slnx -c Release --no-build
dotnet run --project src\PerDeviceMixer.App\PerDeviceMixer.App.csproj -c Release
```

生成 Windows x64 便携发布包：

```powershell
.\build-release.ps1
```

脚本会先执行还原、Release 构建和测试，再将 Windows x64 便携版生成到
`artifacts\PerDeviceMixer-win-x64`。发布包仍然需要目标电脑安装 .NET 10 Desktop Runtime。

需要生成不依赖预装 .NET Runtime 的自包含版本时：

```powershell
.\build-release.ps1 -SelfContained
```

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
- 项目仍处于预览阶段，尚未提供安装器和自动更新。

## 数据与隐私

- 无账号系统；
- 无遥测；
- 无网络 API；
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
└─ PerDeviceMixer.Core.Tests/
```

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
