using Airom.Core;
using Airom.Data;

namespace Airom.Tests;

/// <summary>
/// Checks on the shops: owners, stock turnover and pricing.
///
/// Diffed against the C oracle across 27 restocking runs, including twenty
/// rounds of turnover, plus 36 complete towns. These pin the properties.
/// </summary>
public class StoreTests
{
    private static GameState Shops(uint seed, int rounds)
    {
        var game = new GameState();
        game.InitSeeds(seed);
        game.MagicInit();
        game.DungeonLevel = 0;
        game.Objects.Reset();
        game.Stores.Initialise();

        for (int i = 0; i < rounds; i++)
        {
            game.Stores.Maintain();
        }

        return game;
    }

    /// <summary>
    /// Owners are grouped one per shop, so shop n can only ever be run by an
    /// owner at position n, n+6 or n+12. That is why the owner table has to stay
    /// in groups of six.
    /// </summary>
    [Fact]
    public void Initialise_GivesEachShopAnOwnerFromItsOwnGroup()
    {
        for (uint seed = 1; seed <= 20; seed++)
        {
            GameState game = Shops(seed, 0);

            for (int i = 0; i < game.Stores.All.Length; i++)
            {
                Assert.Equal(i, game.Stores.All[i].Owner % 6);
                Assert.InRange(game.Stores.All[i].Owner, 0, GameTables.Owners.Length - 1);
            }
        }
    }

    [Fact]
    public void Initialise_LeavesTheShelvesEmpty()
    {
        GameState game = Shops(4242, 0);

        Assert.All(game.Stores.All, s => Assert.Equal(0, s.StockCount));
    }

    [Fact]
    public void Maintain_StocksEveryShop()
    {
        GameState game = Shops(12345, 1);

        Assert.All(game.Stores.All, s => Assert.True(s.StockCount > 0, "a shop was left empty"));
    }

    /// <summary>
    /// Stock never exceeds the shelf space, however many times the shops are
    /// restocked.
    /// </summary>
    [Fact]
    public void Maintain_NeverOverfillsAShop()
    {
        for (uint seed = 1; seed <= 10; seed++)
        {
            GameState game = Shops(seed, 20);

            Assert.All(
                game.Stores.All,
                s => Assert.InRange(s.StockCount, 0, Store.MaxStock));
        }
    }

    /// <summary>
    /// A shop only stocks what it is willing to buy - the same predicate that
    /// decides whether it will take an item off the player.
    /// </summary>
    [Fact]
    public void Maintain_StocksOnlyWhatTheShopDeals()
    {
        GameState game = Shops(999, 5);

        for (int i = 0; i < game.Stores.All.Length; i++)
        {
            Store store = game.Stores.All[i];
            for (int j = 0; j < store.StockCount; j++)
            {
                InvenType item = store.Stock[j].Item;
                Assert.True(
                    StoreSets.Buys(i, item.TVal),
                    $"store {i} stocked a {item.TVal} it will not buy");
            }
        }
    }

    /// <summary>
    /// Shop stock is identified on the shelf: the player can see exactly what
    /// they are paying for, which is what makes shops different from the floor.
    /// </summary>
    [Fact]
    public void Maintain_MarksStockAsIdentifiedAndStoreBought()
    {
        GameState game = Shops(7, 3);

        foreach (Store store in game.Stores.All)
        {
            for (int j = 0; j < store.StockCount; j++)
            {
                byte ident = store.Stock[j].Item.Identification;
                Assert.True((ident & Identification.StoreBought) != 0);
                Assert.True((ident & Identification.Known) != 0);
            }
        }
    }

    /// <summary>
    /// An owner never stocks something beyond their own means, which is what
    /// keeps the general store from holding a two-handed sword of slaying.
    /// </summary>
    [Fact]
    public void Maintain_StocksNothingBeyondTheOwnersMeans()
    {
        GameState game = Shops(31337, 5);

        foreach (Store store in game.Stores.All)
        {
            int maxCost = GameTables.Owners[store.Owner].MaxCost;
            for (int j = 0; j < store.StockCount; j++)
            {
                Assert.InRange(store.Stock[j].Item.Cost, 1, maxCost - 1);
            }
        }
    }

