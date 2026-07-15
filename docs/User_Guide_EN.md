# NetCheckMonitor User Guide (English)

Version: 0.9.1

NetCheckMonitor periodically tests public Internet connectivity, records outages, and produces graphical reports. It supports Google Drive backup and PDF downloads and is completely free, open source, and ad-free.

## Interface language

- Windows display languages for Traditional Chinese regions (`zh-TW`, `zh-HK`, `zh-MO`, or `zh-Hant`) use the Traditional Chinese interface.
- Simplified Chinese and every other unsupported language default to English.
- No manual switch or additional language pack is required.

## Start monitoring

1. Run `NetCheckMonitor.exe`.
2. The default interval is 60 seconds. You may change it before starting.
3. Select **Start**. The program keeps running in the system tray when minimized.
4. Select **Pause** when a period should not be counted, then **Resume** to continue. The period remains marked but is excluded from statistics.
5. Select **Stop and Create Report** to finish and create the final HTML report.

## Live reports and PDF

- Select **Create Live Report** at any time; monitoring continues uninterrupted.
- The `_Live.html` report is refreshed automatically every 10 minutes.
- **Download PDF Report** can include all saved data or a selected date range.
- PDFs contain daily effective monitoring time, estimated outage time, outage percentage, event tables, and 24-hour timelines.
- PDF generation uses the headless printing feature included with Microsoft Edge.

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
- While monitoring, the window close button and Alt+F4 minimize the program to the system tray.
- The **Exit** button stops monitoring, saves data, and creates the final report before closing.
- The program requests that Windows not enter system sleep while monitoring; the display may still turn off normally.
- Keep laptops connected to power. Closing a lid configured for sleep may still force Windows to sleep.
- Do not unplug a USB drive containing the running program.

## Clear saved data

**Clear Saved Data** in the lower-right corner removes CSV, HTML, live-report, and recovery files managed by NetCheckMonitor. PDFs downloaded elsewhere are not removed. Clearing is blocked while monitoring or cloud backup is active.

## Daily outage percentage

Daily outage percentage = estimated outage time / effective monitoring time × 100%. Paused periods are excluded from both values. Outage start and recovery times are estimated from adjacent checks, so precision depends on the check interval.

## Requirements

- Windows 10 or Windows 11
- .NET Framework 4.8
- Microsoft Edge for PDF generation
- Internet access and a working default system browser for Google Drive backup
