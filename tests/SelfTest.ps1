param([string]$ExecutableName = 'NetCheckMonitor.exe')

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$testRoot = Join-Path $root '.selftest'
New-Item -ItemType Directory -Force -Path $testRoot | Out-Null
Copy-Item -LiteralPath (Join-Path $root ('NetCheck-Portable\' + $ExecutableName)) -Destination (Join-Path $testRoot 'NetCheckMonitor.exe') -Force
$env:NETCHECK_BACKUP_DIR = Join-Path $testRoot 'Backup'
$env:NETCHECK_DATA_ROOTS = (Join-Path $testRoot 'NetCheck_Data') + ';' + $env:NETCHECK_BACKUP_DIR
$env:NETCHECK_CLOUD_SETTINGS = Join-Path $testRoot 'Cloud\settings.dat'
$env:NETCHECK_UI_LANGUAGE = 'zh-TW'

$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $testRoot 'NetCheckMonitor.exe'))
$type = $assembly.GetType('NetCheck.MainForm', $true)
$form = [Activator]::CreateInstance($type, $true)
$flags = [Reflection.BindingFlags]'Instance,NonPublic'
$staticFlags = [Reflection.BindingFlags]'Static,NonPublic'
$dataButton = $type.GetField('dataButton', $flags).GetValue($form)
$clearDataButton = $type.GetField('clearDataButton', $flags).GetValue($form)
$exitButton = $type.GetField('exitButton', $flags).GetValue($form)
$cloudButton = $type.GetField('cloudButton', $flags).GetValue($form)
$aboutButton = $type.GetField('aboutButton', $flags).GetValue($form)
$downloadButtonLabel = $dataButton.Text -eq '下載報表 PDF 文件'
$clearButtonLayout = $clearDataButton.Width -le 130 -and $clearDataButton.Height -le 28 -and $clearDataButton.Top -ge 495
$exitButtonLayout = $exitButton.Text -eq '關閉程式' -and $exitButton.Width -le 120 -and $exitButton.Height -le 28 -and $exitButton.Top -ge 495 -and $exitButton.Left -gt $clearDataButton.Left
$reportFormType = $assembly.GetType('NetCheck.DataReportForm', $true)
$clearRemovedFromPdfDialog = $null -eq $reportFormType.GetField('clearButton', $flags)
$cloudButtonLayout = $cloudButton.Text -eq 'Google Drive 備份設定' -and $cloudButton.Width -le 180 -and $cloudButton.Height -le 28 -and $cloudButton.Top -ge 495
$aboutButtonLayout = $aboutButton.Text -eq '關於' -and $aboutButton.Width -le 80 -and $aboutButton.Top -ge 495
$aboutFormType = $assembly.GetType('NetCheck.AboutForm', $true)
$aboutForm = [Activator]::CreateInstance($aboutFormType)
$aboutText = @($aboutForm.Controls | ForEach-Object { $_.Text }) -join "`n"
$aboutPageContent = $aboutForm.Text -eq '關於 NetCheckMonitor' -and $aboutText.Contains('NetCheckMonitor') -and $aboutText.Contains('版本 0.9.2') -and $aboutText.Contains('可定時監控對外網路連線，紀錄斷線並產生圖文報表，並支援網路硬碟備份，PDF 下載，程式完全免費開源無廣告。') -and $aboutText.Contains('廖阿輝') -and $aboutText.Contains('chehui@gmail.com') -and $aboutText.Contains('https://ahui3c.com')
$programIdentity = $form.Text -eq '對外網路連線能力監控程式' -and $assembly.GetName().Version.ToString() -eq '0.9.2.0'
$embeddedIcon = [Drawing.Icon]::ExtractAssociatedIcon((Join-Path $testRoot 'NetCheckMonitor.exe'))
$iconStream = New-Object IO.MemoryStream
$defaultIconStream = New-Object IO.MemoryStream
$embeddedIcon.ToBitmap().Save($iconStream, [Drawing.Imaging.ImageFormat]::Png)
[Drawing.SystemIcons]::Application.ToBitmap().Save($defaultIconStream, [Drawing.Imaging.ImageFormat]::Png)
$customIconEmbedded = [Convert]::ToBase64String($iconStream.ToArray()) -ne [Convert]::ToBase64String($defaultIconStream.ToArray()) -and $null -ne $form.Icon
$iconStream.Dispose(); $defaultIconStream.Dispose(); $embeddedIcon.Dispose()
$testUrls = $type.GetField('TestUrls', $staticFlags).GetValue($null)
for ($i = 0; $i -lt $testUrls.Length; $i++) { $testUrls[$i] = 'http://127.0.0.1:9/' }

