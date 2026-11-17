# SP2 Gun Lead Ring / SP2 机炮提前量环

SimplePlanes 2 的机炮提前量 + 炮弹弹道预测指示插件（BepInEx / Harmony）。
A gun lead indicator + cannon ballistic predictor for SimplePlanes 2 (BepInEx / Harmony).

![styles](images/preview_styles.png)

## 功能 / Features

- 自动读取机上初速最快的机枪，按真实弹道方向（`PartScript.transform.forward`）计算提前量
- 锁定目标后，在准心旁边显示提前量环；把环压在敌机上即代表机头已对准提前量，开火即可命中
- 环的颜色随状态变化：未对准 / 可开火 / 提前量超限
- **炮弹预测环（v0.8.0 新增）**：自动找机上初速最快的 `CannonScript` 火炮，按理想抛体运动（只受重力、无空气阻力）解算刚体弹丸；锁定目标时同时计算目标提前量与重力下坠，环=把机头拉到所需弹道方向后目标会出现在的位置；没锁目标时按固定距离显示当前炮弹落点，并用虚线画出抛物线弹道
- **Ace Combat 风格目标标签（v0.9.0 新增）**：瞄准模式开启时，锁定/选中目标显示「距离 / 机体名 / 操作者名」三行，未选中的目标只显示简单方框；选中目标可离散闪烁；敌方红色、友方浅蓝、中立绿色，字号和颜色可调
- 命中反馈：四段短线围成空心 X（FPS 式）+ 上方 HIT 字样 + hit.wav 音效，文字可自定义
- 命中判定同时覆盖网络玩家与本地 AI 机（单人模式也可用）
- 四种准心样式、颜色、尺寸、搜索范围全部可配
- 兼容 Gravy Mod Menu：游戏内「Gun Ring 机炮环」标签页直接调参
- 纯运行时插件，不修改游戏文件

- Reads the fastest muzzle velocity gun on your craft and solves the lead using the real bullet direction
- Draws a lead ring next to the crosshair; putting the ring on the target means your nose is on the lead point
- Colour states: not aligned / ready to fire / offset clamped
- **Cannon ballistic ring (new in v0.8.0)**: finds the fastest `CannonScript` cannon and solves its rigidbody projectile as ideal projectile motion (gravity only, no drag). With a locked target it adds both target lead and gravity drop; the ring marks where the target will appear once the nose is pulled onto the required ballistic direction. Without a target it draws the predicted impact point at a fixed range plus a dotted parabolic trail
- **Ace Combat style target label (new in v0.9.0)**: only while targeting mode is on. The selected target shows distance / aircraft type / operator; unselected targets are reduced to simple boxes; the selected target can blink discretely. Enemy red, friendly light blue, neutral green; font sizes and colours configurable
- Hit feedback: FPS-style X made of four non-crossing segments, a configurable HIT label above it and hit.wav
- Works for both networked players and local AI aircraft (single-player included)
- 4 reticle styles, colours, size, search range all configurable
- Optional 1:1 roll rotation of the reticles with the player aircraft (v0.10.0)
- Optional aim assist (v0.11.0, PID since v0.12.0): within `ToggleAssist` degrees, separate pitch/yaw PID controllers nudge Pitch/Yaw toward the lead point; integrated output is clamped to ±1, then blended with input and clamped to ±1
- Ace Combat style reticle (`Style=5`, v0.13.0): large ring with 8 outer teeth; inner predicted-time arc removed in v0.13.2
- Target box is a pure rectangular frame; the static 45-degree inner square appears after lock is complete
- Aim assist now computes the error between the gun bore and the lead point, so machine-gun mounting deflection is included (v0.13.6)
- Roll assist added as PD only, no integrator (`Assist/RollP`, `Assist/RollD`, v0.13.7)
- Assist sensitivity scaling `clamp01((IAS/TAS) / max(IAS,100)^2 * k)` with `k = SpeedRef^2`; default `SpeedRef=200 m/s` gives scale 1 at 200 m/s (v0.13.8)
- Aim assist is disabled whenever the targeting/aiming mode is off (v0.13.9)
- Local-player name blacklist at the top of `Plugin.cs`; a matching player gets a blocking message and all plugin features are disabled (v0.13.9)
- Gun ring target filter uses predicted flight time < machine-gun bullet lifetime (v0.13.0); cannon ring unchanged
- Integrates with Gravy Mod Menu
- Runtime-only plugin, no game files modified

