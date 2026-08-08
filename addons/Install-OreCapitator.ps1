<#
.SYNOPSIS
    Generates and registers the Orecapitator behavior pack for a Bedrock Dedicated Server.
#>

[CmdletBinding()]
param (
    [Parameter()]
    [string] $ServerPath = "C:\Bedrock\server",

    [Parameter()]
    [string] $PackName = "Orecapitator"
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# 1. Static UUIDs (Generated specifically for Orecapitator)
# ---------------------------------------------------------------------------
$PackUuid   = "b2c3d4e5-2345-6789-01bc-def234567890"
$ModuleUuid = "e5d4c3b2-9876-5432-cb10-98765432fedc"

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
    Write-Log "Starting Orecapitator installation process..."
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
    # PS 5.1 Compat: Join-Path only takes two arguments, so we nest them.
    $bpRoot     = Join-Path (Join-Path $ServerPath "behavior_packs") $PackName
    $scriptsDir = Join-Path $bpRoot "scripts"
    
    if (-not (Test-Path -LiteralPath $bpRoot -PathType Container)) {
        New-Item -Path $bpRoot -ItemType Directory -Force | Out-Null
        Write-Log "Created behavior pack directory: $bpRoot"
    }
    
    if (-not (Test-Path -LiteralPath $scriptsDir -PathType Container)) {
        New-Item -Path $scriptsDir -ItemType Directory -Force | Out-Null
        Write-Log "Created scripts directory: $scriptsDir"
    }
    Write-Log "Directories ready." -Level "OK"

    # ---------------------------------------------------------------------------
    # 4. Write manifest.json
    # ---------------------------------------------------------------------------
    Write-Log "Step 3: Generating manifest.json..."
    $manifestPath = Join-Path $bpRoot "manifest.json"
    
    $manifestContent = @"
{
    "format_version": 2,
    "header": {
        "name": "$PackName BP",
        "description": "Mines whole ore veins instantly when using a pickaxe.",
        "uuid": "$PackUuid",
        "version": [1, 0, 0],
        "min_engine_version": [1, 20, 70]
    },
    "modules": [
        {
            "type": "script",
            "language": "javascript",
            "uuid": "$ModuleUuid",
            "version": [1, 0, 0],
            "entry": "scripts/main.js"
        }
    ],
    "dependencies": [
        {
            "module_name": "@minecraft/server",
            "version": "1.14.0"
        }
    ]
}
"@
    # NOTE: The closing "@ above MUST be at column 0. Do not indent it.
    
    $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllText($manifestPath, $manifestContent, $utf8NoBom)
    Write-Log "manifest.json written to: $manifestPath" -Level "OK"

    # ---------------------------------------------------------------------------
    # 5. Write scripts/main.js
    # ---------------------------------------------------------------------------
    Write-Log "Step 4: Generating scripts/main.js..."
    $mainJsPath = Join-Path $scriptsDir "main.js"
    
    $jsContent = @'
import { world, system } from "@minecraft/server";

// Log to server console so you can verify the script actually loaded
console.warn("[Orecapitator] Script initialized and listening for ore breaks.");

const MAX_BLOCKS = 150;
const PICKAXE_SUFFIXES = ["_pickaxe"];
const ORE_SUFFIX = "_ore";

world.afterEvents.playerBreakBlock.subscribe((event) => {
    const { player, block, brokenBlockPermutation } = event;
    const blockTypeId = brokenBlockPermutation.type.id;

    // Must be an ore or ancient debris
    if (!blockTypeId.endsWith(ORE_SUFFIX) && blockTypeId !== "minecraft:ancient_debris") {
        return;
    }

    // Must be holding a pickaxe
    const heldItem = player.getComponent("equippable")?.getEquipment("Mainhand");
    if (!heldItem || !PICKAXE_SUFFIXES.some(suffix => heldItem.typeId.endsWith(suffix))) {
        return;
    }

    // Get the base ore type so deepslate and normal variants chain together
    const baseOreType = getBaseOreType(blockTypeId);

    console.warn(`[Orecapitator] Triggered by ${player.name} on ${blockTypeId} at X:${block.x} Y:${block.y} Z:${block.z}`);

    system.run(() => {
        mineVein(player, block.dimension, { x: block.x, y: block.y, z: block.z }, baseOreType);
    });
});

function mineVein(player, dimension, startLoc, baseOreType) {
    const queue = getFaceNeighbours(startLoc);
    const visited = new Set();

    visited.add(encodeLocation(startLoc));
    for (const loc of queue) {
        visited.add(encodeLocation(loc));
    }

    let blocksMined = 0;

    function processNext() {
        const BATCH_SIZE = 5;
        let processed = 0;

        while (queue.length > 0 && blocksMined < MAX_BLOCKS && processed < BATCH_SIZE) {
            const loc = queue.shift();
            processed++;

            let targetBlock;
            try {
                targetBlock = dimension.getBlock(loc);
            } catch {
                continue;
            }

            if (!targetBlock) continue;

            const targetBaseType = getBaseOreType(targetBlock.typeId);
            if (targetBaseType !== baseOreType) {
                continue;
            }

            // Break the block and let the engine handle natural drops
            dimension.runCommandAsync(`setblock ${loc.x} ${loc.y} ${loc.z} air destroy`);
            blocksMined++;

            for (const neighbour of getFaceNeighbours(loc)) {
                const key = encodeLocation(neighbour);
                if (!visited.has(key)) {
                    visited.add(key);
                    queue.push(neighbour);
                }
            }
        }

        if (queue.length > 0 && blocksMined < MAX_BLOCKS) {
            system.run(processNext);
        } else {
            console.warn(`[Orecapitator] Finished mining vein. Blocks removed: ${blocksMined}`);
        }
    }

    system.run(processNext);
}

// Helper: Strips "deepslate_" prefix so normal and deepslate ores connect
function getBaseOreType(typeId) {
    if (typeId === "minecraft:ancient_debris") return "ancient_debris";
    return typeId.replace("deepslate_", "");
}

function getFaceNeighbours(loc) {
    return [
        { x: loc.x + 1, y: loc.y,     z: loc.z     },
        { x: loc.x - 1, y: loc.y,     z: loc.z     },
        { x: loc.x,     y: loc.y + 1, z: loc.z     },
        { x: loc.x,     y: loc.y - 1, z: loc.z     },
        { x: loc.x,     y: loc.y,     z: loc.z + 1 },
        { x: loc.x,     y: loc.y,     z: loc.z - 1 },
    ];
}

function encodeLocation(loc) {
    return `${loc.x},${loc.y},${loc.z}`;
}
'@
    # NOTE: The closing '@ above MUST be at column 0. Do not indent it.

    [System.IO.File]::WriteAllText($mainJsPath, $jsContent, $utf8NoBom)
    Write-Log "main.js written to: $mainJsPath" -Level "OK"

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
    # PS 5.1 Compat: Nest Join-Path for multiple path segments
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
        Write-Log "Appending Orecapitator to pack list..."
        $newEntry = [PSCustomObject]@{
            pack_id = $PackUuid
            version = @(1, 0, 0)
        }
        $currentPacks.Add($newEntry)

        # PS5.1 Workaround for single-element arrays: Force JSON array brackets manually if count is 1
        $jsonOutput = $currentPacks | ConvertTo-Json -Depth 10
        if ($currentPacks.Count -eq 1) {
            $jsonOutput = "[$jsonOutput]"
        }

        [System.IO.File]::WriteAllText($worldBpJsonPath, $jsonOutput, $utf8NoBom)
        Write-Log "Pack successfully registered in: $worldBpJsonPath" -Level "OK"
    }

    Write-Log "============================================================="
    Write-Log "Installation Complete!" -Level "OK"
    Write-Log "IMPORTANT: You must restart your Bedrock server completely for the script to load."
    Write-Log "When the server starts, look for '[Orecapitator] Script initialized' in the server console to verify it loaded."
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