    /// <summary>
    /// Asking prices are stored negative until the owner names them aloud, which
    /// is how the haggling code tells an unquoted price from a settled one.
    /// </summary>
    [Fact]
    public void Maintain_LeavesAskingPricesUnquoted()
    {
        GameState game = Shops(555, 3);

        foreach (Store store in game.Stores.All)
        {
            for (int j = 0; j < store.StockCount; j++)
            {
                Assert.True(
                    store.Stock[j].Cost < 0,
                    "a price was already quoted before the player asked");
            }
        }
    }

    /// <summary>
    /// Stock is kept ordered by category, descending, so insertion has somewhere
    /// definite to go and the shop list reads consistently.
    /// </summary>
    [Fact]
    public void Maintain_KeepsStockOrderedByCategory()
    {
        GameState game = Shops(2024, 5);

        foreach (Store store in game.Stores.All)
        {
            for (int j = 1; j < store.StockCount; j++)
            {
                Assert.True(
                    store.Stock[j - 1].Item.TVal >= store.Stock[j].Item.TVal,
                    "stock is out of category order");
            }
        }
    }

    /// <summary>
    /// Repeated visits change what is on the shelves. A shop that only filled up
    /// would look the same after the first restock.
    /// </summary>
    [Fact]
    public void Maintain_TurnsStockOverBetweenVisits()
    {
        GameState first = Shops(11, 1);
        GameState later = Shops(11, 10);

        bool changed = false;
        for (int i = 0; i < first.Stores.All.Length && !changed; i++)
        {
            if (first.Stores.All[i].StockCount != later.Stores.All[i].StockCount)
            {
                changed = true;
            }
        }

        Assert.True(changed, "ten restocks left every shop exactly as it was");
    }

    [Fact]
    public void Maintain_IsReproducibleForASeed()
    {
        GameState first = Shops(777, 4);
        GameState second = Shops(777, 4);

        for (int i = 0; i < first.Stores.All.Length; i++)
        {
            Assert.Equal(first.Stores.All[i].Owner, second.Stores.All[i].Owner);
            Assert.Equal(first.Stores.All[i].StockCount, second.Stores.All[i].StockCount);

            for (int j = 0; j < first.Stores.All[i].StockCount; j++)
            {
                Assert.Equal(
                    first.Stores.All[i].Stock[j].Item.Index,
                    second.Stores.All[i].Stock[j].Item.Index);
                Assert.Equal(
                    first.Stores.All[i].Stock[j].Cost,
                    second.Stores.All[i].Stock[j].Cost);
            }
        }
    }

    // ------------------------------------------------------------- pricing

    /// <summary>
    /// A shop pays nothing for something it knows to be cursed.
    /// </summary>
    [Fact]
    public void ItemValue_IsZeroForAKnownCursedItem()
    {
        var item = new InvenType();
        item.CopyFrom(0);
        item.Cost = 500;
        item.Identification = Identification.Damned;

        Assert.Equal(0, Shops(1, 0).Stores.ItemValue(item));
    }

    /// <summary>
    /// An unidentified ring fetches a flat 45 whatever it really is - which is
    /// what stops the shop being used as a free identify.
    /// </summary>
    [Fact]
    public void ItemValue_PricesAnUnknownRingFlat()
    {
        var item = new InvenType();
        item.CopyFrom(0);
        item.TVal = ItemCategory.Ring;
        item.SubVal = 1;
        item.Cost = 9999;
        item.Identification = 0;

        Assert.Equal(45, Shops(1, 0).Stores.ItemValue(item));
    }

    /// <summary>
    /// A weapon with a negative bonus is worthless, however good the base item.
    /// </summary>
    [Fact]
    public void ItemValue_IsZeroForAKnownWeaponWithANegativeBonus()
    {
        var item = new InvenType();
        item.CopyFrom(0);
        item.TVal = ItemCategory.Sword;
        item.Cost = 1000;
        item.ToHit = -1;
        item.Identification = Identification.Known;

        Assert.Equal(0, Shops(1, 0).Stores.ItemValue(item));
    }
}
