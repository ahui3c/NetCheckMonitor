param([Parameter(Mandatory = $true)][string]$Executable)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$testRoot = Join-Path $root ('.cumulative-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
$env:NETCHECK_DATA_ROOTS = $testRoot
$env:NETCHECK_UI_LANGUAGE = 'zh-TW'

function Write-Session([string]$Path, [string]$Computer, [string]$Id, [DateTime]$Start, [bool]$WithChecks) {
    $lines = New-Object Collections.Generic.List[string]
    $lines.Add('Timestamp,Type,Status,LatencyMs,Target,Detail')
    $lines.Add(('"' + $Start.ToString('o') + '",MARKER,COMPUTER,,,"' + $Computer + ' [' + $Id + ']"'))
    $lines.Add(('"' + $Start.ToString('o') + '",MARKER,STARTED,,,"Started"'))
    if ($WithChecks) {
        $lines.Add(('"' + $Start.AddMinutes(1).ToString('o') + '",CHECK,SUSPECTED,,"https://example.com/","First failure"'))
        $lines.Add(('"' + $Start.AddSeconds(90).ToString('o') + '",MARKER,EVENT_NOTE,,,"Manual note for ' + $Computer + '"'))
        $lines.Add(('"' + $Start.AddMinutes(2).ToString('o') + '",CHECK,OFFLINE,,"https://example.com/","Confirmed failure"'))
        $lines.Add(('"' + $Start.AddMinutes(3).ToString('o') + '",CHECK,ONLINE,12,"https://example.com/","Recovered"'))
        $lines.Add(('"' + $Start.AddMinutes(4).ToString('o') + '",MARKER,STOPPED,,,"Stopped"'))
    }
    else {
        $lines.Add(('"' + $Start.AddDays(20).ToString('o') + '",MARKER,STOPPED,,,"Stopped without checks"'))
    }
    [IO.File]::WriteAllLines($Path, $lines, (New-Object Text.UTF8Encoding($true)))
}

try {
    $assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path $Executable))
    $archiveType = $assembly.GetType('NetCheck.ArchiveReport', $true)
    $writeReport = $archiveType.GetMethod('WriteCumulativeHtml', [Reflection.BindingFlags]'Static,Public')
    $ensureReport = $archiveType.GetMethod('EnsureCumulativeHtml', [Reflection.BindingFlags]'Static,Public')
    $clearData = $archiveType.GetMethod('ClearAllData', [Reflection.BindingFlags]'Static,Public')

    $first = Join-Path $testRoot 'NetCheck_FIRST-A1B2C3D4_20260701_100000.csv'
    Write-Session $first 'FIRST-PC' 'A1B2C3D4' ([DateTime]'2026-07-01T10:00:00') $true
    $ensureArgs = [object[]]::new(2); $ensureArgs[0] = [string]'CURRENT-PC'; $ensureArgs[1] = [string]'CAFEBABE'
    $report = [string]$ensureReport.Invoke($null, $ensureArgs)
    $writeArgs = [object[]]::new(2); $writeArgs[0] = [string]$report; $writeArgs[1] = [bool]$false
    $firstHtml = [IO.File]::ReadAllText($report)
    $firstGeneration = $firstHtml.Contains('FIRST-PC') -and $firstHtml.Contains('來源檔案：1')

    $second = Join-Path $testRoot 'NetCheck_SECOND-E5F6A7B8_20260703_110000.csv'
    Write-Session $second 'SECOND-PC' 'E5F6A7B8' ([DateTime]'2026-07-03T11:00:00') $true
    $empty = Join-Path $testRoot 'NetCheck_NO-CHECK-11223344_20260601_000000.csv'
    Write-Session $empty 'NO-CHECK' '11223344' ([DateTime]'2026-06-01T00:00:00') $false
    $report = [string]$ensureReport.Invoke($null, $ensureArgs)
    $writeArgs[0] = [string]$report
    $combinedHtml = [IO.File]::ReadAllText($report)
    $effective = [regex]::Match($combinedHtml, '<span>有效監控</span><b[^>]*>([^<]+)</b>')
    $historyCombined = $combinedHtml.Contains('FIRST-PC') -and $combinedHtml.Contains('SECOND-PC') -and $combinedHtml.Contains('來源檔案：2')
    $gapsExcluded = $effective.Success -and -not $effective.Groups[1].Value.Contains('天') -and -not $combinedHtml.Contains('NO-CHECK')
    $readableScreenText = $combinedHtml.Contains('body{font-size:16px') -and $combinedHtml.Contains('table{font-size:16px') -and $combinedHtml.Contains('.metric b{font-size:24px')
    $newestTimelineFirst = $combinedHtml.IndexOf('>2026/07/03</td>', [StringComparison]::Ordinal) -ge 0 -and $combinedHtml.IndexOf('>2026/07/03</td>', [StringComparison]::Ordinal) -lt $combinedHtml.IndexOf('>2026/07/01</td>', [StringComparison]::Ordinal)
    $timelineRunsRightToLeft = $combinedHtml.Contains("<div class='timeline-axis'><span>24:00</span><span>18:00</span><span>12:00</span><span>06:00</span><span>00:00</span></div>")
    $twoLineTimelineLayout = $combinedHtml.Contains("<tr class='daily-text-row'>") -and $combinedHtml.Contains("<tr class='timeline-row'><td colspan='7'><div class='timeline-indent'><div class='timeline-chart'>") -and $combinedHtml.Contains('.timeline-chart svg{display:block;width:100%') -and $combinedHtml.Contains('.timeline-indent{margin-left:42px')
    $combinedEventTable = $combinedHtml.Contains("<div class='card'><h2>斷線事件與事件註記</h2>") -and $combinedHtml.Contains("class='event-badge event-outage'") -and $combinedHtml.Contains("class='event-badge event-note'") -and $combinedHtml.Contains('Manual note for SECOND-PC') -and -not $combinedHtml.Contains('<h2>事件註記</h2>')
    $timelineNoteAndHover = $combinedHtml.Contains("fill='#8e44ad'") -and $combinedHtml.Contains("class='timeline-hit'") -and $combinedHtml.Contains('事件註記｜Manual note for SECOND-PC') -and $combinedHtml.Contains('確認斷線｜Confirmed failure')
    $outagesBeforeDiagnostics = $combinedHtml.IndexOf("<div class='card'><h2>斷線事件與事件註記</h2>", [StringComparison]::Ordinal) -lt $combinedHtml.IndexOf("<div class='card'><h2>進階分層連線診斷</h2>", [StringComparison]::Ordinal)
    $dateColorGrouping = $combinedHtml.Contains("<tr class='date-shade-0'>") -and $combinedHtml.Contains("<tr class='date-shade-1'>") -and $combinedHtml.Contains('.date-shade-0 td{background:#f5f9ff}')
    $detailHeading = "<div class='card detail-report'><h2>每日完整測試記錄</h2>"
    $detailReportLast = $combinedHtml.Contains($detailHeading) -and $combinedHtml.IndexOf($detailHeading, [StringComparison]::Ordinal) -gt $combinedHtml.IndexOf("<div class='card'><h2>進階分層連線診斷</h2>", [StringComparison]::Ordinal)
    $detailReportComplete = $combinedHtml.Contains('完整測試內容') -and $combinedHtml.Contains('https://example.com/') -and $combinedHtml.Contains('疑似斷線／快速複查') -and $combinedHtml.Contains('Confirmed failure') -and $combinedHtml.Contains('Recovered')
    $detailNewestFirst = $combinedHtml.IndexOf("<section class='detail-day'><h3>2026/07/03", [StringComparison]::Ordinal) -lt $combinedHtml.IndexOf("<section class='detail-day'><h3>2026/07/01", [StringComparison]::Ordinal) -and $combinedHtml.IndexOf('>11:03:00</td>', [StringComparison]::Ordinal) -lt $combinedHtml.IndexOf('>11:01:00</td>', [StringComparison]::Ordinal)

    $clearArgs = [object[]]@(0)
    $failures = $clearData.Invoke($null, $clearArgs)
    $cleared = $failures.Count -eq 0 -and [int]$clearArgs[0] -ge 4 -and @(Get-ChildItem -LiteralPath $testRoot -File).Count -eq 0
    $writeReport.Invoke($null, $writeArgs) | Out-Null
    $emptyAfterClear = [IO.File]::ReadAllText($report).Contains('目前沒有有效的監控檢查資料')

    if (-not ($firstGeneration -and $historyCombined -and $gapsExcluded -and $readableScreenText -and $newestTimelineFirst -and $timelineRunsRightToLeft -and $twoLineTimelineLayout -and $combinedEventTable -and $timelineNoteAndHover -and $outagesBeforeDiagnostics -and $dateColorGrouping -and $detailReportLast -and $detailReportComplete -and $detailNewestFirst -and $cleared -and $emptyAfterClear)) { throw 'Cumulative report probe failed.' }
    Write-Output 'Cumulative history, daily detailed records, gap exclusion, empty-session exclusion, and clear-data reset passed.'
}
finally {
    $resolvedRoot = [IO.Path]::GetFullPath($testRoot)
    if ([IO.Path]::GetDirectoryName($resolvedRoot) -ne [IO.Path]::GetFullPath($root)) { throw 'Unsafe cumulative test path.' }
    if (Test-Path -LiteralPath $testRoot) { Remove-Item -LiteralPath $testRoot -Recurse -Force }
}
