// Ported from store1.c in Umoria 5.6, with the identity helpers from desc.c.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using Airom.Data;

namespace Airom.Core;

/// <summary>
/// One line of a shop's stock: an item and what the owner is asking for it.
/// Mirrors Umoria's inven_record.
/// </summary>
public sealed class StockLine
{
    public InvenType Item { get; } = new();

    /// <summary>
    /// Asking price. Stored negative while the owner has not yet named it aloud,
    /// which is how the haggling code tells an unquoted price from a settled one.
    /// </summary>
    public int Cost { get; set; }

    public void CopyFrom(StockLine other)
    {
        ArgumentNullException.ThrowIfNull(other);
        Item.CopyStateFrom(other.Item);
        Cost = other.Cost;
    }

    public void Clear()
    {
        Item.Clear();
        Cost = 0;
    }
}

/// <summary>
/// One of the six town shops. Mirrors Umoria's store_type.
/// </summary>
public sealed class Store
{
    /// <summary>Umoria's STORE_INVEN_MAX: distinct items a shop can hold.</summary>
    public const int MaxStock = 24;

    /// <summary>Index into <see cref="GameTables.Owners"/>.</summary>
    public int Owner { get; set; }

    /// <summary>Insults taken this visit, before the owner throws the player out.</summary>
    public int InsultsThisVisit { get; set; }

    /// <summary>Turn the shop reopens, after being closed for the day.</summary>
    public int OpenAgainAt { get; set; }

    /// <summary>How many stock lines are in use.</summary>
    public int StockCount { get; set; }

    /// <summary>Haggles the player has won, which softens the owner.</summary>
    public int GoodBuys { get; set; }

    /// <summary>Haggles the player has lost.</summary>
    public int BadBuys { get; set; }

    public StockLine[] Stock { get; } = [.. Enumerable.Range(0, MaxStock).Select(_ => new StockLine())];
}

/// <summary>
/// The six town shops: what they hold, what they ask, and how their stock turns
/// over between visits. Mirrors store1.c.
/// </summary>
public sealed class Stores(GameState game)
{
    private const int StoreCount = 6;        // MAX_STORES
    private const int StockChoices = 26;     // STORE_CHOICES
    private const int TownItemLevel = 7;     // OBJ_TOWN_LEVEL
    private const int MinStock = 10;         // STORE_MIN_INVEN
    private const int MaxStockBeforeSelling = 18; // STORE_MAX_INVEN
    private const int TurnAround = 9;        // STORE_TURN_AROUND

    /// <summary>
    /// Sub-values at or above this stack in a shop; those above
    /// <see cref="GroupMin"/> arrive in groups and price per group.
    /// </summary>
    private const int SingleStackMin = 64;

    private const int SingleStackMax = 192;
    private const int GroupMin = 192;

    private readonly GameState _game = game;

    /// <summary>The six shops, in the order the store predicates number them.</summary>
    public Store[] All { get; } = [.. Enumerable.Range(0, StoreCount).Select(_ => new Store())];

    private Rng Rng => _game.Rng;

    /// <summary>
    /// Gives each shop an owner and empties its shelves. Mirrors store_init().
    ///
    /// Owners are grouped one per shop, so shop n draws only from the owners at
    /// positions n, n+6, n+12 - which is why the owner table has to stay in
    /// groups of six.
    /// </summary>
    public void Initialise()
    {
        int groups = GameTables.Owners.Length / StoreCount;

        for (int i = 0; i < StoreCount; i++)
        {
            Store store = All[i];
            store.Owner = (StoreCount * (Rng.RandInt(groups) - 1)) + i;
            store.InsultsThisVisit = 0;
            store.OpenAgainAt = 0;
            store.StockCount = 0;
            store.GoodBuys = 0;
            store.BadBuys = 0;

            foreach (StockLine line in store.Stock)
            {
                line.Clear();
            }
        }
    }

