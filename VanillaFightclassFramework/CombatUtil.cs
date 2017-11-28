﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Threading;
using robotManager;
using robotManager.FiniteStateMachine;
using robotManager.Helpful;
using wManager.Wow.Class;
using wManager.Wow.Helpers;
using wManager.Wow.ObjectManager;
using wManager.Wow.Enums;
using wManager.Wow.Bot.Tasks;
using wManager.Wow;
using wManager.Events;

class CombatUtil
{

    private static readonly object LockTargeting = new object();
    private static bool _disableTargeting = false;

    private static List<string> AreaSpells = new List<string>{
        "Mass Dispel",
        "Blizzard",
        "Rain of Fire",
        "Freeze",
        "Volley",
        "Flare",
        "Hurricane",
        "Flamestrike",
        "Distract"
    };

    public static void Start()
    {
        FightEvents.OnFightLoop += FightEvents_OnFightLoop;
    }

    private static void FightEvents_OnFightLoop(WoWUnit unit, CancelEventArgs cancelable)
    {
        while (_disableTargeting)
        {
            Thread.Sleep(100);
        }
    }

    public static void Stop()
    {
        FightEvents.OnFightLoop += FightEvents_OnFightLoop;
    }

    

    public static WoWUnit FindFriend(Func<WoWUnit, bool> predicate)
    {
        if (ObjectManager.Me.HealthPercent < 70)
            return ObjectManager.Me;
        return ObjectManager.GetObjectWoWPlayer().Where(u =>
        u.IsAlive &&
        u.Reaction == Reaction.Friendly &&
        predicate(u) &&
        !TraceLine.TraceLineGo(ObjectManager.Me.Position, u.Position, CGWorldFrameHitFlags.HitTestSpellLoS)).
        OrderBy(u => u.HealthPercent).FirstOrDefault();
    }

    public static WoWUnit FindEnemy(Func<WoWUnit, bool> predicate)
    {
        return FindEnemy(ObjectManager.GetObjectWoWUnit(), predicate);
    }

    public static WoWUnit FindEnemyPlayer(Func<WoWUnit, bool> predicate)
    {
        return FindEnemy(ObjectManager.GetObjectWoWPlayer(), predicate);
    }

    public static WoWUnit FindEnemyCasting(Func<WoWUnit, bool> predicate)
    {
        return FindEnemyCasting(ObjectManager.GetObjectWoWUnit(), predicate);
    }

    public static WoWUnit FindPlayerCasting(Func<WoWUnit, bool> predicate)
    {
        return FindEnemyCasting(ObjectManager.GetObjectWoWPlayer(), predicate);
    }

    public static WoWUnit FindEnemyCastingOnMe(Func<WoWUnit, bool> predicate)
    {
        return FindEnemyCastingOnMe(ObjectManager.GetObjectWoWUnit(), predicate);
    }

    public static WoWUnit FindPlayerCastingOnMe(Func<WoWUnit, bool> predicate)
    {
        return FindEnemyCastingOnMe(ObjectManager.GetObjectWoWPlayer(), predicate);
    }

    private static WoWUnit FindEnemyCasting(IEnumerable<WoWUnit> units, Func<WoWUnit, bool> predicate)
    {
        return FindEnemy(units, (u) => predicate(u) && u.IsCast);
    }

    private static WoWUnit FindEnemyCastingOnMe(IEnumerable<WoWUnit> units, Func<WoWUnit, bool> predicate)
    {
        return FindEnemyCasting(units, (u) => predicate(u) && u.IsTargetingMe);
    }

    private static WoWUnit FindEnemy(IEnumerable<WoWUnit> units, Func<WoWUnit, bool> predicate)
    {
        return units.Where(u =>
        u.IsAlive &&
        (int)u.Reaction < 3 &&
        predicate(u) &&
        !TraceLine.TraceLineGo(ObjectManager.Me.Position, u.Position, CGWorldFrameHitFlags.HitTestSpellLoS))
        .OrderBy(u => u.GetDistance).FirstOrDefault();
    }

    public static WoWUnit BotTarget(Func<WoWUnit, bool> predicate)
    {
        var target = ObjectManager.Target;
        return !TraceLine.TraceLineGo(ObjectManager.Me.Position, target.Position, CGWorldFrameHitFlags.HitTestSpellLoS) && predicate(target) ? target : null;
    }

    public static WoWUnit FindMe()
    {
        return ObjectManager.Me;
    }

    public static WoWUnit FindMe(Func<WoWUnit, bool> predicate)
    {
        return ObjectManager.Me;
    }

    public static bool IsAutoRepeating(string name)
    {
        string luaString = @"
        local i = 1
        while true do
            local spellName, spellRank = GetSpellName(i, BOOKTYPE_SPELL);
            if not spellName then
                break;
            end
   
            -- use spellName and spellRank here
            if(spellName == ""{0}"") then
                PickupSpell(i, BOOKTYPE_SPELL);
                PlaceAction(1);
                ClearCursor();
                return IsAutoRepeatAction(1)
            end

            i = i + 1;
        end
        return false;";
        return Lua.LuaDoString<bool>(Extensions.FormatLua(luaString, name));
    }

