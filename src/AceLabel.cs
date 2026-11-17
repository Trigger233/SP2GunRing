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
        void DrawAceLabelSafe(Camera cam)
        {
            try { DrawAceLabel(cam); }
            catch (Exception e)
            {
                if (Time.unscaledTime >= _aceErrNext)
                {
                    _aceErrNext = Time.unscaledTime + 5f;
                    L.LogWarning("[GunRing] ace label: " + e.Message);
                }
            }
        }

        AircraftScript TargetAircraft()
        {
            if (_target != null)
            {
                try { if (_target.Player != null && _target.Player.Aircraft != null) return _target.Player.Aircraft; } catch { }
            }
            if (_watched != null) return _watched;
            if (_fallback != null) return _fallback;
            return null;
        }

        string TargetModelName()
        {
            AircraftScript ac = TargetAircraft();
            if (ac != null && ac.Aircraft != null && !string.IsNullOrEmpty(ac.Aircraft.Name)) return ac.Aircraft.Name;
            if (_target != null && !string.IsNullOrEmpty(_target.Name))
            {
                int nl = _target.Name.IndexOf('\n');
                if (nl > 0) return _target.Name.Substring(0, nl).Trim();
            }
            return "UNKNOWN";
        }

        string TargetOperatorName()
        {
            if (_target != null)
            {
                try { if (_target.Player != null) { string pn = _target.Player.Name; if (!string.IsNullOrEmpty(pn)) return pn; } } catch { }
                string n = _target.Name;
                if (!string.IsNullOrEmpty(n))
                {
                    int nl = n.LastIndexOf('\n');
                    if (nl >= 0 && nl + 1 < n.Length) return n.Substring(nl + 1).Trim();
                    return n;
                }
            }
            if (_fallback != null) return _fallback.name;
            return "";
        }

        bool TryFriendlyFromTargeting(TargetingSystem ts)
        {
            if (ts == null || _target == null) return false;
            try
            {
                foreach (TrackedTarget tt in ts.Targets)
                {
                    if (tt == null || tt.Target == null) continue;
                    bool same = tt.Target == _target;
                    if (!same && tt.Target.Name == _target.Name)
                        same = Vector3.Distance(tt.Target.Position, TargetPos) < 150f;
                    if (!same) continue;
                    _aceFriendlyTarget = _target;
                    _aceFriendlyValue = tt.IsFriendly;
                    _aceAggression = (int)tt.AggressionLevel;
                    return true;
                }
            }
            catch { }
            return false;
        }

        bool TryFriendlyFromBoxes()
        {
            if (_target == null) return false;
            try
            {
                RefreshAceBoxCache();
                TargetBoxScript[] boxes = _aceBoxCache;
                if (boxes != null)
                {
                    for (int i = 0; i < boxes.Length; i++)
                    {
                        TargetBoxScript b = boxes[i];
                        if (b == null || b.TrackedTarget == null || b.TrackedTarget.Target == null) continue;
                        TrackedTarget tt = b.TrackedTarget;
                        bool same = tt.Target == _target;
                        if (!same && tt.Target.Name == _target.Name)
                            same = Vector3.Distance(tt.Target.Position, TargetPos) < 150f;
                        if (!same) continue;
                        _aceFriendlyTarget = _target;
                        _aceFriendlyValue = tt.IsFriendly;
                        _aceAggression = (int)tt.AggressionLevel;
                        return true;
                    }
                }
                TargetBoxScriptJuicy[] juicy = _aceJuicyCache;
                if (juicy != null)
                {
                    for (int i = 0; i < juicy.Length; i++)
                    {
                        TargetBoxScriptJuicy b = juicy[i];
                        if (b == null || b.TrackedTarget == null || b.TrackedTarget.Target == null) continue;
                        TrackedTarget tt = b.TrackedTarget;
                        bool same = tt.Target == _target;
                        if (!same && tt.Target.Name == _target.Name)
                            same = Vector3.Distance(tt.Target.Position, TargetPos) < 150f;
                        if (!same) continue;
                        _aceFriendlyTarget = _target;
                        _aceFriendlyValue = tt.IsFriendly;
                        _aceAggression = (int)tt.AggressionLevel;
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        int TargetRelation()
        {
            if (_craft == null) return 0;
            if (_target == null)
            {
                if (_fallback == null) return 0;
                return _fallback.TeamId == _craft.TeamId ? 3 : 1;
            }
            if (_aceFriendlyTarget == _target && _aceAggression >= 0) return _aceAggression;
            if (_aceRelationTarget == _target && Time.unscaledTime < _aceRelationNext) return _aceRelationValue;

            int rel;
            if (TryFriendlyFromTargeting(_craft.TargetingSystem)) rel = _aceAggression;
            else if (TryFriendlyFromTargeting(PodSystem())) rel = _aceAggression;
            else if (TryFriendlyFromBoxes()) rel = _aceAggression;
            else
            {
                rel = 2;
                try { if (_target.Player != null) rel = _target.Player.TeamId == _craft.TeamId ? 3 : 1; else rel = _target.TeamId == _craft.TeamId ? 3 : 1; }
                catch { rel = _target.TeamId == _craft.TeamId ? 3 : 1; }
            }
            _aceRelationTarget = _target;
            _aceRelationValue = rel;
            _aceRelationNext = Time.unscaledTime + 0.5f;
            return rel;
        }

        static void DrawHudLabel(Rect r, string text, GUIStyle style, GUIStyle shadow)
        {
            if (shadow != null)
                GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), text, shadow);
            GUI.Label(r, text, style);
        }

        static Texture2D AceBoxTexture(int size)
        {
            string key = "acebox:" + size;
            Texture2D cached;
            if (_texCache.TryGetValue(key, out cached)) return cached;
            int s = Mathf.Max(16, size);
            var t = new Texture2D(s, s, TextureFormat.ARGB32, false);
            float th = Mathf.Max(1.5f, s * 0.05f);
            for (int y = 0; y < s; y++)
            {
                for (int x = 0; x < s; x++)
                {
                    bool on = false;
                    // 纯矩形边框（不要四角+中心点）
                    if (x <= th || x >= s - 1 - th || y <= th || y >= s - 1 - th) on = true;
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, on ? 1f : 0f));
                }
            }
            t.filterMode = FilterMode.Bilinear;
            t.Apply();
            _texCache[key] = t;
            return t;
        }

        static void DrawAceBox(float x, float y, float size, Color col)
        {
            Color old = GUI.color;
            GUI.color = col;
            int px = Mathf.Max(16, Mathf.RoundToInt(size));
            GUI.DrawTexture(new Rect(x - px * 0.5f, y - px * 0.5f, px, px), AceBoxTexture(px));
            GUI.color = old;
        }

        bool TargetLocked()
        {
            try
            {
                if (_craft == null || _target == null) return false;
                TargetingSystem ts = _craft.TargetingSystem;
                if (ts == null) return false;
                TrackedTarget ct = ts.CurrentTrackedTarget;
                if (ct != null && ct.Target == _target) return ct.IsLocked;
                foreach (TrackedTarget tt in ts.Targets)
                {
                    if (tt != null && tt.Target == _target) return tt.IsLocked;
                }
            }
            catch { }
            return false;
        }

        static void DrawAceLockDiamond(float x, float y, float size, Color col)
        {
            Color old = GUI.color;
            GUI.color = col;
            int px = Mathf.Max(20, Mathf.RoundToInt(size));
            Matrix4x4 m = GUI.matrix;
            GUIUtility.RotateAroundPivot(45f, new Vector2(x, y));
            GUI.DrawTexture(new Rect(x - px * 0.5f, y - px * 0.5f, px, px), AceBoxTexture(px));
            GUI.matrix = m;
            GUI.color = old;
        }

        void DrawAceLabel(Camera cam)
        {
            if (!CAceLabel.Value || _craft == null || !AimingModeOn()) return;
            if (Event.current != null && Event.current.type != EventType.Repaint) return;

            bool selectedVisible = true;
            if (_target != null && CAceBlink.Value)
                selectedVisible = Mathf.Repeat(Time.unscaledTime * Mathf.Max(1, CAceBlinkHz.Value), 1f) < 0.5f;
            if (HasTarget && selectedVisible) DrawSelectedAceLabel(cam);
            if (CAceHideVanilla.Value) DrawOtherAceBoxes(cam);
        }

        Color RelationColor(int relation)
        {
            if (!CAceTeamColor.Value) return Rgb(CAceR, CAceG, CAceB);
            if (relation == 3) return Rgb(CAceFriendR, CAceFriendG, CAceFriendB);
            if (relation == 1) return Rgb(CAceEnemyR, CAceEnemyG, CAceEnemyB);
            return Rgb(CAceR, CAceG, CAceB);
        }

        void DrawOtherAceBoxes(Camera cam)
        {
            RefreshAceBoxCache();
            _aceSeen.Clear();
            TargetBoxScript[] boxes = _aceBoxCache;
            if (boxes != null)
            {
                for (int i = 0; i < boxes.Length; i++)
                {
                    if (boxes[i] == null || boxes[i].TrackedTarget == null) continue;
                    DrawOtherAceBox(cam, boxes[i].TrackedTarget, _aceSeen);
                }
            }
            TargetBoxScriptJuicy[] juicy = _aceJuicyCache;
            if (juicy != null)
            {
                for (int i = 0; i < juicy.Length; i++)
                {
                    if (juicy[i] == null || juicy[i].TrackedTarget == null) continue;
                    DrawOtherAceBox(cam, juicy[i].TrackedTarget, _aceSeen);
                }
            }
        }

        void DrawOtherAceBox(Camera cam, TrackedTarget tt, List<Target> seen)
        {
            if (tt == null || tt.Target == null || tt.Target.IsDead) return;
            Target t = tt.Target;
            if (t == _target) return;
            try { if (t.Player != null && t.Player.Aircraft == _craft) return; } catch { }
            if (seen.Contains(t)) return;
            seen.Add(t);
            Vector3 sp = cam.WorldToScreenPoint(t.Position);
            if (sp.z <= 0f) return;
            float x = sp.x, y = Screen.height - sp.y;
            if (x < -200f || x > Screen.width + 200f || y < -200f || y > Screen.height + 200f) return;
            float box = Mathf.Max(10f, CAceBoxPx.Value * 0.8f);
            DrawAceBox(x, y, box, RelationColor((int)tt.AggressionLevel));
        }

        void DrawSelectedAceLabel(Camera cam)
        {
            if (_craft == null || !HasTarget) return;
            Vector3 world = TargetPos;
            Vector3 sp = cam.WorldToScreenPoint(world);
            if (sp.z <= 0f) return;
            float x = sp.x, y = Screen.height - sp.y;
            if (x < -200f || x > Screen.width + 200f || y < -200f || y > Screen.height + 200f) return;

            int relation = TargetRelation();
            bool friendly = relation == 3;
            Color boxCol = RelationColor(relation);
            Color textCol = CAceTextMatchBox.Value ? boxCol : Rgb(CAceR, CAceG, CAceB);
            float box = Mathf.Max(12f, CAceBoxPx.Value);
            if (_aceTeamLogged != TargetName)
            {
                _aceTeamLogged = TargetName;
                L.LogInfo("[GunRing] ace draw target=" + TargetName
                    + " team=" + (_target != null ? _target.TeamId.ToString() : "?")
                    + " selfTeam=" + (_craft != null ? _craft.TeamId.ToString() : "?")
                    + " friendly=" + friendly + " relation=" + relation);
            }
            if (CAceHideVanilla.Value) DrawAceBox(x, y, box, boxCol);
            if (CAceLockFx.Value && TargetLocked()) DrawAceLockDiamond(x, y, box * 0.72f, boxCol);

            string model = TargetModelName();
            string op = TargetOperatorName();
            string dist = Mathf.RoundToInt(Vector3.Distance(CraftPos(), world)).ToString();
            if (string.IsNullOrEmpty(model)) model = "UNKNOWN";
            if (string.IsNullOrEmpty(op)) op = "UNKNOWN";
            model = model.ToUpperInvariant();
            op = op.ToUpperInvariant();

            int dPx = Mathf.Max(8, CAceDistPx.Value);
            int nPx = Mathf.Max(8, CAceNamePx.Value);
            if (!_aceStylesReady)
            {
                _aceStylesReady = true;
                _aceDistStyle = new GUIStyle(GUI.skin.label);
                _aceNameStyle = new GUIStyle(GUI.skin.label);
                _aceDistStyle.fontStyle = FontStyle.Bold; _aceDistStyle.alignment = TextAnchor.UpperLeft; _aceDistStyle.wordWrap = false;
                _aceNameStyle.fontStyle = FontStyle.Bold; _aceNameStyle.alignment = TextAnchor.UpperLeft; _aceNameStyle.wordWrap = false;
                _aceDistShadow = new GUIStyle(_aceDistStyle);
                _aceNameShadow = new GUIStyle(_aceNameStyle);
                _aceDistShadow.normal.textColor = new Color(0f, 0f, 0f, 0.85f);
                _aceNameShadow.normal.textColor = new Color(0f, 0f, 0f, 0.85f);
            }
            _aceDistStyle.fontSize = dPx; _aceDistStyle.normal.textColor = textCol;
            _aceNameStyle.fontSize = nPx; _aceNameStyle.normal.textColor = textCol;
            _aceDistShadow.fontSize = dPx;
            _aceNameShadow.fontSize = nPx;

            // 按实际文字宽度给足空间，避免长名字被 260px 的 Rect 截断
            float textW = 160f;
            textW = Mathf.Max(textW, _aceDistStyle.CalcSize(new GUIContent(dist)).x);
            textW = Mathf.Max(textW, _aceNameStyle.CalcSize(new GUIContent(model)).x);
            textW = Mathf.Max(textW, _aceNameStyle.CalcSize(new GUIContent(op)).x);
            textW = Mathf.Min(Screen.width - 16f, textW + 16f);
            float left = x + box * 0.5f + 8f;
            if (left + textW > Screen.width - 8f) left = x - box * 0.5f - 8f - textW;
            if (left < 8f) left = 8f;
            float top = y - box * 0.5f - 2f;
            DrawHudLabel(new Rect(left, top, textW, dPx + 4f), dist, _aceDistStyle, _aceDistShadow);
            DrawHudLabel(new Rect(left, top + dPx + 2f, textW, nPx + 4f), model, _aceNameStyle, _aceNameShadow);
            DrawHudLabel(new Rect(left, top + dPx + nPx + 5f, textW, nPx + 4f), op, _aceNameStyle, _aceNameShadow);
        }

    }
}