$type.GetMethod('StartMonitoring', $flags).Invoke($form, @())
Start-Sleep -Seconds 18
$closingArgs = New-Object System.Windows.Forms.FormClosingEventArgs([System.Windows.Forms.CloseReason]::UserClosing, $false)
$closingArgs = $closingArgs.PSObject.BaseObject
$closingMethod = $type.GetMethods($flags) | Where-Object { $_.Name -eq 'OnFormClosing' -and $_.DeclaringType -eq $type } | Select-Object -First 1
$closingMethod.Invoke($form, [object[]]@($form, $closingArgs))
$type.GetMethod('TogglePause', $flags).Invoke($form, @())
Start-Sleep -Seconds 1
$type.GetMethod('TogglePause', $flags).Invoke($form, @())
Start-Sleep -Seconds 18
$type.GetMethod('CreateLiveReport', $flags).Invoke($form, @($false))
$liveHtml = Get-ChildItem -LiteralPath (Join-Path $testRoot 'NetCheck_Data') -Filter '*_Live.html' | Select-Object -First 1
$exitSaveCompleted = $type.GetMethod('SaveAndFinalizeForExit', $flags).Invoke($form, @())

$dataDir = Join-Path $testRoot 'NetCheck_Data'
$csv = Get-ChildItem -LiteralPath $dataDir -Filter '*.csv' | Select-Object -First 1
$html = Get-ChildItem -LiteralPath $dataDir -Filter '*.html' | Select-Object -First 1
$rows = Import-Csv -LiteralPath $csv.FullName
$htmlText = Get-Content -LiteralPath $html.FullName -Raw
$reportHasChineseProductName = $htmlText.Contains('對外網路連線能力監控報表')
$backupCsv = Get-ChildItem -LiteralPath $env:NETCHECK_BACKUP_DIR -Filter '*.csv' | Select-Object -First 1
$dailyPattern = [regex]::Escape((Get-Date).ToString('yyyy/MM/dd')) + "</td><td>[^<]*</td><td class='bad'>[^<]*</td><td class='bad'>([0-9.]+)%"
$dailyMatch = [regex]::Match($htmlText, $dailyPattern)
$dailyPercent = if ($dailyMatch.Success) { [double]::Parse($dailyMatch.Groups[1].Value, [Globalization.CultureInfo]::InvariantCulture) } else { 0 }
$archiveType = $assembly.GetType('NetCheck.ArchiveReport', $true)
$exportMethod = $archiveType.GetMethod('ExportPdf', [Reflection.BindingFlags]'Static,Public')
[string]$allPdf = Join-Path $testRoot 'AllData.pdf'
[string]$datePdf = Join-Path $testRoot 'SelectedDate.pdf'
$allArgs = [object[]]::new(4); $allArgs[0] = $allPdf; $allArgs[1] = [bool]$true; $allArgs[2] = [DateTime]::Today; $allArgs[3] = [DateTime]::Today
$dateArgs = [object[]]::new(4); $dateArgs[0] = $datePdf; $dateArgs[1] = [bool]$false; $dateArgs[2] = [DateTime]::Today; $dateArgs[3] = [DateTime]::Today
$exportMethod.Invoke($null, $allArgs)
$exportMethod.Invoke($null, $dateArgs)
$dailyArtifactMethod = $archiveType.GetMethod('ExportDailyArtifacts', [Reflection.BindingFlags]'Static,Public')
[string]$cloudArtifactDir = Join-Path $testRoot 'CloudArtifacts'
$artifactArgs = [object[]]::new(4); $artifactArgs[0] = $cloudArtifactDir; $artifactArgs[1] = [Environment]::MachineName; $artifactArgs[2] = 'A1B2C3D4'; $artifactArgs[3] = [DateTime]::Today
$dailyArtifacts = $dailyArtifactMethod.Invoke($null, $artifactArgs)
$cloudPdf = @($dailyArtifacts | Where-Object { $_ -like '*.pdf' } | Select-Object -First 1)
$cloudCsv = @($dailyArtifacts | Where-Object { $_ -like '*.csv' } | Select-Object -First 1)
$cloudManagerType = $assembly.GetType('NetCheck.CloudBackupManager', $true)
$storageMethod = $cloudManagerType.GetMethod('RunStorageSelfTest', [Reflection.BindingFlags]'Static,Public')
[string]$cloudStorageTest = Join-Path $testRoot 'Cloud\storage-selftest.dat'
$cloudStorageProtected = $storageMethod.Invoke($null, [object[]]@($cloudStorageTest))
$embeddedCredentialsMethod = $cloudManagerType.GetMethod('EmbeddedCredentials', [Reflection.BindingFlags]'Static,NonPublic')
$embeddedCredentials = $embeddedCredentialsMethod.Invoke($null, @())
$oauthBuiltInClient = $embeddedCredentials.ClientId -match '^[0-9]+-[a-z0-9]+\.apps\.googleusercontent\.com$' -and [string]::IsNullOrWhiteSpace($embeddedCredentials.ClientSecret) -and $embeddedCredentials.TokenUri -eq 'https://oauth2.googleapis.com/token'
$cloudFormType = $assembly.GetType('NetCheck.CloudBackupForm', $true)
$cloudManager = [Activator]::CreateInstance($cloudManagerType, [object[]]@([Environment]::MachineName, 'A1B2C3D4'))
$cloudForm = [Activator]::CreateInstance($cloudFormType, [object[]]@($cloudManager))
$cloudConnectButton = $cloudFormType.GetField('connect', $flags).GetValue($cloudForm)
$oauthLoginOnlyUi = $cloudConnectButton.Text -eq '登入 Google Drive' -and $null -eq $cloudFormType.GetMethod('Open', [Reflection.BindingFlags]'Static,NonPublic')
$languageType = $assembly.GetType('NetCheck.L', $true)
$languageMethod = $languageType.GetMethod('IsTraditionalChineseCulture', [Reflection.BindingFlags]'Static,NonPublic')
$languageRouting = $languageMethod.Invoke($null, @('zh-TW')) -and $languageMethod.Invoke($null, @('zh-HK')) -and $languageMethod.Invoke($null, @('zh-Hant')) -and -not $languageMethod.Invoke($null, @('zh-CN')) -and -not $languageMethod.Invoke($null, @('en-US')) -and -not $languageMethod.Invoke($null, @('ja-JP'))
$languageProbe = Join-Path $root 'tests\LanguageProbe.ps1'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $languageProbe -Executable (Join-Path $testRoot 'NetCheckMonitor.exe') -Language en-US
$englishUi = $LASTEXITCODE -eq 0
$allPdfHeader = if (Test-Path $allPdf) { [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($allPdf), 0, 5) } else { '' }
$datePdfHeader = if (Test-Path $datePdf) { [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($datePdf), 0, 5) } else { '' }
$clearMethod = $archiveType.GetMethod('ClearAllData', [Reflection.BindingFlags]'Static,Public')
$clearArgs = [object[]]@(0)
$clearFailures = $clearMethod.Invoke($null, $clearArgs)
$managedFilesLeft = @(Get-ChildItem -LiteralPath (Join-Path $testRoot 'NetCheck_Data') -File -ErrorAction SilentlyContinue) + @(Get-ChildItem -LiteralPath $env:NETCHECK_BACKUP_DIR -File -ErrorAction SilentlyContinue)

