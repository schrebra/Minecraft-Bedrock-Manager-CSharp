<#
.SYNOPSIS
    Removes (undoes) the Treecapitator behavior pack installed by the companion install script.
#>

[CmdletBinding()]
param (
    [Parameter()]
    [string] $ServerPath = "C:\Bedrock\server",

    [Parameter()]
    [string] $PackName = "Treecapitator"
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# 1. Static UUIDs (MUST match the install script)
# ---------------------------------------------------------------------------
$PackUuid   = "a1b2c3d4-1234-5678-90ab-cdef12345678"
$ModuleUuid = "d4c3b2a1-8765-4321-ba09-87654321fedc"

function Write-Log {
    param ([string]$Message, [string]$Level = "INFO")
    $color = switch ($Level) {
        "INFO"  { "Cyan" }
        "OK"    { "Green" }
        "WARN"  { "Yellow" }
        "ERROR" { "Red" }
        default { "White" }
    }
    Write-Host "[$Level] $Message" -ForegroundColor $color
}

try {
    Write-Log "Starting Treecapitator uninstall process..."
    Write-Log "Target Server Path: $ServerPath"
    Write-Log "Pack Name: $PackName"

    # ---------------------------------------------------------------------------
    # 2. Validate server path
    # ---------------------------------------------------------------------------
    Write-Log "Step 1: Validating server path..."
    if (-not (Test-Path -LiteralPath $ServerPath -PathType Container)) {
        throw "ServerPath '$ServerPath' does not exist or is not a directory. Please check the path and try again."
    }
    Write-Log "Server path validated successfully." -Level "OK"

    # ---------------------------------------------------------------------------
    # 3. Resolve World Name (same logic as installer)
    # ---------------------------------------------------------------------------
    Write-Log "Step 2: Resolving active world name from server.properties..."
    $propertiesPath = Join-Path $ServerPath "server.properties"
    $levelName = "Bedrock level"

    if (Test-Path -LiteralPath $propertiesPath) {
        $match = Select-String -LiteralPath $propertiesPath -Pattern '^level-name=(.+)$'
        if ($match) {
            $levelName = $match.Matches[0].Groups[1].Value.Trim()
            Write-Log "Found level-name: '$levelName'"
        } else {
            Write-Log "Could not find 'level-name' in server.properties. Defaulting to '$levelName'." -Level "WARN"
        }
    } else {
        Write-Log "server.properties not found at '$propertiesPath'. Defaulting to '$levelName'." -Level "WARN"
    }

    $utf8NoBom = [System.Text.UTF8Encoding]::new($false)

    # ---------------------------------------------------------------------------
    # 4. Deregister Pack from world_behavior_packs.json
    # ---------------------------------------------------------------------------
    Write-Log "Step 3: Removing Treecapitator from world_behavior_packs.json..."
    $worldsDir = Join-Path $ServerPath "worlds"
    $worldDir = Join-Path $worldsDir $levelName
    $worldBpJsonPath = Join-Path $worldDir "world_behavior_packs.json"

    if (-not (Test-Path -LiteralPath $worldDir -PathType Container)) {
        Write-Log "World directory '$worldDir' not found. Skipping world JSON edit." -Level "WARN"
    } elseif (-not (Test-Path -LiteralPath $worldBpJsonPath)) {
        Write-Log "world_behavior_packs.json does not exist. Nothing to deregister." -Level "WARN"
    } else {
        Write-Log "Reading existing world_behavior_packs.json..."
        $rawJson = Get-Content -LiteralPath $worldBpJsonPath -Raw

        $currentPacks = [System.Collections.Generic.List[object]]::new()
        $malformed = $false

        if (-not [string]::IsNullOrWhiteSpace($rawJson)) {
            try {
                $parsed = ConvertFrom-Json -InputObject $rawJson
                if ($parsed -is [array]) {
                    foreach ($entry in $parsed) { $currentPacks.Add($entry) }
                } elseif ($null -ne $parsed) {
                    $currentPacks.Add($parsed)
                }
            } catch {
                $malformed = $true
                Write-Log "world_behavior_packs.json was malformed; will preserve file as-is but attempt removal may fail." -Level "WARN"
            }
        }

        if (-not $malformed) {
            $beforeCount = $currentPacks.Count
            $toRemove = $currentPacks | Where-Object { $_.pack_id -eq $PackUuid }

            if (-not $toRemove) {
                Write-Log "Treecapitator UUID was not registered in this world. No JSON changes needed." -Level "OK"
            } else {
                # Rebuild the list without the Treecapitator entry/entries
                $filtered = [System.Collections.Generic.List[object]]::new()
                foreach ($entry in $currentPacks) {
                    if ($entry.pack_id -ne $PackUuid) {
                        $filtered.Add($entry)
                    }
                }
                $currentPacks = $filtered

                if ($currentPacks.Count -eq 0) {
                    # Leave an empty array so the file is still valid JSON
                    $jsonOutput = "[]"
                } else {
                    $jsonOutput = $currentPacks | ConvertTo-Json -Depth 10
                    # PS5.1 Workaround for single-element arrays: Force JSON array brackets manually if count is 1
                    if ($currentPacks.Count -eq 1) {
                        $jsonOutput = "[$jsonOutput]"
                    }
                }

                [System.IO.File]::WriteAllText($worldBpJsonPath, $jsonOutput, $utf8NoBom)
                Write-Log "Removed $($beforeCount - $currentPacks.Count) entry/entries. Remaining packs: $($currentPacks.Count)" -Level "OK"
            }
        }
    }

    # ---------------------------------------------------------------------------
    # 5. Remove behavior pack directory
    # ---------------------------------------------------------------------------
    Write-Log "Step 4: Removing Treecapitator behavior pack directory..."
    $bpRoot = Join-Path (Join-Path $ServerPath "behavior_packs") $PackName

    if (Test-Path -LiteralPath $bpRoot -PathType Container) {
        try {
            Remove-Item -LiteralPath $bpRoot -Recurse -Force
            Write-Log "Removed behavior pack directory: $bpRoot" -Level "OK"
        } catch {
            Write-Log "Failed to fully remove directory '$bpRoot': $($_.Exception.Message)" -Level "WARN"
            Write-Log "You may need to stop the server and delete the folder manually." -Level "WARN"
        }
    } else {
        Write-Log "Behavior pack directory '$bpRoot' does not exist. Nothing to remove." -Level "WARN"
    }

    # ---------------------------------------------------------------------------
    # 6. Sanity sweep: also remove any other folders that share the same UUID
    #       in their manifest.json, in case the folder was renamed.
    # ---------------------------------------------------------------------------
    Write-Log "Step 5: Sanity sweep for stray packs with matching UUID..."
    $bpParent = Join-Path $ServerPath "behavior_packs"
    if (Test-Path -LiteralPath $bpParent -PathType Container) {
        Get-ChildItem -LiteralPath $bpParent -Directory | ForEach-Object {
            $manifest = Join-Path $_.FullName "manifest.json"
            if (Test-Path -LiteralPath $manifest -PathType Leaf) {
                try {
                    $m = Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json
                    if ($m.header.uuid -eq $PackUuid) {
                        Write-Log "Found stray pack with matching UUID at: $($_.FullName)" -Level "WARN"
                        try {
                            Remove-Item -LiteralPath $_.FullName -Recurse -Force
                            Write-Log "Removed stray pack: $($_.FullName)" -Level "OK"
                        } catch {
                            Write-Log "Could not remove stray pack: $($_.Exception.Message)" -Level "WARN"
                        }
                    }
                } catch {
                    # Ignore unreadable manifests
                }
            }
        }
    }

    Write-Log "============================================================="
    Write-Log "Uninstall Complete!" -Level "OK"
    Write-Log "IMPORTANT: Restart your Bedrock server completely for the script to unload."
    Write-Log "Tree felling will be disabled after the restart."
    Write-Log "============================================================="

} catch {
    Write-Log "A fatal error occurred during uninstall:" -Level "ERROR"
    Write-Log $_.Exception.Message -Level "ERROR"
    Write-Log "Stack Trace: $($_.ScriptStackTrace)" -Level "ERROR"
} finally {
    Write-Host ""
    Write-Host "Press Enter to exit..." -ForegroundColor Cyan
    Read-Host
}
