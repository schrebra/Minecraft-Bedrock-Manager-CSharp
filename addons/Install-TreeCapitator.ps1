<#
.SYNOPSIS
    Generates and registers the Treecapitator behavior pack for a Bedrock Dedicated Server.
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
# 1. Static UUIDs
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
    Write-Log "Starting Treecapitator installation process..."
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
        "description": "Chops down whole trees when sneaking and breaking a log with an axe.",
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

console.warn("[Treecapitator] Script initialized and listening for block breaks.");

// --- Tunables -------------------------------------------------------------
const MAX_LOGS         = 400;   // hard cap on logs felled per tree
const MAX_LEAVES       = 1200;  // hard cap on leaves cleared per tree
const MAX_RADIUS       = 24;    // max distance from the broken block
const AXE_SUFFIXES     = ["_axe"];
const LOG_SUFFIX       = "_log";
const LEAVES_SUFFIX    = "_leaves";
const BATCH_SIZE       = 8;     // blocks destroyed per tick
const LEAF_RADIUS      = 3;     // max distance a leaf can be from a log
                                // to be considered part of the SAME tree
// --------------------------------------------------------------------------

world.afterEvents.playerBreakBlock.subscribe((event) => {
    const { player, block, brokenBlockPermutation } = event;
    const blockTypeId = brokenBlockPermutation.type.id;

    if (!blockTypeId.endsWith(LOG_SUFFIX)) return;

    const equippable = player.getComponent("equippable");
    const heldItem = equippable?.getEquipment("Mainhand");
    if (!heldItem || !AXE_SUFFIXES.some(s => heldItem.typeId.endsWith(s))) return;

    // Must be holding shift (sneaking) to trigger treecapitator
    if (!player.isSneaking) return;

    console.warn(`[Treecapitator] Triggered by ${player.name} on ${blockTypeId} at X:${block.x} Y:${block.y} Z:${block.z}`);

    const dim = block.dimension;
    const start = { x: block.x, y: block.y, z: block.z };

    system.run(() => chopTree(dim, start, blockTypeId));
});

