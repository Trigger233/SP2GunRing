using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using Assets.Scripts.Craft;
using Assets.Scripts.Craft.Parts;
using Assets.Scripts.Craft.Parts.Modifiers.Weapons;
using Assets.Scripts.Flight;
using Assets.Scripts.Flight.Combat;
using Assets.Scripts.Flight.Combat.Bullets;
using Assets.Scripts.Flight.UI.Targeting;
using Assets.Scripts.Multiplayer.FlightObjects.Damage;

namespace SP2GunRing
{
    public partial class GunRingPlugin
    {
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
            Matrix4x4 mtx = GUI.matrix;
            if (CRollRotation.Value) GUIUtility.RotateAroundPivot(CraftRollDeg(), new Vector2(x, y));
            GUI.DrawTexture(new Rect(x - radius, y - radius, px, px), Reticle(StyleName() == "AceRing" ? "Ring" : StyleName(), px));
            GUI.matrix = mtx;

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

    }
}
