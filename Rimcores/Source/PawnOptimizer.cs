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
using UnityEngine;

namespace Rimcores
{
    // --- SETTINGS ---
    public enum OptiLevel { Simple, Medium, Aggressive, Brutal, Overlord, VOID }

    public class RimcoresSettings : ModSettings
    {
        public OptiLevel level = OptiLevel.VOID;
        public bool optiDraft = true, optiRaids = true, optiVisitors = true, optiItems = true, optiWorld = true;
        public float speed1 = 60f, speed2 = 180f, speed3 = 1200f, speed4 = 15000f;
        public List<Pawn> storedPawns = new List<Pawn>();
        public HashSet<int> FrozenPawns = new HashSet<int>();

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref level, "level", OptiLevel.VOID);
            Scribe_Values.Look(ref optiDraft, "optiDraft", true);
            Scribe_Values.Look(ref optiRaids, "optiRaids", true);
            Scribe_Values.Look(ref optiVisitors, "optiVisitors", true);
            Scribe_Values.Look(ref optiItems, "optiItems", true);
            Scribe_Values.Look(ref optiWorld, "optiWorld", true);
            Scribe_Values.Look(ref speed1, "speed1", 60f);
            Scribe_Values.Look(ref speed2, "speed2", 180f);
            Scribe_Values.Look(ref speed3, "speed3", 1200f);
            Scribe_Values.Look(ref speed4, "speed4", 15000f);
            Scribe_Collections.Look(ref storedPawns, "storedPawns", LookMode.Deep);
            Scribe_Collections.Look(ref FrozenPawns, "FrozenPawns", LookMode.Value);
            if (storedPawns == null) storedPawns = new List<Pawn>();
            if (FrozenPawns == null) FrozenPawns = new HashSet<int>();
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
            listing.Label("=== SPEED LIMITS (TPS TARGET) ===");
            listing.Label("Speed 1: " + (int)settings.speed1); settings.speed1 = listing.Slider(settings.speed1, 30f, 500f);
            listing.Label("Speed 2: " + (int)settings.speed2); settings.speed2 = listing.Slider(settings.speed2, 60f, 1500f);
            listing.Label("Speed 3: " + (int)settings.speed3); settings.speed3 = listing.Slider(settings.speed3, 120f, 5000f);
            listing.Label("Speed 4: " + (int)settings.speed4); settings.speed4 = listing.Slider(settings.speed4, 300f, 30000f);
            
            listing.GapLine();
            listing.Label("=== OPTIMIZATION ENGINE ===");
            if (listing.RadioButton("Simple (Parallel Tick)", settings.level == OptiLevel.Simple)) settings.level = OptiLevel.Simple;
            if (listing.RadioButton("Aggressive (20x Stagger)", settings.level == OptiLevel.Aggressive)) settings.level = OptiLevel.Aggressive;
            if (listing.RadioButton("VOID (150x Stagger - MAX PERFORMANCE)", settings.level == OptiLevel.VOID)) settings.level = OptiLevel.VOID;
            
