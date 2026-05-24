using HarmonyLib;
using Verse;
using Verse.AI;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Reflection;
using System.Linq;
using UnityEngine;

namespace Rimcores
{
    // --- SETTINGS (ENGLISH) ---
    public enum OptiLevel { Simple, Medium, Aggressive }

    public class RimcoresSettings : ModSettings
    {
        public OptiLevel level = OptiLevel.Aggressive;
        public bool optiDraft = true;
        public bool optiRaids = true;
        public bool optiVisitors = true;
        public List<Pawn> storedPawns = new List<Pawn>();
        public HashSet<int> FrozenPawns = new HashSet<int>();

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref level, "level", OptiLevel.Aggressive);
            Scribe_Values.Look(ref optiDraft, "optiDraft", true);
            Scribe_Values.Look(ref optiRaids, "optiRaids", true);
            Scribe_Values.Look(ref optiVisitors, "optiVisitors", true);
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
            listing.Label("=== PERFORMANCE LEVELS (Target 200+ TPS) ===");
            if (listing.RadioButton("Simple Mode (Parallel Every Tick)", settings.level == OptiLevel.Simple)) settings.level = OptiLevel.Simple;
            if (listing.RadioButton("Medium Mode (8x Speed Staggering)", settings.level == OptiLevel.Medium)) settings.level = OptiLevel.Medium;
            if (listing.RadioButton("Aggressive Mode (20x Speed Staggering)", settings.level == OptiLevel.Aggressive)) settings.level = OptiLevel.Aggressive;
            
            listing.GapLine();
            listing.Label("=== COMPONENT FREEZING (Moods/Social/Needs) ===");
            listing.CheckboxLabeled("Optimize Colonists in Draft (Combat)", ref settings.optiDraft);
            listing.CheckboxLabeled("Optimize Enemies (Raids)", ref settings.optiRaids);
            listing.CheckboxLabeled("Optimize Visitors & Traders", ref settings.optiVisitors);
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
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0, 0, inRect.width, 35f), "Colonists in Storage");
            Text.Font = GameFont.Small;
            Rect outRect = new Rect(0, 45f, inRect.width, inRect.height - 50f);
            Rect viewRect = new Rect(0, 0, inRect.width - 25f, (RimcoresMod.settings.storedPawns.Count * 38f) + 20f);
            Widgets.BeginScrollView(outRect, ref scrollPos, viewRect);
            float curY = 0;
            for (int i = 0; i < RimcoresMod.settings.storedPawns.Count; i++)
            {
                Pawn p = RimcoresMod.settings.storedPawns[i];
                Widgets.Label(new Rect(5f, curY, 250f, 30f), p.LabelShortCap);
                if (Widgets.ButtonText(new Rect(280f, curY, 100f, 30f), "SPAWN"))
                {
                    GenSpawn.Spawn(p, UI.MouseMapPosition().ToIntVec3(), Find.CurrentMap);
                    RimcoresMod.settings.storedPawns.RemoveAt(i);
                    break;
                }
                curY += 38f;
            }
            Widgets.EndScrollView();
        }
    }

    // --- CORE ENGINE ---
    [StaticConstructorOnStartup]
    public static class RimcoresLoader
    {
        public static Action<object> fastNeeds, fastMind;
        public static Texture2D IconFreeze, IconDeath, IconBox;

        static RimcoresLoader()
        {
            var harmony = new Harmony("guilherme.rimcores");
            
            // Fast Delegates (Performance extrema)
            var mNeeds = AccessTools.Method("RimWorld.Pawn_NeedsTracker:NeedsTrackerTick");
            var mMind = AccessTools.Method("Verse.AI.Pawn_MindState:MindStateTick");
            if (mNeeds != null) fastNeeds = AccessTools.MethodDelegate<Action<object>>(mNeeds);
            if (mMind != null) fastMind = AccessTools.MethodDelegate<Action<object>>(mMind);

            LongEventHandler.ExecuteWhenFinished(() => {
                IconFreeze = ContentFinder<Texture2D>.Get("UI/Commands/freeze", false) ?? BaseContent.BadTex;
                IconDeath = ContentFinder<Texture2D>.Get("UI/Commands/morto", false) ?? BaseContent.BadTex;
                IconBox = ContentFinder<Texture2D>.Get("UI/Commands/box", false) ?? BaseContent.BadTex;
            });

            harmony.PatchAll();
            Log.Message("[Rimcores] v10 Final Engine Active.");
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
            int total = pawns.Count;
            if (total == 0) return;

            tickCycle++;
            var lv = RimcoresMod.settings.level;
            
            // BUCKET RATIO (200 TPS FOCUS)
            int bucket = (lv == OptiLevel.Simple) ? 1 : (lv == OptiLevel.Medium ? 8 : 20);
            int current = tickCycle % bucket;

            Parallel.For(0, total, i => {
                // Apenas processa se for o balde correto ou modo Simple
                if (lv != OptiLevel.Simple && (i % bucket != current)) return;

                Pawn p = pawns[i];
                if (p == null || RimcoresMod.settings.FrozenPawns.Contains(p.thingIDNumber)) return;
                
                try {
                    bool isDrafted = p.Drafted && RimcoresMod.settings.optiDraft;
                    bool isEnemy = (p.Faction != null && p.Faction.HostileTo(Faction.OfPlayer)) && RimcoresMod.settings.optiRaids;
                    bool isVisitor = (p.Faction != null && !p.Faction.IsPlayer && !isEnemy) && RimcoresMod.settings.optiVisitors;

                    // Se qualquer otimização estiver ligada, o pawn entra em modo "Combate/Visita" (Não gasta IA)
                    if (isDrafted || isEnemy || isVisitor) return; 

                    // Processamento escalonado ultra-veloz
                    if (p.needs != null) RimcoresLoader.fastNeeds(p.needs);
                    if (p.mindState != null) RimcoresLoader.fastMind(p.mindState);
                } catch { }
            });

            if (tickCycle >= 1000) tickCycle = 0;
        }
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
                    defaultLabel = "Stock Pawn",
                    icon = RimcoresLoader.IconBox,
                    action = () => {
                        __instance.mindState?.Reset(true, true, true);
                        RimcoresMod.settings.storedPawns.Add(__instance);
                        __instance.DeSpawn();
                    }
                };
                bool isFrozen = RimcoresMod.settings.FrozenPawns.Contains(__instance.thingIDNumber);
                yield return new Command_Action {
                    defaultLabel = isFrozen ? "Unfreeze" : "Freeze",
                    icon = RimcoresLoader.IconFreeze,
                    action = () => {
                        if (isFrozen) RimcoresMod.settings.FrozenPawns.Remove(__instance.thingIDNumber);
                        else RimcoresMod.settings.FrozenPawns.Add(__instance.thingIDNumber);
                    }
                };
                yield return new Command_Action {
                    defaultLabel = "Kill",
                    icon = RimcoresLoader.IconDeath,
                    action = () => __instance.Kill(null)
                };
            }
        }
    }

    [HarmonyPatch(typeof(Pawn), "Tick")]
    public static class FreezeMovementPatch {
        static bool Prefix(Pawn __instance) => !RimcoresMod.settings.FrozenPawns.Contains(__instance.thingIDNumber);
    }
}