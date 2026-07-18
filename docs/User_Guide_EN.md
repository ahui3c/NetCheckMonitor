# NetCheckMonitor User Guide (English)

Version: 0.9.5

NetCheckMonitor periodically tests public Internet connectivity, records outages, and produces graphical reports. It supports Google Drive backup and PDF downloads and is completely free, open source, and ad-free.

## Interface language

- Windows display languages for Traditional Chinese regions (`zh-TW`, `zh-HK`, `zh-MO`, or `zh-Hant`) use the Traditional Chinese interface.
- Simplified Chinese and every other unsupported language default to English.
- No manual switch or additional language pack is required.

## Start monitoring

1. Run `NetCheckMonitor.exe`.
2. The default interval is 60 seconds. You may change it before starting.
3. To use your own targets, open **Settings**, choose custom targets, and enter one to three websites or IP addresses.
4. Select **Start**. The program keeps running in the system tray when minimized.
   The first failure triggers a fast retry after 5 seconds. Consecutive failures confirm an outage. Prolonged outages automatically use a lower retry frequency, but the interval never exceeds the period configured on the main screen.
   The system tray icon remains visible while monitoring: green means online, red means a confirmed outage, orange means checking, and gray means paused.
   The main window shows the current adapter, connection type (wired/Wi-Fi/VPN), and Wi-Fi signal percentage. Adapter changes are stored in CSV, HTML, and PDF reports.
5. Select **Pause** when a period should not be counted, then **Resume** to continue. The period remains marked but is excluded from statistics.
6. Select **Stop and Create Report** to finish and create the final HTML report.

## Monitoring target settings

- **Use built-in test targets** tries the public Microsoft, Google, and Cloudflare connectivity endpoints in order.
- **Use custom test targets** accepts up to three websites or IP addresses. They are tried in the displayed order, and the first success marks that check online.
- You may enter `https://example.com/status`, `example.com`, or an IP such as `1.1.1.1`. Without a scheme, websites use HTTPS and IP addresses use HTTP.
- Only HTTP and HTTPS are supported. Blank, malformed, credential-containing, or duplicate targets are rejected before saving.
- Settings are stored for the current Windows user in `%LOCALAPPDATA%\NetCheck\Monitor\settings.json`.
- You can independently enable **Start the app after Windows sign-in** and **Start monitoring automatically when the app opens**. Recovery of an unfinished session is handled before a new automatic session starts.
- Startup checks for an existing NetCheckMonitor instance. A duplicate launch shows the existing window instead of starting another monitoring process.
- Settings remain available while monitoring. Target changes safely save the current session and report, then automatically start a new monitoring session. Changing only startup options does not interrupt the current session.
- Optional advanced layered diagnostics run only after an HTTPS failure and check the adapter, default gateway, DNS, IPv4, IPv6, HTTPS target, and Wi-Fi signal. Toggling this option while monitoring takes effect immediately without restarting the session.

## Live reports and PDF

- Select **Create Live Report** at any time; monitoring continues uninterrupted.
- The `_Live.html` report is refreshed automatically every 10 minutes.
- **Download PDF Report** can include all saved data or a selected date range.
- PDFs contain daily effective monitoring time, estimated outage time, outage percentage, event tables, and 24-hour timelines.
- PDF generation uses the headless printing feature included with Microsoft Edge.
- Reports also include outage count, longest/average/shortest outage, 95th-percentile and maximum latency, and average latency variation.

## Resume after a crash or restart

- Active state is saved to `%LOCALAPPDATA%\NetCheck\Monitor\active-session.json` after checks, pauses, and resumes.
- After an abnormal exit or Windows restart, the next launch asks whether to resume the original CSV.
- Time when the app was not running is marked as interrupted and excluded from effective monitoring time and outage percentage.
- A normal stop or safe exit removes the active-session state.

## Daily Google Drive backup

1. Open **Google Drive Backup**.
2. Select **Sign in to Google Drive**, sign in with your own account in the system browser, and grant permission.
3. Set and save the daily backup time. The default is 23:55.
4. The first successful connection creates or reuses `Net_Check` in Drive.
5. A complete PDF and raw CSV are uploaded each day. Existing same-name files are updated instead of duplicated.

The app requests only the `drive.file` scope. Windows DPAPI encrypts the sign-in token for the current Windows account. NetCheckMonitor must remain running for an on-time backup; after a missed run, it prioritizes the most recent day at the next start.

## Output files and computer ID

- Primary records are written to `NetCheck_Data` beside the program.
- If that location is not writable, the app uses `Documents\NetCheck_Data`.
- Recovery copies are stored in `%LOCALAPPDATA%\NetCheck\Recovery`.
- Filenames use `NetCheck_<computer>-<8-character ID>_<date and time>`.
- The ID normally comes from a one-way hash of Windows MachineGuid. If unavailable, a MAC address or computer-name hash is used.
- Raw MachineGuid, MAC addresses, and hardware serial numbers are never written to CSV, reports, or filenames.

## Long-running protection

- Every result is force-flushed to both the primary CSV and recovery copy.
- The window close button and Alt+F4 minimize the app to the system tray instead of exiting. The first use shows a one-time reminder to exit safely with the lower-right **Exit** button.
- The **Exit** button stops monitoring, saves data, and creates the final report before closing.
- The program requests that Windows not enter system sleep while monitoring; the display may still turn off normally.
- Keep laptops connected to power. Closing a lid configured for sleep may still force Windows to sleep.
- Do not unplug a USB drive containing the running program.

## Clear saved data

**Clear Saved Data** in the lower-right corner removes CSV, HTML, live-report, and recovery files managed by NetCheckMonitor. PDFs downloaded elsewhere are not removed. Clearing is blocked while monitoring or cloud backup is active.

## Daily outage percentage

Daily outage percentage = confirmed outage time / effective monitoring time × 100%. Paused, interrupted, and powered-off periods are excluded. A first failure is not counted as an outage until the fast retry confirms it.

Enabling or disabling advanced diagnostics changes only the layered evidence collected after failures. It does not change outage detection or statistics, so availability, outage duration, and percentages remain directly comparable. Reports mark failures recorded without diagnostics as **Advanced diagnostics not performed**.

## GitHub and update checks

- The About page includes the GitHub project link and a **Check for Updates** button.
- GitHub Releases is queried once only when the user selects the button; there is no background polling.
- If an update is found, the app only asks whether to open the Releases page. Downloads and installation remain under the user's control.

## Requirements

- Windows 10 or Windows 11
- .NET Framework 4.8
- Microsoft Edge for PDF generation
- Internet access and a working default system browser for Google Drive backup
