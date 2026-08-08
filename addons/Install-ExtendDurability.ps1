<#
.SYNOPSIS
    Generates and registers a behavior pack that quadruples the durability of all vanilla tools, weapons, and armor.
#>

[CmdletBinding()]
param (
    [Parameter()]
    [string] $ServerPath = "C:\Bedrock\server",

    [Parameter()]
    [string] $PackName = "DurabilityTweaks"
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# 1. Static UUIDs (Generated specifically for DurabilityTweaks)
# ---------------------------------------------------------------------------
$PackUuid   = "d4e5f6a7-4567-8901-23de-f45678901234"
$ModuleUuid = "a7f6e5d4-1098-7654-ed32-10987654fedc"

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
    Write-Log "Starting DurabilityTweaks installation process..."
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
    # 3. Resolve and create directories
    # ---------------------------------------------------------------------------
    Write-Log "Step 2: Creating directory structure..."
    $bpRoot     = Join-Path (Join-Path $ServerPath "behavior_packs") $PackName
    $itemsDir   = Join-Path $bpRoot "items"
    
    if (-not (Test-Path -LiteralPath $bpRoot -PathType Container)) {
        New-Item -Path $bpRoot -ItemType Directory -Force | Out-Null
        Write-Log "Created behavior pack directory: $bpRoot"
    }
    
    if (-not (Test-Path -LiteralPath $itemsDir -PathType Container)) {
        New-Item -Path $itemsDir -ItemType Directory -Force | Out-Null
        Write-Log "Created items directory: $itemsDir"
    }
    Write-Log "Directories ready." -Level "OK"

    # ---------------------------------------------------------------------------
    # 4. Write manifest.json (Data pack, no scripts)
    # ---------------------------------------------------------------------------
    Write-Log "Step 3: Generating manifest.json..."
    $manifestPath = Join-Path $bpRoot "manifest.json"
    
    $manifestContent = @"
{
    "format_version": 2,
    "header": {
        "name": "$PackName BP",
        "description": "Quadruples durability of all tools, weapons, and armor.",
        "uuid": "$PackUuid",
        "version": [1, 0, 0],
        "min_engine_version": [1, 20, 70]
    },
    "modules": [
        {
            "type": "data",
            "uuid": "$ModuleUuid",
            "version": [1, 0, 0]
        }
    ]
}
"@
    # NOTE: The closing "@ above MUST be at column 0. Do not indent it.
    
    $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllText($manifestPath, $manifestContent, $utf8NoBom)
    Write-Log "manifest.json written to: $manifestPath" -Level "OK"

    # ---------------------------------------------------------------------------
    # 5. Generate Item JSON files (4x Durability using 1.20.10+ format)
    # ---------------------------------------------------------------------------
    Write-Log "Step 4: Generating item durability overrides..."
    
    # Base vanilla Bedrock durability values multiplied by 4
    $itemDurabilities = @{
        # Swords
        "wooden_sword" = 236; "stone_sword" = 524; "iron_sword" = 1000; "golden_sword" = 128; "diamond_sword" = 6244; "netherite_sword" = 8124
        # Pickaxes
        "wooden_pickaxe" = 236; "stone_pickaxe" = 524; "iron_pickaxe" = 1000; "golden_pickaxe" = 128; "diamond_pickaxe" = 6244; "netherite_pickaxe" = 8124
        # Axes
        "wooden_axe" = 236; "stone_axe" = 524; "iron_axe" = 1000; "golden_axe" = 128; "diamond_axe" = 6244; "netherite_axe" = 8124
        # Shovels
        "wooden_shovel" = 236; "stone_shovel" = 524; "iron_shovel" = 1000; "golden_shovel" = 128; "diamond_shovel" = 6244; "netherite_shovel" = 8124
        # Hoes
        "wooden_hoe" = 236; "stone_hoe" = 524; "iron_hoe" = 1000; "golden_hoe" = 128; "diamond_hoe" = 6244; "netherite_hoe" = 8124
        # Helmets
        "leather_helmet" = 220; "golden_helmet" = 308; "chainmail_helmet" = 660; "iron_helmet" = 660; "diamond_helmet" = 1452; "netherite_helmet" = 1628; "turtle_helmet" = 1100
        # Chestplates
        "leather_chestplate" = 320; "golden_chestplate" = 448; "chainmail_chestplate" = 960; "iron_chestplate" = 960; "diamond_chestplate" = 2112; "netherite_chestplate" = 2368; "elytra" = 1728
        # Leggings
        "leather_leggings" = 300; "golden_leggings" = 420; "chainmail_leggings" = 900; "iron_leggings" = 900; "diamond_leggings" = 1980; "netherite_leggings" = 2220
        # Boots
        "leather_boots" = 260; "golden_boots" = 364; "chainmail_boots" = 780; "iron_boots" = 780; "diamond_boots" = 1716; "netherite_boots" = 1924
        # Other Tools
        "bow" = 1536; "crossbow" = 1860; "trident" = 1000; "shield" = 1344; "fishing_rod" = 256; "carrot_on_a_stick" = 100; "warped_fungus_on_a_stick" = 400; "shears" = 952; "flint_and_steel" = 256; "brush" = 256
    }

    $itemCount = 0
    foreach ($item in $itemDurabilities.Keys) {
        $durability = $itemDurabilities[$item]
        $itemJson = @"
{
    "format_version": "1.20.10",
    "minecraft:item": {
        "description": {
            "identifier": "minecraft:$item"
        },
        "components": {
            "minecraft:max_damage": $durability
        }
    }
}
"@
        # NOTE: The closing "@ above MUST be at column 0. Do not indent it.
        
        $itemPath = Join-Path $itemsDir "$item.json"
        [System.IO.File]::WriteAllText($itemPath, $itemJson, $utf8NoBom)
        $itemCount++
    }
    Write-Log "Generated $itemCount item durability override files." -Level "OK"

    # ---------------------------------------------------------------------------
    # 6. Resolve World Name
    # ---------------------------------------------------------------------------
    Write-Log "Step 5: Resolving active world name from server.properties..."
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

    # ---------------------------------------------------------------------------
    # 7. Register Pack in World
    # ---------------------------------------------------------------------------
    Write-Log "Step 6: Registering pack in world_behavior_packs.json..."
    $worldsDir = Join-Path $ServerPath "worlds"
    $worldDir = Join-Path $worldsDir $levelName
    $worldBpJsonPath = Join-Path $worldDir "world_behavior_packs.json"

    if (-not (Test-Path -LiteralPath $worldDir -PathType Container)) {
        Write-Log "World directory '$worldDir' not found! Check your level-name." -Level "WARN"
        Write-Log "Creating missing directory to avoid crash, but registration may fail..." -Level "WARN"
        New-Item -Path $worldDir -ItemType Directory -Force | Out-Null
    }

    $currentPacks = [System.Collections.Generic.List[object]]::new()

    if (Test-Path -LiteralPath $worldBpJsonPath) {
        Write-Log "Reading existing world_behavior_packs.json..."
        $rawJson = Get-Content -LiteralPath $worldBpJsonPath -Raw

        if (-not [string]::IsNullOrWhiteSpace($rawJson)) {
            try {
                $parsed = ConvertFrom-Json -InputObject $rawJson
                if ($parsed -is [array]) {
                    foreach ($entry in $parsed) { $currentPacks.Add($entry) }
                } elseif ($null -ne $parsed) {
                    $currentPacks.Add($parsed)
                }
                Write-Log "Found $($currentPacks.Count) existing pack(s)."
            } catch {
                Write-Log "world_behavior_packs.json was malformed. It will be repaired." -Level "WARN"
            }
        }
    } else {
        Write-Log "world_behavior_packs.json does not exist yet. Creating a new one."
    }

    $alreadyRegistered = $currentPacks | Where-Object { $_.pack_id -eq $PackUuid }

    if ($alreadyRegistered) {
        Write-Log "Pack UUID already exists in world JSON. No registration changes needed." -Level "OK"
    } else {
        Write-Log "Appending DurabilityTweaks to pack list..."
        $newEntry = [PSCustomObject]@{
            pack_id = $PackUuid
            version = @(1, 0, 0)
        }
        $currentPacks.Add($newEntry)

        $jsonOutput = $currentPacks | ConvertTo-Json -Depth 10
        if ($currentPacks.Count -eq 1) {
            $jsonOutput = "[$jsonOutput]"
        }

        [System.IO.File]::WriteAllText($worldBpJsonPath, $jsonOutput, $utf8NoBom)
        Write-Log "Pack successfully registered in: $worldBpJsonPath" -Level "OK"
    }

    Write-Log "============================================================="
    Write-Log "Installation Complete!" -Level "OK"
    Write-Log "IMPORTANT: Restart your Bedrock server for the items to update."
    Write-Log "Note: Items that already exist in your inventory from BEFORE installing this will keep their old durability. Craft or spawn new ones to get the 4x durability."
    Write-Log "============================================================="

} catch {
    Write-Log "A fatal error occurred during installation:" -Level "ERROR"
    Write-Log $_.Exception.Message -Level "ERROR"
    Write-Log "Stack Trace: $($_.ScriptStackTrace)" -Level "ERROR"
} finally {
    # Pause at the end so the window doesn't close immediately
    Write-Host ""
    Write-Host "Press Enter to exit..." -ForegroundColor Cyan
    Read-Host
}
