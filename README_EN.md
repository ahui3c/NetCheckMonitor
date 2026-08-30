# NetCheckMonitor

[繁體中文](README.md) | [English](README_EN.md)

<img src="assets/NetCheckMonitor-icon.png" alt="NetCheckMonitor icon" width="128">

NetCheckMonitor is a free, open-source, ad-free Windows utility that periodically checks whether a computer can reach the public Internet. It records outages over hours or days and creates graphical HTML and PDF reports suitable for troubleshooting home Internet service or documenting connection problems for an ISP.

Current version: **0.9.16**

## What's new in 0.9.16

- Adds NetCheck Viewer for centralized analysis of multi-computer backups, availability, outages, and scheduled speed tests.
- Viewer can safely request monitoring-interval and daily-backup-time changes through a control file created by NetCheckMonitor in Google Drive.
- Only allowlisted values are accepted; applied, rejected, and failed synchronization outcomes are written back to the control file and Delivery CSV.

See the complete [0.9.16 release notes](docs/RELEASE_NOTES_0.9.16.md).

## Download

- [Download the portable Windows package](dist/NetCheckMonitor-Portable.zip).
- [Download NetCheck Viewer for Windows](https://github.com/ahui3c/NetCheckMonitor/releases/latest/download/NetCheck_Viewer-Portable.zip).
- Extract it and run `NetCheckMonitor.exe`; no installation is required.
- Requirements: Windows 10 or 11, .NET Framework 4.8, and Microsoft Edge for PDF generation.
- The About page includes one-click online update. It downloads, verifies, replaces, and restarts to the latest public release automatically. When the program is under a protected folder such as `Program Files`, Windows asks once for UAC administrator permission after verification.

## Features

- Tests Microsoft, Google, and Cloudflare HTTPS endpoints every 60 seconds by default.
- Treats any successful endpoint as Internet availability, reducing false alarms caused by one service.
- The first failure triggers a fast retry after 5 seconds. Only consecutive failures confirm an outage. Prolonged outages automatically use a lower retry frequency, but the interval never exceeds the period configured on the main screen.
- The system tray icon shows live status while monitoring: green for online, red for a confirmed outage, orange while checking, and gray while paused.
- The main window shows the current adapter, connection type (wired/Wi-Fi/VPN), and Wi-Fi signal percentage. Adapter changes are recorded in the raw log and reports.
- **Settings** switches between the built-in targets and up to three custom websites or IP addresses, tried in order until one succeeds.
- Application settings are stored in a portable file beside the executable. Changing targets while monitoring starts a separate session so one report cannot mix target definitions.
- Settings remain available while monitoring. If the targets change, the current session and report are saved safely before monitoring restarts automatically with the new targets.
- Optional advanced layered diagnostics run only after an HTTPS failure and check the adapter, gateway, DNS, IPv4, IPv6, HTTPS target, and Wi-Fi signal. This setting never changes outage detection, duration, or percentage statistics.
- Supports Traditional Chinese and English. The user chooses the interface language on first launch and can change it later in Settings.
- Pause and resume monitoring. Paused periods are marked but excluded from availability and daily outage percentages.
- Add timestamped event notes while monitoring using custom text or quick entries for restarting the modem, wireless router, or computer, rain, and thunder. Notes appear in CSV/HTML/PDF reports without changing outage statistics.
- Flushes every check to a UTF-8 CSV immediately and keeps a local recovery copy.
- Creates live HTML reports without interrupting monitoring.
- Live and final HTML reports cumulatively analyze every historical CSV that has not been cleared. Gaps between sessions, app-not-running periods, and sessions without checks are excluded from statistics.
- Downloads A4 landscape PDF reports for all saved data or a selected date range.
- Reports include daily outage statistics, longest/average/shortest outages, 95th-percentile and maximum latency, average latency variation, and 24-hour timelines.
- Active-session state is saved durably. After a crash or Windows restart, the original CSV can be resumed while time when the app was not running is marked and excluded.
- Settings can independently launch NetCheckMonitor after Windows sign-in and start monitoring automatically when the app opens. When both are enabled and Windows launches the app at sign-in, an unfinished session resumes automatically without a confirmation dialog; manual launches still ask first.
- Startup checks whether NetCheckMonitor is already running. A duplicate launch shows the existing window instead of creating a second monitoring process.
- Performs scheduled daily Google Drive backups of the complete PDF and raw CSV to `Net_Check/computer name`, keeping multiple computers organized in one Drive.
- NetCheck Viewer centrally analyzes multiple computers and can safely request monitoring-interval and daily-backup-time changes through an allowlisted control file, normally applied within five minutes after Google Drive synchronization.
- Optionally signs in to Gmail to email the daily PDF and CSV report to that same signed-in account, send a recovery notice after a confirmed outage, and send a test message. Sender and recipient are fixed to the same account.
- When scheduled speed testing is enabled and that day has speed-test records, Drive backup and Gmail delivery also include a daily speed-test HTML report and raw speed-test CSV. The normal monitoring report still succeeds on days without speed-test data.
- Gmail delivery and Google Drive backup success, failure, and skipped outcomes are written to a separate Delivery CSV for schedule, retry, and error tracking.
- Settings can prevent Windows sleep while monitoring and can separately block shutdown or restart. When shutdown protection is enabled, use the lower-right **Exit and Stop Monitoring** button first. Forced updates, power loss, and hardware resets can still interrupt the app.
- Speed testing is currently marked Beta and is available only as an optional scheduled Cloudflare test; the main window does not provide an on-demand test button. Quick, Standard, and Full multi-stream levels are available in Settings.
- Optional scheduled speed tests can run every 1–168 hours (24 hours by default and disabled by default), only while monitoring is active and not paused. Metered, roaming, or over-limit connections are skipped by default; when explicitly allowed, each run still requires two warnings.
- The speed-test settings page provides the separate HTML trend report plus official Speedtest and HiNet comparison links. The report includes download, upload, latency, jitter, daily summaries, transferred data, and network-interface context without affecting outage statistics.
- Cloudflare and Speedtest use different servers, routes, and measurement methods, so their numbers need not match. Up to eight transfer streams are used to reduce under-reporting on fast fiber connections.
- Scheduled tests retain a persistent cooldown of at least 15 minutes after a test starts. Cloudflare does not publish a fixed limit for these speed-test endpoints; HTTP 403/429 responses trigger at least a 60-minute pause, honor a longer `Retry-After`, and progressively back off up to 24 hours.
- The X button minimizes to the system tray; the first use shows a one-time reminder to exit safely with the lower-right **Exit and Stop Monitoring** button.
- Safely exits only after records are flushed and a final report has been created.

## Quick start

1. Download and extract the portable package.
2. Run `NetCheckMonitor.exe`.
3. Open **Settings** to use custom targets or enable advanced layered diagnostics after failures.
4. Confirm the check interval and select **Start Monitoring**. While active, the button is disabled to prevent accidental interruption of a long-running test.
   If Windows started the app at sign-in and both startup options are enabled, an unfinished session resumes automatically. A manual launch still asks whether to resume it.
5. Select **Pause** for periods that should not be included in statistics, then **Resume** to continue.
6. While monitoring, select **View Report** or download a PDF at any time without interrupting monitoring.
7. You can view reports at any time while monitoring. To finish testing, use **Exit and Stop Monitoring** in the lower-right corner.
8. To measure bandwidth periodically, enable it under **Settings** → **Scheduled Speed Test Settings**. The speed trend report and links to other official speed-test services are also available there.
8. Use **Exit and Stop Monitoring** in the lower-right corner so the program stops monitoring and verifies that all data is saved.

See the complete [English user guide](docs/User_Guide_EN.md).

## Google Drive backup

1. Open **Settings** → **Google Drive Backup Settings**.
2. Select **Sign in to Google Drive** and authorize your own Google account in the system browser.
3. Set the daily backup time.

Users do not need to create a Google Cloud project or download credential files. The app uses a Desktop OAuth client ID with PKCE; the installed-app credential required by Google is injected only during release builds and is not committed to the public source. The Drive feature requests only the `drive.file` scope, and Windows DPAPI encrypts the refresh token for the current Windows account.

## Gmail daily reports and recovery notifications

1. Open **Settings** → **Gmail Report & Notification Settings**.
2. Select **Sign in to Gmail**, sign in with your own Google account, and grant the `gmail.send` permission.
3. Choose daily PDF/CSV reports, recovery notifications, and the daily delivery time, then save.
4. Use **Send Test Email** to verify delivery. Messages can only be sent to the same signed-in account; no other recipient address can be entered.

During a complete outage Gmail cannot be reached. The recovery notice is encrypted locally with DPAPI and sent after connectivity returns; temporary failures use automatic backoff and retry. Gmail and Google Drive use separate grants, and the Gmail feature does not request permission to read the mailbox.

For a public release, the Google Cloud project that owns the OAuth client must enable the Gmail API and complete OAuth verification for the sensitive `gmail.send` scope. During development, add the intended accounts to the OAuth test audience.

## Data and privacy

- Filename format: `NetCheck_<computer>-<8-character ID>_yyyyMMdd_HHmmss.csv`.
- The identifier is normally derived from a one-way hash of Windows MachineGuid. Raw MachineGuid, MAC addresses, and hardware serial numbers are never written to output files.
- Primary data is stored in `NetCheck_Data` beside the executable, or in Documents when that location is not writable.
- Recovery copies are stored in `%LOCALAPPDATA%\NetCheck\Recovery`.
- Personal CSV, HTML, PDF, Google refresh-token, queued-notification, and settings files must not be committed to a public repository.

## Build from source

Run this command in Windows PowerShell 5.1:

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

The build uses the C# compiler included with Windows and does not require the .NET SDK. `NetCheck-Portable` receives the app, updater, and update manifest. Run `package-release.ps1` to create a release ZIP containing every automatic-update component.

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
