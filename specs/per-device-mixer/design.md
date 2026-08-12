# PerDeviceMixer V1 Design

## Architecture

```text
WPF App / Probe
      |
 MixerEngine
   /      \
CoreAudio  JsonProfileStore
(NAudio)   (LocalAppData)
```

- `PerDeviceMixer.Core`：平台无关的数据模型、配置存储、自动学习与恢复编排。
- `PerDeviceMixer.Audio`：封装 MMDevice、EndpointVolume 和 AudioSession API。
- `PerDeviceMixer.Probe`：音频状态枚举和事件诊断工具。
- `PerDeviceMixer.App`：WPF 前端、音量合成器、声音设备、设置、托盘与启动逻辑。
- `PerDeviceMixer.Core.Tests`：存储完整性和核心规则测试。

## Identity and matching

设备主键使用 Windows Endpoint ID。应用主键依次使用完整可执行路径、进程名、会话标识；系统声音固定为 `system:sounds`。同一应用的多个会话共享一个应用配置。

## Persistence

配置位于 `%LocalAppData%\PerDeviceMixer\profiles.json`。变化在 500 ms 后合并写入，写入同目录临时文件后替换目标文件。损坏配置会改名为带时间戳的 `.corrupt-*` 文件。

## Safety

首次遇到设备或应用时只读取并学习当前值。只有存在已保存配置时才执行自动恢复。配置与日志保持本地，不使用网络。

## Known technical boundary

NAudio 2.3 的会话事件包装器不暴露 `EventContext`，因此会话恢复期间由引擎抑制学习；主音量使用专用 GUID 精确过滤自身事件。后续可用原生 `IAudioSessionEvents` 互操作消除异步回调窗口。
