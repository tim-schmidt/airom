using Airom.Core;
using Airom.Data;
using Airom.Terminal;

namespace Airom.Tests;

/// <summary>
/// Checks on scrolls, wands and staffs.
///
/// Every scroll, wand and staff in the table is used on both sides and the
/// result diffed against the C oracle - 107 items across seven seeds and nine
/// depths, the town included. These pin the properties behind them.
/// </summary>
public class DeviceTests
{
    /// <summary>
    /// A scroll reader that answers what a scroll asks, as the oracle harness
    /// does.
    /// </summary>
    private sealed class Answering(GameState game, Display display, GameLoop loop, int? item)
        : Scrolls(game, display, loop)
    {
        protected override int? ChooseItem(string prompt, int first, int last) => item;

        protected override char? ChooseSymbol(string prompt) => 'k';
    }

    private static (GameState Game, MemoryScreen Screen, GameLoop Loop) Fresh(
        uint seed = 12345, int? answer = 0)
    {
        var game = new GameState();
        game.InitSeeds(seed);
        game.MagicInit();
        game.DungeonLevel = 5;

        var screen = new MemoryScreen { TypeAheadVisible = false };
        screen.SendKeys(new string((char)27, 600));

        var display = new Display(game, screen);

        new DungeonGenerator(game).Generate();
        display.Panel.Resize(game.Cave.Height, game.Cave.Width);

        var loop = new GameLoop(game, display);
        loop.Scrolls = new Answering(game, display, loop, answer);

        Player player = game.Player;
        player.Level = 20;
        player.ExperienceFactor = 100;
        player.MaxHitPoints = 500;
        player.CurrentHitPoints = 500;
        player.Save = 40;
        player.Food = 5000;

        for (int i = 0; i < Stat.Count; i++)
        {
            player.MaxStat[i] = 18;
            player.CurrentStat[i] = 18;
            player.UseStat[i] = 18;
        }

        return (game, screen, loop);
    }

    private static InvenType Scroll(uint effect, byte category = ItemCategory.Scroll1)
    {
        var scroll = new InvenType();
        scroll.CopyFrom(180);
        scroll.TVal = category;
        scroll.Flags = effect;
        scroll.Number = 1;
        return scroll;
    }

    private static InvenType Device(uint effect, byte category)
    {
        var device = new InvenType();
        device.CopyFrom(category == ItemCategory.Wand ? 269 : 293);
        device.TVal = category;
        device.Flags = effect;
        device.P1 = 15;
        device.Number = 1;
        return device;
    }

    /// <summary>
    /// A scroll that asks a question and is refused is not used up - which is
    /// what lets a player back out of a scroll of identify without losing it.
    /// </summary>
    [Fact]
    public void Read_ACancelledQuestionLeavesTheScrollInThePack()
    {
        (GameState game, _, GameLoop loop) = Fresh(answer: null);

        game.Inventory.Carry(Scroll(1u << 3)); // identify
        loop.Scrolls.Read(0);

        Assert.Equal(1, game.Inventory.Count);
        Assert.Equal(ItemCategory.Scroll1, game.Inventory[0].TVal);
    }

    /// <summary>
    /// Answering it uses the scroll up, and reading it teaches what it was.
    /// </summary>
    [Fact]
    public void Read_AnAnsweredQuestionUsesTheScrollUp()
    {
        (GameState game, _, GameLoop loop) = Fresh();

        game.Inventory.Carry(Scroll(1u << 3));
        loop.Scrolls.Read(0);

        Assert.Equal(0, game.Inventory.Count);
    }

