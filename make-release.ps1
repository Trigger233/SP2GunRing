$ErrorActionPreference = "Stop"
try { [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 } catch {}
[Net.ServicePointManager]::Expect100Continue = $false
$repo = "Trigger233/SP2GunRing"
$zip = "E:\DeepSeek\workspace\sp2-gunring-gh\bin\SP2GunRing-v0.7.0.zip"
$nl = [char]10
$desc = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String("U2ltcGxlUGxhbmVzIDIg5py654Ku5o+Q5YmN6YeP546vIC8gZ3VuIGxlYWQgcmluZwoKKirkv67lpI0gLyBGaXhlcyoqCi0g5o+Q5YmN6YeP5pS555SoIEFpcmNyYWZ0U2NyaXB0LlZlbG9jaXR577yI5Y6f5p2l6K+7IEJvZGllc1swXe+8jOmjnuihjOS4reWPquaciSAzIG0vc++8jOaPkOWJjemHj+eCueS8muWBj+WHuuS4iuWNg+exs++8iQotIOacuuS9k+S9jee9ruaUueeUqCBPcmllbnRlZENlbnRlck9mTWFzc1JpZ2lkQm9kaWVz77yM5LiN5YaN55So5Lya6L+H5pyf55qEIGNyYWZ0LnRyYW5zZm9ybQotIOeOr+S9jee9ruWbnuW9kueos+WumueahOWxj+W5leepuumXtOW3ruWAvO+8mueOryA9IOWHhuW/gyArICjmlYzmnLrlsY/luZUg4oiSIOaPkOWJjemHj+Wxj+W5lSnvvJvomZrmi5/nm7jmnLrph43mipXlvbHlnKjlpKfop5LluqbkuIvlnZDmoIfkvJrniIbmjonvvIzlt7Llup/lvIMKCioq5paw5Yqf6IO9IC8gTmV3KioKLSDlkb3kuK3lj43ppojph43lgZrvvJrlm5vmrrXnn63nur/lm7TmiJDnqbrlv4MgWO+8iEZQUyDlvI/vvIzkuK3pl7TkuI3kuqTlj4nvvIkrIOS4iuaWueWPr+iHquWumuS5ieaWh+Wtl++8iOm7mOiupCBISVTvvIkrIGhpdC53YXYKLSDlkb3kuK3liKTlrpropobnm5bmnKzlnLAgQUkg5py677ya5Y2V5py65qih5byP5Lmf6IO96Kem5Y+R77yI55uR6KeG55uu5qCHIEFpcmNyYWZ0U2NyaXB0LkRhbWFnZSDkuIrmtqjvvIkKLSDmlrDlop4gTWVudS9EZWJ1Z0xvZyDlvIDlhbPvvIzpu5jorqTlhbPpl60KCioq5a6J6KOFIC8gSW5zdGFsbCoq77ya6Kej5Y6LIHppcCDliLDmuLjmiI/moLnnm67lvZXlkIjlubbopobnm5Y="))
$in = "protocol=https" + $nl + "host=github.com" + $nl + $nl
$cred = $in | git credential fill 2>$null
$token = ($cred | Where-Object { $_ -like "password=*" }) -replace "^password=", ""
if ([string]::IsNullOrWhiteSpace($token)) { "NO_TOKEN"; exit }
$headers = @{ Authorization = "token $token"; "User-Agent" = "sp2-gunring-release"; Accept = "application/vnd.github+json" }
$body = @{ tag_name = "v0.7.0"; target_commitish = "main"; name = "SP2 Gun Lead Ring v0.7.0"; body = $desc; draft = $false; prerelease = $false } | ConvertTo-Json
try {
  $rel = Invoke-RestMethod -Uri ("https://api.github.com/repos/" + $repo + "/releases") -Method Post -Headers $headers -Body $body -ContentType "application/json" -TimeoutSec 60
  "release created: " + $rel.html_url
  $upUrl = "https://uploads.github.com/repos/" + $repo + "/releases/" + $rel.id + "/assets?name=SP2GunRing-v0.7.0.zip"
  $up = Invoke-RestMethod -Uri $upUrl -Method Post -Headers $headers -ContentType "application/zip" -InFile $zip -TimeoutSec 180
  "asset uploaded: " + $up.browser_download_url + " (" + $up.size + " bytes)"
} catch {
  "ERR: " + $_.Exception.Message
  if ($_.ErrorDetails) { $m = $_.ErrorDetails.Message; $m.Substring(0, [Math]::Min(500, $m.Length)) }
}
