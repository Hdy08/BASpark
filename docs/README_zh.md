<div align="center">

![BASpark](https://socialify.git.ci/DoomVoss/BASpark/image?description=1&descriptionEditable=Blue%20Archive%20Style%20Particle%20Effect&forks=1&issues=1&logo=https%3A%2F%2Fraw.githubusercontent.com%2FDoomVoss%2FBASpark%2Fmain%2Fassets%2Flogo.png&name=1&pattern=Diagonal%20Stripes&pulls=1&stargazers=1&theme=Auto)

[![GitHub stars](https://img.shields.io/github/stars/DoomVoss/BASpark?style=social)](https://github.com/DoomVoss/BASpark/stargazers)
[![GitHub license](https://img.shields.io/github/license/DoomVoss/BASpark)](https://github.com/DoomVoss/BASpark/blob/main/LICENSE)
[![GitHub issues](https://img.shields.io/github/issues/DoomVoss/BASpark)](https://github.com/DoomVoss/BASpark/issues)
[![Discord](https://img.shields.io/badge/Discord-Join-7289DA)](https://discord.gg/sgZMdqhfZu)
[![QQ](https://img.shields.io/badge/QQ-Doom-blue)](https://qm.qq.com/q/oGwB5mKQtq)

🌐 **Languages:** [English](../README.md) | 简体中文

[快速下载](https://github.com/Hdy08/BASpark/releases/latest) | [反馈 Bug](https://github.com/Hdy08/BASpark/issues/new?template=bug_report.yml) | [功能建议](https://github.com/Hdy08/BASpark/issues/new?template=feature_request.yml)

</div>

---
# BASpark · WinUI 3

### 项目简介

> 为 Windows 桌面带来《蔚蓝档案》风格的点击与拖尾特效，搭配原生 WinUI 3 界面。

**BASpark · WinUI 3** 是基于 [DoomVoss/BASpark](https://github.com/DoomVoss/BASpark)、采用 [CialloKing/ba-click-fx](https://github.com/CialloKing/ba-click-fx) 渲染的 Windows 桌面特效工具，为鼠标与触摸操作添加《蔚蓝档案》风格的圆环、粒子、辉光和拖尾。本分支提供原生控制面板、安装与卸载界面，并支持特效自定义、环境过滤和多显示器。

### 核心特性

* **特效自定义：** 支持颜色、不透明度、缩放、动画速度、辉光和拖尾采样设置；集成完整版 ba-click-fx，可按需开启显示、圆环、光盘、碎片、拖尾和 BLOOM 高级设置，默认关闭。
* **鼠标与多点触摸：** 可分别启用点击特效、鼠标常驻拖尾和触摸特效。各触点独立过滤，支持多处同时点击与拖尾，并可设置特效数量上限。
* **多显示器：** 可选择显示特效的屏幕，拖尾跨屏连续；切换主显示器、调整屏幕位置或增减显示器后，无需重启即可适配。
* **环境过滤：** 支持桌面、全屏应用和本应用窗口的特效控制，以及多配置组进程过滤，并保留隐藏指针场景的过滤行为。截图兼容模式可在识别到截图操作时临时隐藏特效。
* **原生 Windows 界面：** 控制面板、安装器和卸载器采用 WinUI 3，提供层级导航、设置卡片和原生对话框。背景可选浅色、深色或跟随系统，强调颜色可跟随系统或自定义，支持简体中文、英文和日文。
* **设置与备份：** 各设置页独立应用或放弃更改，支持单项、分组及整页重置；可选择导入、导出设置、配置组、显示器配置和点击统计。
* **便捷操作：** 支持首页与托盘暂停或启用特效、静默启动、隐藏托盘图标及查看和复制日志。特效叠加层鼠标穿透，不抢焦点，不出现在 Alt+Tab 中。
* **本地运行：** 特效资源随程序提供，本分支移除了遥测、自动更新检查和公告拉取；通过合并高频输入与共享渲染资源，减少闲置及持续拖尾时的额外开销。

---

## 快速开始

### 1. 系统需求
* *需要 64 位处理器和操作系统
* 操作系统 : Windows 10 1809 (17763) / 11
* 内存: 200 MB RAM
* 显卡: 支持 DirectX 11 / OpenGL 的集成或独立显卡
* 存储空间: 需要 400 MB 可用空间
* 运行库: 无需额外安装，Windows App SDK 与 .NET 运行时已随程序自包含发布

### 2. 安装步骤
1. 前往 [Releases](https://github.com/Hdy08/BASpark/releases) 页面下载最新的安装包 BASpark_Installer_vX.X.X_x64.exe。
2. 运行安装程序并完成安装。
3. Enjoy it！

---

## 免责声明

* 本软件为同人爱好交流项目，严禁任何形式的倒卖行为。
* 本程序不含任何病毒或恶意代码，仅用于桌面视觉特效。
* 软件按“原样”提供，作者不对使用过程中可能产生的任何直接或间接损失承担责任。
* 视觉风格灵感来源于 Nexon / Yostar 《Blue Archive》，版权归原作者所有。

---
## Star History

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="https://api.star-history.com/svg?repos=DoomVoss/BASpark&type=Date&theme=dark" />
  <source media="(prefers-color-scheme: light)" srcset="https://api.star-history.com/svg?repos=DoomVoss/BASpark&type=Date" />
  <img alt="Star History Chart" src="https://api.star-history.com/svg?repos=DoomVoss/BASpark&type=Date" />
</picture>

---

## Contributors

<a href="https://github.com/DoomVoss/BASpark/graphs/contributors">
  <img src="https://contrib.rocks/image?repo=DoomVoss/BASpark&v=0410" />
</a>

---

## 许可协议

- 本项目采用 MIT 许可证，详情请参见 [LICENSE](./LICENSE) 文件

> **MIT License**
>
> Copyright (c) 2026 Doom


<div align="center">
  Made with ❤️ by Doom
</div>