function chopTree(dimension, start, logTypeId) {
    const startKey = encodeLocation(start);

    // ---- 1. Find all connected logs -------------------------------------
    const logVisited = new Set([startKey]);
    const logQueue   = [];
    const logsToBreak = [];

    for (const n of getNeighbours26(start)) {
        const k = encodeLocation(n);
        if (!logVisited.has(k)) {
            logVisited.add(k);
            logQueue.push(n);
        }
    }

    while (logQueue.length > 0 && logsToBreak.length < MAX_LOGS) {
        const loc = logQueue.shift();

        if (!withinRadius(loc, start)) continue;

        let blk;
        try { blk = dimension.getBlock(loc); } catch { continue; }
        if (!blk) continue;

        if (blk.typeId === logTypeId) {
            logsToBreak.push(loc);
            for (const n of getNeighbours26(loc)) {
                const k = encodeLocation(n);
                if (!logVisited.has(k)) {
                    logVisited.add(k);
                    logQueue.push(n);
                }
            }
        }
    }

    // ---- 2. Find leaves of THIS tree only -------------------------------
    // We avoid flood-filling through leaves because overlapping canopies
    // would cause neighboring trees to be destroyed. Instead, we only 
    // collect leaves that are physically close to the logs of THIS tree.
    const leavesToBreak = [];
    const leafVisited = new Set();

    let minX = Infinity, minY = Infinity, minZ = Infinity;
    let maxX = -Infinity, maxY = -Infinity, maxZ = -Infinity;

    for (const log of logsToBreak) {
        minX = Math.min(minX, log.x); maxX = Math.max(maxX, log.x);
        minY = Math.min(minY, log.y); maxY = Math.max(maxY, log.y);
        minZ = Math.min(minZ, log.z); maxZ = Math.max(maxZ, log.z);
    }

    let leafLimitReached = false;
    for (let x = minX - LEAF_RADIUS; x <= maxX + LEAF_RADIUS && !leafLimitReached; x++) {
        for (let y = minY - LEAF_RADIUS; y <= maxY + LEAF_RADIUS && !leafLimitReached; y++) {
            for (let z = minZ - LEAF_RADIUS; z <= maxZ + LEAF_RADIUS && !leafLimitReached; z++) {
                const loc = { x, y, z };
                if (!withinRadius(loc, start)) continue;

                let blk;
                try { blk = dimension.getBlock(loc); } catch { continue; }
                
                if (blk && blk.typeId.endsWith(LEAVES_SUFFIX)) {
                    // Check if this leaf is within LEAF_RADIUS of any log
                    let nearLog = false;
                    for (const log of logsToBreak) {
                        const dx = Math.abs(x - log.x);
                        const dy = Math.abs(y - log.y);
                        const dz = Math.abs(z - log.z);
                        if (dx <= LEAF_RADIUS && dy <= LEAF_RADIUS && dz <= LEAF_RADIUS) {
                            nearLog = true;
                            break;
                        }
                    }
                    
                    if (nearLog) {
                        const key = encodeLocation(loc);
                        if (!leafVisited.has(key)) {
                            leafVisited.add(key);
                            leavesToBreak.push(loc);
                            if (leavesToBreak.length >= MAX_LEAVES) {
                                leafLimitReached = true;
                            }
                        }
                    }
                }
            }
        }
    }

    console.warn(`[Treecapitator] Located ${logsToBreak.length} logs and ${leavesToBreak.length} leaves.`);

    // ---- 3. Destroy in batches to spread load across ticks --------------
    let logIdx = 0;
    let leafIdx = 0;

    function processBatch() {
        let processed = 0;

        while (logIdx < logsToBreak.length && processed < BATCH_SIZE) {
            const loc = logsToBreak[logIdx++];
            processed++;
            try {
                dimension.runCommandAsync(`setblock ${loc.x} ${loc.y} ${loc.z} air destroy`);
            } catch { /* ignore */ }
        }

        while (leafIdx < leavesToBreak.length && processed < BATCH_SIZE) {
            const loc = leavesToBreak[leafIdx++];
            processed++;
            try {
                dimension.runCommandAsync(`setblock ${loc.x} ${loc.y} ${loc.z} air destroy`);
            } catch { /* ignore */ }
        }

        if (logIdx < logsToBreak.length || leafIdx < leavesToBreak.length) {
            system.run(processBatch);
        } else {
            console.warn(`[Treecapitator] Done. Logs removed: ${logsToBreak.length}, Leaves removed: ${leavesToBreak.length}`);
        }
    }

    system.run(processBatch);
}

function getNeighbours26(loc) {
    const out = [];
    for (let dx = -1; dx <= 1; dx++) {
        for (let dy = -1; dy <= 1; dy++) {
            for (let dz = -1; dz <= 1; dz++) {
                if (dx === 0 && dy === 0 && dz === 0) continue;
                out.push({ x: loc.x + dx, y: loc.y + dy, z: loc.z + dz });
            }
        }
    }
    return out;
}

function withinRadius(loc, start) {
    return Math.abs(loc.x - start.x) <= MAX_RADIUS &&
           Math.abs(loc.y - start.y) <= MAX_RADIUS &&
           Math.abs(loc.z - start.z) <= MAX_RADIUS;
}

function encodeLocation(loc) {
    return loc.x + "," + loc.y + "," + loc.z;
}
'@

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
        Write-Log "Appending Treecapitator to pack list..."
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
    Write-Log "IMPORTANT: You must restart your Bedrock server completely for the script to load."
    Write-Log "When the server starts, look for '[Treecapitator] Script initialized' in the server console to verify it loaded."
    Write-Log "============================================================="

} catch {
    Write-Log "A fatal error occurred during installation:" -Level "ERROR"
    Write-Log $_.Exception.Message -Level "ERROR"
    Write-Log "Stack Trace: $($_.ScriptStackTrace)" -Level "ERROR"
} finally {
    Write-Host ""
    Write-Host "Press Enter to exit..." -ForegroundColor Cyan
    Read-Host
}
