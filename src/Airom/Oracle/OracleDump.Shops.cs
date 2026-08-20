// AIrom's side of the oracle's shops mode.
//
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using System.Globalization;
using Airom.Core;

namespace Airom.Oracle;

public static partial class OracleDump
{
    /// <summary>
    /// The six shops: their owners, their stock and their asking prices.
    ///
    /// Repeating the maintenance simulates the shops changing across several
    /// visits to town, which is where the interesting behaviour is - a shop that
    /// only ever filled up would look the same after the first pass.
    /// </summary>
    public static void DumpShops(TextWriter output, uint seed, int rounds)
    {
        ArgumentNullException.ThrowIfNull(output);

        Header(output, "shops", seed);
        output.Write("rounds " + rounds.ToString(CultureInfo.InvariantCulture) + "\n");

        var game = new GameState();
        game.InitSeeds(seed);
        game.MagicInit();
        game.DungeonLevel = 0;
        game.Objects.Reset();

        Stores stores = game.Stores;
        stores.Initialise();

        for (int i = 0; i < stores.All.Length; i++)
        {
            output.Write(string.Join(
                ' ',
                "owner",
                i.ToString(CultureInfo.InvariantCulture),
                stores.All[i].Owner.ToString(CultureInfo.InvariantCulture)) + "\n");
        }

        for (int round = 0; round < rounds; round++)
        {
            stores.Maintain();

            output.Write(string.Join(
                ' ',
                "round",
                round.ToString(CultureInfo.InvariantCulture),
                "state",
                game.Rng.State.ToString(CultureInfo.InvariantCulture)) + "\n");

            for (int i = 0; i < stores.All.Length; i++)
            {
                Store store = stores.All[i];

                output.Write(string.Join(
                    ' ',
                    "store",
                    round.ToString(CultureInfo.InvariantCulture),
                    i.ToString(CultureInfo.InvariantCulture),
                    store.StockCount.ToString(CultureInfo.InvariantCulture)) + "\n");

                for (int j = 0; j < store.StockCount; j++)
                {
                    InvenType item = store.Stock[j].Item;
                    output.Write(string.Join(
                        ' ',
                        "stock",
                        round.ToString(CultureInfo.InvariantCulture),
                        i.ToString(CultureInfo.InvariantCulture),
                        j.ToString(CultureInfo.InvariantCulture),
                        item.Index.ToString(CultureInfo.InvariantCulture),
                        item.TVal.ToString(CultureInfo.InvariantCulture),
                        item.SubVal.ToString(CultureInfo.InvariantCulture),
                        item.Number.ToString(CultureInfo.InvariantCulture),
                        item.Cost.ToString(CultureInfo.InvariantCulture),
                        store.Stock[j].Cost.ToString(CultureInfo.InvariantCulture),
                        item.P1.ToString(CultureInfo.InvariantCulture),
                        item.SpecialName.ToString(CultureInfo.InvariantCulture),
                        item.Identification.ToString(CultureInfo.InvariantCulture)) + "\n");
                }
            }
        }

        Line(output, "final-state", game.Rng.State);
    }
}