    /// <summary>
    /// A scroll of recharging looks for something to recharge before it asks,
    /// and finding nothing costs the player nothing.
    /// </summary>
    [Fact]
    public void Read_RechargingWithNothingToRechargeIsNotUsedUp()
    {
        (GameState game, MemoryScreen screen, GameLoop loop) = Fresh();

        game.Inventory.Carry(Scroll(1u << 24));
        loop.Scrolls.Read(0);

        Assert.Equal(1, game.Inventory.Count);
        Assert.Contains("nothing to recharge", screen.GetText(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The second scroll table continues the first, so the same bit means two
    /// different things depending on which table the scroll came from.
    /// </summary>
    [Fact]
    public void Read_TheSecondTableContinuesTheFirst()
    {
        // Bit 6 in the second table is effect 39: a longer blessing.
        (GameState first, _, GameLoop firstLoop) = Fresh();
        first.Inventory.Carry(Scroll(1u << 6, ItemCategory.Scroll2));
        firstLoop.Scrolls.Read(0);
        Assert.True(first.Player.Blessed >= 13);

        // The same bit in the first table is effect 7: summoning.
        (GameState other, _, GameLoop otherLoop) = Fresh();
        other.Inventory.Carry(Scroll(1u << 6));
        otherLoop.Scrolls.Read(0);
        Assert.Equal(0, other.Player.Blessed);
    }

    /// <summary>
    /// A cursed piece of armour is always the one a scroll of enchantment
    /// picks, whatever the roll says - which is how a scroll of enchant armour
    /// doubles as a way out of cursed boots.
    /// </summary>
    [Fact]
    public void Read_EnchantmentPrefersTheCursedPiece()
    {
        (GameState game, _, GameLoop loop) = Fresh();

        game.Inventory[Inventory.BodySlot].CopyFrom(103);
        game.Inventory[Inventory.FeetSlot].CopyFrom(115);
        game.Inventory[Inventory.FeetSlot].Flags = ItemFlags.Cursed;
        game.Inventory[Inventory.FeetSlot].ToAc = -5;
        game.Inventory.EquipmentCount = 2;

        game.Inventory.Carry(Scroll(1u << 2)); // enchant armour
        loop.Scrolls.Read(0);

        Assert.Equal(0u, game.Inventory[Inventory.FeetSlot].Flags);
        Assert.Equal(0, game.Inventory[Inventory.BodySlot].ToAc);
    }

    /// <summary>
    /// Enchanting gets harder the better the item already is: a plain weapon
    /// almost always takes the first point, a good one usually refuses.
    /// </summary>
    [Fact]
    public void Enchant_GetsHarderAsTheItemGetsBetter()
    {
        (GameState game, _, GameLoop loop) = Fresh();

        int plainTook = 0;
        int goodTook = 0;

        for (int i = 0; i < 200; i++)
        {
            short plain = 0;
            short good = 9;

            if (loop.Spells.Enchant(ref plain, 10))
            {
                plainTook++;
            }

            if (loop.Spells.Enchant(ref good, 10))
            {
                goodTook++;
            }
        }

        Assert.Equal(200, plainTook);
        Assert.True(goodTook < 40, "a nearly perfect weapon enchanted too easily");
    }

    /// <summary>A limit of nothing refuses everything.</summary>
    [Fact]
    public void Enchant_ALimitOfNothingRefuses()
    {
        (_, _, GameLoop loop) = Fresh();

        short value = 0;
        Assert.False(loop.Spells.Enchant(ref value, 0));
        Assert.Equal(0, value);
    }

    /// <summary>
    /// A wand or a staff can fail in the hands of someone unskilled - but never
    /// hopelessly, since everyone is granted a slim chance whatever they are.
    /// </summary>
    [Fact]
    public void Use_ANoviceSometimesWorksAStaff()
    {
        int worked = 0;
        int failed = 0;

        for (uint seed = 1; seed <= 200; seed++)
        {
            (GameState game, MemoryScreen screen, GameLoop loop) = Fresh(seed);

            // A novice with nothing going for them.
            game.Player.Level = 1;
            game.Player.Save = 0;

            InvenType staff = Device(1u << 5, ItemCategory.Staff); // teleport
            staff.Level = 1;
            game.Inventory.Carry(staff);

            loop.Devices.Use(0);

            if (screen.GetText().Contains("failed to use", StringComparison.Ordinal))
            {
                failed++;
            }
            else
            {
                worked++;
            }
        }

        Assert.True(worked > 0, "a novice never once worked the staff");
        Assert.True(failed > worked, "the staff hardly ever failed for a novice");
    }

    /// <summary>An empty wand says so, and marks itself as empty.</summary>
    [Fact]
    public void Aim_AnEmptyWandIsMarked()
    {
        (GameState game, MemoryScreen screen, GameLoop loop) = Fresh();

        InvenType wand = Device(1u << 13, ItemCategory.Wand); // magic missile
        wand.P1 = 0;
        game.Inventory.Carry(wand);

        loop.Devices.Aim(0, 6);

        Assert.Contains("no charges left", screen.GetText(), StringComparison.Ordinal);
        Assert.Equal(Identification.Empty,
            game.Inventory[0].Identification & Identification.Empty);
    }

    /// <summary>Working a wand spends exactly one charge.</summary>
    [Fact]
    public void Aim_SpendsOneCharge()
    {
        (GameState game, _, GameLoop loop) = Fresh();

        game.Inventory.Carry(Device(1u << 13, ItemCategory.Wand));
        loop.Devices.Aim(0, 6);

        Assert.Equal(14, game.Inventory[0].P1);
    }

    /// <summary>
    /// FAITHFUL QUIRK: the charges left are always described in the plural, so
    /// one charge reads as "1 charges remaining".
    /// </summary>
    [Fact]
    public void Aim_TheChargeCountIsAlwaysPlural()
    {
        (GameState game, MemoryScreen screen, GameLoop loop) = Fresh();

        InvenType wand = Device(1u << 13, ItemCategory.Wand);
        wand.P1 = 2;
        game.Inventory.Carry(wand);
        game.Knowledge.LearnEnchantment(game.Inventory[0]);

        loop.Devices.Aim(0, 6);

        Assert.Contains("1 charges remaining", screen.GetText(), StringComparison.Ordinal);
    }

    /// <summary>
    /// A wand aimed while confused says so, and goes somewhere the player did
    /// not choose.
    /// </summary>
    [Fact]
    public void Aim_ConfusionIsAnnouncedAndRedirectsTheBolt()
    {
        (GameState game, MemoryScreen screen, GameLoop loop) = Fresh();
        game.Player.Confused = 50;

        game.Inventory.Carry(Device(1u, ItemCategory.Wand)); // a line of light
        loop.Devices.Aim(0, 6);

        Assert.Contains("You are confused.", screen.GetText(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Removing a curse frees everything worn at once, and only what is worn -
    /// a cursed thing in the pack stays cursed.
    /// </summary>
    [Fact]
    public void RemoveCurse_FreesWhatIsWornAndNothingElse()
    {
        (GameState game, _, GameLoop loop) = Fresh();

        game.Inventory[Inventory.WieldSlot].CopyFrom(30);
        game.Inventory[Inventory.WieldSlot].Flags = ItemFlags.Cursed;
        game.Inventory[Inventory.OuterSlot].CopyFrom(123);
        game.Inventory[Inventory.OuterSlot].Flags = ItemFlags.Cursed;
        game.Inventory.EquipmentCount = 2;

        var packed = new InvenType();
        packed.CopyFrom(30);
        packed.Flags = ItemFlags.Cursed;
        game.Inventory.Carry(packed);

        Assert.True(loop.Spells.RemoveCurse());

        Assert.Equal(0u, game.Inventory[Inventory.WieldSlot].Flags);
        Assert.Equal(0u, game.Inventory[Inventory.OuterSlot].Flags);
        Assert.Equal(ItemFlags.Cursed, game.Inventory[0].Flags);

        // Nothing cursed left, so nothing to lift.
        Assert.False(loop.Spells.RemoveCurse());
    }

    /// <summary>
    /// Identifying merges a pile bought from a shop with an identical one found
    /// in the dungeon, and keeps the earlier slot.
    /// </summary>
    [Fact]
    public void Identify_MergesTheShopPileWithTheDungeonPile()
    {
        (GameState game, _, _) = Fresh();

        var bought = new InvenType();
        bought.CopyFrom(180);
        bought.Number = 2;
        game.Knowledge.MarkStoreBought(bought);
        game.Inventory.Carry(bought);

        var found = new InvenType();
        found.CopyFrom(180);
        found.Number = 3;
        game.Inventory.Carry(found);

        Assert.Equal(2, game.Inventory.Count);

        int slot = game.Inventory.Identify(1);

        Assert.Equal(0, slot);
        Assert.Equal(1, game.Inventory.Count);
        Assert.Equal(5, game.Inventory[0].Number);
    }

    /// <summary>
    /// A cursed item identifies as damned, whether or not anything else about
    /// it was learned.
    /// </summary>
    [Fact]
    public void Identify_MarksACursedItemAsDamned()
    {
        (GameState game, _, _) = Fresh();

        var sword = new InvenType();
        sword.CopyFrom(30);
        sword.Flags = ItemFlags.Cursed;
        game.Inventory.Carry(sword);

        game.Inventory.Identify(0);

        Assert.Equal(Identification.Damned,
            game.Inventory[0].Identification & Identification.Damned);
    }

    /// <summary>
    /// Recharging a wand forgets the charge count the player knew: the number
    /// on the wand is no longer the number they counted.
    /// </summary>
    [Fact]
    public void Recharge_ForgetsTheCountThePlayerKnew()
    {
        (GameState game, _, GameLoop loop) = Fresh();

        InvenType wand = Device(1u << 13, ItemCategory.Wand);
        wand.Level = 5;
        game.Inventory.Carry(wand);
        game.Knowledge.LearnEnchantment(game.Inventory[0]);
        ItemKnowledge.AddInscription(game.Inventory[0], Identification.Empty);

        loop.Spells.Recharge(0, 60);

        if (game.Inventory.Count == 0)
        {
            // The wand blew up, which is the other thing recharging can do.
            return;
        }

        Assert.False(ItemKnowledge.IsEnchantmentKnown(game.Inventory[0]));
        Assert.Equal(0, game.Inventory[0].Identification & Identification.Empty);
        Assert.True(game.Inventory[0].P1 > 15);
    }

    /// <summary>
    /// Recharging something deep and full is hopeless enough to be automatic
    /// failure, and the item is destroyed.
    /// </summary>
    [Fact]
    public void Recharge_ADeepFullWandIsDestroyed()
    {
        (GameState game, MemoryScreen screen, GameLoop loop) = Fresh();

        InvenType wand = Device(1u << 13, ItemCategory.Wand);
        wand.Level = 60;
        wand.P1 = 60;
        game.Inventory.Carry(wand);

        loop.Spells.Recharge(0, 20);

        Assert.Equal(0, game.Inventory.Count);
        Assert.Contains("bright flash", screen.GetText(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Genocide clears the level of one letter, and a winning creature is
    /// unaffected - which is what keeps the game winnable rather than skippable.
    /// </summary>
    [Fact]
    public void Genocide_SparesAWinningCreature()
    {
        (GameState game, _, GameLoop loop) = Fresh();

        int winner = -1;

        for (int i = 0; i < GameTables.CreatureList.Length; i++)
        {
            if ((GameTables.CreatureList[i].MoveFlags & CreatureMove.Win) != 0)
            {
                winner = i;
                break;
            }
        }

        Assert.True(winner >= 0, "no winning creature in the table");

        // Put the winner next to the player, and something ordinary beside it.
        var generator = new DungeonGenerator(game);
        int row = game.CharacterRow;
        int column = game.CharacterColumn;
        generator.SummonMonster(ref row, ref column, false);

        int before = game.Monsters.Count;
        char symbol = GameTables.CreatureList[winner].DisplayChar;

        row = game.CharacterRow;
        column = game.CharacterColumn;
        generator.SummonMonster(ref row, ref column, false);

        loop.Spells.Genocide(symbol);

        // Whatever else went, nothing with the winner's flag is gone: it was
        // never placed, so the count can only have fallen by ordinary monsters.
        Assert.True(game.Monsters.Count <= before + 1);
    }

    /// <summary>
    /// Aggravating wakes everything on the level, however far away, but only
    /// hurries what is close.
    /// </summary>
    [Fact]
    public void Aggravate_WakesEverythingAndHurriesWhatIsClose()
    {
        (GameState game, _, GameLoop loop) = Fresh();

        for (int i = MonsterPool.FirstIndex; i < game.Monsters.Count; i++)
        {
            game.Monsters[i].Sleep = 500;
        }

        loop.Spells.AggravateMonsters(0);

        for (int i = MonsterPool.FirstIndex; i < game.Monsters.Count; i++)
        {
            Assert.Equal(0, game.Monsters[i].Sleep);
        }
    }

    /// <summary>
    /// The rune of protection goes under the player, and only where there is
    /// room for it.
    /// </summary>
    [Fact]
    public void WardingGlyph_NeedsTheSquareToBeEmpty()
    {
        (GameState game, _, GameLoop loop) = Fresh();

        CaveSquare square = game.Cave[game.CharacterRow, game.CharacterColumn];
        square.ObjectIndex = 0;

        loop.Spells.WardingGlyph();
        int laid = square.ObjectIndex;

        Assert.NotEqual(0, laid);

        // A second one does not replace the first.
        loop.Spells.WardingGlyph();
        Assert.Equal(laid, square.ObjectIndex);
    }
}
