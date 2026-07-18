# NetCheckMonitor

[繁體中文](README.md) | [English](README_EN.md)

<img src="assets/NetCheckMonitor-icon.png" alt="NetCheckMonitor icon" width="128">

NetCheckMonitor is a free, open-source, ad-free Windows utility that periodically checks whether a computer can reach the public Internet. It records outages over hours or days and creates graphical HTML and PDF reports suitable for troubleshooting home Internet service or documenting connection problems for an ISP.

Current version: **0.9.5**

## What's new in 0.9.5

- Added automatic launch after Windows sign-in so unfinished monitoring can resume after an unexpected restart.
- Added an option to start monitoring automatically when the app opens.
- Added up to three custom website or IP test targets.
- Expanded reports with daily outage, latency, network-interface, and other detailed test data.
- Added color-coded system-tray connection status: green for online, red for outage, orange for checking, and gray for paused.
- Added connection type, active adapter, and Wi-Fi signal information to the app and CSV/HTML/PDF reports.
- Added optional advanced layered connectivity diagnostics.
- Improved fast outage verification, prolonged-outage tracking, abnormal-exit handling, and data recovery details.

See the complete [0.9.5 release notes](docs/RELEASE_NOTES_0.9.5.md).

## Download

- [Download the portable Windows package](dist/NetCheckMonitor-Portable.zip).
- Extract it and run `NetCheckMonitor.exe`; no installation is required.
- Requirements: Windows 10 or 11, .NET Framework 4.8, and Microsoft Edge for PDF generation.
- The About page includes the GitHub project link and a manual update check. GitHub is queried only when requested, and the user decides whether to open the Releases page.

## Features

- Tests Microsoft, Google, and Cloudflare HTTPS endpoints every 60 seconds by default.
- Treats any successful endpoint as Internet availability, reducing false alarms caused by one service.
- The first failure triggers a fast retry after 5 seconds. Only consecutive failures confirm an outage. Prolonged outages automatically use a lower retry frequency, but the interval never exceeds the period configured on the main screen.
- The system tray icon shows live status while monitoring: green for online, red for a confirmed outage, orange while checking, and gray while paused.
- The main window shows the current adapter, connection type (wired/Wi-Fi/VPN), and Wi-Fi signal percentage. Adapter changes are recorded in the raw log and reports.
- **Settings** switches between the built-in targets and up to three custom websites or IP addresses, tried in order until one succeeds.
- Custom settings are saved for the current Windows user. Changing targets while monitoring starts a separate session so one report cannot mix target definitions.
- Settings remain available while monitoring. If the targets change, the current session and report are saved safely before monitoring restarts automatically with the new targets.
- Optional advanced layered diagnostics run only after an HTTPS failure and check the adapter, gateway, DNS, IPv4, IPv6, HTTPS target, and Wi-Fi signal. This setting never changes outage detection, duration, or percentage statistics.
- Supports Traditional Chinese and English. Traditional Chinese Windows installations use Chinese automatically; every unsupported language defaults to English.
- Pause and resume monitoring. Paused periods are marked but excluded from availability and daily outage percentages.
- Flushes every check to a UTF-8 CSV immediately and keeps a local recovery copy.
- Creates live HTML reports without interrupting monitoring.
- Downloads A4 landscape PDF reports for all saved data or a selected date range.
- Reports include daily outage statistics, longest/average/shortest outages, 95th-percentile and maximum latency, average latency variation, and 24-hour timelines.
- Active-session state is saved durably. After a crash or Windows restart, the original CSV can be resumed while time when the app was not running is marked and excluded.
- Settings can independently launch NetCheckMonitor after Windows sign-in and start monitoring automatically when the app opens. An unfinished session is offered for recovery first.
- Startup checks whether NetCheckMonitor is already running. A duplicate launch shows the existing window instead of creating a second monitoring process.
- Performs scheduled daily Google Drive backups of the complete PDF and raw CSV to `Net_Check`.
- Prevents Windows system sleep while monitoring. The X button minimizes to the system tray; the first use shows a one-time reminder to exit safely with the lower-right **Exit** button.
- Safely exits only after records are flushed and a final report has been created.

## Quick start

1. Download and extract the portable package.
2. Run `NetCheckMonitor.exe`.
3. Open **Settings** to use custom targets or enable advanced layered diagnostics after failures.
4. Confirm the check interval and select **Start**.
   If an unfinished session is found, the app first asks whether to resume it.
5. Select **Pause** for periods that should not be included in statistics, then **Resume** to continue.
6. While monitoring, you may create a live report or download a PDF at any time.
7. Select **Stop and Create Report** when testing is complete.
7. Use the **Exit** button in the lower-right corner so the program can verify that all data is saved.

See the complete [English user guide](docs/User_Guide_EN.md).

## Google Drive backup

1. Open **Google Drive Backup**.
2. Select **Sign in to Google Drive** and authorize your own Google account in the system browser.
3. Set the daily backup time.

Users do not need to create a Google Cloud project or download credential files. The app uses a Desktop OAuth client ID with PKCE and embeds no client secret in source or binaries. It requests only the `drive.file` scope, and Windows DPAPI encrypts the refresh token for the current Windows account.

## Data and privacy

- Filename format: `NetCheck_<computer>-<8-character ID>_yyyyMMdd_HHmmss.csv`.
- The identifier is normally derived from a one-way hash of Windows MachineGuid. Raw MachineGuid, MAC addresses, and hardware serial numbers are never written to output files.
- Primary data is stored in `NetCheck_Data` beside the executable, or in Documents when that location is not writable.
- Recovery copies are stored in `%LOCALAPPDATA%\NetCheck\Recovery`.
- Personal CSV, HTML, PDF, Google refresh-token, and settings files must not be committed to a public repository.

## Build from source

Run this command in Windows PowerShell 5.1:

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

The build uses the C# compiler included with Windows and does not require the .NET SDK. Output is written to `NetCheck-Portable\NetCheckMonitor.exe`.

## Tests

```powershell
powershell -ExecutionPolicy Bypass -File .\tests\SelfTest.ps1
```

The full self-test uses Microsoft Edge headless printing to produce PDFs in an isolated test directory. It verifies monitoring, pause exclusion, reports, durable storage, cloud-backup artifacts, and both interface languages.

## Author and license

- Liao A-Hui (廖阿輝)
- <chehui@gmail.com>
- <https://ahui3c.com>

This project is released under the [MIT License](LICENSE).
