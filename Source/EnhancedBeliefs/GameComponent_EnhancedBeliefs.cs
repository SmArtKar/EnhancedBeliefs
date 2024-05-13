using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Noise;

namespace EnhancedBeliefs
{
    public class GameComponent_EnhancedBeliefs : GameComponent
    {
        // Days to percentage
        public static readonly SimpleCurve CertaintyLossFromInactivity = new SimpleCurve
        {
            new CurvePoint(3f,  0.01f),
            new CurvePoint(5f,  0.02f),
            new CurvePoint(10f, 0.03f),
            new CurvePoint(30f, 0.05f),
        };

        // Sum mood offset to percentage
        public static readonly SimpleCurve CertaintyOffsetFromThoughts = new SimpleCurve
        {
            new CurvePoint(-50f, -0.15f),
            new CurvePoint(-30f, -0.07f),
            new CurvePoint(-10f, -0.03f),
            new CurvePoint(-5f,  -0.015f),
            new CurvePoint(-3f,  -0.005f),
            new CurvePoint(-0,    0f),
            new CurvePoint(3f,    0.005f),
            new CurvePoint(5f,    0.012f),
            new CurvePoint(10f,   0.025f),
            new CurvePoint(30f,   0.05f),
            new CurvePoint(50f,   0.12f),
        };

        // Sum relationship value to multiplier - 1. Values are flipped if summary mood offset is negative
        // I know that this doesn't actually result in symmetric multipliers, but else we'll get x10 certainty loss if you hate everyone and everything in your ideology
        public static readonly SimpleCurve CertaintyMultiplierFromRelationships = new SimpleCurve
        {
            new CurvePoint(-1000f, -0.9f),
            new CurvePoint(-500f,  -0.5f),
            new CurvePoint(-200f,  -0.3f),
            new CurvePoint(-100f,  -0.1f),
            new CurvePoint(-50f,   -0.05f),
            new CurvePoint(-10f,   -0.02f),
            new CurvePoint(0f,     -0f),
            new CurvePoint(10f,     0.01f),
            new CurvePoint(50f,     0.03f),
            new CurvePoint(100f,    0.07f),
            new CurvePoint(200f,    0.2f),
            new CurvePoint(500f,    0.4f),
            new CurvePoint(1000f,   0.6f),
        };

        public Dictionary<Pawn, IdeoTrackerData> pawnTrackerData = new Dictionary<Pawn, IdeoTrackerData> ();
        public Dictionary<Ideo, List<Pawn>> ideoPawnsList = new Dictionary<Ideo, List<Pawn>>();

        public GameComponent_EnhancedBeliefs(Game game) { }

        public override void GameComponentTick()
        {
            base.GameComponentTick();
        }

        public IdeoTrackerData AddTracker(Pawn pawn)
        {
            IdeoTrackerData data = new IdeoTrackerData(pawn);
            pawnTrackerData[pawn] = data;
            return data;
        }

        public void AddIdeoTracker(Ideo ideo)
        {
            ideoPawnsList[ideo] = new List<Pawn>();
        }

        public float ConversionFactor(Pawn initiator, Pawn recipient)
        {
            return 1f;
        }

        public void SetIdeo(Pawn pawn, Ideo ideo)
        {
            if (!pawnTrackerData.ContainsKey(pawn))
            {
                AddTracker(pawn);
            }

            List<Ideo> ideoList = ideoPawnsList.Keys.ToList();

            for (int i = 0; i < ideoList.Count; i++)
            {
                Ideo ideo2 = ideoList[i];

                if (ideoPawnsList[ideo2].Contains(pawn))
                {
                    ideoPawnsList[ideo2].Remove(pawn);
                }
            }

            if (ideo == null)
            {
                return;
            }

            if (!ideoPawnsList.ContainsKey(ideo))
            {
                AddIdeoTracker(ideo);
            }

            if (!ideoPawnsList[ideo].Contains(pawn))
            {
                ideoPawnsList[ideo].Add(pawn);
            }
        }

