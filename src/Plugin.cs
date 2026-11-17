using System;
using System.IO;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;
using HarmonyLib;
using Assets.Scripts.Flight.Combat.Bullets;
using Assets.Scripts.Flight;
using Assets.Scripts.Flight.Combat;
using Assets.Scripts.Flight.UI.Targeting;
using Assets.Scripts.Craft;
using Assets.Scripts.Craft.Parts.Modifiers;
using Assets.Scripts.Craft.Parts.Modifiers.Weapons;
using Assets.Scripts.Multiplayer;
using Assets.Scripts.Multiplayer.FlightObjects.Damage;

namespace SP2GunRing
{
    [HarmonyPatch(typeof(BulletPool), "CreateBullet")]
    static class BulletProbe
    {
        [HarmonyPostfix]
        static void Postfix(Vector3 velocity)
        {
            GunRingPlugin.OnBullet(velocity);
        }
    }

    // 子弹命中真正会走的入口（DamagePart 那条路子弹不走）
    [HarmonyPatch(typeof(NetworkFlightObjectDamageReceiverScript), "OnStandardBulletHit")]
    static class BulletHitProbe
    {
        [HarmonyPostfix]
        static void Postfix(NetworkFlightObjectDamageReceiverScript __instance, float __0)
        {
            GunRingPlugin.OnBulletHit(__instance, __0);
        }
    }

    [HarmonyPatch(typeof(NetworkFlightObjectDamageReceiverScript), "OnDamageReceived")]
    static class DamageRecvProbe
    {
        [HarmonyPostfix]
        static void Postfix(NetworkFlightObjectDamageReceiverScript __instance, float __1)
        {
            GunRingPlugin.OnAnyDamage(__instance, __1, "OnDamageReceived");
        }
    }

    [HarmonyPatch(typeof(NetworkFlightObjectDamageReceiverScript), "OnDamageSynced")]
    static class DamageSyncProbe
    {
        [HarmonyPostfix]
        static void Postfix(NetworkFlightObjectDamageReceiverScript __instance, short __0, short __1)
        {
            GunRingPlugin.OnAnyDamage(__instance, __1, "OnDamageSynced");
        }
    }

    // 单人模式下网络伤害链路可能整条不存在，这里挂 AircraftScript 自己的受伤入口
    [HarmonyPatch(typeof(AircraftScript), "OnDamaged")]
    static class LocalDamagedProbe
    {
        [HarmonyPostfix]
        static void Postfix(AircraftScript __instance)
        {
            GunRingPlugin.OnLocalDamage2(__instance);
        }
    }

    [HarmonyPatch(typeof(NetworkAircraftScript), "DamagePart")]
    static class HitProbe
    {
        [HarmonyPostfix]
        static void Postfix(NetworkAircraftScript __instance)
        {
            GunRingPlugin.OnDamage(__instance);
        }
    }

    // 本地（非联网）载具的受伤入口：AI 机被打中不会走 Network* 那几条
    [HarmonyPatch(typeof(AircraftScript), "AircraftDamaged")]
    static class LocalDamageProbe
    {
        [HarmonyPostfix]
        static void Postfix(AircraftScript __instance)
        {
            GunRingPlugin.OnLocalDamage(__instance);
        }
    }

    // 把游戏原来的目标框文字换成 Ace Combat 风格（距离 / 机体名 / 操作者）
    [HarmonyPatch(typeof(TargetBoxScript), "LateUpdate")]
    static class TargetBoxLabelPatch
    {
        [HarmonyPostfix]
        static void Postfix(TargetBoxScript __instance)
        {
            GunRingPlugin.OnTargetBox(__instance);
        }
    }

    // Juicy HUD 的目标框（UI Widget 版）也要处理
    [HarmonyPatch(typeof(TargetBoxScriptJuicy), "LateUpdate")]
    static class TargetBoxJuicyPatch
    {
        [HarmonyPostfix]
        static void Postfix(TargetBoxScriptJuicy __instance)
        {
            GunRingPlugin.OnTargetBoxJuicy(__instance);
        }
    }

    [BepInPlugin("sp2.gunring", "SP2 Gun Lead Ring", "0.13.9")]
    public partial class GunRingPlugin : BaseUnityPlugin
    {
        // Gravy Mod Menu 会扫每个插件的静态字符串字段 GravyMenuTab，
        // 有它就把本插件的 BepInEx 配置项自动渲染成一个标签页。
        // FieldFlags: Static|Public —— 必须是 public static / const。
        public const string GravyMenuTab = "Gun Ring 机炮环";


        internal static ManualLogSource L;
        static ConfigEntry<bool> CEnabled;
        static ConfigEntry<bool> CDumpNow;
        static ConfigEntry<bool> CTestHit;
        static bool _suppress;
        static ConfigEntry<float> CMaxRange;
        static ConfigEntry<float> CMaxAngle;
        static ConfigEntry<float> CRingPx;
        static ConfigEntry<int> CStyle;
        static ConfigEntry<int> CModeInt;
        static ConfigEntry<bool> CDebugDraw;

        static ConfigEntry<int> CMaxOffsetPx;
        static ConfigEntry<bool> CShowLine;
        static ConfigEntry<float> CGreenAngle;
        static ConfigEntry<int> CIdleR, CIdleG, CIdleB;
        static ConfigEntry<int> CReadyR, CReadyG, CReadyB;
        static ConfigEntry<int> CClampR, CClampG, CClampB;
        static ConfigEntry<int> CAlpha;
        static ConfigEntry<bool> CGunCross;
        static ConfigEntry<bool> CRingFollowsSpread;
        static ConfigEntry<int> CSpreadScale;
        static ConfigEntry<bool> CRollRotation;
        static ConfigEntry<bool> CFireSpin;
        static ConfigEntry<float> CSpinSpeed;
        static ConfigEntry<bool> CAssistEnabled;
        static ConfigEntry<float> CToggleAssist;
        static ConfigEntry<float> CAssistMix;
        static ConfigEntry<int> CAssistMode;
        static ConfigEntry<float> CAssistPitchP;
        static ConfigEntry<float> CAssistPitchD;
        static ConfigEntry<float> CAssistPitchI;
        static ConfigEntry<float> CAssistYawP;
        static ConfigEntry<float> CAssistYawD;
        static ConfigEntry<float> CAssistYawI;
        static ConfigEntry<float> CAssistRollP;
        static ConfigEntry<float> CAssistRollD;
        static ConfigEntry<bool> CAssistSpeedScale;
        static ConfigEntry<float> CAssistSpeedRef;
        static ConfigEntry<bool> COnlySelected;


        static ConfigEntry<bool> CHitMarker;
        static ConfigEntry<int> CHitMarkerMs;
        static ConfigEntry<bool> CHitSound;
        static ConfigEntry<float> CHitVolume;
        static ConfigEntry<int> CHitMarkerPx;
        static ConfigEntry<string> CHitText;
        static ConfigEntry<bool> CDebugLog;

        // 炮弹（CannonScript 的刚体弹丸）预测环配置
        static ConfigEntry<bool> CCannonEnabled;
        static ConfigEntry<bool> CCannonTrace;
        static ConfigEntry<int> CCannonTraceDots;
        static ConfigEntry<float> CCannonRange;
        static ConfigEntry<float> CCannonRingPx;
        static ConfigEntry<int> CCannonR, CCannonG, CCannonB;
        static ConfigEntry<int> CCannonGravityPct;
        static ConfigEntry<bool> CCannonUseTargetRange;
        static ConfigEntry<float> CCannonGreenAngle;

        // Ace Combat 风格目标标签
        static ConfigEntry<bool> CAceLabel;
        static ConfigEntry<bool> CAceHideVanilla;
        static ConfigEntry<int> CAceBoxPx;
        static ConfigEntry<int> CAceDistPx;
        static ConfigEntry<int> CAceNamePx;
        static ConfigEntry<int> CAceR, CAceG, CAceB;
        static ConfigEntry<int> CAceFriendR, CAceFriendG, CAceFriendB;
        static ConfigEntry<int> CAceEnemyR, CAceEnemyG, CAceEnemyB;
        static ConfigEntry<bool> CAceTeamColor;
        static ConfigEntry<bool> CAceTextMatchBox;
        static ConfigEntry<bool> CAceBlink;
        static ConfigEntry<int> CAceBlinkHz;
        static ConfigEntry<bool> CAceLockFx;
        bool _on = true;
        internal static GunRingPlugin Instance;
        static float _hitUntil = -1f;
        static int _hitCount;
        static int _dmgCalls;
        internal static ManualLogSource Log2;

        static int _hitProbeCount;

        static int _anyDamageCount;
        static int _localDmgCount;
        static int _localDmg2Count;

        internal static void OnAnyDamage(NetworkFlightObjectDamageReceiverScript recv, float amount, string via)
        {
            GunRingPlugin p = Instance;
            if (p == null || recv == null) return;
            AircraftScript ac = null;
            try { ac = recv.GetComponentInParent<AircraftScript>(); } catch { }
            if (_anyDamageCount < 8)
            {
                _anyDamageCount++;
                L.LogInfo("[GunRing] dmg#" + _anyDamageCount + " via=" + via + " aircraft=" + (ac != null ? ac.name : "?") + " amount=" + amount.ToString("0.0") +
                          " self=" + (ac == p._craft) + " selected=" + (p._target != null ? p._target.Name : "null"));
            }
            if (ac == null || ac == p._craft) return;
            HitIfTarget(p, ac);
        }

