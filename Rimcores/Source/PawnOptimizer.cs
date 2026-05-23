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
    // --- SETTINGS ---
    public enum OptiLevel { Simple, Medium, Aggressive }
    public class RimcoresSettings : ModSettings
    {
        public OptiLevel level = OptiLevel.Medium;
        public bool optiDraft = true;
        public List<Pawn> storedPawns = new List<Pawn>();
        public HashSet<int> FrozenPawns = new HashSet<int>();

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref level, "level", OptiLevel.Medium);
            Scribe_Values.Look(ref optiDraft, "optiDraft", true);
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
            listing.Label("Optimization Level (Aggressive = Max TPS):");
            if (listing.RadioButton("Simple", settings.level == OptiLevel.Simple)) settings.level = OptiLevel.Simple;
            if (listing.RadioButton("Medium", settings.level == OptiLevel.Medium)) settings.level = OptiLevel.Medium;
            if (listing.RadioButton("Aggressive", settings.level == OptiLevel.Aggressive)) settings.level = OptiLevel.Aggressive;
            listing.Gap();
            listing.CheckboxLabeled("Enable Combat Optimization", ref settings.optiDraft);
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
            Widgets.Label(new Rect(0, 0, inRect.width, 35f), "Pawn Stock (Storage)");
            Text.Font = GameFont.Small;
            if (RimcoresMod.settings.storedPawns == null) RimcoresMod.settings.storedPawns = new List<Pawn>();

            Rect outRect = new Rect(0, 45f, inRect.width, inRect.height - 50f);
            Rect viewRect = new Rect(0, 0, inRect.width - 25f, RimcoresMod.settings.storedPawns.Count * 35f + 20f);
            Widgets.BeginScrollView(outRect, ref scrollPos, viewRect);
            float curY = 0;
            for (int i = 0; i < RimcoresMod.settings.storedPawns.Count; i++)
            {
                Pawn p = RimcoresMod.settings.storedPawns[i];
                Widgets.Label(new Rect(0, curY, 250f, 30f), p.LabelCap);
                if (Widgets.ButtonText(new Rect(260f, curY, 100f, 28f), "LOAD"))
                {
                    GenSpawn.Spawn(p, UI.MouseMapPosition().ToIntVec3(), Find.CurrentMap);
                    RimcoresMod.settings.storedPawns.RemoveAt(i);
                    break;
                }
                curY += 35f;
            }
            Widgets.EndScrollView();
        }
    }

    // --- ENGINE ---
    [StaticConstructorOnStartup]
    public static class RimcoresLoader
    {
        public static MethodInfo mNeeds, mMind, mJobs;
        public static Texture2D IconFreeze, IconDeath;

        static RimcoresLoader()
        {
            var harmony = new Harmony("Guilherme.Rimcores.Fixed");
            mNeeds = AccessTools.Method(typeof(Pawn_NeedsTracker), "NeedsTrackerTick");
            mMind = AccessTools.Method(typeof(Pawn_MindState), "MindStateTick");
            mJobs = AccessTools.Method(typeof(Pawn_JobTracker), "JobTrackerTick");

            // Safe Icon Loading
            IconFreeze = ContentFinder<Texture2D>.Get("UI/Commands/freeze", false) ?? BaseContent.BadTex;
            IconDeath = ContentFinder<Texture2D>.Get("UI/Commands/morto", false) ?? BaseContent.BadTex;

            harmony.PatchAll();
            Log.Message("[Rimcores] Fixed Version: Stability + Multithread.");
        }
    }

    [HarmonyPatch(typeof(TickManager), "DoSingleTick")]
    public static class MultithreadTickPatch
    {
        private static int tickCount = 0;
        static void Postfix()
        {
            if (Find.TickManager.Paused || Find.CurrentMap == null) return;
            var pawns = Find.CurrentMap.mapPawns.AllPawnsSpawned;
            tickCount++;

            Parallel.For(0, pawns.Count, i => {
                Pawn p = pawns[i];
                if (p == null || RimcoresMod.settings.FrozenPawns.Contains(p.thingIDNumber)) return;
                try {
                    bool isDraft = p.Drafted || (p.Faction != null && p.Faction.HostileTo(Faction.OfPlayer));
                    if (isDraft && RimcoresMod.settings.optiDraft) return;

                    var lv = RimcoresMod.settings.level;
                    if (lv == OptiLevel.Simple) {
                        RimcoresLoader.mNeeds?.Invoke(p.needs, null);
                        RimcoresLoader.mMind?.Invoke(p.mindState, null);
                    } else if (lv == OptiLevel.Medium && tickCount % 2 == 0) {
                        RimcoresLoader.mNeeds?.Invoke(p.needs, null);
                    } else if (lv == OptiLevel.Aggressive && tickCount % 5 == 0) {
                        RimcoresLoader.mNeeds?.Invoke(p.needs, null);
                    }
                } catch { }
            });
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
                // STOCK
                yield return new Command_Action {
                    defaultLabel = "Stock Pawn",
                    icon = RimcoresLoader.IconFreeze,
                    action = () => {
                        __instance.mindState?.Reset(true, true, true);
                        RimcoresMod.settings.storedPawns.Add(__instance);
                        __instance.DeSpawn();
                    }
                };
                // KILL
                yield return new Command_Action {
                    defaultLabel = "Kill",
                    icon = RimcoresLoader.IconDeath,
                    action = () => __instance.Kill(null)
                };
                // FREEZE
                bool isFrozen = RimcoresMod.settings.FrozenPawns.Contains(__instance.thingIDNumber);
                yield return new Command_Action {
                    defaultLabel = isFrozen ? "Unfreeze" : "Freeze",
                    icon = RimcoresLoader.IconFreeze,
                    action = () => {
                        if (isFrozen) RimcoresMod.settings.FrozenPawns.Remove(__instance.thingIDNumber);
                        else RimcoresMod.settings.FrozenPawns.Add(__instance.thingIDNumber);
                    }
                };
            }
        }
    }

    [HarmonyPatch(typeof(Pawn), "Tick")]
    public static class FreezeMovementPatch {
        static bool Prefix(Pawn __instance) => !RimcoresMod.settings.FrozenPawns.Contains(__instance.thingIDNumber);
    }
}