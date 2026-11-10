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

    [BepInPlugin("sp2.gunring", "SP2 Gun Lead Ring", "0.8.0")]
    public class GunRingPlugin : BaseUnityPlugin
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
        static readonly Dictionary<string, Texture2D> _texCache = new Dictionary<string, Texture2D>();
        float _nextScan;
        AircraftScript _craft;
        float _bulletSpeed = -1f;
        float _spread;
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
        static float _dbgNext;

        void Awake()
        {
            L = Logger;
            CEnabled = Config.Bind("General", "Enabled", true, "启动时是否开启 / start enabled");
            // 这些 Config 项会出现在 Gravy Mod Menu 的「Gun Ring 机炮环」页里
            CDumpNow = Config.Bind("Menu", "PrintDiagnostics", false, "打勾=立刻把当前测算写进日志（自动取消勾选）/ tick to dump diagnostics now");
            CTestHit = Config.Bind("Menu", "TestHitSound", false, "打勾=播放命中音+绿叉（自动取消勾选）/ tick to play hit sound + marker");
            CMaxRange = Config.Bind("General", "MaxRange", 4000f, "只看这个距离内的目标 / max target range (m)");
            CMaxAngle = Config.Bind("General", "MaxAngle", 25f, "目标偏离机头多少度内才选中 / selection cone (deg)");
            CRingPx = Config.Bind("Visual", "RingRadiusPx", 14f, "准心尺寸(像素) / reticle size in px");
            CStyle = Config.Bind("Visual", "Style", 0, new ConfigDescription("0=六条线 1=四条线 2=虚线圈 3=实心圆 / reticle style", new AcceptableValueRange<int>(0, 3)));
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
                L.LogInfo("[GunRing] bullet probe patched");
            }
            catch (Exception e) { L.LogWarning("[GunRing] bullet probe failed: " + e.Message); }

            L.LogInfo("[GunRing] ready (menu-driven, no hotkeys)");
        }

        static Texture2D MakeRing(int size, int thickness)
        {
            var t = new Texture2D(size, size, TextureFormat.ARGB32, false);
            float c = (size - 1) * 0.5f, rOut = c, rIn = c - thickness;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    float a = d <= rOut && d >= rIn ? 1f : 0f;
                    if (d > rOut && d < rOut + 1.2f) a = Mathf.Clamp(rOut + 1.2f - d, 0f, 1f) * 0.7f;
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            t.filterMode = FilterMode.Bilinear;
            t.Apply();
            return t;
        }


        static string StyleName()
        {
            switch (CStyle.Value)
            {
                case 1: return "FourTick";
                case 2: return "DashedRing";
                case 3: return "Ring";
                default: return "SixTick";
            }
        }

        static Texture2D Reticle(string style, int size)
        {
            string key = style + ":" + size;
            Texture2D cached;
            if (_texCache.TryGetValue(key, out cached)) return cached;
            int s = Mathf.Max(16, size | 1);
            var t = new Texture2D(s, s, TextureFormat.ARGB32, false);
            float c = (s - 1) * 0.5f;
            if (style == "Vector")
            {
                // 速度矢量符号：圆环 + 贯穿水平线 + 上方竖线
                float rOut2 = s * 0.33f, th = Mathf.Max(1.3f, s * 0.075f);
                float rIn2 = rOut2 - th;
                for (int y = 0; y < s; y++)
                    for (int x = 0; x < s; x++)
                    {
                        float dx = x - c, dy = y - c;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        bool a = false;
                        if (d <= rOut2 && d >= rIn2) a = true;                 // 圆环
                        if (Mathf.Abs(dy) <= th * 0.5f) a = true;              // 水平线（贯穿全宽）
                        if (Mathf.Abs(dx) <= th * 0.5f && dy > rOut2) a = true; // 上方竖线
                        t.SetPixel(x, y, new Color(1f, 1f, 1f, a ? 1f : 0f));
                    }
            }
            else if (style == "Ring")
            {
                float rOut = c, rIn = c - Mathf.Max(1.6f, s * 0.07f);
                for (int y = 0; y < s; y++)
                    for (int x = 0; x < s; x++)
                    {
                        float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                        float a = (d <= rOut && d >= rIn) ? 1f : 0f;
                        t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                    }
            }
            else if (style == "FourTick")
            {
                // 4 条线围着一个点（0/90/180/270）
                float dot2 = Mathf.Max(1.2f, s * 0.038f);
                float rIn2 = s * 0.22f, rOut2 = c * 0.94f;
                float hw = Mathf.Max(0.75f, s * 0.022f);
                for (int y = 0; y < s; y++)
                    for (int x = 0; x < s; x++)
                    {
                        float dx = x - c, dy = y - c;
                        float dist = Mathf.Sqrt(dx * dx + dy * dy);
                        float a = 0f;
                        if (dist <= dot2) a = 1f;
                        else if (dist >= rIn2 && dist <= rOut2)
                        {
                            if (Mathf.Abs(dy) <= hw || Mathf.Abs(dx) <= hw) a = 1f;
                        }
                        t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                    }
            }
            else if (style == "DashedRing")
            {
                // ◌ 虚线圆环（无中心点）
                float rr = c * 0.82f, th = Mathf.Max(1.1f, s * 0.045f);
                for (int y = 0; y < s; y++)
                    for (int x = 0; x < s; x++)
                    {
                        float dx = x - c, dy = y - c;
                        float dist = Mathf.Sqrt(dx * dx + dy * dy);
                        float a = 0f;
                        if (Mathf.Abs(dist - rr) <= th)
                        {
                            float ang = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                            if (ang < 0f) ang += 360f;
                            if ((ang % 24f) < 15f) a = 1f;   // 每 24° 一段、留 9° 缺口
                        }
                        t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                    }
            }
            else
            {
                // SixTick: 中心点 + 6 根线（0/60/120/180/240/300 度），中间留空
                float dot = Mathf.Max(1.2f, s * 0.038f);
                float rIn = s * 0.22f, rOut = c * 0.94f;
                float halfW = Mathf.Max(0.7f, s * 0.020f);
                for (int y = 0; y < s; y++)
                    for (int x = 0; x < s; x++)
                    {
                        float dx = x - c, dy = y - c;
                        float dist = Mathf.Sqrt(dx * dx + dy * dy);
                        float a = 0f;
                        if (dist <= dot) a = 1f;
                        else if (dist >= rIn && dist <= rOut)
                        {
                            for (int k = 0; k < 6; k++)
                            {
                                float ang = k * Mathf.PI / 3f;
                                float px = Mathf.Cos(ang), py = Mathf.Sin(ang);
                                float along = dx * px + dy * py;
                                float across = Mathf.Abs(-dx * py + dy * px);
                                if (along > 0f && across <= halfW) { a = 1f; break; }
                            }
                        }
                        t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                    }
            }
            t.filterMode = FilterMode.Bilinear;
            t.Apply();
            _texCache[key] = t;
            return t;
        }

        void Update()
        {
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
                    if (dist < 20f || dist > CMaxRange.Value) continue;
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
                    if (dist < 20f || dist > CMaxRange.Value) continue;
                    float ang = Vector3.Angle(fwd, d);
                    if (ang < best) { best = ang; _fallback = other; }
                }
            }
            PickCannon();
        }

        // ---- 炮弹（CannonScript）扫描 ----
        // 取机上 ProjectileVelocity 最快的那门炮作为主炮；炮的弹丸是 Rigidbody，
        // 初速 = 炮口点速度 + transform.forward * ProjectileVelocity。
        void PickCannon()
        {
            if (_cannonCraft == _craft && Time.unscaledTime < _nextCannonScan) return;
            if (_cannonCraft != _craft) { _cannonCraft = _craft; _cannonLogged = false; }
            _nextCannonScan = Time.unscaledTime + 1.5f;
            _cannon = null;
            _cannonLife = 10f;
            if (_craft == null) return;
            try
            {
                float best = -1f;
                CannonScript[] all = _craft.GetComponentsInChildren<CannonScript>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] == null) continue;
                    float sp = all[i].ProjectileVelocity;
                    if (sp > best) { best = sp; _cannon = all[i]; }
                }
                // 兜底：有些载具的部件不在 Aircraft 子层级里，直接扫场景里的 CannonScript
                if (_cannon == null)
                {
                    CannonScript[] scene = Resources.FindObjectsOfTypeAll<CannonScript>();
                    for (int i = 0; i < scene.Length; i++)
                    {
                        CannonScript c = scene[i];
                        if (c == null) continue;
                        try { if (c.PartScript == null || c.PartScript.Aircraft != _craft) continue; } catch { continue; }
                        float sp = c.ProjectileVelocity;
                        if (sp > best) { best = sp; _cannon = c; }
                    }
                }
            }
            catch (Exception e) { if (CDebugLog.Value) L.LogWarning("[GunRing] cannon scan: " + e.Message); }
            if (_cannon != null)
            {
                _cannonLife = CannonLifetime(_cannon);
                if (!_cannonLogged)
                {
                    _cannonLogged = true;
                    L.LogInfo("[GunRing] cannon found: " + _cannon.name + " speed=" + _cannon.ProjectileVelocity.ToString("0") + "m/s life=" + _cannonLife.ToString("0.0") + "s");
                }
            }
        }

        static float CannonLifetime(CannonScript c)
        {
            float life = 10f;
            try
            {
                FieldInfo fi = typeof(CannonScript).GetField("_cannon", BindingFlags.NonPublic | BindingFlags.Instance);
                object data = fi != null ? fi.GetValue(c) : null;
                if (data != null)
                {
                    PropertyInfo lp = data.GetType().GetProperty("ProjectileLifetime");
                    if (lp != null)
                    {
                        float v = Convert.ToSingle(lp.GetValue(data, null));
                        if (v > 0.5f) life = Mathf.Min(v, 30f);
                    }
                }
            }
            catch { }
            return life;
        }

        Component _gun;
        Transform _muzzleT;

        void PickGun()
        {
            _bulletSpeed = -1f; _gunName = ""; _gun = null; _muzzleT = null; _spread = 0f;
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

        static Transform MuzzleOf(object g)
        {
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

        bool CannonShot(out Vector3 p0, out Vector3 dir, out Vector3 own, out float speed, out Vector3 g)
        {
            p0 = Vector3.zero; dir = Vector3.forward; own = Vector3.zero; speed = 0f; g = Physics.gravity;
            if (_cannon == null || _craft == null) return false;
            speed = _cannon.ProjectileVelocity;
            if (speed <= 10f) return false;
            dir = _cannon.transform.forward;         // CannonScript.Fire 里就是 transform.forward
            try { p0 = _cannon.TipPosition; }        // _muzzleTip.position
            catch { p0 = _cannon.transform.position; }
            own = OwnVelocity();
            try
            {
                // 炮口点的真实速度（含飞机平移 + 角速度），比整机速度更准
                IRigidBody rb = _cannon.PartScript.Body.RigidBody;
                if (rb != null) own = rb.GetPointVelocity(p0);
            }
            catch { }
            g = Physics.gravity * (CCannonGravityPct.Value / 100f);
            return true;
        }

        // 求击中移动目标所需的炮管方向：
        //   T + Vt*t = P0 + Vown*t + f*Vp*t + 0.5*g*t^2
        // => |T-P0+(Vt-Vown)t-0.5*g*t^2| = Vp*t
        static bool SolveCannonIntercept(Vector3 p0, Vector3 own, Vector3 g, float speed,
                                         Vector3 targetPos, Vector3 targetVel, float maxT,
                                         out float tHit, out Vector3 aimDir)
        {
            tHit = 0f; aimDir = Vector3.zero;
            if (speed < 1f || maxT <= 0f) return false;
            Vector3 w = targetVel - own;
            if ((targetPos - p0).magnitude < 1f) return false;
            float prevT = 0.001f;
            if (InterceptH(p0, w, g, speed, targetPos, prevT) <= 0f) return false;
            int steps = 600;
            float dt = maxT / steps;
            for (int i = 1; i <= steps; i++)
            {
                float t = i * dt;
                if (InterceptH(p0, w, g, speed, targetPos, t) > 0f) { prevT = t; continue; }
                float lo = prevT, hi = t;
                for (int k = 0; k < 36; k++)
                {
                    float mid = (lo + hi) * 0.5f;
                    if (InterceptH(p0, w, g, speed, targetPos, mid) > 0f) lo = mid;
                    else hi = mid;
                }
                tHit = (lo + hi) * 0.5f;
                if (tHit <= 0.001f) return false;
                Vector3 d = targetPos - p0 + w * tHit - 0.5f * g * (tHit * tHit);
                if (d.sqrMagnitude < 0.0001f) return false;
                aimDir = d.normalized;
                return true;
            }
            return false;
        }

        static float InterceptH(Vector3 p0, Vector3 w, Vector3 g, float speed, Vector3 targetPos, float t)
        {
            Vector3 d = targetPos - p0 + w * t - 0.5f * g * (t * t);
            return d.sqrMagnitude - speed * speed * t * t;
        }

        // 没有锁定目标时：求炮弹飞到固定射程 range 的飞行时间
        static bool TimeForCannonRange(Vector3 p0, Vector3 v0, Vector3 g, float range, float maxT, out float t)
        {
            t = 0f;
            float sp = v0.magnitude;
            if (sp < 1f) return false;
            float tt = Mathf.Clamp(range / sp, 0.001f, maxT);
            for (int i = 0; i < 10; i++)
            {
                float len = (Ballistic(p0, v0, g, tt) - p0).magnitude;
                if (len < 0.01f) break;
                float nt = Mathf.Clamp(tt * (range / len), 0.001f, maxT);
                if (Mathf.Abs(nt - tt) < 0.002f) { tt = nt; break; }
                tt = nt;
            }
            t = tt;
            float fin = (Ballistic(p0, v0, g, t) - p0).magnitude;
            return Mathf.Abs(fin - range) <= Mathf.Max(5f, range * 0.05f);
        }

        void DrawCannon(Camera cam)
        {
            if (!CCannonEnabled.Value || _craft == null || _cannon == null) return;
            Vector3 p0, dir, own, g; float speed;
            if (!CannonShot(out p0, out dir, out own, out speed, out g)) return;
            float x, y;
            float tUsed;
            bool ready = false;
            bool noTarget = false;
            bool usingTarget = HasTarget && CCannonUseTargetRange.Value;
            float maxT = _cannonLife;

            if (usingTarget)
            {
                float tHit; Vector3 aimDir;
                if (!SolveCannonIntercept(p0, own, g, speed, TargetPos, TargetVel, maxT, out tHit, out aimDir)) return;
                Vector3 cs = cam.WorldToScreenPoint(p0 + dir * 4000f);
                Vector3 rs = cam.WorldToScreenPoint(p0 + aimDir * 4000f);
                Vector3 ts = cam.WorldToScreenPoint(TargetPos);
                if (cs.z <= 0f || rs.z <= 0f || ts.z <= 0f) return;
                float cx = cs.x, cy = Screen.height - cs.y;
                float rx = rs.x, ry = Screen.height - rs.y;
                float tx = ts.x, ty = Screen.height - ts.y;
                // 把机头拉到所需弹道方向后，目标会出现在哪 = 环的位置
                x = tx + (cx - rx);
                y = ty + (cy - ry);
                tUsed = tHit;
                ready = Vector3.Angle(dir, aimDir) <= CCannonGreenAngle.Value;
                if (CDebugLog.Value && Time.unscaledTime >= _dbgCannonNext)
                {
                    _dbgCannonNext = Time.unscaledTime + 1f;
                    Vector3 vShot = own + dir * speed;
                    Vector3 cur = Ballistic(p0, vShot, g, tUsed);
                    Vector3 flat = Ballistic(p0, vShot, Vector3.zero, tUsed);
                    _cannonDrop = Vector3.Distance(cur, flat);
                    _cannonFlight = tUsed;
                    L.LogInfo("[GunRing] cannon d=" + Vector3.Distance(p0, TargetPos).ToString("0")
                        + " t=" + tUsed.ToString("0.00") + "s drop=" + _cannonDrop.ToString("0.0")
                        + "m ang=" + Vector3.Angle(dir, aimDir).ToString("0.00") + "°");
                }
            }
            else
            {
                float range = Mathf.Clamp(CCannonRange.Value, 50f, 20000f);
                float tR;
                if (!TimeForCannonRange(p0, own + dir * speed, g, range, maxT, out tR)) return;
                Vector3 impact = Ballistic(p0, own + dir * speed, g, tR);
                Vector3 isp = cam.WorldToScreenPoint(impact);
                if (isp.z <= 0f) return;
                x = isp.x; y = Screen.height - isp.y;
                tUsed = tR;
                noTarget = true;
            }
            if (x < -300f || y < -300f || x > Screen.width + 300f || y > Screen.height + 300f) return;

            float radius = Mathf.Max(5f, CCannonRingPx.Value);
            int px = Mathf.RoundToInt(radius * 2f);
            Color col = ready ? Rgb(CReadyR, CReadyG, CReadyB) : Rgb(CCannonR, CCannonG, CCannonB);
            if (noTarget) col.a *= 0.7f;
            Color old = GUI.color;
            GUI.color = col;
            GUI.DrawTexture(new Rect(x - radius, y - radius, px, px), Reticle(StyleName(), px));

            if (CCannonTrace.Value)
            {
                int dots = Mathf.Clamp(CCannonTraceDots.Value, 4, 60);
                Vector3 vShot = own + dir * speed;
                GUI.color = new Color(col.r, col.g, col.b, col.a * 0.55f);
                for (int i = 1; i <= dots; i++)
                {
                    float tt = tUsed * i / dots;
                    Vector3 bs = cam.WorldToScreenPoint(Ballistic(p0, vShot, g, tt));
                    if (bs.z <= 0f) continue;
                    float bx = bs.x, by = Screen.height - bs.y;
                    GUI.DrawTexture(new Rect(bx - 1.5f, by - 1.5f, 3f, 3f), Texture2D.whiteTexture);
                }
            }
            GUI.color = old;

            if (CDebugDraw.Value)
            {
                GUIStyle st = new GUIStyle(GUI.skin.label);
                st.fontSize = 11;
                st.normal.textColor = new Color(1f, 0.82f, 0.5f, 0.95f);
                string txt = usingTarget
                    ? ("CANNON d=" + Vector3.Distance(p0, TargetPos).ToString("0") + "m t=" + tUsed.ToString("0.00") + "s spd=" + speed.ToString("0") + (ready ? " READY" : ""))
                    : ("CANNON RANGE " + Mathf.Clamp(CCannonRange.Value, 50f, 20000f).ToString("0") + "m t=" + tUsed.ToString("0.00") + "s");
                GUI.Label(new Rect(x + radius + 4f, y + 8f, 340f, 16f), txt, st);
            }
        }

        void DrawCannonSafe(Camera cam)
        {
            try { DrawCannon(cam); }
            catch (Exception e)
            {
                if (Time.unscaledTime >= _cannonErrNext)
                {
                    _cannonErrNext = Time.unscaledTime + 5f;
                    L.LogWarning("[GunRing] cannon draw: " + e.Message);
                }
            }
        }

        void OnGUI()
        {
            if (!_on || _craft == null) return;
            Camera cam = ViewCam();
            if (cam == null) return;

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
            float radius = CRingPx.Value + Mathf.Clamp(1200f / Mathf.Max(50f, dist), 0f, 12f);
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
            GUI.DrawTexture(new Rect(x - radius, y - radius, px, px), Reticle(StyleName(), px));
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