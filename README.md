<div align="center">

![BASpark](https://socialify.git.ci/DoomVoss/BASpark/image?description=1&descriptionEditable=Blue%20Archive%20Style%20Particle%20Effect&forks=1&issues=1&logo=https%3A%2F%2Fraw.githubusercontent.com%2FDoomVoss%2FBASpark%2Fmain%2Fassets%2Flogo.png&name=1&pattern=Diagonal%20Stripes&pulls=1&stargazers=1&theme=Auto)

[![GitHub stars](https://img.shields.io/github/stars/DoomVoss/BASpark?style=social)](https://github.com/DoomVoss/BASpark/stargazers)
[![GitHub license](https://img.shields.io/github/license/DoomVoss/BASpark)](https://github.com/DoomVoss/BASpark/blob/main/LICENSE)
[![GitHub issues](https://img.shields.io/github/issues/DoomVoss/BASpark)](https://github.com/DoomVoss/BASpark/issues)
[![Discord](https://img.shields.io/badge/Discord-Join-7289DA)](https://discord.gg/sgZMdqhfZu)
[![QQ](https://img.shields.io/badge/QQ-Doom-blue)](https://qm.qq.com/q/oGwB5mKQtq)

🌐 **Languages:** English | [简体中文](./docs/README_zh.md)

[Download](https://github.com/Hdy08/BASpark/releases/latest) | [Bug Report](https://github.com/Hdy08/BASpark/issues/new?template=en_bug_report.yml) | [Feature Request](https://github.com/Hdy08/BASpark/issues/new?template=en_feature_request.yml)

</div>

---

# BASpark · WinUI 3

> Blue Archive-inspired click and trail effects for your Windows desktop, with a native WinUI 3 interface.

**BASpark · WinUI 3** is a Windows desktop effects tool based on [DoomVoss/BASpark](https://github.com/DoomVoss/BASpark), powered by [CialloKing/ba-click-fx](https://github.com/CialloKing/ba-click-fx). It brings Blue Archive-inspired rings, particles, glow and trails to mouse and touch input. This branch combines a native control panel, installer and uninstaller with configurable rendering, environment filtering and multi-monitor support.

---

## Features

* **Customizable Effects:** Adjust color, opacity, scale, animation speed, glow and trail sampling. The full edition of ba-click-fx also provides optional advanced settings for rendering, rings, disks, shards, trails and bloom; these settings are disabled by default.
* **Mouse and Multi-Touch Input:** Enable click effects, persistent mouse trails and touch effects separately. Touch contacts are filtered independently and can produce simultaneous clicks and trails, with a configurable effect-count limit.
* **Multi-Monitor Support:** Select which displays show effects, keep trails continuous across screen boundaries, and adapt to monitor layout and primary-display changes without restarting.
* **Environment Filtering:** Control effects on the desktop, in full-screen applications and on BASpark windows. Switch between process-filter profiles and respect hidden-pointer scenarios. Screenshot compatibility mode temporarily hides effects for recognized screenshot actions.
* **Native Windows Interface:** A WinUI 3 control panel, installer and uninstaller, with hierarchical navigation, settings cards and native dialogs. Choose a light, dark or system background and a system or custom accent color; the interface is available in Simplified Chinese, English and Japanese.
* **Settings and Backups:** Apply or discard changes independently on each settings page, reset individual settings or groups, and selectively import or export settings, profiles, display selections and click statistics.
* **Quick Controls:** Pause or resume effects from the home page or system tray, start silently, hide the tray icon, and view or copy logs. Overlay windows remain transparent to mouse input and do not take focus or appear in Alt+Tab.
* **Local Operation:** Effect resources are bundled with the application. This branch removes telemetry, automatic update checks and announcement fetching, and reduces idle and sustained-trail work through input batching and shared rendering resources.

---

## Getting Started

### System Specifications
* **Architecture:** Requires a 64-bit processor and native OS environment.
* **Operating System:** Windows 10 1809 (build 17763) / Windows 11 (X64 architectures).
* **Memory:** Minimum 200 MB RAM allocation.
* **Graphics Unit:** Discrete or integrated graphics processing units natively supporting DirectX 11 or OpenGL runtimes.
* **Storage Matrix:** Minimum 400 MB available physical storage workspace.
* **Runtime:** None required. The Windows App SDK and .NET runtime ship self-contained with the application.

### Installation
1. Navigate to the official [Releases Hub](https://github.com/Hdy08/BASpark/releases). Download the latest setup binary: `BASpark_Installer_vX.X.X_x64.exe`.
2. Execute the installer application and follow the deployment wizard instructions to initialize the localized directory.
3. Launch the primary execution routine and enjoy the interactive effects.

---

## Disclaimers Clarifications

* This utility is published strictly as a non-commercial, fan-made interactive tribute project. Commercial distribution or unauthorized reselling of this code or asset package is strictly prohibited.
* The source codebase is audited and contains no malicious modules, spyware, telemetry packages, or destructive code. Its functional scope is limited entirely to desktop aesthetic rendering.
* This software distribution is delivered strictly on an "as-is" basis. The maintenance team disclaims any liability for directly or indirectly declared system anomalies or operational losses stemming from execution.
* The fundamental design languages, trademark graphics, and overall creative aesthetics draw artistic inspiration from the intellectual property of Nexon / Yostar (Blue Archive). All related copyrights belong unconditionally to their respective commercial legal owners.

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

## License

This software utility is distributed globally under the terms of the MIT License. For deep structural parameters and legal terminology, refer explicitly to the [LICENSE](./LICENSE) document.

> **MIT License**
>
> Copyright (c) 2026 Doom

<div align="center">
  Made with ❤️ by Doom
</div>
