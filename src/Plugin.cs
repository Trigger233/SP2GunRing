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

    [HarmonyPatch(typeof(NetworkAircraftScript), "DamagePart")]
    static class HitProbe
    {
        [HarmonyPostfix]
        static void Postfix(NetworkAircraftScript __instance)
        {
            GunRingPlugin.OnDamage(__instance);
        }
    }

    [BepInPlugin("sp2.gunring", "SP2 Gun Lead Ring", "0.5.0")]
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
        static ConfigEntry<bool> COnlySelected;


        static ConfigEntry<bool> CHitMarker;
        static ConfigEntry<int> CHitMarkerMs;
        static ConfigEntry<bool> CHitSound;
        static ConfigEntry<float> CHitVolume;
        static ConfigEntry<int> CHitMarkerPx;

        bool _on = true;
        internal static GunRingPlugin Instance;
        static float _hitUntil = -1f;
        static int _hitCount;
        static int _dmgCalls;
        internal static ManualLogSource Log2;

        static int _hitProbeCount;

        static int _anyDamageCount;

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
            if (p._target != null && p._target.Player != null && p._target.Player.Aircraft == ac)
                MarkHit(ac.name);
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
            if (p._target != null && p._target.Player != null && p._target.Player.Aircraft == ac)
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
            if (p._target != null && p._target.Player != null && p._target.Player.Aircraft == ac)
                MarkHit(ac.name);
        }
        static readonly Dictionary<string, Texture2D> _texCache = new Dictionary<string, Texture2D>();
        float _nextScan;
        AircraftScript _craft;
        float _bulletSpeed = -1f;
        string _gunName = "";
        Target _target;
        AircraftScript _fallback;
        int _lastScanTargets = -1;
        string _scanDetail = "";
        string _selectedSource = "";
        bool _aimingOff;
        float _chScrX, _chScrY;

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
            CMaxOffsetPx = Config.Bind("Visual", "MaxOffsetPx", 70, "Pinned 模式下环离准心最远的像素距离，0=不限 / max pixel offset from crosshair");
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
            CGreenAngle = Config.Bind("Visual", "GreenAngle", 2.5f, "变绿条件：机头与提前量点夹角≤该角度(度) / turn green when aim error <= this many degrees");
            COnlySelected = Config.Bind("General", "OnlyWhenSelected", true, "只在你自己锁定/选中目标时显示，没锁就不画 / only draw when you have a locked target");
            CHitMarker = Config.Bind("Hit", "HitMarker", true, "命中时在屏幕中间画叉 / draw hit marker on hit");
            CHitMarkerMs = Config.Bind("Hit", "HitMarkerMs", 180, "命中标记显示时长(毫秒) / hit marker duration in ms");
            CHitMarkerPx = Config.Bind("Hit", "HitMarkerPx", 26, "命中叉的大小(像素) / hit marker size in px");
            CHitSound = Config.Bind("Hit", "HitSound", true, "命中时播放 hit.wav / play hit.wav on hit");
            CHitVolume = Config.Bind("Hit", "HitVolume", 0.8f, "命中音音量 / hit sound volume");
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
            Vector3 aimFwd = viewCam != null ? viewCam.transform.forward : _craft.transform.forward;
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
                    Vector3 d = t.Position - _craft.transform.position;
                    float dist = d.magnitude;
                    if (dist < 20f || dist > CMaxRange.Value) continue;
                    float angCam = Vector3.Angle(aimFwd, d);
                    float angNose = Vector3.Angle(_craft.transform.forward, d);
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
                Vector3 fwd = _craft.transform.forward;
                var all = Resources.FindObjectsOfTypeAll<AircraftScript>();
                for (int i = 0; i < all.Length; i++)
                {
                    AircraftScript other = all[i];
                    if (other == null || other == _craft) continue;
                    Vector3 d = other.transform.position - _craft.transform.position;
                    float dist = d.magnitude;
                    if (dist < 20f || dist > CMaxRange.Value) continue;
                    float ang = Vector3.Angle(fwd, d);
                    if (ang < best) { best = ang; _fallback = other; }
                }
            }
        }

        Component _gun;
        Transform _muzzleT;

        void PickGun()
        {
            _bulletSpeed = -1f; _gunName = ""; _gun = null; _muzzleT = null;
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
                }
            }
            catch (Exception e) { L.LogWarning("[GunRing] gun scan: " + e.Message); }
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

        Vector3 MuzzlePos()
        {
            if (_muzzleT != null) return _muzzleT.position;
            if (_gun != null) return _gun.transform.position;
            return _craft.transform.position;
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
            return _craft.transform.forward;
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

        void OnGUI()
        {
            if (!_on || _craft == null) return;
            Camera cam = ViewCam();
            if (cam == null) return;

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
                    // 环 = 准心 + (敌机 - 提前量点)：等价于"对准时敌机会出现在哪"。
                    // 把环压在敌机身上时，机头正好在提前量点上 -> 与 green 判定(angleToLead<=2.5)完全一致
                    Vector3 tS = cam.WorldToScreenPoint(TargetPos);
                    if (tS.z > 0f)
                    {
                        float tgx = tS.x, tgy = Screen.height - tS.y;
                        x = chx + (tgx - x);
                        y = chy + (tgy - y);
                    }
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

            float dist = Vector3.Distance(_craft.transform.position, TargetPos);
            float radius = CRingPx.Value + Mathf.Clamp(1200f / Mathf.Max(50f, dist), 0f, 12f);
            bool good = _angleToLead <= CGreenAngle.Value;
            int px = Mathf.RoundToInt(radius * 2f);

            Color old = GUI.color;

            GUI.color = clamped ? Rgb(CClampR, CClampG, CClampB)
                                 : (good ? Rgb(CReadyR, CReadyG, CReadyB) : Rgb(CIdleR, CIdleG, CIdleB));
            GUI.DrawTexture(new Rect(x - radius, y - radius, px, px), Reticle(StyleName(), px));
            GUI.color = old;

            // 命中反馈：屏幕中心画叉
            if (CHitMarker.Value && Time.unscaledTime < _hitUntil)
            {
                float hx = _chScrX, hy = _chScrY;
                if (hx <= 0f && hy <= 0f) { hx = Screen.width * 0.5f; hy = Screen.height * 0.5f; }
                float hp = Mathf.Max(8f, CHitMarkerPx.Value);
                float th = 2.5f;
                GUI.color = Rgb(CReadyR, CReadyG, CReadyB);
                Matrix4x4 mm = GUI.matrix;
                GUIUtility.RotateAroundPivot(45f, new Vector2(hx, hy));
                GUI.DrawTexture(new Rect(hx - hp, hy - th * 0.5f, hp * 2f, th), Texture2D.whiteTexture);
                GUI.matrix = mm;
                Matrix4x4 mm2 = GUI.matrix;
                GUIUtility.RotateAroundPivot(-45f, new Vector2(hx, hy));
                GUI.DrawTexture(new Rect(hx - hp, hy - th * 0.5f, hp * 2f, th), Texture2D.whiteTexture);
                GUI.matrix = mm2;
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
                              " d=" + (_craft != null ? Vector3.Distance(_craft.transform.position, t.Position).ToString("0") : "?") +
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
                L.LogInfo("[GunRing] ownVel=" + ov + " fwd=" + _craft.transform.forward + " angOwnFwd=" + Vector3.Angle(ov, _craft.transform.forward).ToString("0.0"));
            }
            L.LogInfo("[GunRing] craft=" + (_craft != null ? _craft.name : "null") +
                      " gun=" + _gunName + " speed=" + _bulletSpeed.ToString("0") +
                      " target=" + (_target != null ? _target.Name + " d=" + Vector3.Distance(_craft.transform.position, _target.Position).ToString("0") : "null") +
                      " angleToLead=" + _angleToLead.ToString("0.0"));
        }
    }
}