$result = [PSCustomObject]@{
    CsvCreated       = [bool]$csv
    HtmlCreated      = [bool]$html
    LiveHtmlCreated  = [bool]$liveHtml
    BackupCsvCreated = [bool]$backupCsv
    CloseWasBlocked  = $closingArgs.Cancel
    CheckRows        = @($rows | Where-Object Type -eq 'CHECK').Count
    PauseMarker      = [bool]($rows | Where-Object Status -eq 'PAUSED')
    ResumeMarker     = [bool]($rows | Where-Object Status -eq 'RESUMED')
    ReportHasTimeline = $htmlText.Contains('每日連線時間軸')
    ReportHasChineseProductName = $reportHasChineseProductName
    ReportHasOutages = $htmlText.Contains('斷線事件')
    ReportMarksPause = $htmlText.Contains('#9aa0a6')
    ReportHasDailyStats = $htmlText.Contains('每日斷線百分比')
    ReportHasComputer = $htmlText.Contains([Environment]::MachineName)
    ComputerMarker = [bool]($rows | Where-Object Status -eq 'COMPUTER')
    UniqueFileName = $csv.BaseName -match '^NetCheck_.+-[0-9A-F]{8}_\d{8}_\d{6}$'
    DailyOutageCalculated = $dailyPercent -gt 0
    AllPdfCreated = $allPdfHeader -eq '%PDF-'
    DatePdfCreated = $datePdfHeader -eq '%PDF-'
    ClearAllPassed = ([int]$clearArgs[0] -gt 0) -and ($clearFailures.Count -eq 0) -and ($managedFilesLeft.Count -eq 0)
    DownloadButtonLabel = $downloadButtonLabel
    ClearButtonLayout = $clearButtonLayout
    ExitButtonLayout = $exitButtonLayout
    ExitSaveCompleted = [bool]$exitSaveCompleted
    ClearRemovedFromPdfDialog = $clearRemovedFromPdfDialog
    CloudButtonLayout = $cloudButtonLayout
    AboutButtonLayout = $aboutButtonLayout
    AboutPageContent = $aboutPageContent
    ProgramIdentity = $programIdentity
    CustomIconEmbedded = $customIconEmbedded
    CloudDailyPdf = ($cloudPdf.Count -eq 1) -and (Test-Path $cloudPdf[0]) -and ([Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($cloudPdf[0]), 0, 5) -eq '%PDF-')
    CloudDailyCsv = ($cloudCsv.Count -eq 1) -and (Test-Path $cloudCsv[0]) -and ((Get-Content -LiteralPath $cloudCsv[0] -TotalCount 1) -eq 'Timestamp,Type,Status,LatencyMs,Target,Detail') -and ((Split-Path $cloudCsv[0] -Leaf) -match '^NetCheck_.+-A1B2C3D4_\d{8}_Raw\.csv$')
    CloudStorageProtected = [bool]$cloudStorageProtected
    OAuthBuiltInClient = $oauthBuiltInClient
    OAuthLoginOnlyUi = $oauthLoginOnlyUi
    LanguageRouting = [bool]$languageRouting
    EnglishUi = [bool]$englishUi
}

