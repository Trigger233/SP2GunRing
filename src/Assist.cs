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
        void LateUpdate()
        {
            UpdateFireSpin();

            if (!_on || !CAssistEnabled.Value || _banned || !AimingModeOn() || _craft == null || !HasTarget)
            {
                _lastAssistYaw = _lastAssistPitch = 0f;
                _lastAssistRollErr = 0f;
                _assistIntYaw = _assistIntPitch = 0f;
                _lastAssistRoll = 9999f;
                return;
            }
            try
            {
                if (!_craft.IsPrimaryLocalPlayer) return;
                float gate = Mathf.Clamp(CToggleAssist.Value, 0f, 45f);
                if (gate <= 0f || _angleToLead > gate)
                {
                    _lastAssistYaw = _lastAssistPitch = 0f;
                _lastAssistRollErr = 0f;
                    _assistIntYaw = _assistIntPitch = 0f;
                    _lastAssistRoll = 9999f;
                    return;
                }
                AircraftControls ctl = _craft.Controls;
                if (ctl == null) return;
                if (CAssistMode.Value == 1 && !ctl.FireGuns)
                {
                    _lastAssistYaw = _lastAssistPitch = 0f;
                _lastAssistRollErr = 0f;
                    _assistIntYaw = _assistIntPitch = 0f;
                    _lastAssistRoll = 9999f;
                    return;
                }

                Vector3 toLead = _lead - MuzzlePos();
                if (toLead.sqrMagnitude < 1f) return;
                Vector3 leadLocal = CraftXform().InverseTransformDirection(toLead.normalized);
                Vector3 gunLocal = CraftXform().InverseTransformDirection(AimDir()).normalized;

                // 飞机坐标系：x=右，y=上，z=前。
                // 机枪可能有机体安装偏角，所以用“枪口方向”与“预瞄方向”的差，
                // 而不是默认枪口=机头；这样辅助会把机炮实际指向拉到预瞄点。
                float gunYaw = Mathf.Atan2(gunLocal.x, gunLocal.z);
                float gunPitch = -Mathf.Atan2(gunLocal.y, Mathf.Sqrt(gunLocal.x * gunLocal.x + gunLocal.z * gunLocal.z));
                float leadYaw = Mathf.Atan2(leadLocal.x, leadLocal.z);
                float leadPitch = -Mathf.Atan2(leadLocal.y, Mathf.Sqrt(leadLocal.x * leadLocal.x + leadLocal.z * leadLocal.z));
                float yawErr = leadYaw - gunYaw;
                float pitchErr = leadPitch - gunPitch;
                float dt = Mathf.Max(0.001f, Time.deltaTime);
                float yawD = (yawErr - _lastAssistYaw) / dt;
                float pitchD = (pitchErr - _lastAssistPitch) / dt;
                _lastAssistYaw = yawErr;
                _lastAssistPitch = pitchErr;

                // 滚转时把积分向量从旧机体坐标转到新机体坐标，否则会沿旧轴推杆
                float rollNow = CraftRollDeg();
                if (_lastAssistRoll > 9998f) _lastAssistRoll = rollNow;
                else
                {
                    float dRoll = Mathf.DeltaAngle(_lastAssistRoll, rollNow);
                    if (Mathf.Abs(dRoll) > 0.001f && Mathf.Abs(dRoll) < 45f)
                    {
                        float rad = -dRoll * Mathf.Deg2Rad;
                        float cs = Mathf.Cos(rad);
                        float sn = Mathf.Sin(rad);
                        float ny = _assistIntYaw * cs - _assistIntPitch * sn;
                        float np = _assistIntYaw * sn + _assistIntPitch * cs;
                        _assistIntYaw = ny;
                        _assistIntPitch = np;
                    }
                    _lastAssistRoll = rollNow;
                }

                // 积分器：I 值改变时由 SettingChanged 清空；输出限制在 [-1,1]
                float yawI = CAssistYawI.Value;
                if (yawI > 0.0001f)
                {
                    float lim = 1f / yawI;
                    _assistIntYaw = Mathf.Clamp(_assistIntYaw + yawErr * dt, -lim, lim);
                }
                float pitchI = CAssistPitchI.Value;
                if (pitchI > 0.0001f)
                {
                    float lim = 1f / pitchI;
                    _assistIntPitch = Mathf.Clamp(_assistIntPitch + pitchErr * dt, -lim, lim);
                }

                float iy = yawI > 0.0001f ? Mathf.Clamp(yawI * _assistIntYaw, -1f, 1f) : 0f;
                float ip = pitchI > 0.0001f ? Mathf.Clamp(pitchI * _assistIntPitch, -1f, 1f) : 0f;
                float ay = Mathf.Clamp(CAssistYawP.Value * yawErr + CAssistYawD.Value * yawD + iy, -1f, 1f);
                float ap = Mathf.Clamp(CAssistPitchP.Value * pitchErr + CAssistPitchD.Value * pitchD + ip, -1f, 1f);
                // 滚转辅助：只做 PD，不加积分器；用水平瞄准误差作为滚转误差，向目标方向压坡度
                float rollErr = yawErr;
                float rollD = (rollErr - _lastAssistRollErr) / dt;
                _lastAssistRollErr = rollErr;
                float ar = Mathf.Clamp(CAssistRollP.Value * rollErr + CAssistRollD.Value * rollD, -1f, 1f);
                // 灵敏度缩放：clamp01((IAS/TAS) / max(IAS,100)^2 * k)，k=SpeedRef^2。
                // SpeedRef 默认 200m/s，所以 IAS=TAS=200 时缩放=1；速度越高辅助越弱。
                if (CAssistSpeedScale.Value)
                {
                    try
                    {
                        float ias = Mathf.Max(0f, CraftFloat(_craft, "IAS"));
                        float tas = Mathf.Max(1f, CraftFloat(_craft, "TAS"));
                        float speed = Mathf.Max(100f, ias);
                        float k = CAssistSpeedRef.Value * CAssistSpeedRef.Value;
                        float speedScale = Mathf.Clamp01((ias / tas) / (speed * speed) * k);
                        ay *= speedScale; ap *= speedScale; ar *= speedScale;
                    }
                    catch { }
                }
                float mix = Mathf.Clamp01(CAssistMix.Value);

                // 混合：在玩家当前输入上叠加，绝对值最终不超过 1
                ctl.Yaw = Mathf.Clamp(ctl.Yaw + ay * mix, -1f, 1f);
                ctl.Pitch = Mathf.Clamp(ctl.Pitch + ap * mix, -1f, 1f);
                ctl.Roll = Mathf.Clamp(ctl.Roll + ar * mix, -1f, 1f);
            }
            catch { }
        }

        void UpdateFireSpin()
        {
            if (!_on || _craft == null)
            {
                _fireSpinAngle = 0f;
                return;
            }
            try
            {
                if (!_craft.IsPrimaryLocalPlayer) return;
                AircraftControls ctl = _craft.Controls;
                if (ctl == null) { _fireSpinAngle = 0f; return; }
                if (CFireSpin.Value && ctl.FireGuns)
                    _fireSpinAngle = Mathf.Repeat(_fireSpinAngle + CSpinSpeed.Value * Time.deltaTime, 360f);
                else
                    _fireSpinAngle = 0f;
            }
            catch { _fireSpinAngle = 0f; }
        }

    }
}
