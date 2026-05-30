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
    // --- 1. PERSISTÊNCIA ETERNA (SALVA NO SEU SAVE .RWS) ---
    public class RimcoresWorldComponent : WorldComponent
    {
        public List<Pawn> storedPawns = new List<Pawn>();
        public List<StoredGroup> storedGroups = new List<StoredGroup>();

        public RimcoresWorldComponent(World world) : base(world) { }

        public override void ExposeData()
        {
            base.ExposeData();
            // LookMode.Deep garante que o DNA, Roupas e Armas fiquem no ficheiro de save
            Scribe_Collections.Look(ref storedPawns, "storedPawns", LookMode.Deep);
            Scribe_Collections.Look(ref storedGroups, "storedGroups", LookMode.Deep);

            if (storedPawns == null) storedPawns = new List<Pawn>();
            if (storedGroups == null) storedGroups = new List<StoredGroup>();
        }
    }

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
        public HashSet<int> FrozenPawns = new HashSet<int>();
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref FrozenPawns, "FrozenPawns", LookMode.Value);
            if (FrozenPawns == null) FrozenPawns = new HashSet<int>();
        }
    }

    public class RimcoresMod : Mod
    {
        public static RimcoresSettings settings;
        public RimcoresMod(ModContentPack content) : base(content) { settings = GetSettings<RimcoresSettings>(); }
    }

    // --- 2. INTERFACE DYNAMICA (SEM SLOTS VAZIOS) ---
    public class MainTabWindow_PawnStock : MainTabWindow
    {
        private Vector2 scrollPos = Vector2.zero;
        private bool showGroups = false;
        public override Vector2 RequestedTabSize => new Vector2(500f, 600f);

        public override void DoWindowContents(Rect inRect)
        {
            var comp = Find.World.GetComponent<RimcoresWorldComponent>();
            if (comp == null) return;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0, 0, 200f, 35f), "Storage System");
            
            if (Widgets.ButtonText(new Rect(210f, 5f, 135f, 30f), "INDIVIDUALS")) showGroups = false;
            if (Widgets.ButtonText(new Rect(355f, 5f, 135f, 30f), "GROUPS")) showGroups = true;
            
            Text.Font = GameFont.Small;
            Widgets.DrawLineHorizontal(0, 45f, inRect.width);

            Rect outRect = new Rect(0, 50f, inRect.width, inRect.height - 60f);
            int itemCount = showGroups ? comp.storedGroups.Count : comp.storedPawns.Count;
            float viewHeight = (itemCount * 40f) + 50f;
            Rect viewRect = new Rect(0, 0, inRect.width - 25f, viewHeight);

            Widgets.BeginScrollView(outRect, ref scrollPos, viewRect);
            float curY = 0;

            if (!showGroups) // ABA INDIVIDUOS
            {
                for (int i = comp.storedPawns.Count - 1; i >= 0; i--)
                {
                    Pawn p = comp.storedPawns[i];
                    if (p == null) { comp.storedPawns.RemoveAt(i); continue; }

                    Rect row = new Rect(0, curY, viewRect.width, 30f);
                    Widgets.Label(new Rect(5, curY + 5, 250f, 30f), p.LabelCap);
                    if (Widgets.ButtonText(new Rect(viewRect.width - 120f, curY, 110f, 30f), "RELEASE"))
                    {
                        SpawnAtMouse(p);
                        comp.storedPawns.RemoveAt(i);
                    }
                    curY += 40f;
                }
            }
            else // ABA GRUPOS
            {
                for (int i = comp.storedGroups.Count - 1; i >= 0; i--)
                {
                    StoredGroup g = comp.storedGroups[i];
                    if (g == null || g.pawns.Count == 0) { comp.storedGroups.RemoveAt(i); continue; }

                    Rect row = new Rect(0, curY, viewRect.width, 35f);
                    Widgets.Label(new Rect(5, curY + 5, 250f, 30f), "Group: " + g.pawns.Count + " units");
                    if (Widgets.ButtonText(new Rect(viewRect.width - 150f, curY, 140f, 32f), "RELEASE ALL"))
                    {
                        foreach (var p in g.pawns) SpawnAtMouse(p);
                        comp.storedGroups.RemoveAt(i);
                    }
                    curY += 40f;
                }
            }
            Widgets.EndScrollView();
        }

        private void SpawnAtMouse(Pawn p)
        {
            IntVec3 pos = UI.MouseMapPosition().ToIntVec3();
            if (!pos.IsValid || pos.Impassable(Find.CurrentMap)) 
                pos = CellFinder.RandomClosewalkCellNear(Find.CameraDriver.MapPosition, Find.CurrentMap, 2);
            GenSpawn.Spawn(p, pos, Find.CurrentMap, WipeMode.Vanish);
        }
    }

    // --- 3. MOTOR DE OTIMIZAÇÃO (16 CORES) ---
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
            Log.Message("[Rimcores] v48 Eternal-Core Active.");
        }

        // FIX: Função renomeada corretamente para coincidir com a chamada no Patch
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
            // Chama a função com o nome correto agora
            RimcoresLoader.RunBackgroundEngine(Find.CurrentMap.mapPawns.AllPawnsSpawned);
        }
    }

    // --- 4. GIZMOS (BOTOES NO PAWN) ---
    [HarmonyPatch(typeof(Pawn), "GetGizmos")]
    public static class PawnGizmoPatch
    {
        static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn __instance)
        {
            foreach (var g in __result) yield return g;
            if (__instance.Faction != null && __instance.Faction.IsPlayer)
            {
                var comp = Find.World.GetComponent<RimcoresWorldComponent>();
                if (comp == null) yield break;

                // INDIVIDUAL
                yield return new Command_Action {
                    defaultLabel = "Stock Pawn", icon = RimcoresLoader.IconBox,
                    action = () => {
                        __instance.mindState?.Reset(true, true, true);
                        comp.storedPawns.Add(__instance);
                        __instance.DeSpawn(DestroyMode.Vanish);
                    }
                };

                // GRUPO DINAMICO
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
                            comp.storedGroups.Add(newGroup);
                        }
                    };
                }

                bool isFrozen = RimcoresMod.settings.FrozenPawns.Contains(__instance.thingIDNumber);
                yield return new Command_Action {
                    defaultLabel = isFrozen ? "Unfreeze" : "Freeze", icon = RimcoresLoader.IconFreeze,
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