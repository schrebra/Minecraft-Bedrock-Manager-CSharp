# Export-Project.ps1
# Run this in your project root directory

 $outputFile = ".\BedrockServerManager_FullSource.txt"
 $extensions = @("*.cs", "*.xaml", "*.csproj", "*.manifest")
 $excludeDirs = @("bin", "obj", ".vs")

Write-Host "Starting project export..." -ForegroundColor Cyan
Write-Host "Scanning directory: $(Get-Location)" -ForegroundColor DarkGray
Write-Host "Looking for extensions: $($extensions -join ', ')" -ForegroundColor DarkGray
Write-Host "Excluding directories: $($excludeDirs -join ', ')" -ForegroundColor DarkGray
Write-Host "---------------------------------------------------------"

if (Test-Path $outputFile) { 
    Write-Host "Removing old output file..." -ForegroundColor Yellow
    Remove-Item $outputFile -Force 
}

Write-Host "Searching for files..." -ForegroundColor Cyan

# Build a regex pattern to match any excluded directory anywhere in the full path
 $excludePattern = '\\(' + (($excludeDirs | ForEach-Object { [regex]::Escape($_) }) -join '|') + ')\\'

 $files = Get-ChildItem -Recurse -Include $extensions | Where-Object {
    $_.FullName -notmatch $excludePattern
} | Sort-Object FullName

Write-Host "Found $($files.Count) files to process." -ForegroundColor Green
Write-Host "---------------------------------------------------------"

 $delimiter = "========================================================="

foreach ($file in $files) {
    # Calculate path relative to the current directory
    $relativePath = $file.FullName.Replace((Get-Location).Path + "\", "")
    
    Write-Host " -> Processing: $relativePath" -ForegroundColor White

    Add-Content -Path $outputFile -Value $delimiter
    Add-Content -Path $outputFile -Value "FILE: $relativePath"
    Add-Content -Path $outputFile -Value $delimiter
    Add-Content -Path $outputFile -Value ""
    
    # Read content as a single string to preserve exact formatting
    $content = Get-Content $file.FullName -Raw
    Add-Content -Path $outputFile -Value $content
    
    Add-Content -Path $outputFile -Value "`n`n"
}

Write-Host "---------------------------------------------------------"
Write-Host "Project successfully exported to $outputFile" -ForegroundColor Green