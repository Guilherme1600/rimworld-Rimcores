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
    // --- CLASSE DO GRUPO (SÓ EXISTE SE TIVER PAWNS) ---
    public class StoredGroup : IExposable
    {
        public List<Pawn> pawns = new List<Pawn>();

        public void ExposeData()
        {
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

    // --- INTERFACE 100% DINÂMICA (SEM SLOTS VAZIOS) ---
    public class MainTabWindow_PawnStock : MainTabWindow
    {
        private Vector2 scrollPos = Vector2.zero;
        private bool showGroups = false;
        public override Vector2 RequestedTabSize => new Vector2(500f, 600f);

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0, 0, 200f, 35f), "Storage");
            
            if (Widgets.ButtonText(new Rect(210f, 5f, 135f, 30f), "INDIVIDUALS")) showGroups = false;
            if (Widgets.ButtonText(new Rect(355f, 5f, 135f, 30f), "GROUPS")) showGroups = true;
            
            Text.Font = GameFont.Small;
            Widgets.DrawLineHorizontal(0, 45f, inRect.width);

            Rect outRect = new Rect(0, 50f, inRect.width, inRect.height - 60f);
            
            // O segredo do Rimcores: A altura da lista é calculada no exato momento
            int itemCount = showGroups ? RimcoresMod.settings.storedGroups.Count : RimcoresMod.settings.storedPawns.Count;
            float viewHeight = (itemCount * 40f) + 20f;
            Rect viewRect = new Rect(0, 0, inRect.width - 25f, viewHeight);

            Widgets.BeginScrollView(outRect, ref scrollPos, viewRect);
            float curY = 0;

            if (!showGroups) // INDIVIDUALS
            {
                for (int i = RimcoresMod.settings.storedPawns.Count - 1; i >= 0; i--)
                {
                    Pawn p = RimcoresMod.settings.storedPawns[i];
                    if (p == null) { RimcoresMod.settings.storedPawns.RemoveAt(i); continue; }

                    Widgets.Label(new Rect(5, curY + 5, 250f, 30f), p.LabelCap);
                    if (Widgets.ButtonText(new Rect(viewRect.width - 120f, curY, 110f, 30f), "RELEASE"))
                    {
                        SpawnPawn(p);
                        RimcoresMod.settings.storedPawns.RemoveAt(i);
                    }
                    curY += 40f;
                }
            }
            else // GROUPS (AGORA SEM SLOTS VAZIOS)
            {
                if (RimcoresMod.settings.storedGroups.Count == 0)
                {
                    Widgets.Label(new Rect(inRect.width / 4, 100f, 250f, 30f), "No groups in storage.");
                }

                for (int i = RimcoresMod.settings.storedGroups.Count - 1; i >= 0; i--)
                {
                    StoredGroup g = RimcoresMod.settings.storedGroups[i];
                    if (g == null || g.pawns.Count == 0) { RimcoresMod.settings.storedGroups.RemoveAt(i); continue; }

                    Widgets.Label(new Rect(5, curY + 5, 250f, 30f), "Group of " + g.pawns.Count + " pawns");
                    if (Widgets.ButtonText(new Rect(viewRect.width - 140f, curY, 130f, 32f), "RELEASE ALL"))
                    {
                        foreach (var p in g.pawns) SpawnPawn(p);
                        RimcoresMod.settings.storedGroups.RemoveAt(i); // Remove e a linha desaparece no próximo frame
                    }
                    curY += 40f;
                }
            }
            Widgets.EndScrollView();
        }

        private void SpawnPawn(Pawn p)
        {
            IntVec3 pos = UI.MouseMapPosition().ToIntVec3();
            if (!pos.IsValid || pos.Impassable(Find.CurrentMap)) 
                pos = CellFinder.RandomClosewalkCellNear(Find.CameraDriver.MapPosition, Find.CurrentMap, 2);
            
            GenSpawn.Spawn(p, pos, Find.CurrentMap, WipeMode.Vanish);
        }
    }

    // --- ENGINE MULTITHREAD (16 CORES SEMPRE ON) ---
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

        public static void RunBackgroundEngine(IEnumerable<Pawn> pawns)
        {
            Parallel.ForEach(pawns, options16, p => {
                if (p == null || RimcoresMod.settings.FrozenPawns.Contains(p.thingIDNumber)) return;
                try {
                    fNeeds?.Invoke(p.needs); fMind?.Invoke(p.mindState);
                    fJobs?.Invoke(p.jobs); fPath?.Invoke(p.pather);
                } catch { }
            });
        }
    }

    [HarmonyPatch(typeof(TickManager), "DoSingleTick")]
    public static class MultithreadTickPatch {
        static void Postfix() {
            if (Find.TickManager.Paused || Find.CurrentMap == null) return;
            RimcoresLoader.RunBackgroundEngine(Find.CurrentMap.mapPawns.AllPawnsSpawned);
        }
    }

    // --- COMANDOS (BOTÕES NO PAWN) ---
    [HarmonyPatch(typeof(Pawn), "GetGizmos")]
    public static class PawnGizmoPatch
    {
        static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn __instance)
        {
            foreach (var g in __result) yield return g;

            if (__instance.Faction != null && __instance.Faction.IsPlayer)
            {
                yield return new Command_Action {
                    defaultLabel = "Stock Pawn", icon = RimcoresLoader.IconBox,
                    action = () => { __instance.mindState?.Reset(true, true, true); RimcoresMod.settings.storedPawns.Add(__instance); __instance.DeSpawn(DestroyMode.Vanish); }
                };

                // CRIAÇÃO DO GRUPO DINÂMICO
                if (Find.Selector.SelectedObjects.OfType<Pawn>().Count() > 1)
                {
                    yield return new Command_Action {
                        defaultLabel = "Stock as Group", icon = RimcoresLoader.IconGroup,
                        action = () => {
                            StoredGroup newGroup = new StoredGroup();
                            var selected = Find.Selector.SelectedObjects.OfType<Pawn>().ToList();
                            foreach (var p in selected) {
                                p.mindState?.Reset(true, true, true);
                                newGroup.pawns.Add(p);
                                p.DeSpawn(DestroyMode.Vanish);
                            }
                            RimcoresMod.settings.storedGroups.Add(newGroup);
                            Messages.Message("New group stored.", MessageTypeDefOf.PositiveEvent);
                        }
                    };
                }

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