        public static int BeliefDifferences(Ideo ideo1, Ideo ideo2)
        {
            int value = 0;

            for (int i = 0; i < ideo1.memes.Count; i++)
            {
                MemeDef meme1 = ideo1.memes[i];

                for (int j = 0; j < ideo2.memes.Count; j++)
                {
                    MemeDef meme2 = ideo2.memes[j];

                    if (meme1 == meme2)
                    {
                        value -= 1;
                    }
                    else if (meme1.exclusionTags.Intersect(meme2.exclusionTags).Count() > 0)
                    {
                        value += 1;
                    }
                }
            }

            return value;
        }

        public void BaseOpinionRecache(Ideo ideo)
        {
            List<Pawn> pawns = pawnTrackerData.Keys.ToList();

            for (int i = 0; i < pawns.Count; i++)
            {
                pawnTrackerData[pawns[i]].baseIdeoOpinions[ideo] = pawnTrackerData[pawns[i]].DefaultIdeoOpinion(ideo);
            }
        }

        public List<Pawn> GetIdeoPawns(Ideo ideo)
        {
            if (ideoPawnsList.ContainsKey(ideo))
            {
                return ideoPawnsList[ideo];
            }

            ideoPawnsList[ideo] = new List<Pawn>();
            List<Pawn> pawns = PawnsFinder.All_AliveOrDead;

            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];