            listing.GapLine();
            listing.CheckboxLabeled("Optimize Enemies & Drafted", ref settings.optiDraft);
            listing.CheckboxLabeled("Optimize Items & Chunks", ref settings.optiItems);
            listing.CheckboxLabeled("Optimize Planet & Factions", ref settings.optiWorld);
            listing.End();
        }
        public override string SettingsCategory() => "Rimcores";
    }

    // --- PAWN STOCK UI ---
    public class MainTabWindow_PawnStock : MainTabWindow
    {
        private Vector2 scrollPos = Vector2.zero;
        public override Vector2 RequestedTabSize => new Vector2(450f, 600f);
        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium; Widgets.Label(new Rect(0, 0, inRect.width, 35f), "Colonist Storage Bank");
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
                if (Widgets.ButtonText(new Rect(280f, curY, 100f, 30f), "SPAWN", true, true, true, null))
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
        public static MethodInfo mNeeds, mMind, mJobs;
        public static Texture2D IconFreeze, IconDeath, IconBox;

        static RimcoresLoader()
        {
            var harmony = new Harmony("guilherme.rimcores");
            
            // USANDO INVOKE DIRETO PARA EVITAR O ERRO DO LOG (MAIOR COMPATIBILIDADE)
            mNeeds = AccessTools.Method("RimWorld.Pawn_NeedsTracker:NeedsTrackerTick");
            mMind = AccessTools.Method("Verse.AI.Pawn_MindState:MindStateTick");
            mJobs = AccessTools.Method("Verse.AI.Pawn_JobTracker:JobTrackerTick");

            LongEventHandler.ExecuteWhenFinished(() => {
                IconFreeze = ContentFinder<Texture2D>.Get("UI/Commands/freeze", false) ?? BaseContent.BadTex;
                IconDeath = ContentFinder<Texture2D>.Get("UI/Commands/morto", false) ?? BaseContent.BadTex;
                IconBox = ContentFinder<Texture2D>.Get("UI/Commands/box", false) ?? BaseContent.BadTex;
            });
            harmony.PatchAll();
            Log.Message("[Rimcores] v24 VOID-STABILITY Active. Multi-core engaged.");
        }
    }

    // --- ENGINE: SPEED BREAKER (ULTRA BURST) ---
    [HarmonyPatch(typeof(TickManager), "TickManagerUpdate")]
    public static class SpeedCeilingPatch
    {
        static bool Prefix(TickManager __instance)
        {
            if (__instance.Paused || __instance.CurTimeSpeed == TimeSpeed.Normal) return true;

            float targetTPS = 60f;
            if (__instance.CurTimeSpeed == TimeSpeed.Fast) targetTPS = RimcoresMod.settings.speed2;
            else if (__instance.CurTimeSpeed == TimeSpeed.Superfast) targetTPS = RimcoresMod.settings.speed3;
            else if (__instance.CurTimeSpeed == TimeSpeed.Ultrafast) targetTPS = RimcoresMod.settings.speed4;

            int burst = (int)(targetTPS / 60f);
            for (int i = 0; i < burst; i++)
            {
                if (__instance.Paused) break;
                __instance.DoSingleTick();
                // OTIMIZAÇÃO DE CPU: Se o frame real demorar demais, permite o desenho gráfico
                if (i > 20 && i % 10 == 0 && (Time.realtimeSinceStartup % 0.016f) > 0.015f) break;
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
            int bucket = (lv == OptiLevel.Simple) ? 1 : (lv == OptiLevel.Medium ? 8 : (lv == OptiLevel.Aggressive ? 20 : 150));
            int current = tickCycle % bucket;

            Parallel.For(0, pawns.Count, i => {
                if (lv != OptiLevel.Simple && (i % bucket != current)) return;
                Pawn p = pawns[i];
                if (p == null || RimcoresMod.settings.FrozenPawns.Contains(p.thingIDNumber)) return;
                try {
                    bool isDraft = p.Drafted && RimcoresMod.settings.optiDraft;
                    bool isEnemy = (p.Faction != null && p.Faction.HostileTo(Faction.OfPlayer)) && RimcoresMod.settings.optiDraft;
                    if (isDraft || isEnemy) return; 

                    RimcoresLoader.mNeeds?.Invoke(p.needs, null);
                    RimcoresLoader.mMind?.Invoke(p.mindState, null);
                    if (lv == OptiLevel.VOID) RimcoresLoader.mJobs?.Invoke(p.jobs, null);
                } catch { }
            });

            if (RimcoresMod.settings.optiWorld && tickCycle % 60 == 0)
            {
                Find.World?.WorldTick();
                Find.FactionManager?.FactionManagerTick();
            }
            if (tickCycle >= 5000) tickCycle = 0;
        }
    }

    [HarmonyPatch(typeof(ThingWithComps), "Tick")]
    public static class CompStaggerPatch {
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