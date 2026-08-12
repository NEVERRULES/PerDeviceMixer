# Implementation Plan

本清单记录 V1 规格的实施状态。更完整、持续更新的当前状态见 `docs/DEVELOPMENT_STATUS.md`。

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

- [x] 4. 完成 WPF 音量合成器
  - 当前输出设备下拉选择、主音量、静音和应用聚合列表
  - Windows 11 风格滑杆、悬停反馈、应用图标和自定义滚动条
  - _Requirements: 1, 3, 6-7_

- [x] 5. 完成声音设备与后台设置
  - 默认输出/输入设备、默认输入音量和静音
  - 托盘、单实例、开机启动以及关闭行为
  - Windows 设备属性、蓝牙和单声道音频入口

- [ ] 6. 加固真实设备事件恢复
  - [x] 端点主音量使用专用 EventContext 过滤自身事件
  - [x] 端点消失时使用瞬态容错
  - [ ] 原生 Session EventContext 互操作
  - [ ] 蓝牙、HDMI、休眠恢复和会话生命周期压力测试
  - _Requirements: 1-3, 5, 7_

- [ ] 7. 扩展设备兼容层
  - Jack detection、设备别名、蓝牙物理设备合并
  - 评估输入设备 Profile 和应用跨设备路由
  - _Requirements: 1_

- [ ] 8. 完善发布与维护能力
  - [x] 便携包、EXE 安装程序和 SHA-256
  - [x] MIT 与第三方许可声明
  - [x] 公开预发布版本
  - [x] GitHub Releases 更新检查、安装版校验下载和便携版 Release 入口
  - [x] 项目主页、Releases 和隐私过滤的问题反馈入口
  - [ ] CI 和代码签名
  - [ ] 配置导入导出和应用内诊断
  - _Requirements: 5, 7_
