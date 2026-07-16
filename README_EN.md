# NetCheckMonitor

[繁體中文](README.md) | [English](README_EN.md)

<img src="assets/NetCheckMonitor-icon.png" alt="NetCheckMonitor icon" width="128">

NetCheckMonitor is a free, open-source, ad-free Windows utility that periodically checks whether a computer can reach the public Internet. It records outages over hours or days and creates graphical HTML and PDF reports suitable for troubleshooting home Internet service or documenting connection problems for an ISP.

Current version: **0.9.1**

## Download

- [Download the portable Windows package](dist/NetCheckMonitor-Portable.zip).
- Extract it and run `NetCheckMonitor.exe`; no installation is required.
- Requirements: Windows 10 or 11, .NET Framework 4.8, and Microsoft Edge for PDF generation.

## Features

- Tests Microsoft, Google, and Cloudflare HTTPS endpoints every 60 seconds by default.
- Treats any successful endpoint as Internet availability, reducing false alarms caused by one service.
- Supports Traditional Chinese and English. Traditional Chinese Windows installations use Chinese automatically; every unsupported language defaults to English.
- Pause and resume monitoring. Paused periods are marked but excluded from availability and daily outage percentages.
- Flushes every check to a UTF-8 CSV immediately and keeps a local recovery copy.
- Creates live HTML reports without interrupting monitoring.
- Downloads A4 landscape PDF reports for all saved data or a selected date range.
- Reports include the test computer name, daily outage time, daily outage percentage, and a 24-hour timeline.
- Performs scheduled daily Google Drive backups of the complete PDF and raw CSV to `Net_Check`.
- Prevents Windows system sleep while monitoring and minimizes to the system tray to protect against accidental closure.
- Safely exits only after records are flushed and a final report has been created.

## Quick start

1. Download and extract the portable package.
2. Run `NetCheckMonitor.exe`.
3. Confirm the check interval and select **Start**.
4. Select **Pause** for periods that should not be included in statistics, then **Resume** to continue.
5. While monitoring, you may create a live report or download a PDF at any time.
6. Select **Stop and Create Report** when testing is complete.
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
