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
    // --- DATA STORAGE CLASSES ---
    public class StoredGroup : IExposable
    {
        public string groupName = "New Group";
        public List<Pawn> pawns = new List<Pawn>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref groupName, "groupName", "New Group");
            Scribe_Collections.Look(ref pawns, "pawns", LookMode.Deep);
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
        // Opções de mod removidas como pedido.
    }

    // --- INTERFACE DE DUAS ABAS (STORAGE) ---
    public class MainTabWindow_PawnStock : MainTabWindow
    {
        private Vector2 scrollPos = Vector2.zero;
        private bool showGroups = false;
        public override Vector2 RequestedTabSize => new Vector2(500f, 600f);

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0, 0, 200f, 35f), "Storage");
            
            // Botões de alternância de Aba
            if (Widgets.ButtonText(new Rect(210f, 5f, 130f, 30f), "INDIVIDUALS")) showGroups = false;
            if (Widgets.ButtonText(new Rect(350f, 5f, 130f, 30f), "GROUPS")) showGroups = true;
            
            Text.Font = GameFont.Small;
            Rect outRect = new Rect(0, 50f, inRect.width, inRect.height - 60f);
            float viewHeight = showGroups ? (RimcoresMod.settings.storedGroups.Count * 45f) : (RimcoresMod.settings.storedPawns.Count * 35f);
            Rect viewRect = new Rect(0, 0, inRect.width - 25f, viewHeight + 50f);

            Widgets.BeginScrollView(outRect, ref scrollPos, viewRect);
            float curY = 0;

            if (!showGroups) // ABA INDIVIDUAL
            {
                for (int i = 0; i < RimcoresMod.settings.storedPawns.Count; i++)
                {
                    Pawn p = RimcoresMod.settings.storedPawns[i];
                    Widgets.Label(new Rect(5, curY, 250f, 30f), p.LabelShortCap);
                    if (Widgets.ButtonText(new Rect(300f, curY, 120f, 28f), "RELEASE", true, true, true, null))
                    {
                        GenSpawn.Spawn(p, UI.MouseMapPosition().ToIntVec3(), Find.CurrentMap);
                        RimcoresMod.settings.storedPawns.RemoveAt(i);
                        break;
                    }
                    curY += 35f;
                }
            }
            else // ABA DE GRUPOS
            {
                for (int i = 0; i < RimcoresMod.settings.storedGroups.Count; i++)
                {
                    StoredGroup g = RimcoresMod.settings.storedGroups[i];
                    Rect row = new Rect(0, curY, viewRect.width, 40f);
                    
                    // Campo para mudar nome do grupo
                    g.groupName = Widgets.TextField(new Rect(5, curY, 150f, 30f), g.groupName);
                    Widgets.Label(new Rect(165f, curY, 100f, 30f), $"({g.pawns.Count} pawns)");

                    if (Widgets.ButtonText(new Rect(300f, curY, 120f, 30f), "RELEASE ALL", true, true, true, null))
                    {
                        IntVec3 center = UI.MouseMapPosition().ToIntVec3();
                        foreach (var p in g.pawns) GenSpawn.Spawn(p, center, Find.CurrentMap);
                        RimcoresMod.settings.storedGroups.RemoveAt(i);
                        break;
                    }
                    curY += 45f;
                }
            }
            Widgets.EndScrollView();
        }
    }

    [StaticConstructorOnStartup]
    public static class RimcoresLoader
    {
        public static Action<object> fNeeds, fMind, fJobs, fPath;
        public static Texture2D IconFreeze, IconDeath, IconBox, IconGroup;

        static RimcoresLoader()
        {
            var harmony = new Harmony("guilherme.rimcores");
            fNeeds = AccessTools.MethodDelegate<Action<object>>(AccessTools.Method("RimWorld.Pawn_NeedsTracker:NeedsTrackerTick"));
            fMind = AccessTools.MethodDelegate<Action<object>>(AccessTools.Method("Verse.AI.Pawn_MindState:MindStateTick"));
            fJobs = AccessTools.MethodDelegate<Action<object>>(AccessTools.Method("Verse.AI.Pawn_JobTracker:JobTrackerTick"));
            fPath = AccessTools.MethodDelegate<Action<object>>(AccessTools.Method("Verse.AI.Pawn_PathFollower:PawnPathFollowerTick"));

            LongEventHandler.ExecuteWhenFinished(() => {
                IconFreeze = ContentFinder<Texture2D>.Get("UI/Commands/freeze", false) ?? BaseContent.BadTex;
                IconDeath = ContentFinder<Texture2D>.Get("UI/Commands/morto", false) ?? BaseContent.BadTex;
                IconBox = ContentFinder<Texture2D>.Get("UI/Commands/box", false) ?? BaseContent.BadTex;
                IconGroup = ContentFinder<Texture2D>.Get("UI/Commands/group", false) ?? BaseContent.BadTex;
            });
            harmony.PatchAll();
        }
    }

    // --- ENGINE BACKGROUND (KRYPTON AUTOMÁTICO) ---
    [HarmonyPatch(typeof(TickManager), "DoSingleTick")]
    public static class MultithreadEnginePatch
    {
        static void Postfix()
        {
            if (Find.TickManager.Paused || Find.CurrentMap == null) return;
            var pawns = Find.CurrentMap.mapPawns.AllPawnsSpawned;
            
            Parallel.For(0, pawns.Count, i => {
                Pawn p = pawns[i];
                if (p == null || RimcoresMod.settings.FrozenPawns.Contains(p.thingIDNumber)) return;
                try {
                    RimcoresLoader.fNeeds?.Invoke(p.needs);
                    RimcoresLoader.fMind?.Invoke(p.mindState);
                    RimcoresLoader.fJobs?.Invoke(p.jobs);
                    RimcoresLoader.fPath?.Invoke(p.pather);
                } catch { }
            });
        }
    }

    // --- GIZMOS ---
    [HarmonyPatch(typeof(Pawn), "GetGizmos")]
    public static class PawnGizmoPatch
    {
        static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn __instance)
        {
            foreach (var g in __result) yield return g;
            if (__instance.Faction == Faction.OfPlayer)
            {
                // BOTÃO INDIVIDUAL
                yield return new Command_Action {
                    defaultLabel = "Stock Pawn",
                    icon = RimcoresLoader.IconBox,
                    action = () => {
                        __instance.mindState?.Reset(true, true, true);
                        RimcoresMod.settings.storedPawns.Add(__instance);
                        __instance.DeSpawn();
                    }
                };

                // BOTÃO DE GRUPO (Apenas se houver múltiplos selecionados)
                if (Find.Selector.SelectedPawns.Count > 1)
                {
                    yield return new Command_Action {
                        defaultLabel = "Group Storage",
                        icon = RimcoresLoader.IconGroup,
                        action = () => {
                            StoredGroup newGroup = new StoredGroup();
                            List<Pawn> toStore = Find.Selector.SelectedPawns.ToList();
                            foreach (var p in toStore)
                            {
                                p.mindState?.Reset(true, true, true);
                                newGroup.pawns.Add(p);
                                p.DeSpawn();
                            }
                            RimcoresMod.settings.storedGroups.Add(newGroup);
                            Messages.Message($"{newGroup.pawns.Count} pawns grouped and stored.", MessageTypeDefOf.PositiveEvent);
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