$cloudForm.Dispose()
$cloudManager.Dispose()
$aboutForm.Dispose()
$form.Dispose()
$result | Format-List

if (-not $result.CsvCreated -or -not $result.HtmlCreated -or -not $result.LiveHtmlCreated -or -not $result.BackupCsvCreated -or
    -not $result.CloseWasBlocked -or $result.CheckRows -lt 1 -or
    -not $result.PauseMarker -or -not $result.ResumeMarker -or
    -not $result.ReportHasTimeline -or -not $result.ReportHasChineseProductName -or -not $result.ReportHasOutages -or -not $result.ReportMarksPause -or
    -not $result.ReportHasDailyStats -or -not $result.ReportHasComputer -or -not $result.ComputerMarker -or -not $result.UniqueFileName -or
    -not $result.DailyOutageCalculated -or -not $result.AllPdfCreated -or -not $result.DatePdfCreated -or -not $result.ClearAllPassed -or
    -not $result.DownloadButtonLabel -or -not $result.ClearButtonLayout -or -not $result.ExitButtonLayout -or -not $result.ExitSaveCompleted -or -not $result.ClearRemovedFromPdfDialog -or
    -not $result.CloudButtonLayout -or -not $result.AboutButtonLayout -or -not $result.AboutPageContent -or -not $result.ProgramIdentity -or -not $result.CustomIconEmbedded -or -not $result.CloudDailyPdf -or -not $result.CloudDailyCsv -or -not $result.CloudStorageProtected -or
    -not $result.OAuthBuiltInClient -or -not $result.OAuthLoginOnlyUi -or -not $result.LanguageRouting -or -not $result.EnglishUi) {
    throw 'NetCheck self-test failed.'
}
