$ErrorActionPreference = "Stop"
try { [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 } catch {}
[Net.ServicePointManager]::Expect100Continue = $false
$repo = "Trigger233/SP2GunRing"
$zip = "E:\DeepSeek\workspace\sp2-gunring-gh\bin\SP2GunRing-v0.9.0.zip"
$nl = [char]10
$desc = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String("U2ltcGxlUGxhbmVzIDIg5py654Ku5o+Q5YmN6YeP546vICsg54Ku5by56aKE5rWL546vICsgQWNlIENvbWJhdCDnm67moIfmoIfnrb4gLyBsZWFkIHJpbmcgKyBjYW5ub24gYmFsbGlzdGljIHJpbmcgKyBBY2UgQ29tYmF0IGxhYmVscwoKKip2MC45LjAg5paw5aKeIC8gTmV3KioKLSBBY2UgQ29tYmF0IOmjjuagvOebruagh+agh+etvu+8mumUgeWumuebruagh+WQjuaYvuekuuOAjOi3neemuyjnsbMpIC8g5py65L2T5ZCNIC8g5pON5L2c6ICF5ZCN44CN5LiJ6KGM77yM5bim5pa55ous5Y+36KeS5qCHCi0g6buY6K6k6ZqQ6JeP5ri45oiP5Y6f55uu5qCH5qGG5paH5a2X5LiO5qGG77yM6Ieq5bex6YeN55S777yb5pWM5pa557qi6Imy44CB5Y+L5pa557u/6Imy77yM5a2X5Y+3L+minOiJsuWPr+iwgwotIOWFvOWuueaZrumAmuebruagh+ahhuS4jiBKdWljeSBIVUQg55uu5qCH5qGGCgoqKnYwLjguMCoqCi0g54Ku5by56aKE5rWL546v77yaQ2Fubm9uU2NyaXB0IOWImuS9k+W8ueS4uO+8jOaMieeQhuaDs+aKm+S9k++8iOmHjeWKm+OAgeaXoOepuuawlOmYu+WKm++8ieino+eul+ebruagh+aPkOWJjemHjyvkuIvlnaDvvJvmipvniannur/omZrnur8KCioq5a6J6KOFIC8gSW5zdGFsbCoq77ya6Kej5Y6LIHppcCDliLDmuLjmiI/moLnnm67lvZXlkIjlubbopobnm5YgLyBleHRyYWN0IHRoZSB6aXAgaW50byB0aGUgZ2FtZSByb290"))
$in = "protocol=https" + $nl + "host=github.com" + $nl + $nl
$cred = $in | git credential fill 2>$null
$token = ($cred | Where-Object { $_ -like "password=*" }) -replace "^password=", ""
if ([string]::IsNullOrWhiteSpace($token)) { "NO_TOKEN"; exit }
$headers = @{ Authorization = "token $token"; "User-Agent" = "sp2-gunring-release"; Accept = "application/vnd.github+json" }
$body = @{ tag_name = "v0.9.0"; target_commitish = "main"; name = "SP2 Gun Lead Ring v0.9.0 - Ace Combat Labels"; body = $desc; draft = $false; prerelease = $false } | ConvertTo-Json
try {
  $rel = Invoke-RestMethod -Uri ("https://api.github.com/repos/" + $repo + "/releases") -Method Post -Headers $headers -Body $body -ContentType "application/json" -TimeoutSec 60
  "release created: " + $rel.html_url
  $upUrl = "https://uploads.github.com/repos/" + $repo + "/releases/" + $rel.id + "/assets?name=SP2GunRing-v0.9.0.zip"
  $up = Invoke-RestMethod -Uri $upUrl -Method Post -Headers $headers -ContentType "application/zip" -InFile $zip -TimeoutSec 180
  "asset uploaded: " + $up.browser_download_url + " (" + $up.size + " bytes)"
} catch {
  "ERR: " + $_.Exception.Message
  if ($_.ErrorDetails) { $m = $_.ErrorDetails.Message; $m.Substring(0, [Math]::Min(500, $m.Length)) }
}