## Source layout / 源码模块

`src/` is split into partial-class files so each feature can be edited independently:

| File | Responsibility |
|---|---|
| `Plugin.cs` | lifecycle, Gravy config, target scan, lead, hit feedback |
| `Blacklist.cs` | player blacklist and ban screen |
| `AceLabel.cs` | Ace Combat labels, rectangular target box, 45° lock square |
| `Reticle.cs` | gun reticle textures/styles (including Ace ring) |
| `Cannon.cs` | cannon ballistic ring and solver |
| `Assist.cs` | aim assist (Pitch/Yaw PID, Roll PD, gun offset, speed scaling) |

Build compiles every `src/*.cs`; edit the feature file and run `build.cmd`.

## 安装 / Install

1. 游戏自带 BepInEx 5.4.x，无需另装 / The game ships with BepInEx, nothing else to install
2. 下载 `bin/SP2GunRing-v0.13.9.zip`，解压到游戏根目录合并覆盖：
   `...\steamapps\common\SimplePlanes 2\`
   Download `bin/SP2GunRing-v0.13.9.zip` and extract it into the game root folder
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

炮弹预测环（v0.8.0）：
- 机上装了 Cannon 火炮时，自动出现天蓝色预测环和虚线弹道
- 锁定目标时：环 = 把机头拉到所需弹道方向后目标会出现的位置；把目标压在环上、环变绿时开火即可命中（已含目标提前量与重力下坠）
- 未锁定目标时：环 = 当前炮口方向在 `Range` 距离上的炮弹落点，虚线是理想抛物线

Cannon ballistic ring (v0.8.0):
- Appears automatically when the craft carries Cannon parts (cyan ring + dotted trail)
- With a locked target: the ring marks where the target will appear once the nose is on the required ballistic direction; put the target on the ring and fire when it turns green (target lead + gravity drop included)
- Without a target: the ring is the impact point of the current barrel direction at `Range`; the dotted line is the ideal parabola

Ace Combat 风格目标标签（v0.9.0）：
- 只在瞄准模式开启时显示；关掉瞄准模式就恢复游戏原版目标框
- 锁定/选中目标：三行「距离 / 机体名 / 操作者名」，可离散闪烁（默认开）
- 未选中的其他目标：只保留简单方框，不显示文字
- 默认把游戏原来的目标框文字和框一起隐藏，换成方括号角标；敌方红色、友方浅蓝、中立绿色
- 关闭 `HideVanillaBox` 可以只替换文字、保留原版目标框

Ace Combat style target label (v0.9.0):
- Only shown while targeting mode is on; turning it off restores the vanilla target boxes
- Selected target: distance / aircraft type / operator, with optional discrete blinking (on by default)
- Other unselected targets: simple boxes only, no text
- Replaces the vanilla target box text and box with bracket corners by default; enemy red, friendly light blue, neutral green
- Turn off `HideVanillaBox` to replace only the text and keep the vanilla boxes

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
| | `Style` | 0=六条线 1=四条线 2=虚线圈 3=实心圆 4=十条线 5=皇牌空战大环 |
| | `MaxRange` | 预留项；机炮环现在按预测飞行时间 < 机枪 Lifetime 筛选 |
| | `RingRadiusPx` | 环基础尺寸（像素，散布环更大时以散布为准） |
| | `MaxOffsetPx` | Pinned 模式环离准心最远像素，0=不限 |
| | `ShowLine` | 1/0 准心到环的连线 |
| | `ShowGunCross` | 1/0 Lead 模式下的机炮指向准心 |
| | `RingFollowsSpread` | 1/0 环大小随机炮散布自动放大（默认 1） |
| | `RollRotation` | 1/0 机炮环/炮弹环随玩家飞机滚转 1:1（默认 0） |
| | `FireSpin` | 1/0 按住机枪开火时准心旋转（默认 0） |
| | `SpinSpeed` | 开火旋转速度(度/秒)，默认 180，角度自动取余 360 |
| | `SpreadScale` | 散布环缩放百分比 50-400 |
| Assist | `Assist` | 1/0 辅助瞄准总开关 |
| | `ToggleAssist` | 预瞄环与机头夹角小于该角度(度)时介入，默认 5 |
| | `Mix` | 吸附混合比例 0-1，默认 0.35 |
| | `Mode` | 0=总是启用 1=只在按住开火时启用 |
| | `PitchP` / `PitchD` / `PitchI` | 俯仰 PID 增益，默认 1.2 / 0.2 / 0.05 |
| | `YawP` / `YawD` / `YawI` | 偏航 PID 增益，默认 1.2 / 0.2 / 0.05 |
| | `GreenAngle` | 机头与提前量点夹角 ≤ 该值（度）时变「可开火色」 |
| | `DebugDraw` | 1/0 调试标记与数值 |
| Color | `IdleR/G/B`、`ReadyR/G/B`、`ClampedR/G/B` | 三态颜色，各 0-255 |
| | `Alpha` | 不透明度 0-255 |
| Cannon | `CannonRing` | 1/0 炮弹预测环开关（默认开） |
| | `UseTargetRange` | 1/0 锁定目标时按目标距离解算；0=用固定 `Range` |
| | `Range` | 没有锁定目标时的预测距离（米，默认 1500） |
| | `RingRadiusPx` | 炮弹环半径（像素，默认 16） |
| | `R/G/B` | 炮弹环颜色，各 0-255（默认天蓝 90/210/255，可开火时变绿） |
| | `GravityPercent` | 重力百分比 0-200，100=真实重力，0=无下坠对比用 |
| | `GreenAngle` | 炮口与所需弹道方向夹角 ≤ 该值（度）时变绿，默认 1.5 |
| | `Trace` / `TraceDots` | 1/0 画理想抛体轨迹虚线 / 轨迹点数 4-60 |
| AceLabel | `Enabled` | 1/0 Ace Combat 风格目标标签开关 |
| | `HideVanillaBox` | 1/0 连游戏原目标框一起隐藏；0=只替换文字保留原框 |
| | `BoxPx` | 目标框边长（像素，默认 34） |
| | `DistanceFontPx` / `NameFontPx` | 距离字号 / 机体名与操作者字号（像素） |
| | `R/G/B` | 中立/通用颜色，各 0-255（默认绿色 80/255/120） |
| | `FriendR/G/B` | 友方颜色，各 0-255（默认浅蓝 120/210/255） |
| | `EnemyR/G/B` | 敌方颜色，各 0-255（默认红色 255/60/60） |
| | `TeamColor` | 1/0 按敌我上色；0=统一用 R/G/B |
| | `TextMatchBox` | 1/0 文字颜色跟目标框一致；0=文字统一用 R/G/B（默认 1） |
| | `Blink` / `BlinkHz` | 1/0 选中目标闪烁 / 闪烁频率 Hz 1-10（默认 1 / 2） |
| Hit | `HitMarker` / `HitMarkerMs` / `HitMarkerPx` | 命中标记：开关 / 时长 ms / 线段长度 px |
| | `HitText` | 标记上方文字，默认 HIT，留空不显示 |
| | `HitSound` / `HitVolume` | 命中音效开关 / 音量 0-100 |
| Menu | `DebugLog` | 1/0 写调试日志（环位置/目标血量），平时关 |
| | `PrintDiagnostics` | 勾一下把当前测算写进日志 |
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

- 命中反馈依赖「目标血量上涨」判定，需要先锁定目标；环的计算与服务器无关
- 炮弹预测环只对 `CannonScript` 火炮生效；普通机枪弹（BulletPool）在 SP2 里是直线、无重力
- 只处理机枪（bullet）与 `CannonScript` 炮弹；导弹/炸弹/火箭不处理
- 炮弹按理想抛体（只受重力、无空气阻力）解算；若弹丸 prefab 自带 drag 会有小误差
- Ace Combat 标签只在瞄准模式开启时替换目标框；未选中目标只画简单框
- Hit detection watches the locked target's damage value, so a locked target is required
- The Ace Combat label only replaces target boxes while targeting mode is on; unselected targets get simple boxes
- The cannon ring only works for `CannonScript` cannons; regular BulletPool rounds are straight-line and gravity-free in SP2
- Guns (bullets) and `CannonScript` cannon projectiles only; no missiles, bombs or rockets
- Cannon solution assumes ideal projectile motion (gravity only, no drag); projectile prefab drag would add a small error

## 许可 / License

MIT
