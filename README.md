# SP2 Gun Lead Ring / SP2 机炮提前量环

SimplePlanes 2 的机炮提前量指示插件（BepInEx / Harmony）。
A gun lead indicator mod for SimplePlanes 2 (BepInEx / Harmony).

![styles](images/preview_styles.png)

## 功能 / Features

- 自动读取机上初速最快的机枪，按真实弹道方向（`PartScript.transform.forward`）计算提前量
- 锁定目标后，在准心旁边显示提前量环；把环压在敌机上即代表机头已对准提前量，开火即可命中
- 环的颜色随状态变化：未对准 / 可开火 / 提前量超限
- 命中反馈：屏幕准心处画叉 + 播放 hit.wav
- 四种准心样式、颜色、尺寸、搜索范围全部可配
- 兼容 Gravy Mod Menu：游戏内「Gun Ring 机炮环」标签页直接调参
- 纯运行时插件，不修改游戏文件

- Reads the fastest muzzle velocity gun on your craft and solves the lead using the real bullet direction
- Draws a lead ring next to the crosshair; putting the ring on the target means your nose is on the lead point
- Colour states: not aligned / ready to fire / offset clamped
- Hit feedback: cross at the gunsight + hit.wav sound
- 4 reticle styles, colours, size, search range all configurable
- Integrates with Gravy Mod Menu
- Runtime-only plugin, no game files modified

## 安装 / Install

1. 游戏自带 BepInEx 5.4.x，无需另装 / The game ships with BepInEx, nothing else to install
2. 下载 `bin/SP2GunRing-v0.5.0.zip`，解压到游戏根目录合并覆盖：
   `...\steamapps\common\SimplePlanes 2\`
   Download `bin/SP2GunRing-v0.5.0.zip` and extract it into the game root folder
3. 启动游戏，进入飞行场景即可 / Launch the game and enter a flight scene

## 用法 / Usage

锁定目标（HUD 出现括号）后，准心旁会出现一个环：

- 未对准色（默认白）= 还没对准提前量
- 可开火色（默认绿）= 机头已对准，开火必中
- 超限色（默认橙）= 所需提前量超过 `MaxOffsetPx`，环被截在边界，需要绕后 / 拉近

Lock a target and a ring appears near the crosshair:

- idle colour (white) – not aligned yet
- ready colour (green) – nose is on the lead point, fire now
- clamped colour (orange) – required lead exceeds `MaxOffsetPx`, get closer or turn in

## 配置 / Configuration

游戏内：Gravy Mod Menu -> 「Gun Ring 机炮环」
或直接编辑 `BepInEx/config/sp2.gunring.cfg`。所有选项都是数字。

| 分组 | 项 | 说明 |
|---|---|---|
| General | `Enabled` | 1/0 总开关 |
| | `OnlyWhenSelected` | 1/0 只在锁定目标时显示 |
| | `MaxRange` | 目标搜索半径 (m) |
| | `MaxAngle` | 搜索锥角 (度) |
| Visual | `Mode` | 0=Pinned 环贴准心 / 1=Lead 环画在提前量点（并显示机炮指向准心） |
| | `Style` | 0=六条线 1=四条线 2=虚线圈 3=实心圆 |
| | `RingRadiusPx` | 环尺寸（像素） |
| | `MaxOffsetPx` | Pinned 模式环离准心最远像素，0=不限 |
| | `ShowLine` | 1/0 准心到环的连线 |
| | `ShowGunCross` | 1/0 Lead 模式下的机炮指向准心 |
| | `GreenAngle` | 机头与提前量点夹角 ≤ 该值（度）时变「可开火色」 |
| | `DebugDraw` | 1/0 调试标记与数值 |
| Color | `IdleR/G/B`、`ReadyR/G/B`、`ClampedR/G/B` | 三态颜色，各 0-255 |
| | `Alpha` | 不透明度 0-255 |
| Hit | `HitMarker` / `HitMarkerMs` / `HitMarkerPx` | 命中叉 |
| | `HitSound` / `HitVolume` | 命中音效 |
| Menu | `PrintDiagnostics` | 勾一下把当前测算写进日志 |
| | `TestHitSound` | 勾一下播放命中音（测试用） |

## 从源码构建 / Build from source

需要 .NET Framework 4 的 `csc.exe`（Windows 自带），无需安装 SDK。
Requires only `csc.exe` from the .NET Framework (ships with Windows).

```powershell
# 编辑 build.ps1 顶部的 $GameDir 指向你的游戏目录
.\build.ps1          # 编译并部署到 BepInEx/plugins/SP2GunRing
.\build.cmd          # 同上（双击也能用）
```

编译引用来自游戏自己的 `SimplePlanes 2_Data/Managed` 与 `BepInEx/core`，所以版本跟随游戏本体。

## 已知限制 / Known limitations

- 多人服务器上伤害在服务端结算，命中反馈可能不触发；环的提前量计算本身与服务器无关
- 只处理机炮（bullet），不涉及导弹/炸弹
- Hit feedback may not trigger on multiplayer servers (damage is server-authoritative); the lead solution itself is client-side and unaffected
- Guns only (bullets), no missiles or bombs

## 许可 / License

MIT