    /// <summary>
    /// What a shop reckons an item is worth. Mirrors item_value().
    ///
    /// The branches all turn on how much the player knows: an unidentified ring
    /// fetches a flat 45 whatever it really is, and a known-cursed item fetches
    /// nothing. Shop stock is always fully identified, so for restocking only
    /// the known paths run.
    /// </summary>
    public static int ItemValue(InvenType item)
    {
        ArgumentNullException.ThrowIfNull(item);

        int value = item.Cost;

        if ((item.Identification & Identification.Damned) != 0)
        {
            return 0; // nobody knowingly buys a cursed item
        }

        bool known1 = IsKnown1(item);
        bool known2 = (item.Identification & Identification.Known) != 0;

        if ((item.TVal >= ItemCategory.Bow && item.TVal <= ItemCategory.Sword)
            || (item.TVal >= ItemCategory.Boots && item.TVal <= ItemCategory.SoftArmor))
        {
            if (!known2)
            {
                value = GameTables.ObjectList[item.Index].Cost;
            }
            else if (item.TVal >= ItemCategory.Bow && item.TVal <= ItemCategory.Sword)
            {
                value = item.ToHit < 0 || item.ToDam < 0 || item.ToAc < 0
                    ? 0
                    : item.Cost + ((item.ToHit + item.ToDam + item.ToAc) * 100);
            }
            else
            {
                value = item.ToAc < 0 ? 0 : item.Cost + (item.ToAc * 100);
            }
        }
        else if (item.TVal >= ItemCategory.SlingAmmo && item.TVal <= ItemCategory.Spike)
        {
            if (!known2)
            {
                value = GameTables.ObjectList[item.Index].Cost;
            }
            else
            {
                // Five rather than a hundred, because missiles come in twenties.
                value = item.ToHit < 0 || item.ToDam < 0 || item.ToAc < 0
                    ? 0
                    : item.Cost + ((item.ToHit + item.ToDam + item.ToAc) * 5);
            }
        }
        else if (item.TVal is ItemCategory.Scroll1 or ItemCategory.Scroll2
            or ItemCategory.Potion1 or ItemCategory.Potion2)
        {
            if (!known1)
            {
                value = 20;
            }
        }
        else if (item.TVal == ItemCategory.Food)
        {
            if (item.SubVal < SingleStackMin + GameTables.Mushrooms.Length && !known1)
            {
                value = 1;
            }
        }
        else if (item.TVal is ItemCategory.Amulet or ItemCategory.Ring)
        {
            if (!known1)
            {
                value = 45;
            }
            else if (!known2)
            {
                // The player knows the kind but not whether it is cursed. Paying
                // the book price stops the shop being used as a free identify.
                value = GameTables.ObjectList[item.Index].Cost;
            }
        }
        else if (item.TVal is ItemCategory.Staff or ItemCategory.Wand)
        {
            if (!known1)
            {
                value = item.TVal == ItemCategory.Wand ? 50 : 70;
            }
            else if (known2)
            {
                value = item.Cost + (item.Cost / 20 * item.P1);
            }
        }
        else if (item.TVal == ItemCategory.Digging)
        {
            if (!known2)
            {
                value = GameTables.ObjectList[item.Index].Cost;
            }
            else if (item.P1 < 0)
            {
                value = 0;
            }
            else
            {
                // Some digging tools start with a non-zero p1, so only the
                // improvement over the template is paid for.
                value = item.Cost + ((item.P1 - GameTables.ObjectList[item.Index].P1) * 100);
                if (value < 0)
                {
                    value = 0;
                }
            }
        }

        // Group items are priced by the pile. Torches are excluded.
        if (item.SubVal > GroupMin)
        {
            value *= item.Number;
        }

        return value;
    }

    /// <summary>
    /// Whether the player recognises what kind of thing this is. Mirrors
    /// known1_p() in desc.c.
    ///
    /// Items with no disguise - a sword is visibly a sword - are always known.
    /// Only the categories with a randomised appearance can be a mystery, and
    /// anything bought from a shop is known by definition.
    /// </summary>
    private static bool IsKnown1(InvenType item)
    {
        if (AppearanceGroup(item) < 0)
        {
            return true;
        }

        // Shop stock is identified on the shelf.
        return (item.Identification & Identification.StoreBought) != 0;
    }

