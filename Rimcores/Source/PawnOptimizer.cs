using HarmonyLib;
using Verse;
using Verse.AI;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Reflection;
using System.Linq;
using System.Collections.Concurrent;
using UnityEngine;

namespace Rimcores
{
    // --- CONFIGURAÇÕES ---
    public enum OptiLevel { Simple, VOID, KRYPTON }

    public class RimcoresSettings : ModSettings
    {
        public OptiLevel level = OptiLevel.KRYPTON;
        public bool optiDraft = true, optiRaids = true, optiVisitors = true, optiItems = true, optiWorld = true;
        public float speed1 = 60f, speed2 = 180f, speed3 = 900f, speed4 = 5000f;
        public List<Pawn> storedPawns = new List<Pawn>();
        public HashSet<int> FrozenPawns = new HashSet<int>();

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref level, "level", OptiLevel.KRYPTON);
            Scribe_Values.Look(ref optiDraft, "optiDraft", true);
            Scribe_Values.Look(ref optiRaids, "optiRaids", true);
            Scribe_Values.Look(ref optiVisitors, "optiVisitors", true);
            Scribe_Values.Look(ref optiItems, "optiItems", true);
            Scribe_Values.Look(ref optiWorld, "optiWorld", true);
            Scribe_Values.Look(ref speed1, "speed1", 60f);
            Scribe_Values.Look(ref speed2, "speed2", 180f);
            Scribe_Values.Look(ref speed3, "speed3", 900f);
            Scribe_Values.Look(ref speed4, "speed4", 5000f);
            Scribe_Collections.Look(ref storedPawns, "storedPawns", LookMode.Deep);
            Scribe_Collections.Look(ref FrozenPawns, "FrozenPawns", LookMode.Value);
            if (storedPawns == null) storedPawns = new List<Pawn>();
        }
    }

    public class RimcoresMod : Mod
    {
        public static RimcoresSettings settings;
        public RimcoresMod(ModContentPack content) : base(content) { settings = GetSettings<RimcoresSettings>(); }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);
            
            listing.Label("=== SPEED SLIDERS (TPS) ===");
            listing.Label("Speed 1: " + (int)settings.speed1); settings.speed1 = listing.Slider(settings.speed1, 30f, 500f);
            listing.Label("Speed 2: " + (int)settings.speed2); settings.speed2 = listing.Slider(settings.speed2, 60f, 1500f);
            listing.Label("Speed 3: " + (int)settings.speed3); settings.speed3 = listing.Slider(settings.speed3, 120f, 5000f);
            listing.Label("Speed 4: " + (int)settings.speed4); settings.speed4 = listing.Slider(settings.speed4, 300f, 30000f);
            
            listing.GapLine();
            listing.Label("=== OPTIMIZATION LEVELS ===");
            if (listing.RadioButton("Simple (Parallel Tick)", settings.level == OptiLevel.Simple)) settings.level = OptiLevel.Simple;
            if (listing.RadioButton("VOID (Staggered Engine)", settings.level == OptiLevel.VOID)) settings.level = OptiLevel.VOID;
            if (listing.RadioButton("KRYPTON (Direct Multithread - Best for CPU)", settings.level == OptiLevel.KRYPTON)) settings.level = OptiLevel.KRYPTON;
            
            listing.GapLine();
            listing.Label("=== TOGGLES ===");
            listing.CheckboxLabeled("Optimize Drafted Colonists", ref settings.optiDraft);
            listing.CheckboxLabeled("Optimize Enemies (Raids)", ref settings.optiRaids);
            listing.CheckboxLabeled("Optimize Visitors", ref settings.optiVisitors);
            listing.CheckboxLabeled("Optimize Map Items & Chunks", ref settings.optiItems);
            listing.CheckboxLabeled("Optimize World & Planet", ref settings.optiWorld);
            
            listing.End();
        }
        public override string SettingsCategory() => "Rimcores";
    }

    // --- STORAGE UI ---
    public class MainTabWindow_PawnStock : MainTabWindow
    {
        private Vector2 scrollPos = Vector2.zero;
        public override Vector2 RequestedTabSize => new Vector2(450f, 600f);
        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium; Widgets.Label(new Rect(0, 0, inRect.width, 35f), "Storage");
            Text.Font = GameFont.Small;
            if (RimcoresMod.settings.storedPawns == null) RimcoresMod.settings.storedPawns = new List<Pawn>();
            Rect outRect = new Rect(0, 45f, inRect.width, inRect.height - 55f);
            Rect viewRect = new Rect(0, 0, inRect.width - 25f, (RimcoresMod.settings.storedPawns.Count * 38f) + 20f);
            Widgets.BeginScrollView(outRect, ref scrollPos, viewRect);
            float curY = 0;
            for (int i = 0; i < RimcoresMod.settings.storedPawns.Count; i++)
            {
                Pawn p = RimcoresMod.settings.storedPawns[i];
                Widgets.Label(new Rect(5, curY, 200f, 30f), p.LabelShortCap);
                if (Widgets.ButtonText(new Rect(inRect.width - 130f, curY, 100f, 28f), "SPAWN", true, true, true, null))
                {
                    GenSpawn.Spawn(p, UI.MouseMapPosition().ToIntVec3(), Find.CurrentMap);
                    RimcoresMod.settings.storedPawns.RemoveAt(i); break;
                }
                curY += 38f;
            }
            Widgets.EndScrollView();
        }
    }

    [StaticConstructorOnStartup]
    public static class RimcoresLoader
    {
        public static Action<object> fNeeds, fMind, fJobs, fPath;
        public static Texture2D IconFreeze, IconDeath, IconBox;

        static RimcoresLoader()
        {
            var harmony = new Harmony("guilherme.rimcores");
            try {
                fNeeds = AccessTools.MethodDelegate<Action<object>>(AccessTools.Method("RimWorld.Pawn_NeedsTracker:NeedsTrackerTick"));
                fMind = AccessTools.MethodDelegate<Action<object>>(AccessTools.Method("Verse.AI.Pawn_MindState:MindStateTick"));
                fJobs = AccessTools.MethodDelegate<Action<object>>(AccessTools.Method("Verse.AI.Pawn_JobTracker:JobTrackerTick"));
                fPath = AccessTools.MethodDelegate<Action<object>>(AccessTools.Method("Verse.AI.Pawn_PathFollower:PawnPathFollowerTick"));
            } catch { }

            LongEventHandler.ExecuteWhenFinished(() => {
                IconFreeze = ContentFinder<Texture2D>.Get("UI/Commands/freeze", false) ?? BaseContent.BadTex;
                IconDeath = ContentFinder<Texture2D>.Get("UI/Commands/morto", false) ?? BaseContent.BadTex;
                IconBox = ContentFinder<Texture2D>.Get("UI/Commands/box", false) ?? BaseContent.BadTex;
            });
            harmony.PatchAll();
            Log.Message("[Rimcores] v29 Loaded. Ready for 200+ pawns.");
        }
    }

    // --- ENGINE: SPEED BREAKER ---
    [HarmonyPatch(typeof(TickManager), "TickManagerUpdate")]
    public static class SpeedCeilingPatch
    {
        static bool Prefix(TickManager __instance)
        {
            if (__instance.Paused || __instance.CurTimeSpeed == TimeSpeed.Normal) return true;
            float targetTPS = (__instance.CurTimeSpeed == TimeSpeed.Fast) ? RimcoresMod.settings.speed2 : 
                             ((__instance.CurTimeSpeed == TimeSpeed.Superfast) ? RimcoresMod.settings.speed3 : RimcoresMod.settings.speed4);

            int burst = (int)(targetTPS / 60f);
            for (int i = 0; i < burst; i++)
            {
                if (__instance.Paused) break;
                __instance.DoSingleTick();
                if (i > 30 && (Time.realtimeSinceStartup % 0.0166f) > 0.015f) break;
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(TickManager), "DoSingleTick")]
    public static class MultithreadTickPatch
    {
        private static int tickCycle = 0;
        static void Postfix()
        {
            if (Find.TickManager.Paused || Find.CurrentMap == null) return;
            var pawns = Find.CurrentMap.mapPawns.AllPawnsSpawned;
            tickCycle++;

            var lv = RimcoresMod.settings.level;
            int bucket = (lv == OptiLevel.KRYPTON) ? 1 : (lv == OptiLevel.VOID ? 10 : 1);
            int current = tickCycle % bucket;

            Parallel.For(0, pawns.Count, i => {
                if (lv == OptiLevel.VOID && (i % bucket != current)) return;
                Pawn p = pawns[i];
                if (p == null || RimcoresMod.settings.FrozenPawns.Contains(p.thingIDNumber)) return;
                
                try {
                    bool isDraft = p.Drafted && RimcoresMod.settings.optiDraft;
                    bool isEnemy = (p.Faction != null && p.Faction.HostileTo(Faction.OfPlayer)) && RimcoresMod.settings.optiRaids;
                    bool isVisitor = (p.Faction != null && !p.Faction.IsPlayer && !isEnemy) && RimcoresMod.settings.optiVisitors;
                    if (isDraft || isEnemy || isVisitor) return; 

                    if (p.needs != null) RimcoresLoader.fNeeds?.Invoke(p.needs);
                    if (p.mindState != null) RimcoresLoader.fMind?.Invoke(p.mindState);
                    
                    if (lv == OptiLevel.KRYPTON)
                    {
                        if (p.jobs != null) RimcoresLoader.fJobs?.Invoke(p.jobs);
                        if (p.pather != null) RimcoresLoader.fPath?.Invoke(p.pather);
                    }
                } catch { }
            });

            if (RimcoresMod.settings.optiWorld && tickCycle % 100 == 0)
            {
                Find.World?.WorldTick();
                Find.FactionManager?.FactionManagerTick();
            }
            if (tickCycle >= 5000) tickCycle = 0;
        }
    }

    [HarmonyPatch(typeof(ThingWithComps), "Tick")]
    public static class GlobalStaggerPatch {
        static bool Prefix(ThingWithComps __instance) => !RimcoresMod.settings.optiItems || (__instance is Pawn) || (__instance.thingIDNumber % 60 == Find.TickManager.TicksGame % 60);
    }

    [HarmonyPatch(typeof(Pawn), "GetGizmos")]
    public static class PawnGizmoPatch
    {
        static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn __instance)
        {
            foreach (var g in __result) yield return g;
            if (__instance.Faction == Faction.OfPlayer)
            {
                yield return new Command_Action {
                    defaultLabel = "Stock", icon = RimcoresLoader.IconBox,
                    action = () => { __instance.mindState?.Reset(true, true, true); RimcoresMod.settings.storedPawns.Add(__instance); __instance.DeSpawn(); }
                };
                bool isFrozen = RimcoresMod.settings.FrozenPawns.Contains(__instance.thingIDNumber);
                yield return new Command_Action {
                    defaultLabel = isFrozen ? "Unfreeze" : "Freeze", icon = RimcoresLoader.IconFreeze,
                    action = () => { if (isFrozen) RimcoresMod.settings.FrozenPawns.Remove(__instance.thingIDNumber); else RimcoresMod.settings.FrozenPawns.Add(__instance.thingIDNumber); }
                };
                yield return new Command_Action { defaultLabel = "Kill", icon = RimcoresLoader.IconDeath, action = () => __instance.Kill(null) };
            }
        }
    }

    [HarmonyPatch(typeof(Pawn), "Tick")]
    public static class FreezeMovementPatch {
        static bool Prefix(Pawn __instance) => !RimcoresMod.settings.FrozenPawns.Contains(__instance.thingIDNumber);
    }
}