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
                case 4: return "TenTick";
                case 5: return "AceRing";
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
            else if (style == "TenTick")
            {
                // 中心点 + 10 根线（每 36° 一根），中间留空
                float dot10 = Mathf.Max(1.2f, s * 0.038f);
                float rIn10 = s * 0.22f, rOut10 = c * 0.94f;
                float halfW10 = Mathf.Max(0.7f, s * 0.019f);
                for (int y = 0; y < s; y++)
                    for (int x = 0; x < s; x++)
                    {
                        float dx = x - c, dy = y - c;
                        float dist = Mathf.Sqrt(dx * dx + dy * dy);
                        float a = 0f;
                        if (dist <= dot10) a = 1f;
                        else if (dist >= rIn10 && dist <= rOut10)
                        {
                            for (int k = 0; k < 10; k++)
                            {
                                float ang = k * Mathf.PI / 5f;
                                float px = Mathf.Cos(ang), py = Mathf.Sin(ang);
                                float along = dx * px + dy * py;
                                float across = Mathf.Abs(-dx * py + dy * px);
                                if (along > 0f && across <= halfW10) { a = 1f; break; }
                            }
                        }
                        t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                    }
            }
            else if (style == "AceRing")
            {
                // 皇牌空战风格大环：外圈厚环 + 8 个外齿 + 内圈细环，中间留空
                float t1 = Mathf.Max(1.8f, s * 0.035f);
                float rOuter = c * 0.78f;
                float rOuterIn = rOuter - t1;
                float toothIn = c * 0.78f;
                float toothOut = c * 0.95f;
                float toothHalfW = Mathf.Max(2.2f, s * 0.040f);
                for (int y = 0; y < s; y++)
                    for (int x = 0; x < s; x++)
                    {
                        float dx = x - c, dy = y - c;
                        float dist = Mathf.Sqrt(dx * dx + dy * dy);
                        float a = 0f;
                        if (dist >= rOuterIn && dist <= rOuter) a = 1f;
                        else if (dist >= toothIn && dist <= toothOut)
                        {
                            for (int k = 0; k < 8; k++)
                            {
                                float ang = k * Mathf.PI / 4f;
                                float px = Mathf.Cos(ang), py = Mathf.Sin(ang);
                                float across = Mathf.Abs(-dx * py + dy * px);
                                if (across <= toothHalfW) { a = 1f; break; }
                            }
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

        void DrawAceTimeArc(float cx, float cy, float radius, float ratio, float thickness)
        {
            if (ratio <= 0.001f) return;
            int segs = Mathf.Clamp(Mathf.CeilToInt(ratio * 140f), 2, 180);
            float sweep = ratio * 360f;
            for (int i = 0; i <= segs; i++)
            {
                float ang = (-90f + sweep * i / segs) * Mathf.Deg2Rad;
                float px = cx + Mathf.Cos(ang) * radius;
                float py = cy + Mathf.Sin(ang) * radius;
                GUI.DrawTexture(new Rect(px - thickness * 0.5f, py - thickness * 0.5f, thickness, thickness), Texture2D.whiteTexture);
            }
        }

    }
}
