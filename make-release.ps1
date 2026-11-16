$ErrorActionPreference = "Stop"
try { [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 } catch {}
[Net.ServicePointManager]::Expect100Continue = $false
$repo = "Trigger233/SP2GunRing"
$version = "v0.12.4"
$zip = "E:\DeepSeek\workspace\sp2-gunring-gh\bin\SP2GunRing-v0.12.4.zip"
$nl = [char]10
$desc = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String("U1AyIEd1biBMZWFkIFJpbmcgdjAuMTIuNAotIOaWsOWHhuW/g+agt+W8jyBTdHlsZT0077yaMTYg5p2h57q/5Zu0552A5LiA5Liq5Lit5b+D54K5Ci0g5py654Ku546vL+eCruW8ueeOr+WPr+mAiemaj+mjnuacuua7mui9rCAxOjHvvIhWaXN1YWwvUm9sbFJvdGF0aW9u77yJCi0g6L6F5Yqp556E5YeGIEFzc2lzdO+8mlBpdGNoL1lhdyDni6znq4sgUElE77yM56ev5YiG6L6T5Ye66ZmQ5Yi2IMKxMe+8jOacgOe7iCBQaXRjaC9ZYXcg5aS55ZyoIMKxMQotIEFzc2lzdC9Nb2RlPTAg5oC75piv5ZCv55So77yMPTEg5Y+q5Zyo5oyJ5L2P5byA54Gr5pe25ZCv55SoCi0g5L+u5q2jIFBpdGNoIOWQuOmZhOaWueWQke+8m0k9MCDml7blnKjorr7nva7lm57osIPmuIXnp6/liIbvvJvmu5rovazml7bml4vovaznp6/liIblkJHph48KLSDkv53nlZnmnLrngq7mj5DliY3ph4/njq/jgIHngq7lvLnpooTmtYvnjq/jgIFBY2UgQ29tYmF0IOebruagh+agh+etvgoK5a6J6KOF77ya6Kej5Y6LIHppcCDliLAgU2ltcGxlUGxhbmVzIDIg5ri45oiP5qC555uu5b2V44CCCkluc3RhbGw6IGV4dHJhY3QgdGhlIHppcCBpbnRvIHRoZSBTaW1wbGVQbGFuZXMgMiBnYW1lIHJvb3Qu"))
$in = "protocol=https" + $nl + "host=github.com" + $nl + $nl
$cred = $in | git credential fill 2>$null
$token = ($cred | Where-Object { $_ -like "password=*" }) -replace "^password=", ""
if ([string]::IsNullOrWhiteSpace($token)) { "NO_TOKEN"; exit }
$headers = @{ Authorization = "token $token"; "User-Agent" = "sp2-gunring-release"; Accept = "application/vnd.github+json" }
$body = @{ tag_name = $version; target_commitish = "main"; name = "SP2 Gun Lead Ring v0.12.4 - 16-tick reticle + aim assist"; body = $desc; draft = $false; prerelease = $false } | ConvertTo-Json
$bodyBytes = [System.Text.Encoding]::UTF8.GetBytes($body)
try {
  $rel = Invoke-RestMethod -Uri ("https://api.github.com/repos/" + $repo + "/releases") -Method Post -Headers $headers -Body $bodyBytes -ContentType "application/json; charset=utf-8" -TimeoutSec 60
  "release created: " + $rel.html_url
  $assetName = "SP2GunRing-v0.12.4.zip"
  $upUrl = "https://uploads.github.com/repos/" + $repo + "/releases/" + $rel.id + "/assets?name=" + $assetName
  $up = Invoke-RestMethod -Uri $upUrl -Method Post -Headers $headers -ContentType "application/zip" -InFile $zip -TimeoutSec 180
  "asset uploaded: " + $up.browser_download_url + " (" + $up.size + " bytes)"
} catch {
  "ERR: " + $_.Exception.Message
  if ($_.ErrorDetails) { $m = $_.ErrorDetails.Message; $m.Substring(0, [Math]::Min(500, $m.Length)) }
}