        internal static void OnBulletHit(NetworkFlightObjectDamageReceiverScript recv, float damage)
        {
            GunRingPlugin p = Instance;
            if (p == null || recv == null) return;
            AircraftScript ac = recv.GetComponentInParent<AircraftScript>();
            if (ac == null || ac == p._craft) return;
            if (_hitProbeCount < 6)
            {
                _hitProbeCount++;
                L.LogInfo("[GunRing] bulletHit#" + _hitProbeCount + " on " + ac.name + " dmg=" + damage.ToString("0.0") + " selected=" + (p._target != null ? p._target.Name : "null"));
            }
            HitIfTarget(p, ac);
        }

        // 统一的「这一发是不是打在锁定目标上」判定：
        // 网络玩家按 Player.Aircraft 精确匹配；本地 AI 没有 Player，按位置距离兜底
        internal static bool HitIfTarget(GunRingPlugin p, AircraftScript ac)
        {
            if (p == null || ac == null || ac == p._craft) return false;
            if (p._target == null) return false;
            try
            {
                if (p._target.Player != null && p._target.Player.Aircraft == ac) { MarkHit(ac.name); return true; }
            }
            catch { }
            Vector3 bodyPos = ac.transform.position;
            try { if (ac.Bodies != null && ac.Bodies.Count > 0) bodyPos = ac.Bodies[0].transform.position; } catch { }
            if (Vector3.Distance(p._target.Position, bodyPos) <= 60f) { MarkHit(ac.name); return true; }
            return false;
        }

        internal static void OnLocalDamage2(AircraftScript ac)
        {
            GunRingPlugin p = Instance;
            if (p == null || ac == null) return;
            if (ac == p._craft) return;
            Vector3 bodyPos = ac.transform.position;
            try { if (ac.Bodies != null && ac.Bodies.Count > 0) bodyPos = ac.Bodies[0].transform.position; } catch { }
            float d = p._target != null ? Vector3.Distance(p._target.Position, bodyPos) : -1f;
            if (_localDmg2Count < 8)
            {
                _localDmg2Count++;
                L.LogInfo("[GunRing] onDamaged aircraft=" + ac.name + " distToTarget=" + d.ToString("0.0")
                    + " selected=" + (p._target != null ? p._target.Name : "null"));
            }
            HitIfTarget(p, ac);
        }

        internal static void OnLocalDamage(AircraftScript ac)
        {
            GunRingPlugin p = Instance;
            if (p == null || ac == null || ac == p._craft) return;
            if (p._target == null) return;
            Vector3 bodyPos = ac.transform.position;
            try { if (ac.Bodies != null && ac.Bodies.Count > 0) bodyPos = ac.Bodies[0].transform.position; } catch { }
            float d = Vector3.Distance(p._target.Position, bodyPos);
            if (_localDmgCount < 6)
            {
                _localDmgCount++;
                L.LogInfo("[GunRing] localDmg on " + ac.name + " distToTarget=" + d.ToString("0.0") + " selected=" + p._target.Name);
            }
            if (d > 60f) return;   // 不是当前锁定那架
            MarkHit(ac.name);
        }

        internal static void OnDamage(NetworkAircraftScript n)
        {
            GunRingPlugin p = Instance;
            if (p == null || n == null) return;
            AircraftScript ac = n.AircraftScript;
            if (ac == null) return;
            if (_dmgCalls < 6)
            {
                _dmgCalls++;
                GunRingPlugin.L.LogInfo("[GunRing] DamagePart#" + _dmgCalls + " aircraft=" + ac.name + " self=" + (ac == p._craft) + " selected=" + (p._target != null ? p._target.Name : "null"));
            }
            if (ac == p._craft) return;
            HitIfTarget(p, ac);
        }

