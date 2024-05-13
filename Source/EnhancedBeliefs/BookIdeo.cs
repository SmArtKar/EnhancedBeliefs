using Mono.Unix.Native;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;
using static RimWorld.IdeoDescriptionUtility;
using Verse.Grammar;
using static RimWorld.IdeoFoundation_Deity;

namespace EnhancedBeliefs
{
    public class BookIdeo : Book
    {
        public ReadingOutcomeDoer_CertaintyChange doer;

        public ReadingOutcomeDoer_CertaintyChange Doer
        {
            get
            {
                if (doer == null)
                {
                    CompBook comp = GetComp<CompBook>();

                    for (int i = 0; i < comp.doers.Count; i++)
                    {
                        if (comp.doers[i] is ReadingOutcomeDoer_CertaintyChange change)
                        {
                            doer = change;
                            break;
                        }
                    }
                }

                return doer;
            }
        }

        public Ideo Ideo
        {
            get
            {
                return Doer.ideo;
            }
            set
            {
                Doer.ideo = value;
            }
        }

        public override void PostQualitySet()
        {
        }

        public override void GenerateBook(Pawn author = null, long? fixedDate = null)
        {
            base.GenerateBook(author, fixedDate);

            if (Ideo != null)
            {
                RegenerateName();
            }
        }

        // Ensure that traders get their book ideo
        public override void PostGeneratedForTrader(TraderKindDef trader, int forTile, Faction forFaction)
        {
            base.PostGeneratedForTrader(trader, forTile, forFaction);


            if (Ideo == null)
            {
                if (forFaction == null || forFaction.ideos == null)
                {
                    Ideo = Find.IdeoManager.IdeosListForReading.RandomElement();
                }
                else
                {
                    Ideo = forFaction.ideos.PrimaryIdeo;
                }
            }

            RegenerateName();
        }

        // Checks for null ideos in case something goes wrong
        public override void TickRare()
        {
            base.TickRare();

            if (Ideo == null)
            {
                Ideo = Find.IdeoManager.IdeosListForReading.RandomElement();
                RegenerateName();
            }
        }

        //Completely copied over from ideo generation code, also generates description
        public void RegenerateName()
        {
            GrammarRequest request = default(GrammarRequest);
            request.Includes.Add(Ideo.culture.ideoNameMaker);
            IdeoFoundation foundation = Ideo.foundation;
            IdeoFoundation_Deity foundationDeity = foundation as IdeoFoundation_Deity;
            foundation.AddPlaceRules(ref request);
            if (foundationDeity != null)
            {
                foundationDeity.AddDeityRules(ref request);
            }
            List<SymbolSource> list = new List<SymbolSource>();
            if (Ideo.memes.Any((MemeDef m) => !m.symbolPacks.NullOrEmpty()))
            {
                list.Add(SymbolSource.Pack);
            }
            if (foundationDeity != null && foundationDeity.deities.Count >= 1 && !Ideo.memes.Any((MemeDef m) => !m.allowSymbolsFromDeity))
            {
                list.Add(SymbolSource.Deity);
            }
            if (list.Count == 0)
            {
                return;
            }
            switch (list.RandomElementByWeight((SymbolSource s) => s == SymbolSource.Pack ? 1f : 0.5f))
            {
                case SymbolSource.Pack:
                    SetupFromSymbolPack();
                    break;
                case SymbolSource.Deity:
                    SetupFromDeity();
                    break;
            }
            title = GenText.CapitalizeAsTitle(GrammarResolver.Resolve("r_ideoName", request, null, false, null, null, null, true));

            List<IdeoDescriptionMaker.PatternEntry> patterns = (from entry in Ideo.memes.Where((MemeDef meme) => meme.descriptionMaker?.patterns != null).SelectMany((MemeDef meme) => meme.descriptionMaker.patterns)
                                                            group entry by entry.def into grp
                                                            select grp.MaxBy((IdeoDescriptionMaker.PatternEntry entry) => entry.weight)).ToList();
            if (!list.Any())
            {
                return;
            }

            IdeoStoryPatternDef def = patterns.RandomElementByWeight((IdeoDescriptionMaker.PatternEntry entry) => entry.weight).def;
            descriptionFlavor = IdeoDescriptionUtility.ResolveDescription(Ideo, def, true).text;
            description = GenerateFullDescription();

            void AddMemeContent()
            {
                foreach (MemeDef item in Ideo.memes)
                {
                    if (item.generalRules != null)
                    {
                        request.IncludesBare.Add(item.generalRules);
                    }
                }
            }
            void AddSymbolPack(IdeoSymbolPack pack, MemeCategory memeCategory)
            {
                request.Constants.SetOrAdd("forcePrefix", pack.prefix.ToString());
                string text = (pack.prefix ? (GrammarResolver.Resolve("hyphenPrefix", request) + "-") : string.Empty);
                if (pack.ideoName != null)
                {
                    if (memeCategory == MemeCategory.Structure)
                    {
                        request.Rules.Add(new Rule_String("packIdeoNameStructure", text + pack.ideoName));
                    }
                    else
                    {
                        request.Rules.Add(new Rule_String("packIdeoName", text + pack.ideoName));
                    }
                }
                if (pack.theme != null)
                {
                    request.Rules.Add(new Rule_String("packTheme", pack.theme));
                }
                if (pack.adjective != null)
                {
                    request.Rules.Add(new Rule_String("packAdjective", text + pack.adjective));
                }
                if (pack.member != null)
                {
                    request.Rules.Add(new Rule_String("packMember", text + pack.member));
                }
            }
            void SetupFromDeity()
            {
                request.Rules.Add(new Rule_String("keyDeity", Ideo.KeyDeityName));
                AddMemeContent();
            }
            void SetupFromSymbolPack()
            {
                MemeDef result;
                if (Ideo.StructureMeme.symbolPackOverride)
                {
                    result = Ideo.StructureMeme;
                }
                else if (!Ideo.memes.Where((MemeDef m) => m.symbolPacks.HasData() && m.symbolPacks.Any()).TryRandomElement(out result))
                {
                    result = Ideo.memes.Where((MemeDef m) => m.symbolPacks.HasData()).RandomElement();
                }
                AddMemeContent();
                if (result.symbolPacks.TryRandomElement(out var result2))
                {
                    AddSymbolPack(result2, result.category);
                }
                else
                {
                    AddSymbolPack(result.symbolPacks.RandomElement(), result.category);
                }
            }
        }
    }
}
