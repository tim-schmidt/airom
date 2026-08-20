// AIrom's side of the oracle's search mode.
//
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using System.Globalization;
using Airom.Core;
using Airom.Data;
using Airom.Terminal;

namespace Airom.Oracle;

public static partial class OracleDump
{
    /// <summary>The first of the eighteen traps in the object table. Umoria's OBJ_TRAP_LIST.</summary>
    private const int TrapListStart = 378;

    private const int SecretDoorObject = 369;

    private const int OpenDoorObject = 367;

    /// <summary>
    /// Searching for what is hidden.
    ///
    /// The walk mode strips the level of objects, so nothing there is ever
    /// found. Here the opposite: the eight squares around the player are filled
    /// with the things a search can turn up - invisible traps of every kind,
    /// secret doors and a trapped chest - and the search is run over and over.
    ///
    /// Every square is rolled for separately, so what is found and in which
    /// order is entirely the generator's doing.
    /// </summary>
    public static void DumpSearch(TextWriter output, uint seed, int level, int rounds, int chance)
    {
        ArgumentNullException.ThrowIfNull(output);

        Header(output, "search", seed);
        output.Write("level " + level.ToString(CultureInfo.InvariantCulture) + "\n");
        output.Write("rounds " + rounds.ToString(CultureInfo.InvariantCulture) + "\n");
        output.Write("chance " + chance.ToString(CultureInfo.InvariantCulture) + "\n");

        (GameState game, _, MemoryScreen screen, GameLoop loop) = StrippedLevel(seed, level);

        // Ring the player with things to find. The traps walk the whole trap
        // list, so their names are compared as well as the finding of them.
        int k = 0;
        for (int row = game.CharacterRow - 1; row <= game.CharacterRow + 1; row++)
        {
            for (int column = game.CharacterColumn - 1; column <= game.CharacterColumn + 1; column++)
            {
                if (row == game.CharacterRow && column == game.CharacterColumn)
                {
                    continue;
                }

                int slot = game.Objects.Allocate();

                if (k == 7)
                {
                    // A chest, which is found differently: the trap on it is
                    // discovered rather than the chest itself.
                    game.Objects[slot].CopyFrom(OpenDoorObject);
                    game.Objects[slot].TVal = ItemCategory.Chest;
                    game.Objects[slot].Flags = ChestFlags.LoseStrength | ChestFlags.Poison;
                }
                else if (k == 6)
                {
                    game.Objects[slot].CopyFrom(SecretDoorObject);
                }
                else
                {
                    game.Objects[slot].CopyFrom(TrapListStart + k);
                }

                game.Cave[row, column].ObjectIndex = slot;
                k++;
            }
        }

        // Spaces to answer any -more- the run of messages puts up: a good
        // searcher finds several things in a round, and the message line only
        // holds two.
        screen.SendKeys(new string(' ', 2000));

        for (int round = 0; round < rounds; round++)
        {
            loop.Movement.Search(game.CharacterRow, game.CharacterColumn, chance);
            output.Write("round " + round.ToString(CultureInfo.InvariantCulture) + "\n");

            k = 0;
            for (int row = game.CharacterRow - 1; row <= game.CharacterRow + 1; row++)
            {
                for (int column = game.CharacterColumn - 1;
                     column <= game.CharacterColumn + 1;
                     column++)
                {
                    if (row == game.CharacterRow && column == game.CharacterColumn)
                    {
                        continue;
                    }

                    InvenType item = game.Objects[game.Cave[row, column].ObjectIndex];
                    output.Write(string.Join(
                        ' ',
                        "  found",
                        k.ToString(CultureInfo.InvariantCulture),
                        "tval",
                        ((int)item.TVal).ToString(CultureInfo.InvariantCulture),
                        "index",
                        item.Index.ToString(CultureInfo.InvariantCulture),
                        "ident",
                        ((int)item.Identification).ToString(CultureInfo.InvariantCulture)) + "\n");
                    k++;
                }
            }
        }

        for (int row = 0; row < screen.Rows; row++)
        {
            output.Write(
                "srch " + row.ToString(CultureInfo.InvariantCulture)
                + " " + screen.GetRow(row).TrimEnd() + "\n");
        }

        Line(output, "final-state", game.Rng.State);
    }
}
