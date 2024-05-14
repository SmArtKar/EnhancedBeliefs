using HarmonyLib;
using LudeonTK;
using Mono.Security.Cryptography;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using Verse.AI;
using static HarmonyLib.Code;

namespace EnhancedBeliefs
{
    // Bootleg solution because prepatcher will scare off workshop dummies
    [HarmonyPatch(typeof(PawnComponentsUtility), nameof(PawnComponentsUtility.CreateInitialComponents))]
    public static class PawnComponentsUtility_Initialize
    {
        public static Pawn lastPawn;

        public static void Postfix(Pawn pawn)
        {
            lastPawn = null;

            if (pawn.ideo == null)
            {
                return;
            }

            GameComponent_EnhancedBeliefs comp = Current.Game.GetComponent<GameComponent_EnhancedBeliefs>();

            if (!comp.pawnTrackerData.ContainsKey(pawn))
            {
                comp.AddTracker(pawn);
                lastPawn = pawn;
            }
        }
    }

    [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.TryGenerateNewPawnInternal))]
    public static class PawnGenerator_Generate
    {
        public static void Postfix(Pawn __result)
        {
            if (__result != null || PawnComponentsUtility_Initialize.lastPawn == null)
            {
                return;
            }

            GameComponent_EnhancedBeliefs comp = Current.Game.GetComponent<GameComponent_EnhancedBeliefs>();

            if (comp.pawnTrackerData.ContainsKey(PawnComponentsUtility_Initialize.lastPawn))
            {
                comp.pawnTrackerData.Remove(PawnComponentsUtility_Initialize.lastPawn);
                PawnComponentsUtility_Initialize.lastPawn = null;
            }
        }
    }

    [HarmonyPatch(typeof(Ideo), MethodType.Constructor)]
    public static class Ideo_Constructor
    {
        public static void Postfix(Ideo __instance)
        {
            Current.Game.GetComponent<GameComponent_EnhancedBeliefs>().AddIdeoTracker(__instance);
        }
    }

    [HarmonyPatch(typeof(Pawn_IdeoTracker), nameof(Pawn_IdeoTracker.CertaintyChangePerDay), MethodType.Getter)]
    public static class IdeoTracker_CertaintyChange
    {
        public static bool Prefix(Pawn_IdeoTracker __instance, ref float __result)
        {
            __result = 0;

            Pawn pawn = __instance.pawn;
            GameComponent_EnhancedBeliefs comp = Current.Game.GetComponent<GameComponent_EnhancedBeliefs>();
            IdeoTrackerData data = comp.pawnTrackerData.TryGetValue(pawn);

            if (data == null)
            {
                data = comp.AddTracker(pawn);
            }

            // 4 recaches per second should be enough
            if (data.cachedCertaintyChange == -9999f || pawn.IsHashIntervalTick(GenTicks.TickRareInterval))
            {
                data.CertaintyChangeRecache(comp);
            }

            __result += data.cachedCertaintyChange;

            if (__result > 0)
            {
                data.lastPositiveThoughtTick = Find.TickManager.TicksGame;
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(Pawn_IdeoTracker), nameof(Pawn_IdeoTracker.IdeoTrackerTick))]
    public static class IdeoTracker_Tick
    {
        public static void Postfix(Pawn_IdeoTracker __instance)
        {
            Pawn pawn = __instance.pawn;

            if (pawn.IsHashIntervalTick(GenTicks.TickLongInterval) && !pawn.Destroyed && pawn.Map != null && __instance.ideo != null && !Find.IdeoManager.classicMode)
            {
                GameComponent_EnhancedBeliefs comp = Current.Game.GetComponent<GameComponent_EnhancedBeliefs>();
                IdeoTrackerData data = comp.pawnTrackerData.TryGetValue(pawn);

                if (data == null)
                {
                    data = comp.AddTracker(pawn);
                }

                data.RecalculateRelationshipIdeoOpinions();
            }
        }
    }

    // Another bootleg tracker, no idea why Tynan didn't implement it in vanilla considering the amount of work and performance it would've saved him
    // Smh, backseat coding
    [HarmonyPatch(typeof(Pawn_IdeoTracker), nameof(Pawn_IdeoTracker.SetIdeo))]
    public static class IdeoTracker_SetIdeo
    {
        public static void Postfix(Pawn_IdeoTracker __instance, Ideo ideo)
        {
            Current.Game.GetComponent<GameComponent_EnhancedBeliefs>().SetIdeo(__instance.pawn, ideo);
        }
    }

    [HarmonyPatch(typeof(IdeoDevelopmentTracker), nameof(IdeoDevelopmentTracker.Notify_Reformed))]
    public static class FluidIdeoTracker_Reformed
    {
        public static void Postfix(IdeoDevelopmentTracker __instance)
        {
            Current.Game.GetComponent<GameComponent_EnhancedBeliefs>().BaseOpinionRecache(__instance.ideo);
        }
    }

    [HarmonyPatch(typeof(TraitSet), nameof(TraitSet.GainTrait))]
    public static class TraitSet_TraitAdded
    {
        public static void Postfix(TraitSet __instance)
        {
            Current.Game.GetComponent<GameComponent_EnhancedBeliefs>().pawnTrackerData?.TryGetValue(__instance.pawn)?.RecacheAllBaseOpinions();
        }
    }

    [HarmonyPatch(typeof(TraitSet), nameof(TraitSet.RemoveTrait))]
    public static class TraitSet_TraitRemoved
    {
        public static void Postfix(TraitSet __instance)
        {
            Current.Game.GetComponent<GameComponent_EnhancedBeliefs>().pawnTrackerData?.TryGetValue(__instance.pawn)?.RecacheAllBaseOpinions();
        }
    }

    [HarmonyPatch(typeof(SocialCardUtility), nameof(SocialCardUtility.DrawPawnCertainty))]
    public static class SocialCardUtility_DrawCertainty
    {
        public static Rect containerRect;

        public static bool Prefix(Pawn pawn, Rect rect)
        {
            float num = rect.x + 17f;
            Rect iconRect = new Rect(num, rect.y + rect.height / 2f - 16f, 32f, 32f);
            pawn.Ideo.DrawIcon(iconRect);
            num += 42f;
            Text.Anchor = TextAnchor.MiddleLeft;
            Rect rect3 = new Rect(num, rect.y, rect.width / 2f - num, rect.height);
            Widgets.Label(rect3, pawn.Ideo.name.Truncate(rect3.width));
            Text.Anchor = TextAnchor.UpperLeft;
            num += rect3.width + 10f;
            containerRect = new Rect(iconRect.x, rect.y + rect.height / 2f - 16f, 0f, 32f);
            Rect barRect = new Rect(num, rect.y + rect.height / 2f - 16f, rect.width - num - 26f, 32f);
            containerRect.xMax = barRect.xMax;

            if (Widgets.ButtonInvisible(containerRect))
            {
                IdeoUIUtility.OpenIdeoInfo(pawn.Ideo);
            }

            Widgets.FillableBar(barRect.ContractedBy(4f), pawn.ideo.Certainty, SocialCardUtility.BarFullTexHor);

            return false;
        }
    }

    // Ugly code cut into two chunks because DrawPawnCertainty is called before rendering anything else in the tab and UI will get overwritten by other elements

    [HarmonyPatch(typeof(SocialCardUtility), nameof(SocialCardUtility.DrawSocialCard))]
    public static class SocialCardUtility_DrawCard
    {
        public static Rect hoverRect;
        public static bool opinionMenuOpen = false;

        public static void Postfix(Rect rect, Pawn pawn)
        {
            bool hovering = Mouse.IsOver(SocialCardUtility_DrawCertainty.containerRect);

            if (hovering || (opinionMenuOpen && Mouse.IsOver(hoverRect)))
            {
                Widgets.DrawHighlight(SocialCardUtility_DrawCertainty.containerRect);
                DrawOpinionTab(SocialCardUtility_DrawCertainty.containerRect, pawn, hovering);
            }
            else
            {
                opinionMenuOpen = false;
            }
        }

        public static void DrawOpinionTab(Rect containerRect, Pawn pawn, bool hovering)
        {
            GameComponent_EnhancedBeliefs comp = Current.Game.GetComponent<GameComponent_EnhancedBeliefs>();
            IdeoTrackerData data = comp.pawnTrackerData[pawn];
            opinionMenuOpen = true;

            if (hovering)
            {
                string certaintyChange = (pawn.ideo.CertaintyChangePerDay >= 0f ? "+" : "") + pawn.ideo.CertaintyChangePerDay.ToStringPercent();

                TaggedString tip = "{0}'s certainty in {1}: ".Formatted(pawn, pawn.Ideo) + pawn.ideo.Certainty.ToStringPercent() + "\n\n";
                tip += "Certainty change per day from beliefs: " + certaintyChange + "\n";

                if (pawn.needs.mood.CurLevelPercentage < 0.8 && Find.TickManager.TicksGame - data.lastPositiveThoughtTick > 180000f)
                {
                    tip += "Certainty loss from lack of belief affirmation: " + GameComponent_EnhancedBeliefs.CertaintyLossFromInactivity.Evaluate((Find.TickManager.TicksGame - data.lastPositiveThoughtTick) / 60000f).ToStringPercent() + "\n";
                }

                tip += "\n";

                TooltipHandler.TipRegion(containerRect, () => tip.Resolve(), 10218219);
            }

            float maxNameWidth = 0f;

            for (int i = 0; i < Find.IdeoManager.ideos.Count; i++)
            {
                maxNameWidth = Math.Max(maxNameWidth, Text.CalcSize(Find.IdeoManager.ideos[i].name).x);
            }

            Rect opinionRect = new Rect(containerRect.x, containerRect.y + 40f, 264f + maxNameWidth, Find.IdeoManager.ideos.Count * 38f + 8f);
            Rect tabRect = opinionRect.ContractedBy(4f);
            hoverRect = new Rect(opinionRect.x, opinionRect.y - 8f, opinionRect.width, opinionRect.height + 8f);

            Widgets.DrawShadowAround(opinionRect);
            Widgets.DrawWindowBackground(opinionRect);

            for (int i = 0; i < Find.IdeoManager.ideos.Count; i++)
            {
                Ideo ideo = Find.IdeoManager.ideos[i];

                Rect iconRect = new Rect(tabRect.x + 4, tabRect.y + 4 + i * 38f, 32f, 32f);
                ideo.DrawIcon(iconRect);

                Text.Anchor = TextAnchor.MiddleLeft;
                Rect textRect = new Rect(iconRect.x + 40, iconRect.y, maxNameWidth, 32f);
                Widgets.Label(textRect, ideo.name);
                Text.Anchor = TextAnchor.UpperLeft;

                Rect barRect = new Rect(textRect.x + textRect.width + 8f, textRect.y, 200f, 32f);
                float opinion = data.IdeoOpinion(ideo);
                Widgets.FillableBar(barRect.ContractedBy(4f), opinion, SocialCardUtility.BarFullTexHor);

                Rect tooltipRect = new Rect(tabRect.x + 4, tabRect.y + 4 + i * 38f, 248f + maxNameWidth, 32f);
                if (Widgets.ButtonInvisible(tooltipRect))
                {
                    IdeoUIUtility.OpenIdeoInfo(ideo);
                }

                if (Mouse.IsOver(tooltipRect))
                {
                    Widgets.DrawHighlight(tooltipRect);

                    float[] opinionRundown = data.DetailedIdeoOpinion(ideo);

                    TaggedString tip = "Opinion of {0}: {1}\n\n".Formatted(ideo.name, opinion.ToStringPercent());
                    tip += "From memes and precepts: " + opinionRundown[0].ToStringPercent() + "\n";
                    tip += "From personal beliefs: " + opinionRundown[1].ToStringPercent() + "\n";
                    tip += "From interpersonal relationships: " + opinionRundown[2].ToStringPercent() + "\n";

                    TooltipHandler.TipRegion(tooltipRect, () => tip.Resolve(), 10218220);
                }
            }
        }
    }

    // Changing ideo break to instead use new mechanics and handler
    [HarmonyPatch(typeof(MentalState_IdeoChange), nameof(MentalState_IdeoChange.PreStart))]
    public static class IdeoChangeBreak_Start
    {
        public static bool Prefix(MentalState_IdeoChange __instance)
        {
            Pawn pawn = __instance.pawn;
            __instance.oldIdeo = pawn.Ideo;
            __instance.oldRole = __instance.oldIdeo.GetRole(pawn);

            pawn.ideo.Certainty = Mathf.Clamp01(pawn.ideo.Certainty - 0.5f);

            GameComponent_EnhancedBeliefs comp = Current.Game.GetComponent<GameComponent_EnhancedBeliefs>();
            IdeoTrackerData data = comp.pawnTrackerData[pawn];

            if (data.CheckConversion(noBreakdown: true, opinionThreshold: 0.4f) == ConversionOutcome.Success)
            {
                __instance.newIdeo = pawn.Ideo;
                __instance.changedIdeo = true;
            }

            __instance.newCertainty = pawn.ideo.Certainty;

            return false;
        }
    }

    // Debates use meme/precept symbol instead for their motes
    [HarmonyPatch(typeof(InteractionDef), nameof(InteractionDef.GetSymbol))]
    public static class InteractionDef_Symbol
    {
        public static bool Prefix(InteractionDef __instance, Faction initiatorFaction, Ideo initatorIdeo, ref Texture2D __result)
        {
            if (__instance.Worker is InteractionWorker_IdeologicalDebateMeme worker)
            {
                if (worker.topic != null)
                {
                    __result = worker.topic.Icon;
                    worker.topic = null;
                    return false;
                }
            }

            if (__instance.Worker is InteractionWorker_IdeologicalDebatePrecept worker2)
            {
                if (worker2.topic != null)
                {
                    __result = worker2.topic.Icon;
                    worker2.topic = null;
                    return false;
                }
            }

            return true;
        }
    }

    // Scribing data in a postfix to ensure that no junk data is saved
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.ExposeData))]
    public static class Pawn_ExposeData
    {
        public static void Postfix(Pawn __instance)
        {
            if (__instance.ideo == null)
            {
                return;
            }

            GameComponent_EnhancedBeliefs comp = Current.Game.GetComponent<GameComponent_EnhancedBeliefs>();
            IdeoTrackerData data = comp.pawnTrackerData.TryGetValue(__instance);

            Scribe_Deep.Look(ref data, "EB_IdeoTrackerData");

            if (Scribe.mode != LoadSaveMode.Saving && data != null)
            {
                comp.pawnTrackerData[__instance] = data;
            }
        }
    }
}
