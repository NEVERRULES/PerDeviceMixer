# PerDeviceMixer 开发与贡献指南

感谢参与 PerDeviceMixer。开始修改前，请先阅读 [项目总手册](docs/PROJECT_GUIDE.md) 和 [开发状态](docs/DEVELOPMENT_STATUS.md)。

## 1. 开发环境

- Windows 10/11 x64；
- .NET SDK `10.0.303` 或 `global.json` 允许的同功能带补丁版本；
- PowerShell；
- 生成安装程序时需要 Inno Setup 6。

安装可选的安装器编译依赖：

```powershell
winget install --id JRSoftware.InnoSetup -e
```

## 2. 首次构建

```powershell
git clone https://github.com/NEVERRULES/PerDeviceMixer.git
cd PerDeviceMixer
dotnet restore
dotnet build PerDeviceMixer.slnx -c Release
dotnet test PerDeviceMixer.slnx -c Release --no-build
dotnet run --project src\PerDeviceMixer.App\PerDeviceMixer.App.csproj -c Release
```

## 3. 项目边界

- Core 层通过 `IAudioService` 使用音频能力，不直接访问 NAudio 或 WPF。
- Audio 层集中管理 Core Audio 和 COM，避免把端点对象跨线程泄漏到 UI。
- App 层不复制自动学习和恢复策略，策略应保留在 `MixerEngine`。
- 未知应用不得使用其他应用或设备的历史值。
- 设备切换过程中的瞬态消失必须被视为正常情况。
- 所有账号、遥测、远程同步和后台服务都不在默认项目范围内；更新与支持功能只允许访问项目的 GitHub HTTPS 地址。

## 4. 修改流程

1. 先复现问题或写出可验证的验收条件；
2. 检查工作区是否有用户未提交的修改；
3. 修改最小必要层，避免把 UI、策略和 Core Audio 逻辑混在一起；
4. 为 Core 策略和存储行为增加单元测试；
5. 涉及真实设备、WPF 或托盘时，补充手工验证；
6. 同步更新 README、开发状态或架构文档；
7. 清理验证过程中产生的无用文件。

## 5. 代码检查基线

```powershell
dotnet format PerDeviceMixer.slnx --verify-no-changes
dotnet build PerDeviceMixer.slnx -c Release
dotnet test PerDeviceMixer.slnx -c Release --no-build
git diff --check
```

需要检查覆盖率时，使用 Debug 构建保留可解析的本地源码路径：

```powershell
dotnet test PerDeviceMixer.slnx -c Debug --collect:"XPlat Code Coverage" --results-directory TestResults\coverage
```

`TestResults` 是本地验证产物，不应提交。

涉及窗口或托盘生命周期时，可用隔离配置测量自包含构建：

```powershell
.\measure-memory.ps1 -ExecutablePath .\artifacts\PerDeviceMixer-win-x64\PerDeviceMixer.App.exe -IdleSeconds 60
.\measure-memory.ps1 -ExecutablePath .\artifacts\PerDeviceMixer-win-x64\PerDeviceMixer.App.exe -IdleSeconds 60 -OpenThenClose
```

脚本只终止它自己启动且可执行路径完全匹配的进程，并清理隔离的临时配置目录。

托盘测量同时记录工作集和私有内存。工作集反映当前驻留物理页，会受托盘状态的一次性回收明显影响；私有内存反映进程提交量，不应把两者当成同一个指标。关闭后还应重新打开窗口，确认按需重载和音频控制仍然可用。

当前项目将编译警告视为错误。不要通过关闭分析器或降低警告级别绕过问题。

## 6. 音频相关手工回归

至少检查与修改相关的项目：

- 软件内主音量滑杆可拖动并立即影响 Windows；
- Windows 快捷设置或系统合成器修改音量后软件不崩溃，并更新显示；
- 应用滑杆和喇叭静音按钮有效；
- 输出设备下拉框、设备页和托盘切换结果一致；
- 已知设备恢复历史配置，未知设备保持当前值；
- 已知应用的新会话恢复，未知应用不被修改；
- 蓝牙断开、HDMI 拔出或无默认设备时程序继续运行；
- Beats Fit Pro 作为默认输出时，声音设备页和托盘能显示左右耳/充电盒电量；关闭功能或断开耳机后 BLE 状态立即清除且音量管理继续工作；
- 插入耳机、断开耳机或手动切换输出设备时，屏幕底部出现设备名与主音量提示，几秒后自动消失，且提示不拦截鼠标点击；
- 关闭到托盘和直接退出都能正确保存配置；
- 重复启动只唤醒现有实例；
- 开机启动项指向当前实际可执行文件。

测试真实设备时不要把本机 `profiles.json`、日志、设备 ID 或用户名加入提交。

## 7. 构建发布资产

开发验证目录：

```powershell
.\build-release.ps1
```

正式的自包含安装程序、便携 ZIP 和 SHA-256：

```powershell
.\build-distribution.ps1
```

输出位于 `artifacts\release\<version>\assets`，该目录被 Git 忽略。

默认发布流程：

1. 更新 `Directory.Build.props` 及相关版本文档并提交；
2. 推送与源码版本完全一致的标签，例如 `v0.5.0-preview.3`；
3. `.github/workflows/release.yml` 在 GitHub Windows runner 上运行格式检查、Release 构建和测试，再生成安装程序、便携包及校验清单；
4. 三项资产先上传到草稿 Release，GitHub SHA-256 与构建产物逐项一致后才公开。预览版本自动标记为 Pre-release。

标签与 `Directory.Build.props` 不一致、构建/测试失败、上传失败或哈希不一致都会停止发布；GitHub runner 随任务销毁，因此开发电脑无需保留发布产物。

需要在本机备用发布时，确保最终版本已提交、工作区干净且 `gh auth status` 成功，再使用：

```powershell
.\publish-github-release.ps1
```

本地脚本会调用 `build-distribution.ps1`，创建预发布 GitHub Release，逐个核对远端资产的
SHA-256；全部一致后清理 `artifacts\release` 中的旧版本，仅保留当前版本。上传或远端
校验失败时不会清理本地资产。正式版使用 `-Stable`，需要保留旧目录时使用
`-KeepOlderLocalVersions`。

发布前验证：

- 所有自动化测试通过；
- 安装、卸载和便携版解压通过；
- 发布包包含 `LICENSE` 和 `THIRD-PARTY-NOTICES.md`；
- 不包含 PDB、本机路径、用户配置、Token 或日志；
- `SHA256SUMS.txt` 与最终资产匹配；
- 安装器是否签名必须在 Release 说明中如实标注。

## 8. 版本发布清单

发布新版本时同步检查：

- `Directory.Build.props` 中的版本；
- README 顶部版本和下载说明；
- `build-distribution.ps1` 默认版本；
- `publish-github-release.ps1` 的目标提交、预发布/正式版参数和远端资产校验结果；
- `installer/PerDeviceMixer.iss` 的回退版本；
- `docs/DEVELOPMENT_STATUS.md` 的版本、commit 和验证结果；
- Git 标签、Release 标题和预发布/正式版状态。

发布资产应从最终版本 commit 重新构建，避免可执行文件的 `ProductVersion` 指向较早的 commit。

## 9. 提交内容安全

提交前检查：

- 密码、Token、私钥和连接字符串；
- 邮箱、手机号、用户真实姓名或其他个人信息；
- `C:\Users\...`、本机项目目录等绝对路径；
- `bin/`、`obj/`、`artifacts/`、PDB、日志和测试临时文件；
- 与功能无关的编辑器或操作系统文件。