    public static bool CanWand()
    {
        return !IsAutoRepeating("Shoot") /*&& EquippedItems.GetEquippedItem(WoWInventorySlot.Ranged).GetItemInfo.ItemType == "Wands"*/;
    }

    public static void CastBuff(RotationSpell buff, WoWUnit target)
    {
        switch (buff.Spell.Name)
        {
            case "Power Word: Fortitude" when target.HaveBuff("Prayer of Fortitude"):
                return;
            case "Divine Spirit" when target.HaveBuff("Prayer of Spirit"):
                return;
            case "Blessing of Kings" when target.HaveBuff("Greater Blessing of Kings"):
                return;
        }

        if (buff.IsKnown() && buff.CanCast() && !target.HaveBuff(buff.Spell.Name))
        {
            CastSpell(buff, target);
        }
    }

    public static void CastBuff(RotationSpell buff)
    {
        switch (buff.Spell.Name)
        {
            case "Power Word: Fortitude" when ObjectManager.Me.HaveBuff("Prayer of Fortitude"):
                return;
            case "Divine Spirit" when ObjectManager.Me.HaveBuff("Prayer of Spirit"):
                return;
            case "Blessing of Kings" when ObjectManager.Me.HaveBuff("Greater Blessing of Kings"):
                return;
            case "Arcane Intellect" when ObjectManager.Me.HaveBuff("Arcane Brilliance"):
                return;
        }

        if (buff.IsKnown() && buff.CanCast() && !buff.Spell.HaveBuff)
        {
            buff.Spell.Launch(false, false, false, "player");
        }
    }

    public static bool CastSpell(RotationSpell spell, WoWUnit unit, bool force)
    {
        // targetfinder function already checks that they are in LoS
        if (unit == null || !spell.IsKnown() || !spell.CanCast() || !unit.IsValid || unit.IsDead)
        {
            return false;
        }

        if (wManager.wManagerSetting.CurrentSetting.IgnoreFightGoundMount && ObjectManager.Me.IsMounted)
        {
            return false;
        }

        MountTask.DismountMount();

        if (ObjectManager.Me.IsCast && !force)
            return false;

        if (ObjectManager.Me.GetMove && spell.Spell.CastTime > 0)
            //MovementManager.StopMove();
            MovementManager.StopMoveTo(false, Usefuls.Latency + 500);

        if (force)
            Lua.LuaDoString("SpellStopCasting();");

        if (AreaSpells.Contains(spell.Spell.Name))
        {
            /*spell.Launch(true, true, false);
                Thread.Sleep(Usefuls.Latency + 50);
                ClickOnTerrain.Pulse(unit.Position);*/

            Lua.LuaDoString("CastSpellByName(\"" + spell.FullName() + "\")");
            //SpellManager.CastSpellByIDAndPosition(spell.Spell.Id, unit.Position);
            ClickOnTerrain.Pulse(unit.Position);
        }
        else
        {
            if (unit.Guid != ObjectManager.Me.Guid)
            {
                FaceUnit(unit);
            }

            _disableTargeting = true;
            WoWUnit temp = ObjectManager.Target;
            TargetUnit(unit);
            Lua.LuaDoString("CastSpellByName(\"" + spell.FullName() + "\")");
            TargetUnit(temp);
            //SpellManager.CastSpellByNameOn(spell.FullName(), GetLuaId(unit));
            //Interact.InteractObject also works and can be used to target another unit
            _disableTargeting = false;
        }
        return true;
    }

    public static float GetGlobalCooldown()
    {
        //check several spells in case one doesn't have a GCD
        for (var i = 1; i <= 10; i++)
        {
            Spell spell = SpellManager.SpellBook()[i];

            string luaString = @"
            cooldownLeft = 0;

            local start, duration, enabled = GetSpellCooldown(""{0}"", BOOKTYPE_SPELL);
            if enabled == 1 and start > 0 and duration > 0 then
                cooldownLeft = duration - (GetTime() - start)
            end";
            
            float duration = Lua.LuaDoString<float>(Extensions.FormatLua(luaString, i), "cooldownLeft");
            if ((duration <= 1.5 && duration != 0))
                return spell.GetCooldown();

        }
        
        return 0;
    }

    public static bool CastSpell(RotationSpell spell, WoWUnit unit)
    {
        return CastSpell(spell, unit, false);
    }

    public static WoWUnit GetWoWObjectByLuaUnitId(string luaUnitId)
    {
        ulong guid = GetGUIDForLuaGUID(Lua.LuaDoString("guid = UnitGUID('" + luaUnitId + "')", "guid"));
        if (!string.IsNullOrWhiteSpace(luaUnitId))
            return ObjectManager.GetObjectWoWUnit().Where(o => o.Guid == guid).FirstOrDefault();
        return null;
    }

