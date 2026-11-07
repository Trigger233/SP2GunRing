$ErrorActionPreference = "Stop"
# point this at your SimplePlanes 2 folder / 指向你的游戏目录
$GameDir = "C:\SteamGames\Steam\steamapps\common\SimplePlanes 2"

$Managed = Join-Path $GameDir "SimplePlanes 2_Data\Managed"
$Bep     = Join-Path $GameDir "BepInEx\core"
$OutDir  = Join-Path $PSScriptRoot "dist"
$OutDll  = Join-Path $OutDir "SP2GunRing.dll"
$Csc     = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if (-not (Test-Path $Csc))     { throw "csc.exe not found: $Csc" }
if (-not (Test-Path $Managed)) { throw "game Managed folder not found: $Managed" }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$modules = @(
  "mscorlib.dll","System.dll","System.Core.dll","netstandard.dll",
  "UnityEngine.CoreModule.dll","UnityEngine.dll","UnityEngine.IMGUIModule.dll",
  "UnityEngine.InputLegacyModule.dll","UnityEngine.TextRenderingModule.dll",
  "UnityEngine.AudioModule.dll","Game.dll","FishNet.Runtime.dll"
)
$refs = $modules | ForEach-Object { "-r:""$Managed\$_""" }
$refs += "-r:""$Bep\BepInEx.dll"""
$refs += "-r:""$Bep\0Harmony.dll"""

Write-Host "building..."
& $Csc /noconfig /nostdlib+ /target:library /langversion:5 "/out:$OutDll" @refs (Join-Path $PSScriptRoot "src\Plugin.cs")
if (-not (Test-Path $OutDll)) { throw "build failed" }
Write-Host ("ok  {0} bytes  ->  {1}" -f (Get-Item $OutDll).Length, $OutDll)

$PluginDir = Join-Path $GameDir "BepInEx\plugins\SP2GunRing"
if (Get-Process -Name "SimplePlanes 2","SimplePlanes2" -ErrorAction SilentlyContinue) {
  Write-Host "game is running - close it and run again to deploy." -ForegroundColor Yellow
  exit 0
}
New-Item -ItemType Directory -Force -Path $PluginDir | Out-Null
Copy-Item $OutDll $PluginDir -Force
$wav = Join-Path $PSScriptRoot "assets\hit.wav"
if (Test-Path $wav) { Copy-Item $wav $PluginDir -Force }
Write-Host "deployed to $PluginDir"
