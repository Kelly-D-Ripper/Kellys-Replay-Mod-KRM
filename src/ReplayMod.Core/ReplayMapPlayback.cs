using System;
using System.Collections.Generic;
using UnityEngine;

namespace ReplayMod.Core;

// Native tracking/radar discovery does not run for recorded spawns. Maintain
// icons from this controller's exact actor lifetimes instead, with fixed work
// per tick. No scene scan, map regeneration, faction or camera selection change.
public sealed class ReplayMapPlayback
{
    private sealed class Actor
    {
        internal uint id;
        internal Unit unit;
        internal DynamicMap attemptedMap;
        internal double retryAfter;
        internal int failures;
    }
    private readonly UnitController controller;
    private readonly Dictionary<uint, Actor> actors = new Dictionary<uint, Actor>();
    private readonly Queue<Actor> pending = new Queue<Actor>();
    private readonly Queue<Actor> cycle = new Queue<Actor>();
    private DynamicMap map;
    private double nextRefresh;
    private int warningCount;
    private const int MaximumWarnings = 8;
    public ReplayMapPlayback(UnitController controller) { this.controller = controller; }

    public void Track(uint id, Unit unit)
    {
        // Spawn must not depend on native map/UI initialization. Replacement
        // entries retire naturally when the bounded queue reaches them.
        if (unit == null) { actors.Remove(id); return; }
        var actor = new Actor { id = id, unit = unit };
        actors[id] = actor; pending.Enqueue(actor);
    }
    private static bool Ready(DynamicMap value) => value != null && value.gameObject.activeInHierarchy &&
        value.iconLayer != null && value.unitMapIconPrefab != null && value.mapImage != null &&
        SceneSingleton<CombatHUD>.i != null && SceneSingleton<MapOptions>.i != null && SceneSingleton<TargetListSelector>.i != null;

    public void Refresh(double realTime)
    {
        if (double.IsNaN(realTime) || double.IsInfinity(realTime) || realTime < nextRefresh) return;
        nextRefresh = realTime + ReplayMapPolicy.RefreshInterval;
        DynamicMap currentMap;
        try { currentMap = SceneSingleton<DynamicMap>.i; if (!Ready(currentMap)) return; }
        catch (Exception error) { nextRefresh = realTime + 1; Warn("map readiness", null, error); return; }
        map = currentMap;
        // New launches precede maintenance of already-visible buildings. They
        // still consume the same fixed native-UI work budget.
        int remaining = System.Math.Min(ReplayMapPolicy.RefreshBudget, pending.Count + cycle.Count);
        while (remaining-- > 0 && (pending.Count > 0 || cycle.Count > 0))
        {
            var actor = pending.Count > 0 ? pending.Dequeue() : cycle.Dequeue();
            try
            {
                if (!actors.TryGetValue(actor.id, out var current) || !ReferenceEquals(current, actor))
                { RemoveIcon(currentMap, actor.unit); continue; }
                if (actor.unit == null || !ReferenceEquals(controller.FindActor(actor.id), actor.unit))
                { actors.Remove(actor.id); RemoveIcon(currentMap, actor.unit); continue; }
                if (!ReferenceEquals(actor.attemptedMap, currentMap))
                { actor.attemptedMap = currentMap; actor.failures = 0; actor.retryAfter = 0; }
                if (realTime < actor.retryAfter) continue;
                Refresh(currentMap, actor); actor.failures = 0; actor.retryAfter = 0;
            }
            catch (Exception error)
            {
                actor.failures = Math.Min(20, actor.failures + 1);
                actor.retryAfter = realTime + Math.Min(10, Math.Pow(2, Math.Min(4, actor.failures - 1)));
                if (actor.failures == 1 || actor.failures == 3) Warn("icon refresh", actor, error);
            }
            finally
            {
                // One failing icon must not disappear from maintenance or
                // prevent the next actor's native icon from being refreshed.
                if (actors.TryGetValue(actor.id, out var current) && ReferenceEquals(current, actor)) cycle.Enqueue(actor);
            }
        }
    }
    private void Refresh(DynamicMap currentMap, Actor actor)
    {
        var unit = actor.unit;
        bool visible = ReplayMapPolicy.Visible(ReferenceEquals(controller.FindActor(actor.id), unit), unit.disabled,
            unit is Building building && building.hideOnMap, controller.IsTerminalMissile(actor.id, unit), unit.definition?.mapIconSize ?? 0);
        if (!visible) { RemoveIcon(currentMap, unit); return; }
        if (!currentMap.TryGetIcon(unit, out var icon) || icon == null) icon = currentMap.SpawnIconForUnit(unit);
        // An offline replay displays recorded positions, not a live faction's
        // stale last-known tracking position. Colours still use native HQ data.
        if (icon != null) icon.trackingInfo = null;
    }
    public void Forget(uint id)
    {
        if (!actors.TryGetValue(id, out var actor)) return;
        try { RemoveFromMaps(actor); }
        finally
        {
            if (actors.TryGetValue(id, out var current) && ReferenceEquals(current, actor)) actors.Remove(id);
            actor.unit = null;
        }
    }
    private static void RemoveIcon(DynamicMap currentMap, Unit unit)
    {
        // Unity's destroyed-object null must not hide a still-indexed icon key.
        if (currentMap == null || ReferenceEquals(unit, null) || !currentMap.TryGetIcon(unit, out var icon) || icon == null) return;
        try { currentMap.RemoveIcon(icon); }
        finally { icon.RemoveIcon(); }
    }
    private void RemoveFromMaps(Actor actor)
    {
        DynamicMap currentMap = null;
        try { currentMap = SceneSingleton<DynamicMap>.i; }
        catch (Exception error) { Warn("map lookup during cleanup", actor, error); }
        try { RemoveIcon(currentMap, actor.unit); }
        catch (Exception error) { Warn("current map icon cleanup", actor, error); }
        if (!ReferenceEquals(map, currentMap))
            try { RemoveIcon(map, actor.unit); }
            catch (Exception error) { Warn("previous map icon cleanup", actor, error); }
    }
    private void Warn(string operation, Actor actor, Exception error)
    {
        if (warningCount >= MaximumWarnings) return;
        warningCount++;
        string message = error.Message ?? error.GetType().Name;
        if (message.Length > 180) message = message.Substring(0, 180);
        try { Plugin.SafeLogWarning("Replay " + operation + (actor == null ? "" : " for actor " + actor.id) + " failed; replay playback continues: " + message); }
        catch (Exception) { }
    }
    public void Clear()
    {
        try
        {
            foreach (var actor in actors.Values) RemoveFromMaps(actor);
        }
        finally { actors.Clear(); pending.Clear(); cycle.Clear(); map = null; nextRefresh = 0; }
    }
}
