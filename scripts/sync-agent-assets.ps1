$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$CanonicalSkills = Join-Path $RepoRoot ".agents\skills"
$ClaudeSkills = Join-Path $RepoRoot ".claude\skills"
$CodexSkills = Join-Path $RepoRoot ".codex\skills"
$CanonicalRules = Join-Path $RepoRoot ".agents\rules"
$ClaudeRules = Join-Path $RepoRoot ".claude\rules"

function Sync-Directory($Source, $Destination) {
    if (Test-Path $Destination) {
        Remove-Item $Destination -Recurse -Force
    }
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    Copy-Item (Join-Path $Source "*") $Destination -Recurse -Force
}

Sync-Directory $CanonicalSkills $ClaudeSkills
Sync-Directory $CanonicalSkills $CodexSkills
Sync-Directory $CanonicalRules $ClaudeRules

Write-Host "Agent rules/skills synchronized."
Write-Host "Canonical source: .agents/"