    public static ulong GetGUIDForLuaGUID(string luaId)
    {
        ulong guid;
        ulong.TryParse(luaId.Replace("x", string.Empty), System.Globalization.NumberStyles.HexNumber, null, out guid);
        return guid;

    }

    public static string GetWoWGUIDForGUID(ulong guid)
    {
        var wowGuid = ObjectManager.Me.Guid.ToString("x").ToUpper();
        var c = 16 - wowGuid.Length;
        for (var i = 0; i < c; i++)
        {
            wowGuid = "0" + wowGuid;
        }
        return "0x" + wowGuid;
    }

    public static void Face(Vector3 to, float addRadian = 0)
    {
        ClickToMove.CGPlayer_C__ClickToMove(0, 0, 0, ObjectManager.Me.Guid, (int)ClickToMoveType.Face, (float)System.Math.Atan2(to.Y - ObjectManager.Me.Position.Y, to.X - ObjectManager.Me.Position.X) + addRadian);
        Thread.Sleep(10);
        Move.SitStandOrDescend();
        Lua.LuaDoString("TurnLeftStart();");
        Lua.LuaDoString("TurnLeftStop();");
    }

    public static void FaceUnit(WoWUnit unit)
    {
        ClickToMove.CGPlayer_C__ClickToMove(unit.Position.X, unit.Position.Y, unit.Position.Z, unit.Guid, (int)ClickToMoveType.Face, 0.5f);
        Thread.Sleep(10);
        Move.SitStandOrDescend();
        Lua.LuaDoString("TurnLeftStart();");
        Lua.LuaDoString("TurnLeftStop();");
        //wManager.Wow.Helpers.Keybindings.PressKeybindings(wManager.Wow.Enums.Keybindings.TURNLEFT);
    }

    /// <summary>
    /// Used to calculate new position by parameter.
    /// </summary>
    /// <param name="from">The position to calculate from.</param>
    /// <param name="rotation">The rotation of the object in radians.</param>
    /// <param name="radius">The radius to add.</param>
    /// <returns></returns>
    public static Vector3 CalculatePosition(Vector3 from, float rotation, float radius)
    {
        return new Vector3(System.Math.Sin(rotation) * radius + from.X, System.Math.Cos(rotation) * radius + from.Y, from.Z);
    }

    /// <summary>
    /// Used to calculate atan2 of two positions.
    /// </summary>
    /// <param name="from">Position 1.</param>
    /// <param name="to">Position 2.</param>
    /// <param name="addRadian">Radians to add.</param>
    /// <returns></returns>
    public static float Atan2Rotation(Vector3 from, Vector3 to, float addRadian = 0)
    {
        return (float)System.Math.Atan2(to.Y - from.Y, to.X - from.X) + addRadian;
    }


    /*
     * NOT THREAD SAFE!
     */
    public static string GetLuaId(WoWUnit unit)
    {
        if (unit.Guid == ObjectManager.Me.Guid)
            return "player";
        if (unit.Guid == ObjectManager.Target.Guid)
            return "target";

        SetMouseoverUnit(unit);
        return "mouseover";
    }

    //sets mouseover unit
    public static void Target(WoWUnit unit)
    {
        ulong guid = unit.Guid;

        if (guid == 0)
        {
            return;
        }
        ulong tmp = Memory.WowMemory.Memory.ReadUInt64((uint)Memory.WowMemory.Memory.MainModuleAddress + 0x74E2C8);
        SetMouseoverUnit(unit);
        Lua.LuaDoString(@"TargetUnit(""mouseover"");");
        Memory.WowMemory.Memory.WriteUInt64((uint)Memory.WowMemory.Memory.MainModuleAddress + 0x74E2C8, tmp);
    }

    public static void TargetUnit(WoWUnit unit)
    {
        ulong guid = unit.Guid;

        if (guid == 0)
        {
            return;
        }

        // Allocate memory for the target guid
        uint alloc = Memory.WowMemory.Memory.AllocateMemory(8);

        // Write guid
        Memory.WowMemory.Memory.WriteUInt64(alloc, guid);

        // Create asm
        string[] asm = {
                    $"mov ecx, {alloc}",
                    $"call {0x489A40}",
                    Memory.WowMemory.RetnToHookCode
        };

        lock (LockTargeting)
        {
            // Execute
            Memory.WowMemory.InjectAndExecute(asm);
        }

        // Free memory
        Memory.WowMemory.Memory.FreeMemory(alloc);
    }

    public static void SetMouseoverUnit(WoWUnit unit)
    {
        Memory.WowMemory.Memory.WriteUInt64((uint)Memory.WowMemory.Memory.MainModuleAddress + 0x74E2C8, unit.Guid);
    }
}