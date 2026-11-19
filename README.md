# FixMate

### Your all-in-one Windows PC diagnostics, monitoring and maintenance companion.

[![Platform](https://img.shields.io/badge/Platform-Windows-0078D4?logo=windows)](https://www.microsoft.com/windows)
[![Language](https://img.shields.io/badge/Language-C%23-512BD4?logo=csharp)](https://learn.microsoft.com/dotnet/csharp/)
[![Framework](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![UI](https://img.shields.io/badge/UI-WPF-0078D4)](https://learn.microsoft.com/dotnet/desktop/wpf/)
[![Status](https://img.shields.io/badge/Status-In%20Development-orange)]()

---

## Overview

**FixMate** is a Windows desktop application designed to help users understand, monitor, diagnose and maintain their computers.

Whether you're a casual PC user trying to solve a problem or an enthusiast who wants detailed information about their hardware, FixMate aims to bring useful PC management and diagnostic tools together in one place.

The goal is simple: make PC troubleshooting more accessible, system information easier to understand, and computer maintenance less complicated.

## Features

FixMate is currently under development. The following features represent the project's development goals.

### System Overview
- Detailed computer specifications.
- CPU, GPU, RAM and storage information.
- Windows version and system information.
- System status dashboard.
- Recent diagnostic results.

### Hardware Monitoring
- Real-time CPU and memory usage.
- GPU monitoring where supported.
- Disk activity and network throughput.
- Hardware temperature monitoring where supported.
- Performance graphs and monitoring history.

### Diagnostics Center
- Hardware information checks.
- Storage diagnostics.
- Network connectivity tests.
- Windows system integrity checks.
- Diagnostic reports and results history.

### Troubleshooting Center
- Guided troubleshooting workflows.
- Blue Screen of Death (BSOD) information.
- Windows startup problems.
- Performance issues.
- Hardware and driver problems.
- Internet connectivity issues.

### Maintenance Center
- Temporary file analysis and cleanup.
- Startup application management.
- Installed software overview.
- Storage management.
- Maintenance reminders and history.

### Error Center
- Windows Event Log analysis.
- Error and warning filtering.
- BSOD stop-code information.
- Searchable troubleshooting explanations.

### Additional Tools
- Process Manager.
- Driver Center.
- Network diagnostics.
- Software inventory.
- Report generation.
- Notification system.

## Current Implementation

The current WPF shell includes dark graphite styling, page navigation, section search, and an overview populated from Windows system and storage providers. CPU, memory, disk, and network metrics are sampled from Windows APIs/WMI and plotted from live ViewModel history; unsupported or warming-up readings are reported as unavailable. Live Monitor supports pause/resume, refresh, history clearing and ranges, summary statistics, and per-process CPU/memory where Windows provides the readings. CPU and memory warning thresholds are configurable. GPU utilization and hardware temperature sensors are not connected in this build. The charts use a lightweight in-app WPF control rather than an external charting package.

Network Center reads local Windows adapter configuration and provides user-targeted ping, traceroute, DNS lookup, and ICMP connectivity checks. The user must enter the target; the UI warns that network requests may contact external hosts or configured DNS. Network test history is stored locally. Speed test and Wi-Fi SSID/signal data are not implemented. Software Center reads standard uninstall registry entries, supports search and CSV export, and only opens verified local install folders or HTTP(S) publisher URLs recorded by Windows. Reports Center supports selected-section previews, HTML/JSON/CSV export, local export history, and printing; PDF export is not implemented. Maintenance reminders and maintenance-operation history are stored locally in SQLite; history includes completed startup-item changes and temporary-file cleanup outcomes. Due reminders are presented while FixMate is running. Settings persist Dark, Light, or Windows System theme (System follows Windows appearance changes while FixMate is open), named accent colors, reduced navigation animations, compact layout, sidebar preference with a directional collapse/expand icon, monitoring polling/history settings, CPU/RAM warning thresholds, reminder-notification preference, Windows sign-in launch, and start-minimized behavior. Appearance and accessibility selections preview immediately and can be saved directly from their section. Settings saves keep Windows startup integration and persisted preferences consistent; clearing local data removes saved histories, report exports, and logs, then resets preferences. Telemetry is not collected. Minimize-to-tray and additional languages are not implemented. Update checks and license metadata are unavailable because no update feed or license source is configured.

## Technology Stack

FixMate is being developed using:

| Technology | Purpose |
|---|---|
| C# | Main programming language |
| .NET 10 | Application framework |
| WPF | Windows desktop interface |
| MVVM | Application architecture |
| CommunityToolkit.Mvvm | MVVM implementation |
| SQLite | Local data storage |
| Microsoft.Extensions.DependencyInjection | Dependency injection |
| Windows APIs / WMI | System information and diagnostics |
| xUnit | Automated testing |

## Architecture

The project follows a modular architecture designed to keep the application maintainable and extensible.

```text
FixMate/
│
├── src/
│   ├── FixMate.App/
│   ├── FixMate.Core/
│   ├── FixMate.Infrastructure/
│   ├── FixMate.Diagnostics/
│   ├── FixMate.Monitoring/
│   ├── FixMate.Troubleshooting/
│   ├── FixMate.Maintenance/
│   └── FixMate.Reporting/
│
├── tests/
│   ├── FixMate.Core.Tests/
│   ├── FixMate.Diagnostics.Tests/
│   └── FixMate.Infrastructure.Tests/
│
└── FixMate.sln
```

## Design Philosophy

FixMate is being designed around a few important principles:

- **Clarity:** Technical information should be understandable to everyone.
- **Reliability:** Diagnostic results should reflect actual system information.
- **Safety:** Potentially destructive operations must require user confirmation.
- **Privacy:** Personal diagnostic information should remain local by default.
- **Performance:** Monitoring and background operations should avoid unnecessary resource consumption.
- **Accessibility:** The application should accommodate both beginners and advanced users.

## Development Status

FixMate is currently in its early development stage.

The initial focus is on building the application foundation, establishing its architecture, creating the user interface and implementing reliable Windows system information gathering.

Features will be introduced progressively as development continues.

## Roadmap

- [ ] Application foundation and navigation.
- [ ] Dark and light themes.
- [ ] Settings and local configuration.
- [ ] Hardware information detection.
- [ ] System overview dashboard.
- [ ] Real-time performance monitoring.
- [ ] Diagnostics center.
- [ ] Troubleshooting engine.
- [ ] Windows Event Log analysis.
- [ ] Maintenance tools.
- [ ] Process and driver management.
- [ ] Network diagnostics.
- [ ] Reporting and data export.
- [ ] Automated testing.
- [ ] Windows installer and public release.

## Installation

**There is currently no stable public release.**

Installation instructions and downloadable builds will be provided here once the application is ready for distribution.

## Contributing

Contributions, suggestions and bug reports will be welcome once the project reaches a suitable stage for external contributions.

If you have ideas for useful diagnostic tools or improvements, feel free to open an issue.

## License

A license will be selected before the first public release.

---

**FixMate** — Understand your PC. Diagnose problems. Keep it healthy.
