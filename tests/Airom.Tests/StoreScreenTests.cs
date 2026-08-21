using Airom.Core;
using Airom.Data;
using System.Globalization;
using Airom.Terminal;

namespace Airom.Tests;

/// <summary>
/// Checks on being in a shop: the screen, the commands and the haggling.
///
/// A visit is diffed against the C oracle - seventeen scripts through all six
/// shops across seven seeds - so what these pin is the behaviour behind them,
/// and the cases the scripts cannot reach.
/// </summary>
public class StoreScreenTests
{
    private static (GameState Game, MemoryScreen Screen, Display Display, GameLoop Loop)
        Fresh(string keys = "", uint seed = 12345, int charisma = 18)
    {
        var game = new GameState();
        game.InitSeeds(seed);
        game.MagicInit();
        game.DungeonLevel = 0;

        // The doors are locked until the clock has started.
        game.Turn = 100;

        game.Stores.Initialise();
        game.Stores.Maintain();
        game.Stores.Maintain();

        var screen = new MemoryScreen { TypeAheadVisible = false };
        screen.SendKeys(keys + new string((char)27, 500));

        var display = new Display(game, screen);

        new DungeonGenerator(game).Generate();
        display.Panel.Resize(game.Cave.Height, game.Cave.Width);

        var loop = new GameLoop(game, display);
        Player player = game.Player;

        player.Level = 20;
        player.ExperienceFactor = 100;
        player.Gold = 5000;
        player.Food = 5000;

        for (int i = 0; i < Stat.Count; i++)
        {
            player.MaxStat[i] = 18;
            player.CurrentStat[i] = 18;
            player.UseStat[i] = 18;
        }

        player.UseStat[Stat.Charisma] = charisma;

        game.Inventory.Reset();
        game.Inventory[Inventory.LightSlot].CopyFrom(365);
        game.Inventory[Inventory.LightSlot].P1 = 5000;
        game.Inventory.EquipmentCount = 1;

        player.MaxHitPoints = 500;
        player.CurrentHitPoints = 500;

        display.MessageWaitingFlag = false;
        loop.FreeTurn = false;

        return (game, screen, display, loop);
    }

    /// <summary>
    /// The screen as it stood the first time the shop stopped to ask for a key.
    ///
    /// A visit ends by drawing the map back over itself, so by the time
    /// <see cref="StoreScreen.Enter"/> returns the shop is gone. This catches it
    /// while it is still there.
    /// </summary>
    private static string ScreenInside(MemoryScreen screen, GameLoop loop, int storeIndex)
    {
        string caught = string.Empty;

        screen.BeforeReadKey = () =>
        {
            if (caught.Length == 0)
            {
                caught = screen.GetText();
            }
        };

        loop.StoreScreen.Enter(storeIndex);
        screen.BeforeReadKey = null;

        return caught;
    }

    /// <summary>Everything said so far, newest last.</summary>
    private static string Said(Display display) =>
        string.Join(" | ", display.RecentMessages.Where(m => !string.IsNullOrEmpty(m)));

    private const int GeneralStore = 0;

    // ------------------------------------------------------------- the doors

    /// <summary>
    /// A shop the player has been thrown out of stays shut, and saying so is
    /// the whole of the visit.
    /// </summary>
    [Fact]
    public void Enter_ARecentlyClosedShopIsLocked()
    {
        (GameState game, MemoryScreen screen, Display display, GameLoop loop) = Fresh();

        game.Stores.All[GeneralStore].OpenAgainAt = game.Turn + 1000;
        loop.StoreScreen.Enter(GeneralStore);

        Assert.Contains("doors are locked", Said(display));

        // Nothing was drawn: the shop was never entered.
        Assert.DoesNotContain("Asking Price", screen.GetText(), StringComparison.Ordinal);
    }

