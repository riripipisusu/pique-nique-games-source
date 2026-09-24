# Publie une nouvelle version : build Windows, zip, puis release GitHub.
# Les jeux deja installes la detectent au lancement et proposent "Mettre a jour".
#   .\publish.ps1 2.2.0 "Nouveau jeu : ..."
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Notes = "Nouvelle version"
)
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$unity = "C:\Program Files\Unity\Hub\Editor\6000.4.1f1\Editor\Unity.exe"
$repo = (Select-String "$root\Assets\Scripts\Updater.cs" -Pattern 'Repo = "([^"]+)"').Matches[0].Groups[1].Value
if ($repo -like "OWNER/*") { throw "Renseigne le depot GitHub dans Assets\Scripts\Updater.cs (constante Repo)." }
if (Get-Process Unity -ErrorAction SilentlyContinue) { throw "Ferme l'editeur Unity avant de publier." }

Set-Content "$root\VERSION" $Version -Encoding ascii
Write-Host "Build de la version $Version..."
& $unity -batchmode -quit -projectPath $root -executeMethod Setup.Build -logFile "$root\Logs\publish.log" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Build echouee, voir Logs\publish.log" }

$zip = "$root\PiqueNiqueGames-Windows.zip"
if (Test-Path $zip) { [IO.File]::Delete($zip) }
Compress-Archive -Path "$root\Build\*" -DestinationPath $zip
Write-Host ("Zip : {0:N0} Mo" -f ((Get-Item $zip).Length / 1MB))

$gh = (Get-Command gh -ErrorAction SilentlyContinue).Source; if (!$gh) { $gh = "C:\Program Files\GitHub CLI\gh.exe" }
& $gh release create "v$Version" $zip --repo $repo --title "Pique-Nique's Games $Version" --notes $Notes
if ($LASTEXITCODE -ne 0) { throw "Publication GitHub echouee" }
Write-Host "Publie : https://github.com/$repo/releases/tag/v$Version"