    /// <summary>
    /// Which appearance table an item is disguised by, or -1 for none. Mirrors
    /// object_offset().
    /// </summary>
    private static int AppearanceGroup(InvenType item) => item.TVal switch
    {
        ItemCategory.Amulet => 0,
        ItemCategory.Ring => 1,
        ItemCategory.Staff => 2,
        ItemCategory.Wand => 3,
        ItemCategory.Scroll1 or ItemCategory.Scroll2 => 4,
        ItemCategory.Potion1 or ItemCategory.Potion2 => 5,
        ItemCategory.Food =>
            (item.SubVal & (SingleStackMin - 1)) < GameTables.Mushrooms.Length ? 6 : -1,
        _ => -1,
    };

    /// <summary>
    /// What the owner will ask. Mirrors sell_price().
    ///
    /// The base value is adjusted by how the owner's race regards the player's,
    /// then spread into the range the haggling will run between.
    /// </summary>
    /// <returns>The adjusted value, or zero if the shop will not stock it.</returns>
    public int SellPrice(int storeIndex, InvenType item, out int maxPrice, out int minPrice)
    {
        ArgumentNullException.ThrowIfNull(item);

        maxPrice = 0;
        minPrice = 0;

        int value = ItemValue(item);

        // item.Cost guards a cursed item, value guards a damaged one.
        if (item.Cost <= 0 || value <= 0)
        {
            return 0;
        }

        OwnerType owner = GameTables.Owners[All[storeIndex].Owner];
        value = value * GameTables.RaceGoldAdjust[owner.OwnerRace][_game.PlayerRace] / 100;
        if (value < 1)
        {
            value = 1;
        }

        maxPrice = value * owner.MaxInflate / 100;
        minPrice = value * owner.MinInflate / 100;
        if (minPrice > maxPrice)
        {
            minPrice = maxPrice;
        }

        return value;
    }

    /// <summary>
    /// Whether the shop has room for this. Mirrors store_check_num().
    ///
    /// A full shop can still take something that stacks onto a line it already
    /// holds, which is why this is more than a count.
    /// </summary>
    public bool HasRoomFor(int storeIndex, InvenType item)
    {
        ArgumentNullException.ThrowIfNull(item);

        Store store = All[storeIndex];
        if (store.StockCount < Store.MaxStock)
        {
            return true;
        }

        if (item.SubVal < SingleStackMin)
        {
            return false;
        }

        for (int i = 0; i < store.StockCount; i++)
        {
            InvenType held = store.Stock[i].Item;

            // Items at or above the group threshold only stack when their
            // sub-values and their p1 both match.
            if (held.TVal == item.TVal
                && held.SubVal == item.SubVal
                && held.Number + item.Number < 256
                && (item.SubVal < GroupMin || held.P1 == item.P1))
            {
                return true;
            }
        }

        return false;
    }

    private void InsertStock(int storeIndex, int position, int cost, InvenType item)
    {
        Store store = All[storeIndex];

        for (int i = store.StockCount - 1; i >= position; i--)
        {
            store.Stock[i + 1].CopyFrom(store.Stock[i]);
        }

        store.Stock[position].Item.CopyStateFrom(item);

        // Negative until the owner names the price aloud.
        store.Stock[position].Cost = -cost;
        store.StockCount++;
    }

    /// <summary>
    /// Puts an item on the shelves, stacking or inserting to keep the stock
    /// ordered by category. Mirrors store_carry().
    /// </summary>
    public void Carry(int storeIndex, InvenType item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (SellPrice(storeIndex, item, out int cost, out _) <= 0)
        {
            return;
        }

        Store store = All[storeIndex];
        int slot = 0;
        bool done = false;
        int arriving = item.Number;

        do
        {
            InvenType held = store.Stock[slot].Item;

            if (held.TVal == item.TVal)
            {
                if (item.SubVal == held.SubVal
                    && item.SubVal >= SingleStackMin
                    && (item.SubVal < GroupMin || held.P1 == item.P1))
                {
                    held.Number = (byte)(held.Number + arriving);

                    if (item.SubVal > GroupMin)
                    {
                        // Group prices are for the whole pile, so the line has
                        // to be repriced now it is larger.
                        SellPrice(storeIndex, held, out cost, out _);
                        store.Stock[slot].Cost = -cost;
                    }
                    else if (held.Number > 24)
                    {
                        held.Number = 24;
                    }

                    done = true;
                }
            }
            else if (item.TVal > held.TVal)
            {
                InsertStock(storeIndex, slot, cost, item);
                done = true;
            }

            slot++;
        }
        while (slot < store.StockCount && !done);

        if (!done)
        {
            InsertStock(storeIndex, store.StockCount, cost, item);
        }
    }

