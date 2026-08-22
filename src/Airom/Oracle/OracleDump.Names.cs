// AIrom's side of the oracle's names mode.
//
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using System.Globalization;
using Airom.Core;
using Airom.Data;

namespace Airom.Oracle;

public static partial class OracleDump
{
    /// <summary>
    /// Naming things.
    ///
    /// objdes() builds every item name in the game, and what it can say depends
    /// on what the player knows: an unidentified wand is named by its metal, the
    /// same wand once identified by what it does, and either may carry a count,
    /// an article, dice, bonuses, charges and a brace of guesses at the end.
    ///
    /// Every object in the table is named four ways - unknown, kind known,
    /// enchantment known, both - and again as a pile rather than one, so the
    /// pluralising and the article are covered too. The scrolls are the reason
    /// this has to run after magic_init(): their titles are made of shuffled
    /// syllables.
    /// </summary>
    public static void DumpNames(TextWriter output, uint seed, int first, int count)
    {
        ArgumentNullException.ThrowIfNull(output);

        Header(output, "names", seed);
        output.Write("first " + first.ToString(CultureInfo.InvariantCulture) + "\n");
        output.Write("count " + count.ToString(CultureInfo.InvariantCulture) + "\n");

        var game = OracleGame();
        game.InitSeeds(seed);
        game.MagicInit();

        var knowledge = new ItemKnowledge();
        var names = new ItemNames(game.Appearances, knowledge);
        var item = new InvenType();

        for (int i = first; i < first + count && i < GameTables.ObjectList.Length; i++)
        {
            for (int variation = 0; variation < 8; variation++)
            {
                item.CopyFrom(i);

                // Something worth printing in every field: a pile of four, an
                // enchantment, plusses and a name that only shows once
                // identified.
                if ((variation & 4) != 0)
                {
                    item.Number = 4;
                    item.ToHit = 7;
                    item.ToDam = -3;
                    item.ToAc = 2;
                    item.P1 = 5;
                    item.SpecialName = SpecialName.SlayDragon;
                    item.Identification |= Identification.Magik;
                }

                // Forget everything about this kind, then learn back what the
                // variation calls for.
                knowledge.Forget(item);

                if ((variation & 1) != 0)
                {
                    knowledge.LearnKind(item);
                }
                else
                {
                    knowledge.MarkTried(item);
                }

                if ((variation & 2) != 0)
                {
                    item.Identification |= Identification.Known;
                }

                output.Write(string.Join(
                    ' ',
                    i.ToString(CultureInfo.InvariantCulture),
                    variation.ToString(CultureInfo.InvariantCulture),
                    "full",
                    names.Describe(item, withArticle: true)) + "\n");

                output.Write(string.Join(
                    ' ',
                    i.ToString(CultureInfo.InvariantCulture),
                    variation.ToString(CultureInfo.InvariantCulture),
                    "bare",
                    names.Describe(item, withArticle: false)) + "\n");
            }
        }

        Line(output, "final-state", game.Rng.State);
    }
}
