# Generates a fixture Steam library for testing quarantine/restore flows.
# Pure ASCII on purpose: PowerShell 5.1 misreads BOM-less UTF-8 scripts.
$ErrorActionPreference = 'Stop'
$root = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Definition) 'test-fixture'
if (Test-Path $root) { Remove-Item $root -Recurse -Force }

$ws   = Join-Path $root 'steamapps\workshop'
$cont = Join-Path $ws   'content\431960'
$dl   = Join-Path $ws   'downloads\431960'
New-Item -ItemType Directory -Force -Path $cont, $dl | Out-Null

function Make-Item {
    param([string]$Id, [string]$Title, [string]$Type, [int]$Size)
    $d = Join-Path $cont $Id
    New-Item -ItemType Directory -Force -Path $d | Out-Null
    $pj = @{
        title       = $Title
        type        = $Type
        file        = 'scene.pkg'
        preview     = 'preview.jpg'
        workshopid  = $Id
        version     = 1
        description = "fixture item $Id"
    } | ConvertTo-Json -Depth 4
    [System.IO.File]::WriteAllText((Join-Path $d 'project.json'), $pj, (New-Object System.Text.UTF8Encoding $false))

    $fs = [System.IO.File]::Open((Join-Path $d 'scene.pkg'), [System.IO.FileMode]::Create)
    $fs.SetLength([int64]$Size)
    $fs.Close()

    $png = [Convert]::FromBase64String('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')
    [System.IO.File]::WriteAllBytes((Join-Path $d 'preview.jpg'), $png)
}

Make-Item -Id '1111111111' -Title '演示·订阅中的壁纸'      -Type 'web'   -Size 20480
Make-Item -Id '2222222222' -Title '演示·已退订的残留' -Type 'scene' -Size 65536
Make-Item -Id '3333333333' -Title '演示·无索引空目录'     -Type 'scene' -Size 32768
Make-Item -Id '4444444444' -Title '演示·订阅标记丢失' -Type 'video' -Size 40960
Make-Item -Id '5555555555' -Title '演示·白名单保护项'         -Type 'scene' -Size 8192

$junk = Join-Path $cont 'notanid'
New-Item -ItemType Directory -Force -Path $junk | Out-Null
[System.IO.File]::WriteAllText((Join-Path $junk 'readme.txt'), 'should be ignored')

$d1 = Join-Path $dl '9999999999'
New-Item -ItemType Directory -Force -Path $d1 | Out-Null
$fs2 = [System.IO.File]::Open((Join-Path $d1 'part.chunk'), [System.IO.FileMode]::Create)
$fs2.SetLength([int64]4096)
$fs2.Close()

$acf = @'
"AppWorkshop"
{
	"appid"		"431960"
	"SizeOnDisk"		"155648"
	"NeedsUpdate"		"0"
	"NeedsDownload"		"0"
	"TimeLastUpdated"		"1762300000"
	"TimeLastAppRan"		"1762300000"
	"WorkshopItemsInstalled"
	{
		"1111111111"
		{
			"size"		"20480"
			"timeupdated"		"1762000000"
			"manifest"		"1000000000000000001"
		}
		"2222222222"
		{
			"size"		"65536"
			"timeupdated"		"1762000100"
			"manifest"		"1000000000000000002"
		}
		"4444444444"
		{
			"size"		"40960"
			"timeupdated"		"1762000200"
			"manifest"		"1000000000000000004"
		}
	}
	"WorkshopItemDetails"
	{
		"1111111111"
		{
			"manifest"		"1000000000000000001"
			"timeupdated"		"1762000000"
			"timetouched"		"1762300000"
			"subscribedby"		"358117891"
			"latest_manifest"		"1000000000000000001"
		}
		"4444444444"
		{
			"manifest"		"1000000000000000004"
			"timeupdated"		"1762000200"
			"timetouched"		"0"
			"latest_manifest"		"1000000000000000004"
		}
	}
}
'@
[System.IO.File]::WriteAllText((Join-Path $ws 'appworkshop_431960.acf'), $acf, (New-Object System.Text.UTF8Encoding $false))

Write-Host "fixture ready: $root" -ForegroundColor Green
Get-ChildItem $root -Recurse -File | ForEach-Object { "  " + $_.FullName.Substring($root.Length + 1) }
