$ErrorActionPreference = "Stop"
try { [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 } catch {}
[Net.ServicePointManager]::Expect100Continue = $false
$repo = "Trigger233/SP2GunRing"
$zip = "E:\DeepSeek\workspace\sp2-gunring-gh\bin\SP2GunRing-v0.5.0.zip"
$nl = [char]10
$desc = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String("U2ltcGxlUGxhbmVzIDIg5py654Ku5o+Q5YmN6YeP546vIC8gZ3VuIGxlYWQgcmluZwoKLSDplIHlrprnm67moIflkI7lnKjlh4blv4Pml4HmmL7npLrmj5DliY3ph4/njq/vvIhQaW5uZWQgLyBMZWFkIOS4pOenjeaooeW8j++8iQotIOWbm+enjeWHhuW/g+agt+W8j+OAgeminOiJsuS4juWwuuWvuOWPr+iwg+OAgeWRveS4reWPjemmiCBoaXQud2F2Ci0g5a6J6KOF77ya5oqK6YeM6Z2i55qEIEJlcEluRXgg5paH5Lu25aS56Kej5Y6L5Yiw5ri45oiP5qC555uu5b2V5ZCI5bm26KaG55uWCi0g6YWN572u77ya5ri45oiP5YaFIEdyYXZ5IE1vZCBNZW51IOKGkuOAjEd1biBSaW5nIOacuueCrueOr+OAjQ=="))
$in = "protocol=https" + $nl + "host=github.com" + $nl + $nl
$cred = $in | git credential fill 2>$null
$token = ($cred | Where-Object { $_ -like "password=*" }) -replace "^password=", ""
if ([string]::IsNullOrWhiteSpace($token)) { "NO_TOKEN"; exit }
"token length: " + $token.Length
$headers = @{ Authorization = "token $token"; "User-Agent" = "sp2-gunring-release"; Accept = "application/vnd.github+json" }
$body = @{ tag_name = "v0.5.0"; target_commitish = "main"; name = "SP2 Gun Lead Ring v0.5.0"; body = $desc; draft = $false; prerelease = $false } | ConvertTo-Json
try {
  $rel = Invoke-RestMethod -Uri ("https://api.github.com/repos/" + $repo + "/releases") -Method Post -Headers $headers -Body $body -ContentType "application/json" -TimeoutSec 60
  "release created: " + $rel.html_url
  $upUrl = "https://uploads.github.com/repos/" + $repo + "/releases/" + $rel.id + "/assets?name=SP2GunRing-v0.5.0.zip"
  $up = Invoke-RestMethod -Uri $upUrl -Method Post -Headers $headers -ContentType "application/zip" -InFile $zip -TimeoutSec 180
  "asset uploaded: " + $up.browser_download_url + " (" + $up.size + " bytes)"
} catch {
  "ERR: " + $_.Exception.Message
  if ($_.ErrorDetails) { $m = $_.ErrorDetails.Message; $m.Substring(0, [Math]::Min(500, $m.Length)) }
}