        // TargetBoxScript.LateUpdate 的 Postfix：把当前锁定目标的原版文字/框换成我们的
        static readonly PropertyInfo _boxDistProp = typeof(TargetBoxScript).GetProperty("DistanceLabelObject", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        static readonly PropertyInfo _boxNameProp = typeof(TargetBoxScript).GetProperty("NameLabelObject", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        static readonly PropertyInfo _boxSpriteProp = typeof(TargetBoxScript).GetProperty("SpriteEnabled", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        static readonly FieldInfo _jDistField = typeof(TargetBoxScriptJuicy).GetField("_distanceLabel", BindingFlags.NonPublic | BindingFlags.Instance);
        static readonly FieldInfo _jNameField = typeof(TargetBoxScriptJuicy).GetField("_nameLabel", BindingFlags.NonPublic | BindingFlags.Instance);
        static readonly FieldInfo _jSpriteField = typeof(TargetBoxScriptJuicy).GetField("_sprite", BindingFlags.NonPublic | BindingFlags.Instance);
        static readonly FieldInfo _jLockField = typeof(TargetBoxScriptJuicy).GetField("_lockSprite", BindingFlags.NonPublic | BindingFlags.Instance);
        static bool _aceBoxLogged;

        static void HideFieldComponent(FieldInfo fi, object target)
        {
            if (fi == null) return;
            try
            {
                object o = fi.GetValue(target);
                Component c = o as Component;
                if (c != null) { c.gameObject.SetActive(false); return; }
                if (o != null)
                {
                    PropertyInfo gp = o.GetType().GetProperty("gameObject");
                    GameObject go = gp != null ? gp.GetValue(o, null) as GameObject : null;
                    if (go != null) go.SetActive(false);
                }
            }
            catch { }
        }

        internal static bool AimingModeOn()
        {
            GunRingPlugin p = Instance;
            if (p == null || p._craft == null || _banned) return false;
            try
            {
                TargetingSystem ts = p._craft.TargetingSystem;
                return ts != null && (int)ts.Mode != 0;
            }
            catch { return false; }
        }

        internal static void OnTargetBoxJuicy(TargetBoxScriptJuicy box)
        {
            try
            {
                if (box == null || !CAceLabel.Value || Instance == null || !AimingModeOn()) return;
                TrackedTarget tt = box.TrackedTarget;
                if (tt == null || tt.Target == null) return;
                if (tt.Target == Instance._target) RememberFriendly(tt);
                HideFieldComponent(_jDistField, box);
                HideFieldComponent(_jNameField, box);
                if (CAceHideVanilla.Value)
                {
                    HideFieldComponent(_jSpriteField, box);
                    HideFieldComponent(_jLockField, box);
                }
                if (tt.Target == Instance._target && !_aceBoxLogged)
                {
                    _aceBoxLogged = true;
                    L.LogInfo("[GunRing] ace label active target=" + tt.Target.Name);
                }
            }
            catch { }
        }

        internal static void OnTargetBox(TargetBoxScript box)
        {
            try
            {
                if (box == null || !CAceLabel.Value || Instance == null || !AimingModeOn()) return;
                TrackedTarget tt = box.TrackedTarget;
                if (tt == null || tt.Target == null) return;
                if (tt.Target == Instance._target) RememberFriendly(tt);
                if (_boxDistProp != null)
                {
                    GameObject go = _boxDistProp.GetValue(box, null) as GameObject;
                    if (go != null) go.SetActive(false);
                }
                if (_boxNameProp != null)
                {
                    GameObject go = _boxNameProp.GetValue(box, null) as GameObject;
                    if (go != null) go.SetActive(false);
                }
                if (CAceHideVanilla.Value && _boxSpriteProp != null)
                    _boxSpriteProp.SetValue(box, false, null);
                if (tt.Target == Instance._target && !_aceBoxLogged)
                {
                    _aceBoxLogged = true;
                    L.LogInfo("[GunRing] ace label active target=" + tt.Target.Name);
                }
            }
            catch { }
        }
        static readonly Dictionary<string, Texture2D> _texCache = new Dictionary<string, Texture2D>();
        float _nextScan;
        AircraftScript _craft;
        float _bulletSpeed = -1f;
        float _spread;
        float _bulletLifetime = 3f;
        string _gunName = "";
        Target _target;
        AircraftScript _fallback;
        int _lastScanTargets = -1;
        string _scanDetail = "";
        string _selectedSource = "";
        bool _aimingOff;
        float _chScrX, _chScrY;

        // 炮弹弹道预测
        CannonScript _cannon;
        AircraftScript _cannonCraft;
        float _nextCannonScan;
        bool _cannonLogged;
        float _cannonDrop;
        float _cannonFlight;
        float _cannonLife = 10f;
        float _dbgCannonNext;
        float _cannonErrNext;
        float _aceErrNext;
        static string _aceTeamLogged;
        static Target _aceFriendlyTarget;
        static bool _aceFriendlyValue;
        static int _aceAggression = -1;
        TargetBoxScript[] _aceBoxCache;
        TargetBoxScriptJuicy[] _aceJuicyCache;
        float _nextAceBoxScan;
        Target _aceRelationTarget;
        int _aceRelationValue;
        float _aceRelationNext;
        readonly List<Target> _aceSeen = new List<Target>();

        void RefreshAceBoxCache()
        {
            if (Time.unscaledTime < _nextAceBoxScan) return;
            _nextAceBoxScan = Time.unscaledTime + 0.5f;
            _aceBoxCache = Resources.FindObjectsOfTypeAll<TargetBoxScript>();
            _aceJuicyCache = Resources.FindObjectsOfTypeAll<TargetBoxScriptJuicy>();
        }

        static void RememberFriendly(TrackedTarget tt)
        {
            try
            {
                if (tt == null || tt.Target == null) return;
                _aceFriendlyTarget = tt.Target;
                _aceFriendlyValue = tt.IsFriendly;
                _aceAggression = (int)tt.AggressionLevel;
            }
            catch { }
        }

        Target TargetOf(TargetingSystem ts)
        {
            if (ts == null) return null;
            try
            {
                Target t = ts.CurrentTarget;
                if (t != null && !t.IsDead && !IsSelf(t)) return t;
            }
            catch { }
            return null;
        }

        TargetingSystem PodSystem()
        {
            try
            {
                TargetingPodScript pod = _craft.TargetingPod;
                if (pod == null) return null;
                FieldInfo fi = typeof(TargetingPodScript).GetField("_targetingSystem", BindingFlags.NonPublic | BindingFlags.Instance);
                return fi != null ? fi.GetValue(pod) as TargetingSystem : null;
            }
            catch { return null; }
        }

        bool IsSelf(Target t)
        {
            try { return t.Player != null && t.Player.Aircraft == _craft; } catch { return false; }
        }

        bool HasTarget { get { return _target != null || _fallback != null; } }
        Vector3 TargetPos
        {
            get
            {
                if (_target != null) return _target.Position;
                if (_fallback != null) return _fallback.transform.position;
                return Vector3.zero;
            }
        }
        Vector3 TargetVel
        {
            get
            {
                if (_target != null) return _target.Velocity;
                if (_fallback != null) return VelocityOf(_fallback);
                return Vector3.zero;
            }
        }
        string TargetName { get { return _target != null ? _target.Name : (_fallback != null ? _fallback.name : "?"); } }

        static Vector3 VelocityOf(AircraftScript craft)
        {
            try
            {
                var bodies = craft.Bodies;
                if (bodies != null && bodies.Count > 0)
                {
                    object b = bodies[0];
                    if (b != null)
                    {
                        PropertyInfo vp = b.GetType().GetProperty("Velocity");
                        if (vp != null) return (Vector3)vp.GetValue(b, null);
                    }
                }
            }
            catch { }
            return Vector3.zero;
        }
        Vector3 _lead;
        float _angleToLead;
        float _leadTime;
        static float _dbgNext;

        void Awake()
        {
            L = Logger;
            CEnabled = Config.Bind("General", "Enabled", true, "启动时是否开启 / start enabled");
            // 这些 Config 项会出现在 Gravy Mod Menu 的「Gun Ring 机炮环」页里
            CDumpNow = Config.Bind("Menu", "PrintDiagnostics", false, "打勾=立刻把当前测算写进日志（自动取消勾选）/ tick to dump diagnostics now");
            CTestHit = Config.Bind("Menu", "TestHitSound", false, "打勾=播放命中音+绿叉（自动取消勾选）/ tick to play hit sound + marker");
            CMaxRange = Config.Bind("General", "MaxRange", 4000f, "预留项；机炮环现在按“预测飞行时间 < 机枪 Lifetime”筛选 / legacy; gun ring now uses predicted time vs gun lifetime");
            CMaxAngle = Config.Bind("General", "MaxAngle", 25f, "目标偏离机头多少度内才选中 / selection cone (deg)");
            CRingPx = Config.Bind("Visual", "RingRadiusPx", 14f, "准心尺寸(像素) / reticle size in px");
            CStyle = Config.Bind("Visual", "Style", 0, new ConfigDescription("0=六条线 1=四条线 2=虚线圈 3=实心圆 4=十条线 5=皇牌空战大环 / reticle style", new AcceptableValueRange<int>(0, 5)));
            CDebugDraw = Config.Bind("Visual", "DebugDraw", true, "画出目标位置(蓝)与数值，排错用 / draw target marker + numbers");
            CModeInt = Config.Bind("Visual", "Mode", 0, new ConfigDescription("0=环钉在准心旁 1=环画在提前量点(额外显示机炮指向准心) / ring placement mode", new AcceptableValueRange<int>(0, 1)));
            CMaxOffsetPx = Config.Bind("Visual", "MaxOffsetPx", 0, "Pinned 模式下环离准心最远的像素距离，0=不限（默认不限，避免环被吸在固定半径上）/ max pixel offset from crosshair, 0 = unlimited");
            CShowLine = Config.Bind("Visual", "ShowLine", true, "从准心到环画一条连线 / draw connector line");
            CIdleR = Config.Bind("Color", "IdleR", 255, "未对准颜色 R (0-255) / idle red");
            CIdleG = Config.Bind("Color", "IdleG", 255, "未对准颜色 G / idle green");
            CIdleB = Config.Bind("Color", "IdleB", 255, "未对准颜色 B / idle blue");
            CReadyR = Config.Bind("Color", "ReadyR", 89, "对准颜色 R / ready red");
            CReadyG = Config.Bind("Color", "ReadyG", 255, "对准颜色 G / ready green");
            CReadyB = Config.Bind("Color", "ReadyB", 115, "对准颜色 B / ready blue");
            CClampR = Config.Bind("Color", "ClampedR", 255, "超限颜色 R / clamped red");
            CClampG = Config.Bind("Color", "ClampedG", 158, "超限颜色 G / clamped green");
            CClampB = Config.Bind("Color", "ClampedB", 51, "超限颜色 B / clamped blue");
            CAlpha = Config.Bind("Color", "Alpha", 240, "整体不透明度 0-255 / alpha");
            CGunCross = Config.Bind("Visual", "ShowGunCross", true, "Lead 模式下额外画一个准心显示当前机炮指向 / draw gun-direction cross in Lead mode");
            CRingFollowsSpread = Config.Bind("Visual", "RingFollowsSpread", true, "环大小随机炮散布自动放大(散布/初速) / scale ring with gun dispersion");
            CRollRotation = Config.Bind("Visual", "RollRotation", false, "机炮环/炮弹环随玩家飞机滚转 1:1 / rotate reticles with aircraft roll 1:1");
            CFireSpin = Config.Bind("Visual", "FireSpin", false, "按住机枪开火时准心旋转 / spin reticle while firing");
            CSpinSpeed = Config.Bind("Visual", "SpinSpeed", 180f, new ConfigDescription("开火旋转速度(度/秒)，角度自动取余 360 / spin speed in deg/sec, angle wrapped", new AcceptableValueRange<float>(0f, 1440f)));
            CAssistEnabled = Config.Bind("Assist", "Assist", true, "辅助瞄准总开关 / aim assist on/off");
            CToggleAssist = Config.Bind("Assist", "ToggleAssist", 5f, new ConfigDescription("预瞄环与机头夹角小于该角度(度)时介入 / engage assist within this angle (deg)", new AcceptableValueRange<float>(0f, 45f)));
            CAssistMix = Config.Bind("Assist", "Mix", 0.35f, new ConfigDescription("吸附混合比例 0-1，最终 Pitch/Yaw 仍会夹到 ±1 / assist blend 0-1", new AcceptableValueRange<float>(0f, 1f)));
            CAssistMode = Config.Bind("Assist", "Mode", 0, new ConfigDescription("0=总是启用 1=只在按住开火(FireGuns)时启用 / 0=always, 1=only while firing", new AcceptableValueRange<int>(0, 1)));
            CAssistPitchP = Config.Bind("Assist", "PitchP", 1.2f, new ConfigDescription("俯仰 P 增益 / pitch proportional gain", new AcceptableValueRange<float>(0f, 10f)));
            CAssistPitchD = Config.Bind("Assist", "PitchD", 0.2f, new ConfigDescription("俯仰 D 增益 / pitch derivative gain", new AcceptableValueRange<float>(0f, 5f)));
            CAssistPitchI = Config.Bind("Assist", "PitchI", 0.05f, new ConfigDescription("俯仰 I 增益（积分输出限制 ±1）/ pitch integral gain, output clamped to ±1", new AcceptableValueRange<float>(0f, 5f)));
            CAssistYawP = Config.Bind("Assist", "YawP", 1.2f, new ConfigDescription("偏航 P 增益 / yaw proportional gain", new AcceptableValueRange<float>(0f, 10f)));
            CAssistYawD = Config.Bind("Assist", "YawD", 0.2f, new ConfigDescription("偏航 D 增益 / yaw derivative gain", new AcceptableValueRange<float>(0f, 5f)));
            CAssistYawI = Config.Bind("Assist", "YawI", 0.05f, new ConfigDescription("偏航 I 增益（积分输出限制 ±1）/ yaw integral gain, output clamped to ±1", new AcceptableValueRange<float>(0f, 5f)));
            CAssistRollP = Config.Bind("Assist", "RollP", 0.8f, new ConfigDescription("滚转 P 增益（无积分器）/ roll proportional gain, no integrator", new AcceptableValueRange<float>(0f, 10f)));
            CAssistRollD = Config.Bind("Assist", "RollD", 0.15f, new ConfigDescription("滚转 D 增益（无积分器）/ roll derivative gain, no integrator", new AcceptableValueRange<float>(0f, 5f)));
            CAssistSpeedScale = Config.Bind("Assist", "SpeedScale", true, "按 IAS/TAS 和速度做辅助灵敏度缩放 / scale assist sensitivity by IAS/TAS and speed");
            CAssistSpeedRef = Config.Bind("Assist", "SpeedRef", 200f, new ConfigDescription("灵敏度=1 的参考空速(m/s)；k=SpeedRef^2 / speed at which scale is 1", new AcceptableValueRange<float>(50f, 600f)));
            // 只在设置改变时清积分，不在每帧反复清；避免调参时出现突兀的跳变
            CAssistPitchI.SettingChanged += (s, e) => { _assistIntPitch = 0f; };
            CAssistYawI.SettingChanged += (s, e) => { _assistIntYaw = 0f; };
            CSpreadScale = Config.Bind("Visual", "SpreadScale", 100, new ConfigDescription("散布环缩放百分比 50-400 / dispersion ring scale %", new AcceptableValueRange<int>(50, 400)));
            CGreenAngle = Config.Bind("Visual", "GreenAngle", 2.5f, "变绿条件：机头与提前量点夹角≤该角度(度) / turn green when aim error <= this many degrees");
            COnlySelected = Config.Bind("General", "OnlyWhenSelected", true, "只在你自己锁定/选中目标时显示，没锁就不画 / only draw when you have a locked target");
            CHitMarker = Config.Bind("Hit", "HitMarker", true, "命中时在屏幕中间画叉 / draw hit marker on hit");
            CHitMarkerMs = Config.Bind("Hit", "HitMarkerMs", 180, "命中标记显示时长(毫秒) / hit marker duration in ms");
            CHitMarkerPx = Config.Bind("Hit", "HitMarkerPx", 26, "命中标记线段长度(像素) / hit marker arm length in px");
            CDebugLog = Config.Bind("Menu", "DebugLog", false, "写调试日志（环位置/目标血量/伤害），平时关掉 / verbose debug logging");
            CHitText = Config.Bind("Hit", "HitText", "HIT", "命中时上方显示的文字，留空则不显示 / text shown above hit marker, empty = hide");
            CHitSound = Config.Bind("Hit", "HitSound", true, "命中时播放 hit.wav / play hit.wav on hit");
            CHitVolume = Config.Bind("Hit", "HitVolume", 0.8f, "命中音音量 / hit sound volume");

            // ---- 炮弹（Cannon）预测环 ----
            // SP2 的 CannonScript 弹丸是真正的 Rigidbody（useGravity=true），
            // 初速 = 炮口点速度 + 炮管 forward * ProjectileVelocity，
            // 所以这里按理想抛体（只受重力、无空气阻力）解算落点/提前量。
            CCannonEnabled = Config.Bind("Cannon", "CannonRing", true, "炮弹预测环总开关 / cannon ballistic ring on/off");
            CCannonUseTargetRange = Config.Bind("Cannon", "UseTargetRange", true, "锁定目标时按目标距离解算；关掉则用固定 Range / solve with target range when locked");
            CCannonRange = Config.Bind("Cannon", "Range", 1500f, "没有锁定目标时的预测距离(米) / fallback prediction range (m)");
            CCannonRingPx = Config.Bind("Cannon", "RingRadiusPx", 16f, "炮弹预测环半径(像素) / cannon ring radius in px");
            CCannonR = Config.Bind("Cannon", "R", 90, "炮弹环颜色 R / cannon ring red");
            CCannonG = Config.Bind("Cannon", "G", 210, "炮弹环颜色 G / cannon ring green");
            CCannonB = Config.Bind("Cannon", "B", 255, "炮弹环颜色 B / cannon ring blue");
            CCannonGravityPct = Config.Bind("Cannon", "GravityPercent", 100, new ConfigDescription("重力百分比 0-200，0=直线(对比用) / gravity percent 0-200", new AcceptableValueRange<int>(0, 200)));
            CCannonGreenAngle = Config.Bind("Cannon", "GreenAngle", 1.5f, "炮口与所需弹道方向夹角≤该角度(度)时变绿 / turn green when cannon aim error <= this angle");
            CCannonTrace = Config.Bind("Cannon", "Trace", true, "画理想抛体轨迹虚线 / draw ballistic trail");
            CCannonTraceDots = Config.Bind("Cannon", "TraceDots", 18, new ConfigDescription("轨迹点数 4-60 / trail dots 4-60", new AcceptableValueRange<int>(4, 60)));

            // ---- Ace Combat 风格目标标签 ----
            // 隐藏游戏原来的目标框文字（可选连框一起隐藏），改成：
            // 距离(第一行) / 机体名(第二行) / 操作者名(第三行)
            CAceLabel = Config.Bind("AceLabel", "Enabled", true, "Ace Combat 风格目标标签 / Ace Combat style target labels");
            CAceHideVanilla = Config.Bind("AceLabel", "HideVanillaBox", true, "隐藏游戏原目标框并自己画框 / hide vanilla target box and draw our own");
            CAceBoxPx = Config.Bind("AceLabel", "BoxPx", 34, new ConfigDescription("目标框边长(像素) / target box size px", new AcceptableValueRange<int>(16, 120)));
            CAceDistPx = Config.Bind("AceLabel", "DistanceFontPx", 18, new ConfigDescription("距离字号(像素) / distance font px", new AcceptableValueRange<int>(8, 48)));
            CAceNamePx = Config.Bind("AceLabel", "NameFontPx", 13, new ConfigDescription("机体名/操作者字号(像素) / name font px", new AcceptableValueRange<int>(8, 36)));
            CAceR = Config.Bind("AceLabel", "R", 80, "中立/通用颜色 R / neutral or normal red");
            CAceG = Config.Bind("AceLabel", "G", 255, "中立/通用颜色 G / neutral or normal green");
            CAceB = Config.Bind("AceLabel", "B", 120, "中立/通用颜色 B / neutral or normal blue");
            CAceFriendR = Config.Bind("AceLabel", "FriendR", 120, "友方颜色 R / friendly red");
            CAceFriendG = Config.Bind("AceLabel", "FriendG", 210, "友方颜色 G / friendly green");
            CAceFriendB = Config.Bind("AceLabel", "FriendB", 255, "友方颜色 B / friendly blue");
            CAceEnemyR = Config.Bind("AceLabel", "EnemyR", 255, "敌方颜色 R / enemy red");
            CAceEnemyG = Config.Bind("AceLabel", "EnemyG", 60, "敌方颜色 G / enemy green");
            CAceEnemyB = Config.Bind("AceLabel", "EnemyB", 60, "敌方颜色 B / enemy blue");
            CAceTeamColor = Config.Bind("AceLabel", "TeamColor", true, "按敌我上色；关掉则统一用 R/G/B / colour by team");
            CAceTextMatchBox = Config.Bind("AceLabel", "TextMatchBox", true, "文字颜色跟目标框一致；0=文字统一用 R/G/B / text matches box colour");
            CAceBlink = Config.Bind("AceLabel", "Blink", true, "锁定目标闪烁 / blink the selected target");
            CAceBlinkHz = Config.Bind("AceLabel", "BlinkHz", 2, new ConfigDescription("闪烁频率(Hz)，离散开关不渐变 / blink rate in Hz", new AcceptableValueRange<int>(1, 10)));
            CAceLockFx = Config.Bind("AceLabel", "LockDiamond", true, "锁定目标框内的 45° 正方形 / 45-degree square inside the selected target box");

            CEnabled.SettingChanged += (s, e) => { _on = CEnabled.Value; L.LogInfo("[GunRing] " + (_on ? "on" : "off") + " (menu)"); };
            CDumpNow.SettingChanged += (s, e) =>
            {
                if (_suppress || !CDumpNow.Value) return;
                Dump();
                _suppress = true; CDumpNow.Value = false; _suppress = false;
            };
            CTestHit.SettingChanged += (s, e) =>
            {
                if (_suppress || !CTestHit.Value) return;
                L.LogInfo("[GunRing] menu hit test");
                MarkHit("menu-test");
                _suppress = true; CTestHit.Value = false; _suppress = false;
            };
            LoadHitSound();
            Instance = this;
            _on = CEnabled.Value;
            try
            {
                var h = new Harmony("sp2.gunring");
                h.PatchAll(typeof(BulletProbe));
                h.PatchAll(typeof(HitProbe));
                h.PatchAll(typeof(BulletHitProbe));
                h.PatchAll(typeof(DamageRecvProbe));
                h.PatchAll(typeof(DamageSyncProbe));
                h.PatchAll(typeof(TargetBoxLabelPatch));
                h.PatchAll(typeof(TargetBoxJuicyPatch));
                L.LogInfo("[GunRing] bullet probe + target label patched (distProp=" + (_boxDistProp != null) + " nameProp=" + (_boxNameProp != null) + " spriteProp=" + (_boxSpriteProp != null) + " juicy=" + (_jNameField != null) + ")");
            }
            catch (Exception e) { L.LogWarning("[GunRing] bullet probe failed: " + e.Message); }

            L.LogInfo("[GunRing] ready (menu-driven, no hotkeys)");
        }




































































































































































































        void Update()
        {
            CheckBan();
            if (_banned) return;
            if (!_on) return;

            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + 0.5f;
                Scan();
            }
            if (_craft == null || !HasTarget) return;
            SolveLead();
            WatchTargetDamage();
        }



























        GUIStyle _banStyle;





















        // 辅助瞄准：把预瞄点误差换算到飞机坐标系，Pitch/Yaw 各自 PID，
        // 叠加到玩家输入上，最后仍夹在 ±1。LateUpdate 做，尽量不抢玩家原始输入。
        float _lastAssistYaw, _lastAssistPitch;
        float _lastAssistRollErr;
        float _assistIntYaw, _assistIntPitch;
        float _lastAssistRoll = 9999f;
        float _fireSpinAngle;

        // 按住机枪开火时准心旋转；角度用 Repeat 限制在 [0,360)，松开立即归零














































































































































        // 直接盯锁定目标的机体血量：只要它在掉，就说明我们打中了。
        // 单人模式下网络伤害回调整条不存在，这条是唯一可靠的判据。
        AircraftScript _watched;
        float _watchedDmg;
        float _nextWatch;

        void WatchTargetDamage()
        {
            if (_target == null) { _watched = null; return; }
            if (_watched == null || Time.unscaledTime >= _nextWatch)
            {
                _nextWatch = Time.unscaledTime + 0.5f;
                AircraftScript found = null;
                float best = 120f;
                try
                {
                    AircraftScript[] all = UnityEngine.Object.FindObjectsOfType<AircraftScript>();
                    Vector3 tp = TargetPos;
                    for (int i = 0; i < all.Length; i++)
                    {
                        AircraftScript a = all[i];
                        if (a == null || a == _craft) continue;
                        Vector3 ap = a.transform.position;
                        try { if (a.Bodies != null && a.Bodies.Count > 0) ap = a.Bodies[0].transform.position; } catch { }
                        float d = Vector3.Distance(ap, tp);
                        if (d < best) { best = d; found = a; }
                    }
                }
                catch { }
                _watched = found;
                if (_watched != null)
                {
                    _watchedDmg = _watched.Damage;
                    if (CDebugLog.Value) L.LogInfo("[GunRing] watch target=" + _watched.name + " dist=" + best.ToString("0") + " dmg=" + _watchedDmg.ToString("0.00"));
                }
            }
            if (_watched == null) return;
            float now = _watched.Damage;
            if (now > _watchedDmg + 0.0001f)
            {
                if (CDebugLog.Value) L.LogInfo("[GunRing] damage rise " + _watchedDmg.ToString("0.00") + " -> " + now.ToString("0.00"));
                _watchedDmg = now;
                MarkHit(_watched.name);
            }
            else if (now < _watchedDmg) _watchedDmg = now;
        }

        static FlightSceneScript Scene()
        {
            try { return FlightSceneScript.Instance; } catch { return null; }
        }

        void Scan()
        {
            var sc = Scene();
            _craft = null;
            if (sc != null && sc.LocalPlayer != null) _craft = sc.LocalPlayer.Aircraft;
            if (_craft == null)
            {
                var all = Resources.FindObjectsOfTypeAll<AircraftScript>();
                for (int i = 0; i < all.Length; i++)
                    if (all[i] != null && all[i].IsPrimaryLocalPlayer) { _craft = all[i]; break; }
            }
            PickGun();

            _target = null;
            // 第一优先：玩家自己选/锁定的目标（HUD 上那个括号）
            if (_craft != null)
            {
                try
                {
                    TargetingSystem ts = _craft.TargetingSystem;
                    bool aimingOn = ts == null || (int)ts.Mode != 0;
                    if (aimingOn) { Target a = TargetOf(ts); if (a != null) { _target = a; _selectedSource = "player"; } }
                    // 瞄准吊舱自带一套 TargetingSystem，锁定船只/地面目标常走这条
                    if (_target == null)
                    {
                        Target b = TargetOf(PodSystem());
                        if (b != null) { _target = b; _selectedSource = "pod"; }
                    }
                    if (_target == null && !aimingOn)
                    {
                        _selectedSource = "off";
                        _aimingOff = true;
                    }
                }
                catch { }
                if (_target == null)
                {
                    try
                    {
                        Target ct = _craft.Target;
                        if (ct != null && !ct.IsDead && !IsSelf(ct)) { _target = ct; _selectedSource = "craftTarget"; }
                    }
                    catch { }
                }
            }
            if (_selectedSource != "off") _selectedSource = _target != null ? "player" : "";
            if (_target != null) _aimingOff = false;
            Camera viewCam = ViewCam();
            Vector3 aimFwd = viewCam != null ? viewCam.transform.forward : CraftXform().forward;
            _scanDetail = "";
            if (_target == null && sc != null && sc.TargetRegistry != null && _craft != null)
            {
                float best = float.MaxValue;
                var list = sc.TargetRegistry.Targets;
                for (int i = 0; i < list.Count; i++)
                {
                    Target t = list[i];
                    if (t == null || t.IsDead) continue;
                    if (t.Player != null && t.Player.Aircraft == _craft) continue; // skip self
                    Vector3 d = t.Position - CraftPos();
                    float dist = d.magnitude;
                    if (dist < 20f) continue;
                    float reach = _bulletSpeed * Mathf.Max(0.1f, _bulletLifetime);
                    if (_bulletSpeed > 10f && dist > reach) continue;
                    float angCam = Vector3.Angle(aimFwd, d);
                    float angNose = Vector3.Angle(CraftXform().forward, d);
                    _scanDetail += "  " + t.Name + " d=" + dist.ToString("0") + " angCam=" + angCam.ToString("0") + " angNose=" + angNose.ToString("0") + " t=" + t.TeamId + ";";
                    // 不再硬性踢掉视角外的目标：取"离准星最近"的那个，同队加惩罚
                    float score = angCam + (t.TeamId == _craft.TeamId ? 25f : 0f) + (angCam > CMaxAngle.Value ? 8f : 0f);
                    if (score < best) { best = score; _target = t; }
                }
                if (_target != null) _selectedSource = "auto";
            }
            _lastScanTargets = sc != null && sc.TargetRegistry != null ? sc.TargetRegistry.Targets.Count : -1;

            // 注册表里没有可用目标时，退一步直接扫场景里的飞机（只要锁定了目标就不猜）
            if (_target == null && _craft != null && !COnlySelected.Value && !_aimingOff)
            {
                _fallback = null;
                float best = float.MaxValue;
                Vector3 fwd = CraftXform().forward;
                var all = Resources.FindObjectsOfTypeAll<AircraftScript>();
                for (int i = 0; i < all.Length; i++)
                {
                    AircraftScript other = all[i];
                    if (other == null || other == _craft) continue;
                    Vector3 d = other.transform.position - CraftPos();
                    float dist = d.magnitude;
                    if (dist < 20f) continue;
                    float reach = _bulletSpeed * Mathf.Max(0.1f, _bulletLifetime);
                    if (_bulletSpeed > 10f && dist > reach) continue;
                    float ang = Vector3.Angle(fwd, d);
                    if (ang < best) { best = ang; _fallback = other; }
                }
            }
            PickCannon();
        }

        // ---- 炮弹（CannonScript）扫描 ----
        // 取机上 ProjectileVelocity 最快的那门炮作为主炮；炮的弹丸是 Rigidbody，
        // 初速 = 炮口点速度 + transform.forward * ProjectileVelocity。

































































        Component _gun;
        Transform _muzzleT;

        void PickGun()
        {
            _bulletSpeed = -1f; _gunName = ""; _gun = null; _muzzleT = null; _spread = 0f; _bulletLifetime = 3f;
            if (_craft == null || _craft.GunManager == null) return;
            try
            {
                FieldInfo fi = typeof(CraftGunManager).GetField("_guns", BindingFlags.NonPublic | BindingFlags.Instance);
                if (fi == null) return;
                var list = fi.GetValue(_craft.GunManager) as IEnumerable;
                if (list == null) return;
                foreach (object g in list)
                {
                    if (g == null) continue;
                    float sp = SpeedOf(g);
                    if (sp <= _bulletSpeed) continue;
                    _bulletSpeed = sp;
                    _gun = g as Component;
                    _gunName = g.GetType().Name;
                    _muzzleT = MuzzleOf(g);
                    _spread = SpreadOf(g);
                    _bulletLifetime = LifetimeOf(g);
                }
            }
            catch (Exception e) { L.LogWarning("[GunRing] gun scan: " + e.Message); }
        }

        // 子弹生成时 velocity 里含 Random.insideUnitSphere * Spread，所以散布是"速度单位"
        static float SpreadOf(object g)
        {
            try
            {
                Type t = g.GetType();
                PropertyInfo p = t.GetProperty("Spread", BindingFlags.Public | BindingFlags.Instance);
                if (p != null) return Convert.ToSingle(p.GetValue(g, null));
                FieldInfo f = t.GetField("Spread", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null) return Convert.ToSingle(f.GetValue(g));
                PropertyInfo gp = t.GetProperty("Gun");
                if (gp != null)
                {
                    object data = gp.GetValue(g, null);
                    if (data != null)
                    {
                        Type dt = data.GetType();
                        PropertyInfo sp = dt.GetProperty("Spread", BindingFlags.Public | BindingFlags.Instance);
                        if (sp != null) return Convert.ToSingle(sp.GetValue(data, null));
                        FieldInfo sfd = dt.GetField("Spread", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (sfd != null) return Convert.ToSingle(sfd.GetValue(data));
                        foreach (MemberInfo mi in dt.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                            if (mi.Name.IndexOf("pread", StringComparison.OrdinalIgnoreCase) >= 0)
                                L.LogInfo("[GunRing] spread-ish member: " + dt.Name + "." + mi.Name + " (" + mi.MemberType + ")");
                    }
                }
            }
            catch (Exception e) { L.LogWarning("[GunRing] spread: " + e.Message); }
            return 0f;
        }

        // RotatingGunScript exposes BulletSpeed; fixed GunScript exposes Gun.MuzzleVelocity
        static float SpeedOf(object g)
        {
            Type t = g.GetType();
            PropertyInfo p = t.GetProperty("BulletSpeed");
            if (p != null) return Convert.ToSingle(p.GetValue(g, null));
            PropertyInfo gp = t.GetProperty("Gun");
            if (gp != null)
            {
                object data = gp.GetValue(g, null);
                if (data != null)
                {
                    PropertyInfo mv = data.GetType().GetProperty("MuzzleVelocity");
                    if (mv != null) return Convert.ToSingle(mv.GetValue(data, null));
                }
            }
            return -1f;
        }

        // 机枪子弹的存活秒数（GunData.DefaultLifetime=3；GunScript.Gun.Lifetime）
        static float CraftFloat(AircraftScript ac, string name)
        {
            try
            {
                PropertyInfo p = ac.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (p != null) return Convert.ToSingle(p.GetValue(ac, null));
            }
            catch { }
            return 0f;
        }

        static float LifetimeOf(object g)
        {
            try
            {
                Type t = g.GetType();
                PropertyInfo p = t.GetProperty("BulletLifetime", BindingFlags.Public | BindingFlags.Instance);
                if (p != null) return Mathf.Max(0.1f, Convert.ToSingle(p.GetValue(g, null)));
                p = t.GetProperty("Lifetime", BindingFlags.Public | BindingFlags.Instance);
                if (p != null) return Mathf.Max(0.1f, Convert.ToSingle(p.GetValue(g, null)));
                PropertyInfo gp = t.GetProperty("Gun");
                if (gp != null)
                {
                    object data = gp.GetValue(g, null);
                    if (data != null)
                    {
                        PropertyInfo lp = data.GetType().GetProperty("Lifetime", BindingFlags.Public | BindingFlags.Instance);
                        if (lp != null) return Mathf.Max(0.1f, Convert.ToSingle(lp.GetValue(data, null)));
                    }
                }
            }
            catch { }
            return 3f;
        }

        static Transform MuzzleOf(object g)        {
            Type t = g.GetType();
            PropertyInfo bp = t.GetProperty("BulletStartPositions");
            if (bp != null)
            {
                var arr = bp.GetValue(g, null) as Transform[];
                if (arr != null)
                    for (int i = 0; i < arr.Length; i++)
                        if (arr[i] != null) return arr[i];
            }
            FieldInfo fi = t.GetField("_bulletStartPoint", BindingFlags.NonPublic | BindingFlags.Instance);
            if (fi != null) return fi.GetValue(g) as Transform;
            Component c = g as Component;
            return c != null ? c.transform : null;
        }

        // bullets spawn with velocity = muzzleForward*MuzzleVelocity + owner body velocity
        Vector3 OwnVelocity()
        {
            try
            {
                // AircraftScript.Velocity 才是整机速度；Bodies[0] 可能是轮子/局部刚体
                Vector3 cv = _craft.Velocity;
                if (cv.sqrMagnitude > 0.1f) return cv;
            }
            catch { }
            try
            {
                PropertyInfo bp = typeof(AircraftScript).GetProperty("Bodies");
                if (bp != null)
                {
                    var bodies = bp.GetValue(_craft, null) as IList;
                    if (bodies != null && bodies.Count > 0)
                    {
                        object b = bodies[0];
                        if (b != null)
                        {
                            PropertyInfo vp = b.GetType().GetProperty("Velocity");
                            if (vp != null) return (Vector3)vp.GetValue(b, null);
                        }
                    }
                }
            }
            catch { }
            return Vector3.zero;
        }

        // craft.transform 在 SP2 里经常是过期值，优先用机体朝向节点
        Transform CraftXform()
        {
            try
            {
                if (_craft.OrientedCenterOfMassRigidBodies != null) return _craft.OrientedCenterOfMassRigidBodies;
            }
            catch { }
            return _craft.transform;
        }

        // 飞机相对世界地平线的滚转角，绕机头轴算；用于把机炮环按 1:1 跟着滚
        float CraftRollDeg()
        {
            try
            {
                Transform tr = CraftXform();
                Vector3 f = tr.forward;
                Vector3 refUp = Vector3.ProjectOnPlane(Vector3.up, f);
                Vector3 up = Vector3.ProjectOnPlane(tr.up, f);
                if (refUp.sqrMagnitude < 0.0001f || up.sqrMagnitude < 0.0001f) return 0f;
                return -Vector3.SignedAngle(refUp.normalized, up.normalized, f);
            }
            catch { return 0f; }
        }

        Vector3 CraftPos()
        {
            return CraftXform().position;
        }

        Vector3 MuzzlePos()
        {
            if (_muzzleT != null) return _muzzleT.position;
            if (_gun != null) return _gun.transform.position;
            return CraftPos();
        }

        static AudioClip _hitClip;
        static AudioSource _hitSrc;
        static bool _hitLogged;

        void LoadHitSound()
        {
            try
            {
                string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string path = Path.Combine(dir, "hit.wav");
                if (!File.Exists(path)) { L.LogWarning("[GunRing] hit.wav missing at " + path); return; }
                _hitClip = WavToClip(path);
                if (_hitClip != null) L.LogInfo("[GunRing] hit.wav loaded: " + _hitClip.samples + " samples @" + _hitClip.frequency + "Hz");
            }
            catch (Exception e) { L.LogWarning("[GunRing] hit.wav load failed: " + e.Message); }
        }

        static AudioClip WavToClip(string path)
        {
            byte[] b = File.ReadAllBytes(path);
            int channels = BitConverter.ToInt16(b, 22);
            int rate = BitConverter.ToInt32(b, 24);
            int bits = BitConverter.ToInt16(b, 34);
            if (bits != 16) throw new Exception("only 16-bit pcm supported, got " + bits);
            int dataStart = 12;
            int dataLen = 0;
            while (dataStart + 8 < b.Length)
            {
                string id = Encoding.ASCII.GetString(b, dataStart, 4);
                int len = BitConverter.ToInt32(b, dataStart + 4);
                if (id == "data") { dataStart += 8; dataLen = Math.Min(len, b.Length - dataStart); break; }
                dataStart += 8 + len + (len & 1);
            }
            if (dataLen <= 0) throw new Exception("no data chunk");
            int frames = dataLen / (2 * channels);
            float[] mono = new float[frames];
            for (int i = 0; i < frames; i++)
            {
                float acc = 0f;
                for (int c = 0; c < channels; c++)
                    acc += BitConverter.ToInt16(b, dataStart + (i * channels + c) * 2) / 32768f;
                mono[i] = acc / channels;
            }
            AudioClip clip = AudioClip.Create("sp2_hitmarker", frames, 1, rate, false);
            clip.SetData(mono, 0);
            return clip;
        }

        static void PlayHitSound()
        {
            if (!CHitSound.Value || _hitClip == null) return;
            if (_hitSrc == null)
            {
                var go = new GameObject("SP2GunRingAudio");
                DontDestroyOnLoad(go);
                _hitSrc = go.AddComponent<AudioSource>();
                _hitSrc.playOnAwake = false;
                _hitSrc.spatialBlend = 0f;
            }
            _hitSrc.PlayOneShot(_hitClip, Mathf.Clamp01(CHitVolume.Value));
        }

        static void MarkHit(string what)
        {
            _hitUntil = Time.unscaledTime + Mathf.Max(20, CHitMarkerMs.Value) / 1000f;
            PlayHitSound();
            if (!_hitLogged) { _hitLogged = true; L.LogInfo("[GunRing] hit detected: " + what); }
            _hitCount++;
        }



        static Color Rgb(ConfigEntry<int> r, ConfigEntry<int> g, ConfigEntry<int> b)
        {
            float a = Mathf.Clamp01(CAlpha.Value / 255f);
            return new Color(Mathf.Clamp01(r.Value / 255f), Mathf.Clamp01(g.Value / 255f), Mathf.Clamp01(b.Value / 255f), a);
        }

        static Vector3 _lastBulletDir;
        static bool _hasBulletDir;

        Vector3 PartForward()
        {
            try
            {
                if (_gun != null)
                {
                    // 子弹真实生成方向 = 机枪所在的 PartScript.transform.forward
                    // （XML 里那个 x=上下偏 / y=左右偏 的旋转已经烘进这个 transform 了）
                    PropertyInfo pp = _gun.GetType().GetProperty("PartScript", BindingFlags.Public | BindingFlags.Instance);
                    object ps = pp != null ? pp.GetValue(_gun, null) : null;
                    Component comp = ps as Component;
                    if (comp != null) return comp.transform.forward;
                }
            }
            catch { }
            if (_muzzleT != null) return _muzzleT.forward;
            if (_gun != null) return _gun.transform.forward;
            return CraftXform().forward;
        }

        Vector3 AimDir()
        {
            return PartForward();
        }

        void SolveLead()
        {
            if (_bulletSpeed <= 10f) return;
            Vector3 muzzle = MuzzlePos();
            Vector3 p = TargetPos, v = TargetVel - OwnVelocity();
            float t = 0f;
            for (int i = 0; i < 4; i++)
            {
                Vector3 lead = p + v * t;
                t = Vector3.Distance(muzzle, lead) / _bulletSpeed;
            }
            _lead = p + v * t;
            _leadTime = t;
            _angleToLead = Vector3.Angle(AimDir(), _lead - muzzle);
        }

        Camera ManagerCam()
        {
            try
            {
                var sc = Scene();
                if (sc != null && sc.CameraScript != null) return sc.CameraScript.MainCamera;
            }
            catch { }
            return null;
        }

        Camera ViewCam()
        {
            Camera c = ManagerCam();
            if (c != null) return c;
            c = Camera.main;
            if (c != null) return c;
            var cams = Resources.FindObjectsOfTypeAll<Camera>();
            Camera bestCam = null;
            for (int i = 0; i < cams.Length; i++)
            {
                var cc = cams[i];
                if (cc == null || cc.cameraType != CameraType.Game) continue;
                if (bestCam == null || cc.depth > bestCam.depth) bestCam = cc;
            }
            return bestCam;
        }

        // ---- 理想抛体：p(t) = p0 + v0*t + 0.5*g*t^2 ----
        static Vector3 Ballistic(Vector3 p0, Vector3 v0, Vector3 g, float t)
        {
            return p0 + v0 * t + 0.5f * g * (t * t);
        }






















        // 求击中移动目标所需的炮管方向：
        //   T + Vt*t = P0 + Vown*t + f*Vp*t + 0.5*g*t^2
        // => |T-P0+(Vt-Vown)t-0.5*g*t^2| = Vp*t







































        // 没有锁定目标时：求炮弹飞到固定射程 range 的飞行时间
































        // ---- Ace Combat 风格目标标签 ----













































































































        // 返回 AggressionLevel：1=Hostile，2=Neutral，3=Friendly；0=Unknown


































        GUIStyle _aceDistStyle, _aceNameStyle, _aceDistShadow, _aceNameShadow;
        bool _aceStylesReady;
























































































































































































































































































































        void OnGUI()
        {
            if (_banned) { DrawBanScreen(); return; }
            if (!_on || _craft == null) return;
            Camera cam = ViewCam();
            if (cam == null) return;

            DrawAceLabelSafe(cam);
            DrawCannonSafe(cam);

            if (!HasTarget || _bulletSpeed <= 10f) return;
            if (_aimingOff) return;
            if (COnlySelected.Value && _selectedSource != "player" && _selectedSource != "pod" && _selectedSource != "craftTarget") return;
            {
                Vector3 aimPt0 = MuzzlePos() + AimDir() * 4000f;
                Vector3 ch0 = cam.WorldToScreenPoint(aimPt0);
                if (ch0.z > 0f) { _chScrX = ch0.x; _chScrY = Screen.height - ch0.y; }
            }
            Vector3 sp = cam.WorldToScreenPoint(_lead);
            if (sp.z <= 0f) return;
            float x = sp.x, y = Screen.height - sp.y;
            bool clamped = false;
            float chx = 0f, chy = 0f;
            bool pinned = CModeInt.Value == 0;
            if (pinned)
            {
                Vector3 aimPt = MuzzlePos() + AimDir() * 4000f;
                Vector3 chS = cam.WorldToScreenPoint(aimPt);
                if (chS.z > 0f)
                {
                    chx = chS.x; chy = Screen.height - chS.y;
                    // 环 = 准心 + (敌机屏幕位置 − 提前量点屏幕位置)：
                    // 这就是「把机头拉到提前量点后，敌机会挪到哪」，一阶近似、稳定、任何视角都不会爆
                    Vector3 spT = cam.WorldToScreenPoint(TargetPos);
                    Vector3 spL = cam.WorldToScreenPoint(_lead);
                    if (spT.z > 0f && spL.z > 0f)
                    {
                        float tgx = spT.x, tgy = Screen.height - spT.y;
                        float ldx = spL.x, ldy = Screen.height - spL.y;
                        x = chx + (tgx - ldx);
                        y = chy + (tgy - ldy);
                        if (CDebugLog.Value && Time.unscaledTime >= _dbgNext)
                        {
                            _dbgNext = Time.unscaledTime + 1f;
                            L.LogInfo("[GunRing] dbg ch=" + chx.ToString("0") + "," + chy.ToString("0")
                                + " ring=" + x.ToString("0") + "," + y.ToString("0")
                                + " tgt=" + tgx.ToString("0") + "," + tgy.ToString("0")
                                + " lead=" + ldx.ToString("0") + "," + ldy.ToString("0")
                                + " d=" + Vector3.Distance(CraftPos(), TargetPos).ToString("0")
                                + " leadOff=" + Vector3.Distance(_lead, TargetPos).ToString("0")
                                + " ang=" + _angleToLead.ToString("0.0")
                                + " gunFwd=" + AimDir().ToString() + " camFwd=" + cam.transform.forward.ToString());
                        }
                    }
                    else { x = chx; y = chy; }
                    float dx = x - chx, dy = y - chy;
                    float len = Mathf.Sqrt(dx * dx + dy * dy);
                    float maxLen = CMaxOffsetPx.Value > 0 ? CMaxOffsetPx.Value : 1e6f;
                    if (len > maxLen && len > 0.001f)
                    {
                        dx *= maxLen / len; dy *= maxLen / len; clamped = true;
                    }
                    x = chx + dx; y = chy + dy;
                    if (CShowLine.Value && !clamped)
                    {
                        float angL = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                        float lenL = Mathf.Sqrt(dx * dx + dy * dy);
                        Matrix4x4 mtx = GUI.matrix;
                        GUIUtility.RotateAroundPivot(angL, new Vector2(chx, chy));
                        GUI.color = new Color(1f, 1f, 1f, 0.5f);
                        GUI.DrawTexture(new Rect(chx, chy - 0.75f, lenL, 1.5f), Texture2D.whiteTexture);
                        GUI.matrix = mtx;
                    }
                }
            }

            float dist = Vector3.Distance(CraftPos(), TargetPos);
            float baseRadius = StyleName() == "AceRing" ? Mathf.Max(42f, CRingPx.Value * 3f) : CRingPx.Value;
            float radius = baseRadius + Mathf.Clamp(1200f / Mathf.Max(50f, dist), 0f, 12f);
            // 散布：子弹从炮口散开半角 atan(Spread / MuzzleVelocity)，屏幕半径 = 半角 * 每弧度像素
            if (CRingFollowsSpread.Value && _spread > 0.0001f && _bulletSpeed > 10f)
            {
                float halfRad = Mathf.Atan2(_spread, _bulletSpeed);
                float pxPerRad = (Screen.height * 0.5f) / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
                float spreadPx = halfRad * pxPerRad * (CSpreadScale.Value / 100f);
                radius = Mathf.Max(radius, Mathf.Min(spreadPx, 400f));
            }
            bool good = _angleToLead <= CGreenAngle.Value;
            int px = Mathf.RoundToInt(radius * 2f);

            Color old = GUI.color;

            GUI.color = clamped ? Rgb(CClampR, CClampG, CClampB)
                                 : (good ? Rgb(CReadyR, CReadyG, CReadyB) : Rgb(CIdleR, CIdleG, CIdleB));
            Matrix4x4 ringMtx = GUI.matrix;
            float ringRot = (CRollRotation.Value ? CraftRollDeg() : 0f) + (CFireSpin.Value ? _fireSpinAngle : 0f);
            if (Mathf.Abs(ringRot) > 0.001f) GUIUtility.RotateAroundPivot(ringRot, new Vector2(x, y));
            GUI.DrawTexture(new Rect(x - radius, y - radius, px, px), Reticle(StyleName(), px));
            GUI.matrix = ringMtx;
            GUI.color = old;

            // 命中反馈：4 条短线围一个空心 X（中间不交错，像 FPS 的 hitmarker）+ 上方 Hit 字样
            if (CHitMarker.Value && Time.unscaledTime < _hitUntil)
            {
                // 准心在画面里就画在准心上；机炮指向偏出画面时退回画在环上，别让反馈飞到屏幕外
                float hx = _chScrX, hy = _chScrY;
                if (hx < 0f || hy < 0f || hx > Screen.width || hy > Screen.height) { hx = x; hy = y; }
                if (hx <= 0f && hy <= 0f) { hx = Screen.width * 0.5f; hy = Screen.height * 0.5f; }
                float hp = Mathf.Max(6f, CHitMarkerPx.Value);   // 线段长度
                float gap = Mathf.Max(3f, hp * 0.45f);          // 中间空隙
                float th = 2.5f;
                GUI.color = Rgb(CReadyR, CReadyG, CReadyB);
                for (int i = 0; i < 4; i++)
                {
                    float ang = 45f + i * 90f;
                    float rad = ang * Mathf.Deg2Rad;
                    Vector2 dir = new Vector2(Mathf.Cos(rad), -Mathf.Sin(rad));   // GUI 的 y 向下
                    Vector2 p0 = new Vector2(hx, hy) + dir * gap;
                    Matrix4x4 mmx = GUI.matrix;
                    GUIUtility.RotateAroundPivot(-ang, p0);
                    GUI.DrawTexture(new Rect(p0.x, p0.y - th * 0.5f, hp, th), Texture2D.whiteTexture);
                    GUI.matrix = mmx;
                }
                if (CHitText.Value != null && CHitText.Value.Length > 0)
                {
                    GUIStyle hs = new GUIStyle(GUI.skin.label);
                    hs.fontSize = 13;
                    hs.fontStyle = FontStyle.Bold;
                    hs.alignment = TextAnchor.MiddleCenter;
                    hs.normal.textColor = Rgb(CReadyR, CReadyG, CReadyB);
                    GUI.Label(new Rect(hx - 60f, hy - gap - hp - 20f, 120f, 18f), CHitText.Value, hs);
                }
                GUI.color = old;
            }

            // Lead 模式：额外画一个准心，表示当前机炮实际指向（子弹现在会飞到哪）
            if (!pinned && CGunCross.Value)
            {
                Color oldG = GUI.color;
                GUI.color = Rgb(CIdleR, CIdleG, CIdleB);
                float gx = _chScrX, gy = _chScrY;
                GUI.DrawTexture(new Rect(gx - 10f, gy - 1f, 20f, 2f), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(gx - 1f, gy - 10f, 2f, 20f), Texture2D.whiteTexture);
                GUI.color = oldG;
            }

            if (CDebugDraw.Value)
            {
                var st0 = new GUIStyle(GUI.skin.label);
                st0.fontSize = 11;
                st0.normal.textColor = new Color(0.5f, 0.75f, 1f, 0.95f);
                Vector3 tsp = cam.WorldToScreenPoint(TargetPos);
                if (tsp.z > 0f)
                {
                    GUI.color = new Color(0.5f, 0.75f, 1f, 0.9f);
                    float tx = tsp.x, ty = Screen.height - tsp.y;
                    GUI.DrawTexture(new Rect(tx - 8f, ty - 1f, 16f, 2f), Texture2D.whiteTexture);
                    GUI.DrawTexture(new Rect(tx - 1f, ty - 8f, 2f, 16f), Texture2D.whiteTexture);
                    GUI.color = old;
                    GUI.Label(new Rect(tx + 10f, ty - 8f, 220f, 16f), "TGT d=" + dist.ToString("0") + "m t=" + (Vector3.Distance(MuzzlePos(), TargetPos) / Mathf.Max(1f, _bulletSpeed)).ToString("0.00") + "s spd=" + _bulletSpeed.ToString("0"), st0);
                }
                GUI.Label(new Rect(x + radius + 4f, y + 8f, 300f, 16f), (pinned ? "Pinned" : "Lead") + " src=" + _selectedSource + " ang=" + _angleToLead.ToString("0.0") + "°" + (clamped ? " CLAMPED" : "") + " tgt=" + TargetName, st0);
            }

            if (good)
            {
                var st = new GUIStyle(GUI.skin.label);
                st.fontSize = 12;
                st.normal.textColor = new Color(0.35f, 1f, 0.45f, 0.95f);
                GUI.Label(new Rect(x + radius + 4f, y - 8f, 120f, 18f), "READY", st);
            }
        }

        internal static void OnBullet(Vector3 velocity)
        {
            if (velocity.sqrMagnitude > 100f) { _lastBulletDir = velocity.normalized; _hasBulletDir = true; }
            if (!_bulletLogged)
            {
                _bulletLogged = true;
                Vector3 pf = Vector3.zero;
                try { var p0 = Instance; if (p0 != null) pf = p0.PartForward(); } catch { }
                L.LogInfo("[GunRing] bullet dir=" + velocity.normalized + " | partForward=" + pf + " | angle=" + Vector3.Angle(pf, velocity.normalized).ToString("0.00") + "°");
            }
            // 几何上算一下这条弹道离锁定目标有多近，用来判断到底有没有打中
            var p = Instance;
            if (p == null || p._target == null || _nearMissLogged >= 8) return;
            try
            {
                Vector3 muzzle = p.MuzzlePos();
                Vector3 dir = velocity.normalized;
                Vector3 toT = p.TargetPos - muzzle;
                float t = Vector3.Dot(toT, dir);
                if (t <= 0f) return;
                float miss = (toT - dir * t).magnitude;
                if (miss < 12f)
                {
                    _nearMissLogged++;
                    L.LogInfo("[GunRing] shot#" + _nearMissLogged + " miss=" + miss.ToString("0.0") + "m d=" + toT.magnitude.ToString("0") + " target=" + p.TargetName);
                }
            }
            catch { }
        }
        static bool _bulletLogged;
        static int _nearMissLogged;

        void Dump()
        {
            L.LogInfo("[GunRing] registry targets=" + _lastScanTargets + " team=" + (_craft != null ? _craft.TeamId.ToString() : "?") + " selected=" + (_target != null ? _target.Name : "null") + " src=" + _selectedSource + " fb=" + (_fallback != null ? _fallback.name : "null") + " hits=" + _hitCount);
            if (_craft != null)
            {
                try
                {
                    TargetingSystem ts0 = _craft.TargetingSystem;
                    L.LogInfo("[GunRing] targetingSystem=" + (ts0 != null ? "ok targets=" + ts0.TargetCount : "null") +
                              " currentTarget=" + (ts0 != null && ts0.CurrentTarget != null ? ts0.CurrentTarget.Name : "null") +
                              " craftTarget=" + (_craft.Target != null ? _craft.Target.Name : "null"));
                }
                catch (Exception e) { L.LogInfo("[GunRing] targetingSystem read failed: " + e.Message); }
            }
            if (_scanDetail.Length > 0) L.LogInfo("[GunRing] candidates:" + _scanDetail);
            var sc0 = Scene();
            if (sc0 != null && sc0.TargetRegistry != null)
            {
                var tl = sc0.TargetRegistry.Targets;
                for (int i = 0; i < tl.Count && i < 12; i++)
                {
                    Target t = tl[i];
                    if (t == null) continue;
                    L.LogInfo("[GunRing]   target[" + i + "] " + t.Name + " team=" + t.TeamId + " dead=" + t.IsDead +
                              " d=" + (_craft != null ? Vector3.Distance(CraftPos(), t.Position).ToString("0") : "?") +
                              " vel=" + t.Velocity.magnitude.ToString("0"));
                }
            }
            Camera cam = ViewCam();
            string camInfo = cam == null ? "cam=null" : ("cam=" + cam.name + " " + cam.pixelWidth + "x" + cam.pixelHeight);
            string leadInfo = "";
            if (cam != null && _target != null)
            {
                Vector3 lp = cam.WorldToScreenPoint(_lead);
                Vector3 tp = cam.WorldToScreenPoint(_target.Position);
                leadInfo = " leadScr=" + lp.x.ToString("0") + "," + lp.y.ToString("0") + " tgtScr=" + tp.x.ToString("0") + "," + tp.y.ToString("0") + " leadOff=" + Vector3.Distance(_lead, _target.Position).ToString("0") + "m ownVel=" + OwnVelocity().magnitude.ToString("0");
            }
            L.LogInfo("[GunRing] " + camInfo + leadInfo);
            {
                Camera mcam = ManagerCam();
                Camera ucam = Camera.main;
                L.LogInfo("[GunRing] managerCam=" + (mcam != null ? mcam.name + "@" + mcam.transform.position.ToString("0") + " fov=" + mcam.fieldOfView.ToString("0") : "null") +
                          " camMain=" + (ucam != null ? ucam.name + "@" + ucam.transform.position.ToString("0") + " fov=" + ucam.fieldOfView.ToString("0") : "null") +
                          " same=" + (mcam == ucam));
            }
            if (_craft != null)
            {
                Vector3 ov = OwnVelocity();
                L.LogInfo("[GunRing] ownVel=" + ov + " fwd=" + CraftXform().forward + " angOwnFwd=" + Vector3.Angle(ov, CraftXform().forward).ToString("0.0"));
            }
            L.LogInfo("[GunRing] craft=" + (_craft != null ? _craft.name : "null") +
                      " gun=" + _gunName + " speed=" + _bulletSpeed.ToString("0") + " spread=" + _spread.ToString("0.00") +
                      " target=" + (_target != null ? _target.Name + " d=" + Vector3.Distance(CraftPos(), _target.Position).ToString("0") : "null") +
                      " angleToLead=" + _angleToLead.ToString("0.0") +
                      " | cannon=" + (_cannon != null ? _cannon.name + " speed=" + _cannon.ProjectileVelocity.ToString("0") + " life=" + _cannonLife.ToString("0.0") : "none"));
        }
    }
}