    /// <summary>
    /// Removes stock. Mirrors store_destroy().
    /// </summary>
    /// <param name="oneOnly">
    /// True when a single item is sold. False means the shop is clearing the
    /// line, though stackable goods only lose a random part of the pile - which
    /// the original notes keeps the general store and alchemist worth visiting.
    /// </param>
    public void Destroy(int storeIndex, int slot, bool oneOnly)
    {
        Store store = All[storeIndex];
        InvenType item = store.Stock[slot].Item;

        int removed = item.SubVal >= SingleStackMin && item.SubVal <= SingleStackMax
            ? (oneOnly ? 1 : Rng.RandInt(item.Number))
            : item.Number;

        if (removed != item.Number)
        {
            item.Number = (byte)(item.Number - removed);
            return;
        }

        for (int i = slot; i < store.StockCount - 1; i++)
        {
            store.Stock[i].CopyFrom(store.Stock[i + 1]);
        }

        store.Stock[store.StockCount - 1].Clear();
        store.StockCount--;
    }

    /// <summary>
    /// Generates one item for a shop and shelves it. Mirrors store_create().
    ///
    /// It draws from the shop's own stocking list, enchants at town level, and
    /// keeps only what is worth something and within the owner's means. Four
    /// attempts, then it gives up - so a restock can leave a shop shorter than
    /// intended.
    /// </summary>
    public void CreateStock(int storeIndex)
    {
        Store store = All[storeIndex];
        var candidate = new InvenType();
        int tries = 0;

        do
        {
            int pick = GameTables.StoreChoice[storeIndex][Rng.RandInt(StockChoices) - 1];
            candidate.CopyFrom(pick);
            _game.Enchantment.Apply(candidate, TownItemLevel);

            if (HasRoomFor(storeIndex, candidate)
                && candidate.Cost > 0
                && candidate.Cost < GameTables.Owners[store.Owner].MaxCost)
            {
                // Shop stock is identified on the shelf.
                candidate.Identification |=
                    Identification.StoreBought | Identification.Known;

                Carry(storeIndex, candidate);
                tries = 10;
            }

            tries++;
        }
        while (tries <= 3);
    }

    /// <summary>
    /// Turns the shops' stock over. Mirrors store_maint().
    ///
    /// A well-stocked shop sells some off and a bare one takes some in, both by
    /// amounts drawn around <see cref="TurnAround"/>. The two are not exclusive:
    /// a shop between the thresholds does both, which is what keeps its shelves
    /// changing rather than merely filling.
    /// </summary>
    public void Maintain()
    {
        for (int i = 0; i < StoreCount; i++)
        {
            Store store = All[i];
            store.InsultsThisVisit = 0;

            if (store.StockCount >= MinStock)
            {
                int selling = Rng.RandInt(TurnAround);
                if (store.StockCount >= MaxStockBeforeSelling)
                {
                    selling += 1 + store.StockCount - MaxStockBeforeSelling;
                }

                while (--selling >= 0)
                {
                    Destroy(i, Rng.RandInt(store.StockCount) - 1, oneOnly: false);
                }
            }

            if (store.StockCount <= MaxStockBeforeSelling)
            {
                int buying = Rng.RandInt(TurnAround);
                if (store.StockCount < MinStock)
                {
                    buying += MinStock - store.StockCount;
                }

                while (--buying >= 0)
                {
                    CreateStock(i);
                }
            }
        }
    }
}
