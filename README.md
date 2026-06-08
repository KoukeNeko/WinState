<div align="center">

<img src="Assets/WinState-icon-512.png" width="120" alt="WinState logo" />

# WinState

**A lightweight, real-time Windows system monitor that lives in your tray.**

[![build](https://github.com/KoukeNeko/WinState/actions/workflows/build.yml/badge.svg?branch=dev)](https://github.com/KoukeNeko/WinState/actions/workflows/build.yml)
![platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078D6?logo=windows&logoColor=white)
![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)
![arch](https://img.shields.io/badge/arch-x64%20%7C%20arm64-blue)

CPU · GPU · RAM · Disk · Network · Power — at a glance, with rich frosted-glass flyouts.

**[English](#english) · [繁體中文](#繁體中文)**

</div>

---

## 📸 Screenshots / 截圖

**CPU**

<img width="480" height="1516" alt="image" src="https://github.com/user-attachments/assets/eb822672-2c68-4d78-9fd1-0eda48958628" />


**GPU**

<img width="480" height="693" alt="image" src="https://github.com/user-attachments/assets/2caf1112-419e-4508-abcf-d8586c7ad67c" />


**Memory (RAM)**

<img width="480" height="1287" alt="image" src="https://github.com/user-attachments/assets/d2f41eca-adc1-4f92-b241-e6369e596b91" />


**Network**

<img width="480" height="1100" alt="image" src="https://github.com/user-attachments/assets/27b125fb-e031-489d-9432-a0bd58f9718d" />


**Sensors**

<img width="480" height="1456" alt="image" src="https://github.com/user-attachments/assets/0a0e7d6e-ca89-43c4-b421-6e1e1059d848" />


---

## English

WinState packs six live metrics into your Windows notification area. Each tray icon shows a number at a glance; click one and a Windows 11–style **acrylic flyout** opens with detailed graphs, sensors and the top processes for that category. Built on .NET 8 + WPF (WPF-UI / Fluent), with hardware data from LibreHardwareMonitor.

> ⚙️ **Runs as administrator** — hardware sensors, SMART data and per-process ETW tracing all require elevation.

### ✨ Features

#### Tray icons

| Icon | Shows | Color thresholds |
|:----:|-------|:----------------:|
| **CPU** | Overall CPU usage % | 🟡 🟠 🔴 configurable |
| **GPU** | Busiest GPU usage % | 🟡 🟠 🔴 configurable |
| **RAM** | Memory usage % | 🟡 🟠 🔴 configurable |
| **DISK** | Active disk % | 🟡 🟠 🔴 configurable |
| **NET** | ▲▼ arrows light up on activity | — |
| **PWR** | CPU package power (W) | — |

Icons are text-rendered and re-rasterized for the **taskbar monitor's DPI**, so they stay crisp at any display scale. Each percentage icon turns yellow → orange → red at thresholds you choose.

#### Detail flyouts (frosted acrylic)

- **CPU** — total + per-core usage history, clock / temperature / voltage / package power, process / thread / handle counts, uptime, and top CPU processes.
- **Memory** — usage history + commit pressure, full breakdown (in-use, compressed, cached, committed, commit limit…), and top memory processes.
- **Network** — upload/download with history graph, public & local IP, MAC, adapter info, and top processes by traffic. The "primary" adapter is the one that owns the **default route**, so a busy VPN or virtual switch can't hijack the reading.
- **Disk** — per-volume capacity, **SMART** (temperature, health, total reads/writes), per-disk read/write graphs, and top disk processes.
- **GPU** — usage, VRAM, temperature and clock (multi-GPU aware).
- **Sensors** — every detailed hardware sensor, grouped by device.

#### Settings (a single Fluent page)

| Group | What you can change |
|-------|---------------------|
| **Appearance** | Light / Dark theme |
| **General** | Launch WinState automatically at logon (registers a Scheduled Task that runs elevated) |
| **Hardware driver** | One-click install of the PawnIO driver via WinGet, or a link to pawnio.eu, plus a live status indicator |
| **Tray icons** | Which icons show, their order, and per-icon warning thresholds |
| **Process list** | How many top processes each flyout lists (1–50) |
| **Refresh rate** | Per-category polling interval in ms (250–10000) |

#### Efficient by design

- **Visibility-gated polling** — heavy collection (per-process lists, SMART, the ETW kernel trace) runs only while a window is on screen. Idling in the tray costs almost nothing.
- DPI-aware tray rendering, careful GDI handle hygiene, and frozen WPF geometry for smooth graphs.

### 📦 Download

CI builds **self-contained, single-file** executables for every push — no .NET install required on the target machine.

1. Open the [**Actions → build**](https://github.com/KoukeNeko/WinState/actions/workflows/build.yml) tab and pick the latest green run.
2. Download the artifact for your CPU:
   - `WinState-Setup-win-x64` / `WinState-Setup-win-arm64` — guided installer (recommended; the artifact zip contains exactly one `WinState-Setup-<rid>.exe`).
   - `WinState-win-x64` / `WinState-win-arm64` — bare WinState.exe, no wizard.
3. Unzip the artifact and double-click the `.exe` inside; accept the UAC prompt. The installer's options page lets you toggle PawnIO install and launch-at-logon.
4. **PawnIO driver:** the installer offers a one-click WinGet install (`winget install -e --id namazso.PawnIO`); alternatively grab it from [pawnio.eu](https://pawnio.eu/). Without it, GPU / RAM / disk / network still work, but CPU temperature, voltage, package power and motherboard sensors stay blank.

> Artifacts need a GitHub login and expire after 90 days — building from source is the most reliable route.

> **Why PawnIO?** Microsoft's [vulnerable-driver blocklist](https://learn.microsoft.com/en-us/windows/security/threat-protection/windows-defender-application-control/microsoft-recommended-driver-block-rules) now includes WinRing0, which is what stock LibreHardwareMonitor uses to read CPU MSRs. WinState ships namazso's [PawnIO fork](https://github.com/namazso/LibreHardwareMonitor/tree/pawnio-squashed) instead — same API, signed driver, no Defender warning.

### 🔨 Build from source

```bash
git clone --recursive https://github.com/KoukeNeko/WinState.git
cd WinState
dotnet run -c Release
```

The `--recursive` pulls the vendored LibreHardwareMonitor PawnIO fork under `Vendor/`. If you forgot it, run `git submodule update --init --recursive`.

Produce the same single-file exe as CI:

```bash
dotnet publish WinState.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

**Requirements:** Windows 10/11 · .NET 8 SDK.

### 🧱 Tech stack

- **.NET 8 / WPF** (`net8.0-windows`)
- **[WPF-UI](https://github.com/lepoco/wpfui)** 4.0 — Fluent controls, Mica / Acrylic
- **[LibreHardwareMonitorLib (PawnIO fork)](https://github.com/namazso/LibreHardwareMonitor/tree/pawnio-squashed)** — CPU / GPU / disk sensors & SMART, talking to the signed PawnIO driver instead of WinRing0
- **Microsoft.Diagnostics.Tracing.TraceEvent** — per-process disk & network via ETW
- **[Hardcodet.NotifyIcon.Wpf](https://github.com/hardcodet/wpf-notifyicon)** — tray icons
- **CommunityToolkit.Mvvm** + **Microsoft.Extensions.Hosting** — MVVM and DI host

### 📄 License

No license has been specified yet; all rights reserved by the author. Open an issue if you'd like one added.

### 🙏 Credits

Built with [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor), [WPF-UI](https://github.com/lepoco/wpfui) and [Hardcodet NotifyIcon](https://github.com/hardcodet/wpf-notifyicon).

---

## 繁體中文

WinState 把六項即時指標放進 Windows 系統匣。每個圖示一眼看到數字；點下去就會彈出 Windows 11 風格的**壓克力霜玻璃視窗**，顯示該類別的詳細圖表、感測器與佔用最高的程序。以 .NET 8 + WPF（WPF-UI / Fluent）打造，硬體資料來自 LibreHardwareMonitor。

> ⚙️ **以系統管理員執行** — 硬體感測、SMART 資料、以及 per-process 的 ETW 追蹤都需要提權。

### ✨ 功能

#### 系統匣圖示

| 圖示 | 顯示 | 變色門檻 |
|:----:|------|:--------:|
| **CPU** | 整體 CPU 使用率 % | 🟡 🟠 🔴 可設定 |
| **GPU** | 最忙 GPU 使用率 % | 🟡 🟠 🔴 可設定 |
| **RAM** | 記憶體使用率 % | 🟡 🟠 🔴 可設定 |
| **DISK** | 磁碟活動 % | 🟡 🟠 🔴 可設定 |
| **NET** | ▲▼ 箭頭隨流量亮起 | — |
| **PWR** | CPU 封裝功耗 (W) | — |

圖示以文字繪製，並依**工作列所在螢幕的 DPI** 重新點陣化，任何縮放下都保持清晰。百分比圖示會在你設定的門檻由黃 → 橘 → 紅變色。

#### 詳細彈出視窗（霜玻璃）

- **CPU** — 整體＋每核心使用率歷史、時脈／溫度／電壓／封裝功耗、處理程序／執行緒／控制代碼數、開機時長，以及 CPU 佔用最高的程序。
- **記憶體** — 使用率歷史＋認可壓力、完整明細（使用中、壓縮、快取、已認可、認可上限…），以及記憶體佔用最高的程序。
- **網路** — 上傳/下載與歷史圖、公網與內網 IP、MAC、介面卡資訊，以及流量最高的程序。「主要介面」是**持有預設路由**的那張網卡，所以忙碌的 VPN 或虛擬交換器不會搶走判讀。
- **磁碟** — 各磁區容量、**SMART**（溫度、健康度、總讀寫量）、各碟讀寫圖，以及磁碟佔用最高的程序。
- **GPU** — 使用率、VRAM、溫度與時脈（支援多 GPU）。
- **感測器** — 依裝置分組顯示所有詳細硬體感測器。

#### 設定（單一 Fluent 頁面）

| 群組 | 可調整 |
|------|--------|
| **外觀** | 亮色 / 暗色主題 |
| **一般** | 登入 Windows 時自動啟動（背後是一個以系統管理員執行的 Scheduled Task） |
| **硬體驅動程式** | 一鍵透過 WinGet 安裝 PawnIO 驅動，或開官網連結，並即時顯示驅動狀態 |
| **系統匣圖示** | 顯示哪些圖示、排序、各圖示變色門檻 |
| **程序清單** | 每個彈出視窗列出的程序數量（1–50） |
| **更新頻率** | 各類別輪詢間隔，毫秒（250–10000） |

#### 省資源設計

- **可見性閘控輪詢** — 繁重的收集（per-process 清單、SMART、ETW 核心追蹤）只在有視窗顯示時執行；縮在系統匣時幾乎不耗資源。
- DPI 感知的圖示渲染、嚴謹的 GDI 控制代碼管理、凍結（freeze）WPF 幾何讓圖表更順。

### 📦 下載

CI 每次 push 都會建置 **self-contained 單檔** 執行檔，目標機器不需安裝 .NET。

1. 開 [**Actions → build**](https://github.com/KoukeNeko/WinState/actions/workflows/build.yml) 頁，選最新一次綠燈的執行。
2. 依你的 CPU 下載 artifact：
   - `WinState-win-x64` — Intel / AMD 64 位元
   - `WinState-win-arm64` — ARM64
3. 解壓後執行 `WinState.exe`（同意 UAC 提權）。
4. **第一次啟動：** 若尚未安裝 **PawnIO 驅動程式**，WinState 會在設定頁的「硬體驅動程式」區塊提示安裝 — 按 *Install via WinGet*（`winget install -e --id namazso.PawnIO`）或到 [pawnio.eu](https://pawnio.eu/) 下載。沒裝的話 GPU / RAM / 磁碟 / 網路一切照常，只是 CPU 溫度、電壓、Package Power 與主機板感測器會空白。

> Artifact 需登入 GitHub 才能下載、且 90 天後過期 — 從原始碼建置最穩。

> **為何用 PawnIO？** 微軟的 [vulnerable driver blocklist](https://learn.microsoft.com/en-us/windows/security/threat-protection/windows-defender-application-control/microsoft-recommended-driver-block-rules) 已收錄 WinRing0，而那正是原版 LibreHardwareMonitor 讀 CPU MSR 用的驅動。WinState 改用 namazso 的 [PawnIO fork](https://github.com/namazso/LibreHardwareMonitor/tree/pawnio-squashed) — 介面相同、簽章驅動、不會被 Defender 提示。

### 🔨 從原始碼建置

```bash
git clone --recursive https://github.com/KoukeNeko/WinState.git
cd WinState
dotnet run -c Release
```

`--recursive` 會把 PawnIO 版的 LibreHardwareMonitor 拉進 `Vendor/`。若忘記，補一句 `git submodule update --init --recursive`。

產生與 CI 相同的單檔 exe：

```bash
dotnet publish WinState.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

**需求：** Windows 10/11 · .NET 8 SDK。

### 🧱 技術堆疊

- **.NET 8 / WPF**（`net8.0-windows`）
- **[WPF-UI](https://github.com/lepoco/wpfui)** 4.0 — Fluent 控制項、Mica / Acrylic
- **[LibreHardwareMonitorLib（PawnIO fork）](https://github.com/namazso/LibreHardwareMonitor/tree/pawnio-squashed)** — CPU / GPU / 磁碟感測與 SMART，透過簽章版 PawnIO 驅動讀取硬體
- **Microsoft.Diagnostics.Tracing.TraceEvent** — 透過 ETW 取得 per-process 磁碟與網路
- **[Hardcodet.NotifyIcon.Wpf](https://github.com/hardcodet/wpf-notifyicon)** — 系統匣圖示
- **CommunityToolkit.Mvvm** + **Microsoft.Extensions.Hosting** — MVVM 與 DI host

### 📄 授權

目前尚未指定授權；版權保留於作者。若需要加入授權條款，歡迎開 issue。

### 🙏 致謝

使用 [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor)、[WPF-UI](https://github.com/lepoco/wpfui) 與 [Hardcodet NotifyIcon](https://github.com/hardcodet/wpf-notifyicon) 打造。
