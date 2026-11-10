$ErrorActionPreference = "Stop"
try { [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 } catch {}
[Net.ServicePointManager]::Expect100Continue = $false
$repo = "Trigger233/SP2GunRing"
$zip = "E:\DeepSeek\workspace\sp2-gunring-gh\bin\SP2GunRing-v0.8.0.zip"
$nl = [char]10
$desc = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String("U2ltcGxlUGxhbmVzIDIg5py654Ku5o+Q5YmN6YeP546vICsg54Ku5by56aKE5rWL546vIC8gZ3VuIGxlYWQgcmluZyArIGNhbm5vbiBiYWxsaXN0aWMgcHJlZGljdG9yCgoqKnYwLjguMCDmlrDlop4gLyBOZXcqKgotIOeCruW8ue+8iENhbm5vblNjcmlwdCDliJrkvZPlvLnkuLjvvInpooTmtYvnjq/vvJrmjInnkIbmg7PmipvkvZPvvIjlj6rlj5fph43lipvjgIHml6DnqbrmsJTpmLvlipvvvInop6PnrpcKLSDplIHlrprnm67moIfml7blkIzml7borqHnrpfnm67moIfmj5DliY3ph4/kuI7ph43lipvkuIvlnaDvvIznjq8gPSDmiormnLrlpLTmi4nliLDmiYDpnIDlvLnpgZPmlrnlkJHlkI7nm67moIfkvJrlh7rnjrDlnKjnmoTkvY3nva4KLSDmnKrplIHlrprnm67moIfml7bmjInlm7rlrpogUmFuZ2Ug5pi+56S65b2T5YmN54Ku5Y+j5pa55ZCR55qE54Ku5by56JC954K577yM5bm255S75Ye655CG5oOz5oqb54mp57q/6Jma57q/Ci0g5Y+v6LCD6YeN5Yqb55m+5YiG5q+U44CB6aKc6Imy44CB546v5aSn5bCP44CB6L2o6L+554K55pWw77yb5YWo6YOo5piv5pWw5a2X6YWN572uCgoqKuWuieijhSAvIEluc3RhbGwqKu+8muino+WOiyB6aXAg5Yiw5ri45oiP5qC555uu5b2V5ZCI5bm26KaG55uWIC8gZXh0cmFjdCB0aGUgemlwIGludG8gdGhlIGdhbWUgcm9vdA=="))
$in = "protocol=https" + $nl + "host=github.com" + $nl + $nl
$cred = $in | git credential fill 2>$null
$token = ($cred | Where-Object { $_ -like "password=*" }) -replace "^password=", ""
if ([string]::IsNullOrWhiteSpace($token)) { "NO_TOKEN"; exit }
$headers = @{ Authorization = "token $token"; "User-Agent" = "sp2-gunring-release"; Accept = "application/vnd.github+json" }
$body = @{ tag_name = "v0.8.0"; target_commitish = "main"; name = "SP2 Gun Lead Ring v0.8.0 - Cannon Ballistic Ring"; body = $desc; draft = $false; prerelease = $false } | ConvertTo-Json
try {
  $rel = Invoke-RestMethod -Uri ("https://api.github.com/repos/" + $repo + "/releases") -Method Post -Headers $headers -Body $body -ContentType "application/json" -TimeoutSec 60
  "release created: " + $rel.html_url
  $upUrl = "https://uploads.github.com/repos/" + $repo + "/releases/" + $rel.id + "/assets?name=SP2GunRing-v0.8.0.zip"
  $up = Invoke-RestMethod -Uri $upUrl -Method Post -Headers $headers -ContentType "application/zip" -InFile $zip -TimeoutSec 180
  "asset uploaded: " + $up.browser_download_url + " (" + $up.size + " bytes)"
} catch {
  "ERR: " + $_.Exception.Message
  if ($_.ErrorDetails) { $m = $_.ErrorDetails.Message; $m.Substring(0, [Math]::Min(500, $m.Length)) }
}
