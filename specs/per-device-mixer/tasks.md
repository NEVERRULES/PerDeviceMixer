# Implementation Plan

- [x] 1. 建立 .NET 10 分层解决方案
  - Core、Audio、Probe、WPF App、Tests
  - _Requirements: 1-7_

- [x] 2. 完成 Core Audio 技术原型
  - 枚举端点、默认设备、主音量和音频会话
  - 调整主音量与按应用聚合的会话音量
  - 监听默认设备、端点音量、会话创建与会话音量变化
  - _Requirements: 1-3, 6-7_

- [x] 3. 完成首版配置引擎
  - 每设备 Profile、未知应用保守策略、去抖与原子写入
  - 切设备恢复和新会话恢复
  - _Requirements: 1-5_

- [x] 4. 完成最小 WPF 合成器界面
  - 当前设备、主音量、静音、按应用聚合列表
  - _Requirements: 1, 3, 6-7_

- [ ] 5. 加固事件恢复与退出生命周期
  - [ ] 原生 Session EventContext
  - [x] 单实例、托盘、优雅退出
  - _Requirements: 1-3, 5_

- [ ] 6. 扩展设备兼容层
  - Jack detection、设备别名、蓝牙物理设备合并
  - _Requirements: 1_

- [ ] 7. 发布准备
  - [x] 自启动与便携包构建
  - [ ] 日志、导入导出、安装包和 CI
  - _Requirements: 5, 7_