    /// <summary>The shop draws its owner, its headings and the player's gold.</summary>
    [Fact]
    public void Enter_DrawsTheShopAndItsStock()
    {
        (GameState game, MemoryScreen screen, _, GameLoop loop) = Fresh();

        string text = ScreenInside(screen, loop, GeneralStore);
        Store store = game.Stores.All[GeneralStore];

        Assert.Contains(GameTables.Owners[store.Owner].Name, text, StringComparison.Ordinal);
        Assert.Contains("Asking Price", text, StringComparison.Ordinal);
        Assert.Contains("Gold Remaining : 5000", text, StringComparison.Ordinal);
        Assert.Contains("p) Purchase an item.", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Browsing turns to the second page when there is one, and says so when
    /// there is not.
    /// </summary>
    [Fact]
    public void Enter_BrowsingSaysWhenThereIsOnlyOnePage()
    {
        (GameState game, _, Display display, GameLoop loop) = Fresh("b");

        // A shop with a single page of stock.
        Store store = game.Stores.All[GeneralStore];
        store.StockCount = 5;

        loop.StoreScreen.Enter(GeneralStore);

        Assert.Contains("Entire inventory is shown.", Said(display));
    }

    /// <summary>An out-of-stock shop has nothing to sell.</summary>
    [Fact]
    public void Purchase_AnEmptyShopSaysSo()
    {
        (GameState game, _, Display display, GameLoop loop) = Fresh("p");

        game.Stores.All[GeneralStore].StockCount = 0;
        loop.StoreScreen.Enter(GeneralStore);

        Assert.Contains("currently out of stock", Said(display));
    }

    // ------------------------------------------------------------ the prices

    /// <summary>
    /// Charisma is the whole of what a shopkeeper thinks of the player, and it
    /// moves the price both ways around a hundred.
    /// </summary>
    [Fact]
    public void CharismaAdjust_DiscountsTheCharmingAndPenalisesTheRest()
    {
        var player = new Player();

        player.UseStat[Stat.Charisma] = 3;
        Assert.Equal(130, Stats.CharismaAdjust(player));

        player.UseStat[Stat.Charisma] = 18;
        Assert.Equal(100, Stats.CharismaAdjust(player));

        player.UseStat[Stat.Charisma] = 118;
        Assert.Equal(90, Stats.CharismaAdjust(player));
    }

    /// <summary>
    /// A charming player is quoted less for the same item than a repellent one.
    /// </summary>
    [Fact]
    public void Enter_TheAskingPriceFollowsTheCharisma()
    {
        string Prices(int charisma)
        {
            (_, MemoryScreen screen, _, GameLoop loop) = Fresh(charisma: charisma);

            // The stock, caught while the shop is still on screen. All of it
            // rather than one line: an item can be cheap enough that the two
            // adjustments round to the same price.
            return string.Join(
                '\n', ScreenInside(screen, loop, GeneralStore).Split('\n')[5..17]);
        }

        Assert.NotEqual(Prices(3), Prices(18));
    }

    /// <summary>
    /// What the player has learned decides what a shop will pay: a potion whose
    /// kind is a mystery fetches the flat price of a mystery, and a known one
    /// fetches what it is worth.
    ///
    /// This is the bug the shop oracle found - the pricing consulted only the
    /// store-bought flag, so an item the player had identified was still
    /// treated as unknown.
    /// </summary>
    [Fact]
    public void ItemValue_FollowsWhatThePlayerKnows()
    {
        (GameState game, _, _, _) = Fresh();

        var potion = new InvenType();
        potion.CopyFrom(FirstOfKind(ItemCategory.Potion1));
        potion.Cost = 400;

        game.Knowledge.Reset();
        Assert.Equal(20, game.Stores.ItemValue(potion));

        game.Knowledge.LearnKind(potion);
        Assert.Equal(400, game.Stores.ItemValue(potion));
    }

    // --------------------------------------------------------- the bargaining

    /// <summary>
    /// A shopkeeper who has been dealt with fairly often enough names their
    /// floor at once rather than haggling all over again - but a reputation
    /// earned over trinkets does not carry to something expensive.
    /// </summary>
    [Fact]
    public void NoNeedToBargain_NeedsARecordWorthTheStakes()
    {
        (GameState game, _, _, _) = Fresh();

        Store store = game.Stores.All[GeneralStore];

        store.GoodBuys = 0;
        store.BadBuys = 0;
        Assert.False(game.Stores.NoNeedToBargain(GeneralStore, 10));

        store.GoodBuys = 12;
        Assert.True(game.Stores.NoNeedToBargain(GeneralStore, 10));

        // The same record against a far larger price is not enough.
        Assert.False(game.Stores.NoNeedToBargain(GeneralStore, 1000000));

        // Bad deals count treble against good ones.
        store.BadBuys = 3;
        Assert.False(game.Stores.NoNeedToBargain(GeneralStore, 10));
    }

    /// <summary>A shopkeeper who never haggles is always agreeable.</summary>
    [Fact]
    public void NoNeedToBargain_IsAlwaysTrueAtTheCeiling()
    {
        (GameState game, _, _, _) = Fresh();

        game.Stores.All[GeneralStore].GoodBuys = GameLoop.MaxShort;

        Assert.True(game.Stores.NoNeedToBargain(GeneralStore, int.MaxValue));
    }

    /// <summary>
    /// A deal struck at the shopkeeper's own price is remembered kindly, and
    /// one struck anywhere else is not - but only above nine gold, so an
    /// opinion cannot be built out of trinkets.
    /// </summary>
    [Fact]
    public void UpdateBargain_CountsOnlyDealsWorthHaving()
    {
        (GameState game, _, _, _) = Fresh();

        Store store = game.Stores.All[GeneralStore];

        game.Stores.UpdateBargain(GeneralStore, 5, 5);
        Assert.Equal(0, store.GoodBuys);
        Assert.Equal(0, store.BadBuys);

        game.Stores.UpdateBargain(GeneralStore, 100, 100);
        Assert.Equal(1, store.GoodBuys);

        game.Stores.UpdateBargain(GeneralStore, 150, 100);
        Assert.Equal(1, store.BadBuys);
    }

    /// <summary>
    /// Enough insults and the shopkeeper shuts the door for a few thousand
    /// turns.
    /// </summary>
    [Fact]
    public void Enter_EnoughInsultsCloseTheShop()
    {
        // Offering one gold over and over is an insult each time.
        // The space is for the -more- that announcing the sale puts up: writing
        // the haggling prompt over the message line flushes it, and the flush
        // takes a key with it.
        (GameState game, _, Display display, GameLoop loop) =
            Fresh(string.Concat(Enumerable.Repeat("pa1\r", 20)));

        Store store = game.Stores.All[GeneralStore];
        store.InsultsThisVisit = GameTables.Owners[store.Owner].InsultMax;

        loop.StoreScreen.Enter(GeneralStore);

        Assert.True(store.OpenAgainAt > game.Turn,
            "the shop did not close after the insults");

        Assert.Equal(1, store.BadBuys);

        // Which parting shot is used is a roll of its own, so any of them will
        // do: what matters is that the shopkeeper said one.
        string said = Said(display);

        Assert.Contains("THAT DOES IT!", said, StringComparison.Ordinal);
        Assert.Contains(
            GameTables.ShopkeeperThrowsOut,
            line => said.Contains(line, StringComparison.Ordinal));
    }

    // --------------------------------------------------------------- trading

    /// <summary>
    /// Offering the asking price buys the thing outright: the gold goes, the
    /// item arrives, and the shop is one shorter.
    /// </summary>
    [Fact]
    public void Purchase_OfferingTheAskingPriceBuysIt()
    {
        (GameState setup, _, _, _) = Fresh();
        Store shelf = setup.Stores.All[GeneralStore];

        // What the shopkeeper would open at for the first line.
        var wanted = new InvenType();
        Inventory.TakeOne(wanted, shelf.Stock[0].Item);
        setup.Stores.SellPrice(GeneralStore, wanted, out int maxSell, out _);
        int asking = Math.Max(1, maxSell * Stats.CharismaAdjust(setup.Player) / 100);

        (GameState game, _, _, GameLoop loop) = Fresh("pa" + asking + "\r");

        int stockWas = game.Stores.All[GeneralStore].StockCount;
        loop.StoreScreen.Enter(GeneralStore);

        Assert.Equal(1, game.Inventory.Count);
        Assert.Equal(5000 - asking, game.Player.Gold);
        Assert.True(game.Stores.All[GeneralStore].StockCount <= stockWas);
    }

    /// <summary>
    /// A price agreed but not affordable is an insult, and the shopkeeper says
    /// so in as many words.
    /// </summary>
    [Fact]
    public void Purchase_AgreeingToAPriceYouCannotPayIsAnInsult()
    {
        (GameState setup, _, _, _) = Fresh();
        Store shelf = setup.Stores.All[GeneralStore];

        var wanted = new InvenType();
        Inventory.TakeOne(wanted, shelf.Stock[0].Item);
        setup.Stores.SellPrice(GeneralStore, wanted, out int maxSell, out _);
        int asking = Math.Max(1, maxSell * Stats.CharismaAdjust(setup.Player) / 100);

        (GameState game, _, Display display, GameLoop loop) = Fresh("pa" + asking + "\r");

        game.Player.Gold = 0;
        loop.StoreScreen.Enter(GeneralStore);

        Assert.Contains("You have not the gold!", Said(display), StringComparison.Ordinal);
        Assert.Equal(0, game.Inventory.Count);
        Assert.Equal(1, game.Stores.All[GeneralStore].InsultsThisVisit);
    }

    /// <summary>A shop will not look at what it does not deal in.</summary>
    [Fact]
    public void Sell_AShopWithNothingItWantsSaysSo()
    {
        (GameState game, _, Display display, GameLoop loop) = Fresh("s");

        // A wand, which the general store does not deal in.
        var wand = new InvenType();
        wand.CopyFrom(FirstOfKind(ItemCategory.Wand));
        game.Inventory.Carry(wand);

        loop.StoreScreen.Enter(GeneralStore);

        Assert.Contains("nothing to sell to this store", Said(display));
    }

    /// <summary>
    /// Something the shopkeeper values at nothing is refused outright, and
    /// being asked to buy it is an insult of its own.
    /// </summary>
    [Fact]
    public void Sell_SomethingWorthlessIsRefused()
    {
        (GameState game, _, Display display, GameLoop loop) = Fresh("sa1\r");

        var food = new InvenType();
        food.CopyFrom(FirstOfKind(ItemCategory.Food));

        // Known to be cursed, which is what makes a shop value it at nothing.
        food.Identification = Identification.Damned;
        game.Inventory.Carry(food);

        loop.StoreScreen.Enter(GeneralStore);

        Assert.Contains("I will not buy that!", Said(display), StringComparison.Ordinal);
        Assert.Equal(1, game.Stores.All[GeneralStore].InsultsThisVisit);
    }

    /// <summary>
    /// Asking exactly what the shopkeeper offers sells the thing: the gold
    /// arrives and the pack is one lighter.
    ///
    /// What they offer is read off the screen rather than worked out again
    /// here - the shop prints it before it asks, and taking it from there tests
    /// the display and the arithmetic at once.
    /// </summary>
    [Fact]
    public void Sell_AskingWhatIsOfferedSellsIt()
    {
        // A first visit that only looks at the offer and then walks away.
        (GameState looking, MemoryScreen watching, _, GameLoop looker) = Fresh("sa");

        var sample = new InvenType();
        sample.CopyFrom(FirstOfKind(ItemCategory.Food));
        looking.Inventory.Carry(sample);

        string offerLine = string.Empty;

        watching.BeforeReadKey = () =>
        {
            string line = watching.GetRow(1).TrimEnd();

            if (line.StartsWith("Offer", StringComparison.Ordinal))
            {
                offerLine = line;
            }
        };

        looker.StoreScreen.Enter(GeneralStore);
        watching.BeforeReadKey = null;

        Assert.StartsWith("Offer", offerLine, StringComparison.Ordinal);

        int offered = int.Parse(
            offerLine.Split(':')[1].Trim(), CultureInfo.InvariantCulture);

        // And a second that takes it.
        (GameState game, _, Display display, GameLoop loop) =
            Fresh("sa " + offered.ToString(CultureInfo.InvariantCulture) + "\r");

        var food = new InvenType();
        food.CopyFrom(FirstOfKind(ItemCategory.Food));
        game.Inventory.Carry(food);

        loop.StoreScreen.Enter(GeneralStore);

        Assert.Contains("You've sold", Said(display), StringComparison.Ordinal);
        Assert.Equal(0, game.Inventory.Count);
        Assert.Equal(5000 + offered, game.Player.Gold);
    }

    private static int FirstOfKind(byte category)
    {
        for (int i = 0; i < GameTables.ObjectList.Length; i++)
        {
            if (GameTables.ObjectList[i].TVal == category)
            {
                return i;
            }
        }

        throw new InvalidOperationException("no object of category " + category);
    }
}