                if (pawn.ideo != null && pawn.Ideo == ideo)
                {
                    ideoPawnsList[ideo].Add(pawn);
                }
            }
            return ideoPawnsList[ideo];
        }

        public void RemoveTracker(IdeoTrackerData tracker)
        {
            foreach (KeyValuePair<Pawn, IdeoTrackerData> pair in pawnTrackerData)
            {
                if (pair.Value == tracker)
                {
                    pawnTrackerData.Remove(pair.Key);
                    return;
                }
            }
        }
    }

    public class IdeoTrackerData : IExposable
    {
        public Pawn pawn;
        public int lastPositiveThoughtTick = -1;
        public float cachedCertaintyChange = -9999f;

        // Separate because recalculating base from memes in case player's ideo is fluid cuts down on overall performance cost
        // Breaks if you multiply opinion but you really shouldn't do that
        public Dictionary<Ideo, float> baseIdeoOpinions = new Dictionary<Ideo, float>();
        public Dictionary<Ideo, float> personalIdeoOpinions = new Dictionary<Ideo, float>();
        public Dictionary<Ideo, float> cachedRelationshipIdeoOpinions = new Dictionary<Ideo, float>();
        public Dictionary<Pawn, float> cachedRelationships = new Dictionary<Pawn, float>();

        public Dictionary<MemeDef, float> memeOpinions = new Dictionary<MemeDef, float>();
        public Dictionary<PreceptDef, float> preceptOpinions = new Dictionary<PreceptDef, float>();

        private List<Ideo> cache1;
        private List<Ideo> cache2;
        private List<MemeDef> cache3;
        private List<PreceptDef> cache4;
        private List<float> cache5;
        private List<float> cache6;
        private List<float> cache7;
        private List<float> cache8;

        public IdeoTrackerData()
        {

        }

        public IdeoTrackerData(Pawn pawn)
        {
            lastPositiveThoughtTick = Find.TickManager.TicksGame;
            this.pawn = pawn;
        }

        public void CertaintyChangeRecache(GameComponent_EnhancedBeliefs worldComp)
        {
            cachedCertaintyChange = 0;
            List<Thought> thoughts = new List<Thought>();
            pawn.needs.mood.thoughts.GetAllMoodThoughts(thoughts);
            float moodSum = 0;

            if (pawn.needs?.mood?.thoughts != null)
            {
                for (int i = 0; i < thoughts.Count; i++)
                {
                    Thought thought = thoughts[i];

                    if (thought.sourcePrecept != null || thought.def.Worker is ThoughtWorker_Precept)
                    {
                        moodSum += thought.MoodOffset();
                    }
                }
            }

            float moodCertaintyOffset = GameComponent_EnhancedBeliefs.CertaintyOffsetFromThoughts.Evaluate(moodSum);
            float relationshipMultiplier = 1 + GameComponent_EnhancedBeliefs.CertaintyMultiplierFromRelationships.Evaluate(IdeoOpinionFromRelationships(pawn.Ideo) / 0.02f) * Math.Sign(moodCertaintyOffset);

            cachedCertaintyChange += moodCertaintyOffset * relationshipMultiplier;

            // Certainty only starts decreasing at moods below stellar and after 3 days of lacking positive precept moodlets
            if (pawn.needs.mood.CurLevelPercentage < 0.8 && Find.TickManager.TicksGame - lastPositiveThoughtTick > GenDate.TicksPerDay * 3f)
            {
                cachedCertaintyChange -= GameComponent_EnhancedBeliefs.CertaintyLossFromInactivity.Evaluate((Find.TickManager.TicksGame - lastPositiveThoughtTick) / GenDate.TicksPerDay);
            }
        }

        // Form opinion based on memes, personal thoughts and experience with other pawns from that ideo
        public float IdeoOpinion(Ideo ideo)
        {
            if (!baseIdeoOpinions.ContainsKey(ideo) || !personalIdeoOpinions.ContainsKey(ideo))
            {
                baseIdeoOpinions[ideo] = DefaultIdeoOpinion(ideo);
                personalIdeoOpinions[ideo] = 0;
            }

            if (ideo == pawn.Ideo)
            {
                baseIdeoOpinions[ideo] = pawn.ideo.Certainty * 100f;
            }

            return Mathf.Clamp(baseIdeoOpinions[ideo] + PersonalIdeoOpinion(ideo) + IdeoOpinionFromRelationships(ideo), 0, 100) / 100f;
        }

        // Rundown on the function above, for UI reasons
        public float[] DetailedIdeoOpinion(Ideo ideo, bool noRelationship = false)
        {
            if (!baseIdeoOpinions.ContainsKey(ideo))
            {
                IdeoOpinion(ideo);
            }

            if (ideo == pawn.Ideo)
            {
                baseIdeoOpinions[ideo] = pawn.ideo.Certainty * 100f;
            }

            // In cases where you want to avoid recursion
            if (noRelationship)
            {
                return new float[] { baseIdeoOpinions[ideo] / 100f, PersonalIdeoOpinion(ideo) / 100f };
            }

            return new float[] { baseIdeoOpinions[ideo] / 100f, PersonalIdeoOpinion(ideo) / 100f, IdeoOpinionFromRelationships(ideo) / 100f };
        }

        // Get pawn's basic opinion from hearing about ideos beliefs, based on their traits, relationships and current ideo
        public float DefaultIdeoOpinion(Ideo ideo)
        {
            Ideo pawnIdeo = pawn.Ideo;

            if (ideo == pawnIdeo)
            {
                return pawn.ideo.Certainty * 100f;
            }

            float opinion = 30;

            if (pawn.Ideo.HasMeme(EnhancedBeliefsDefOf.Supremacist))
            {
                opinion -= 20;
            }
            else if (pawn.Ideo.HasMeme(EnhancedBeliefsDefOf.Loyalist))
            {
                opinion -= 10;
            }
            else if (pawn.Ideo.HasMeme(EnhancedBeliefsDefOf.Guilty))
            {
                opinion += 10;
            }

            for (int i = 0; i < ideo.memes.Count; i++)
            {
                MemeDef meme = ideo.memes[i];

                if (!meme.agreeableTraits.NullOrEmpty())
                {
                    for (int j = 0; j < meme.agreeableTraits.Count; j++)
                    {
                        TraitRequirement trait = meme.agreeableTraits[j];

                        if (trait.HasTrait(pawn))
                        {
                            opinion += 10;
                        }
                    }
                }

                if (!meme.disagreeableTraits.NullOrEmpty())
                {
                    for (int j = 0; j < meme.disagreeableTraits.Count; j++)
                    {
                        TraitRequirement trait = meme.disagreeableTraits[j];

                        if (trait.HasTrait(pawn))
                        {
                            opinion -= 10;
                        }
                    }
                }
            }

            for (int i = 0; i < pawn.Ideo.precepts.Count; i++)
            {
                Precept precept = pawn.Ideo.precepts[i];
                List<PreceptComp_OpinionOffset> comps = precept.TryGetComps<PreceptComp_OpinionOffset>();

                for (int j = 0; j < comps.Count; j++)
                {
                    opinion += comps[j].InternalOffset;
                }
            }

            for (int i = 0; i < ideo.precepts.Count; i++)
            {
                Precept precept = ideo.precepts[i];
                List<PreceptComp_OpinionOffset> comps = precept.TryGetComps<PreceptComp_OpinionOffset>();

                for (int j = 0; j < comps.Count; j++)
                {
                    opinion += comps[j].ExternalOffset + comps[j].GetTraitOpinion(pawn);
                }
            }

            // -5 opinion per incompatible meme, +5 per shared meme
            opinion -= GameComponent_EnhancedBeliefs.BeliefDifferences(pawnIdeo, ideo) * 5f;
            // Only decrease opinion if we don't like getting converted, shouldn't go the other way
            opinion *= Mathf.Clamp01(pawn.GetStatValue(StatDefOf.CertaintyLossFactor));

            return Mathf.Clamp(opinion, 0, 100);
        }

        public float PersonalIdeoOpinion(Ideo ideo)
        {
            if (!baseIdeoOpinions.ContainsKey(ideo) || !personalIdeoOpinions.ContainsKey(ideo))
            {
                baseIdeoOpinions[ideo] = DefaultIdeoOpinion(ideo);
                personalIdeoOpinions[ideo] = 0;
            }

            float opinion = 0;

            for (int i = 0; i < ideo.memes.Count; i++)
            {
                MemeDef meme = ideo.memes[i];
                if (memeOpinions.ContainsKey(meme))
                {
                    opinion += memeOpinions[meme];
                }
            }

            for (int i = 0; i < ideo.precepts.Count; i++)
            {
                PreceptDef precept = ideo.precepts[i].def;

                if (preceptOpinions.ContainsKey(precept))
                {
                    opinion += preceptOpinions[precept];
                }
            }

            // Makes sure that pawn's personal opinion cannot go below/above 100% purely from circlejerking
            float curOpinion = Mathf.Clamp(baseIdeoOpinions[ideo] + opinion, 0, 100);
            if (personalIdeoOpinions[ideo] > 100f - curOpinion)
            {
                personalIdeoOpinions[ideo] = 100f - curOpinion;
            }
            else if (personalIdeoOpinions[ideo] < -curOpinion)
            {
                personalIdeoOpinions[ideo] = -curOpinion;
            }

            return opinion + personalIdeoOpinions[ideo];
        }

        public float IdeoOpinionFromRelationships(Ideo ideo)
        {
            if (!cachedRelationshipIdeoOpinions.ContainsKey(ideo))
            {
                CacheRelationshipIdeoOpinion(ideo);
            }

            return cachedRelationshipIdeoOpinions[ideo];
        }

        // Calculates ideo opinion offset based on how much pawn likes other pawns of other ideos, should have little weight overall
        // Relationships are a dynamic mess of cosmic scale so there really isn't a better way to do this
        public void RecalculateRelationshipIdeoOpinions()
        {
            List<Ideo> storedIdeos = baseIdeoOpinions.Keys.ToList();

            for (int i = 0; i < baseIdeoOpinions.Count; i++)
            {
                CacheRelationshipIdeoOpinion(storedIdeos[i]);
            }
        }

        // Caches specific ideo opinion from relationships
        public void CacheRelationshipIdeoOpinion(Ideo ideo)
        {
            float opinion = 0;
            GameComponent_EnhancedBeliefs comp = Current.Game.GetComponent<GameComponent_EnhancedBeliefs>();
            List<Pawn> pawns = comp.GetIdeoPawns(ideo);

            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn otherPawn = pawns[i];

                // Up to +-2 opinion per pawn
                float pawnOpinion = pawn.relations.OpinionOf(otherPawn);
                opinion += pawnOpinion * 0.02f;
                cachedRelationships[pawn] = pawnOpinion;
            }

            cachedRelationshipIdeoOpinions[ideo] = opinion;
        }

        public void ExposeData()
        {
            Scribe_References.Look(ref pawn, "pawn");
            Scribe_Values.Look(ref lastPositiveThoughtTick, "lastPositiveThoughtTick");
            Scribe_Collections.Look(ref baseIdeoOpinions, "baseIdeoOpinions", LookMode.Reference, LookMode.Value, ref cache1, ref cache5);
            Scribe_Collections.Look(ref personalIdeoOpinions, "personalIdeoOpinions", LookMode.Reference, LookMode.Value, ref cache2, ref cache6);
            Scribe_Collections.Look(ref memeOpinions, "memeOpinions", LookMode.Def, LookMode.Value, ref cache3, ref cache7);
            Scribe_Collections.Look(ref preceptOpinions, "preceptOpinions", LookMode.Def, LookMode.Value, ref cache4, ref cache8);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                GameComponent_EnhancedBeliefs comp = Current.Game.GetComponent<GameComponent_EnhancedBeliefs>();

                if (pawn == null)
                {
                    comp.RemoveTracker(this);
                    return;
                }

                comp.SetIdeo(pawn, pawn.Ideo);
            }
        }

        // Change pawn's personal opinion of another ideo, usually positively
        public void AdjustPersonalOpinion(Ideo ideo, float power)
        {
            if (!baseIdeoOpinions.ContainsKey(ideo) || !personalIdeoOpinions.ContainsKey(ideo))
            {
                baseIdeoOpinions[ideo] = DefaultIdeoOpinion(ideo);
                personalIdeoOpinions[ideo] = 0;
            }

            personalIdeoOpinions[ideo] += power * 100f;
        }

        public void AdjustMemeOpinion(MemeDef meme, float power)
        {
            if (memeOpinions == null)
            {
                memeOpinions = new Dictionary<MemeDef, float>();
            }

            if (!memeOpinions.ContainsKey(meme))
            {
                memeOpinions[meme] = 0;
            }

            memeOpinions[meme] += power * 100f;
        }

        public void AdjustPreceptOpinion(PreceptDef precept, float power)
        {
            if (preceptOpinions == null)
            {
                preceptOpinions = new Dictionary<PreceptDef, float>();
            }

            if (!preceptOpinions.ContainsKey(precept))
            {
                preceptOpinions[precept] = 0;
            }

            preceptOpinions[precept] += power * 100f;
        }

        public float TrueMemeOpinion(MemeDef meme)
        {
            if (!memeOpinions.ContainsKey(meme))
            {
                memeOpinions[meme] = 0;
            }

            float opinion = memeOpinions[meme];

            if (!meme.agreeableTraits.NullOrEmpty())
            {
                for (int j = 0; j < meme.agreeableTraits.Count; j++)
                {
                    TraitRequirement trait = meme.agreeableTraits[j];

                    if (trait.HasTrait(pawn))
                    {
                        opinion += 10;
                    }
                }
            }

            if (!meme.disagreeableTraits.NullOrEmpty())
            {
                for (int j = 0; j < meme.disagreeableTraits.Count; j++)
                {
                    TraitRequirement trait = meme.disagreeableTraits[j];

                    if (trait.HasTrait(pawn))
                    {
                        opinion -= 10;
                    }
                }
            }

            return opinion;
        }

        public float TruePreceptOpinion(PreceptDef precept)
        {
            if (!preceptOpinions.ContainsKey(precept))
            {
                preceptOpinions[precept] = 0;
            }

            float opinion = preceptOpinions[precept];
            List<PreceptComp_OpinionOffset> comps = precept.TryGetComps<PreceptComp_OpinionOffset>();

            for (int j = 0; j < comps.Count; j++)
            {
                opinion += comps[j].ExternalOffset + comps[j].GetTraitOpinion(pawn);
            }

            return opinion;
        }

        public void RecacheAllBaseOpinions()
        {
            List<Ideo> ideoKeys = baseIdeoOpinions.Keys.ToList();

            for (int j = 0; j < ideoKeys.Count; j++)
            {
                baseIdeoOpinions[ideoKeys[j]] = DefaultIdeoOpinion(ideoKeys[j]);
            }
        }

        // Check if pawn should get converted to a new ideo after losing certainty in some way.
        public ConversionOutcome CheckConversion(Ideo priorityIdeo = null, bool noBreakdown = false, List<Ideo> excludeIdeos = null, List<Ideo> whitelistIdeos = null, float? opinionThreshold = null)
        {
            if (!ModLister.CheckIdeology("Ideoligion conversion") || pawn.DevelopmentalStage.Baby())
            {
                return ConversionOutcome.Failure;
            }
            if (Find.IdeoManager.classicMode)
            {
                return ConversionOutcome.Failure;
            }

            float certainty = pawn.ideo.Certainty;
            
            if (certainty > 0.2f)
            {
                return ConversionOutcome.Failure;
            }

            float threshold = certainty <= 0f ? 0.6f : 0.85f; //Drastically lower conversion threshold if we're about to have a breakdown

            if (opinionThreshold.HasValue) // Or if we're already having one
            {
                threshold = opinionThreshold.Value;
            }

            float currentOpinion = IdeoOpinion(pawn.Ideo);
            List<Ideo> ideos = whitelistIdeos == null ? Find.IdeoManager.IdeosListForReading : whitelistIdeos;
            ideos.SortBy((Ideo x) => IdeoOpinion(x));

            if (excludeIdeos != null)
            {
                ideos = ideos.Except(excludeIdeos).ToList();
            }

            // Moves priority ideo up to the top of the list so if the pawn is being converted and not having a random breakdown, they're gonna probably get converted to the target ideology
            if (priorityIdeo != null)
            {
                ideos.Remove(priorityIdeo);
                ideos.Add(priorityIdeo);
            }

            for (int i = ideos.Count - 1; i >= 0; i--)
            {
                Ideo ideo = ideos[i];

                if (ideo == pawn.Ideo)
                {
                    continue;
                }

                float opinion = IdeoOpinion(ideo);

                // Also don't convert in case we somehow like our current ideo significantly more than the new one
                // Either we have VERY high relationships with a lot of people or very strong personal opinions on current ideology for this to even be possible
                if (opinion < threshold || currentOpinion > opinion)
                {
                    continue;
                }

                // 17% minimal chance of conversion at 20% certrainty and 85% opinion, half that if we're being converted and this is a wrong ideology. Randomly converting to a wrong ideology should be just a rare lol moment
                if (Rand.Value > (1 - certainty * 4f) * (opinion + (certainty <= 0f ? 0.3f : 0)) * ((priorityIdeo != null && priorityIdeo != ideo) ? 0.5f : 1f))
                {
                    continue;
                }

                Ideo oldIdeo = pawn.Ideo;
                pawn.ideo.SetIdeo(ideo);
                ideo.Notify_MemberGainedByConversion();

                // Move personal opinion into certainty i.e. base opinion, then zero it, since base opinions are fixed and personal beliefs are what is usually meant by certainty anyways
                float[] rundown = DetailedIdeoOpinion(ideo);
                pawn.ideo.Certainty = Mathf.Min(rundown[0], 0.2f) + rundown[1];
                personalIdeoOpinions[ideo] = 0;
                
                // Keep current opinion of our old ideo by moving difference between new base and old base (certainty) into personal thoughts
                AdjustPersonalOpinion(oldIdeo, certainty - DetailedIdeoOpinion(oldIdeo)[0]);

                if (!pawn.ideo.PreviousIdeos.Contains(ideo))
                {
                    Find.HistoryEventsManager.RecordEvent(new HistoryEvent(HistoryEventDefOf.ConvertedNewMember, pawn.Named(HistoryEventArgsNames.Doer), ideo.Named(HistoryEventArgsNames.Ideo)));
                }

                RecacheAllBaseOpinions();

                return ConversionOutcome.Success;
            }

            if (certainty > 0f || noBreakdown)
            {
                return ConversionOutcome.Failure;
            }

            // Oops
            pawn.mindState.mentalStateHandler.TryStartMentalState(EnhancedBeliefsDefOf.IdeoChange);
            return ConversionOutcome.Breakdown;
        }
    }

    public enum ConversionOutcome : byte
    {
        Failure = 0,
        Breakdown = 1,
        Success = 2
    }
}
