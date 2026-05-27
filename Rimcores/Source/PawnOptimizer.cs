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
    // --- CLASSE DE DADOS (GRUPOS) ---
    public class StoredGroup : IExposable
    {
        public List<Pawn> pawns = new List<Pawn>();

        public void ExposeData()
        {
            // LookMode.Deep é essencial para não perder o corpo do pawn
            Scribe_Collections.Look(ref pawns, "pawns", LookMode.Deep);
            if (pawns == null) pawns = new List<Pawn>();
        }
    }

    public class RimcoresSettings : ModSettings
    {
        public List<Pawn> storedPawns = new List<Pawn>();
        public List<StoredGroup> storedGroups = new List<StoredGroup>();
        public HashSet<int> FrozenPawns = new HashSet<int>();

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref storedPawns, "storedPawns", LookMode.Deep);
            Scribe_Collections.Look(ref storedGroups, "storedGroups", LookMode.Deep);
            Scribe_Collections.Look(ref FrozenPawns, "FrozenPawns", LookMode.Value);
            
            if (storedPawns == null) storedPawns = new List<Pawn>();
            if (storedGroups == null) storedGroups = new List<StoredGroup>();
            if (FrozenPawns == null) FrozenPawns = new HashSet<int>();
        }
    }

    public class RimcoresMod : Mod
    {
        public static RimcoresSettings settings;
        public RimcoresMod(ModContentPack content) : base(content) { settings = GetSettings<RimcoresSettings>(); }
    }

    // --- INTERFACE DE STORAGE ---
    public class MainTabWindow_PawnStock : MainTabWindow
    {
        private Vector2 scrollPos = Vector2.zero;
        private bool showGroups = false;
        public override Vector2 RequestedTabSize => new Vector2(500f, 600f);

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0, 0, 200f, 35f), "Storage System");
            
            if (Widgets.ButtonText(new Rect(210f, 5f, 135f, 30f), "INDIVIDUALS")) showGroups = false;
            if (Widgets.ButtonText(new Rect(350f, 5f, 135f, 30f), "GROUPS")) showGroups = true;
            
            Text.Font = GameFont.Small;
            GUI.color = Color.gray;
            Widgets.DrawLineHorizontal(0, 45f, inRect.width);
            GUI.color = Color.white;

            Rect outRect = new Rect(0, 50f, inRect.width, inRect.height - 60f);
            float viewHeight = showGroups ? (RimcoresMod.settings.storedGroups.Count * 40f) : (RimcoresMod.settings.storedPawns.Count * 35f);
            Rect viewRect = new Rect(0, 0, inRect.width - 25f, viewHeight + 50f);

            Widgets.BeginScrollView(outRect, ref scrollPos, viewRect);
            float curY = 0;

            if (!showGroups) // INDIVÍDUOS
            {
                for (int i = RimcoresMod.settings.storedPawns.Count - 1; i >= 0; i--)
                {
                    Pawn p = RimcoresMod.settings.storedPawns[i];
                    if (p == null) { RimcoresMod.settings.storedPawns.RemoveAt(i); continue; }

                    Rect row = new Rect(0, curY, viewRect.width, 30f);
                    Widgets.Label(new Rect(5, curY + 5, 250f, 30f), p.LabelCap);
                    if (Widgets.ButtonText(new Rect(viewRect.width - 110f, curY, 100f, 28f), "RELEASE"))
                    {
                        SafeSpawn(p);
                        RimcoresMod.settings.storedPawns.RemoveAt(i);
                    }
                    curY += 35f;
                }
            }
            else // GRUPOS
            {
                for (int i = RimcoresMod.settings.storedGroups.Count - 1; i >= 0; i--)
                {
                    StoredGroup g = RimcoresMod.settings.storedGroups[i];
                    if (g == null || g.pawns == null) { RimcoresMod.settings.storedGroups.RemoveAt(i); continue; }

                    Rect row = new Rect(0, curY, viewRect.width, 35f);
                    Widgets.Label(new Rect(5, curY + 5, 250f, 30f), "Group: " + g.pawns.Count + " pawns");

                    if (Widgets.ButtonText(new Rect(viewRect.width - 150f, curY, 140f, 32f), "RELEASE ALL"))
                    {
                        foreach (var p in g.pawns) SafeSpawn(p);
                        RimcoresMod.settings.storedGroups.RemoveAt(i);
                    }
                    curY += 40f;
                }
            }
            Widgets.EndScrollView();
        }

        private void SafeSpawn(Pawn p)
        {
            IntVec3 loc = UI.MouseMapPosition().ToIntVec3();
            if (!loc.IsValid || loc.Impassable(Find.CurrentMap)) 
                loc = CellFinder.RandomClosewalkCellNear(Find.CameraDriver.MapPosition, Find.CurrentMap, 5);
            
            // Re-insere o colono no mundo físico
            GenSpawn.Spawn(p, loc, Find.CurrentMap, WipeMode.Vanish);
            if (p.Faction != Faction.OfPlayer) p.SetFaction(Faction.OfPlayer); // Garante que volta como seu
        }
    }

    [StaticConstructorOnStartup]
    public static class RimcoresLoader
    {
        public static Action<object> fNeeds, fMind, fJobs, fPath;
        public static Texture2D IconFreeze, IconDeath, IconBox, IconGroup;
        private static readonly ParallelOptions options16 = new ParallelOptions { MaxDegreeOfParallelism = 16 };

        static RimcoresLoader()
        {
            var harmony = new Harmony("guilherme.rimcores");
            try {
                fNeeds = AccessTools.MethodDelegate<Action<object>>(AccessTools.Method("RimWorld.Pawn_NeedsTracker:NeedsTrackerTick"));
                fMind = AccessTools.MethodDelegate<Action<object>>(AccessTools.Method("Verse.AI.Pawn_MindState:MindStateTick"));
                fJobs = AccessTools.MethodDelegate<Action<object>>(AccessTools.Method("Verse.AI.Pawn_JobTracker:JobTrackerTick"));
                fPath = AccessTools.MethodDelegate<Action<object>>(AccessTools.Method("Verse.AI.PawnPathFollower:PawnPathFollowerTick"));
            } catch { }

            LongEventHandler.ExecuteWhenFinished(() => {
                IconFreeze = ContentFinder<Texture2D>.Get("UI/Commands/freeze", false) ?? BaseContent.BadTex;
                IconDeath = ContentFinder<Texture2D>.Get("UI/Commands/morto", false) ?? BaseContent.BadTex;
                IconBox = ContentFinder<Texture2D>.Get("UI/Commands/box", false) ?? BaseContent.BadTex;
                IconGroup = ContentFinder<Texture2D>.Get("UI/Commands/group", false) ?? BaseContent.BadTex;
            });
            harmony.PatchAll();
        }

        public static void RunEngine(List<Pawn> pawns)
        {
            Parallel.ForEach(pawns, options16, p => {
                if (p == null || RimcoresMod.settings.FrozenPawns.Contains(p.thingIDNumber)) return;
                try {
                    fNeeds?.Invoke(p.needs);
                    fMind?.Invoke(p.mindState);
                    fJobs?.Invoke(p.jobs);
                    fPath?.Invoke(p.pather);
                } catch { }
            });
        }
    }

    [HarmonyPatch(typeof(TickManager), "DoSingleTick")]
    public static class EngineTrigger
    {
        static void Postfix()
        {
            if (Find.TickManager.Paused || Find.CurrentMap == null) return;
            var pawns = Find.CurrentMap.mapPawns.AllPawnsSpawned;
            RimcoresLoader.RunEngine(pawns.ToList());
        }
    }

    [HarmonyPatch(typeof(Pawn), "GetGizmos")]
    public static class PawnGizmoPatch
    {
        static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn __instance)
        {
            foreach (var g in __result) yield return g;

            if (__instance.Faction != null && __instance.Faction.IsPlayer)
            {
                yield return new Command_Action {
                    defaultLabel = "Stock Pawn",
                    icon = RimcoresLoader.IconBox,
                    action = () => {
                        __instance.mindState?.Reset(true, true, true);
                        RimcoresMod.settings.storedPawns.Add(__instance);
                        __instance.DeSpawn(DestroyMode.Vanish); // Uso de Vanish para não apagar dados
                    }
                };

                if (Find.Selector.SelectedObjects.OfType<Pawn>().Count() > 1)
                {
                    yield return new Command_Action {
                        defaultLabel = "Store as Group",
                        icon = RimcoresLoader.IconGroup,
                        action = () => {
                            StoredGroup newGroup = new StoredGroup();
                            var selected = Find.Selector.SelectedObjects.OfType<Pawn>().ToList();
                            foreach (var p in selected) {
                                p.mindState?.Reset(true, true, true);
                                newGroup.pawns.Add(p);
                                p.DeSpawn(DestroyMode.Vanish);
                            }
                            RimcoresMod.settings.storedGroups.Add(newGroup);
                            Messages.Message("Group stored.", MessageTypeDefOf.PositiveEvent);
                        }
                    };
                }

                bool isFrozen = RimcoresMod.settings.FrozenPawns.Contains(__instance.thingIDNumber);
                yield return new Command_Action {
                    defaultLabel = isFrozen ? "Unfreeze" : "Freeze",
                    icon = RimcoresLoader.IconFreeze,
                    action = () => {
                        if (isFrozen) RimcoresMod.settings.FrozenPawns.Remove(__instance.thingIDNumber);
                        else RimcoresMod.settings.FrozenPawns.Add(__instance.thingIDNumber);
                    }
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