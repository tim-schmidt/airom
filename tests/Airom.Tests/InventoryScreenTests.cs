using Airom.Core;
using Airom.Data;
using Airom.Terminal;

namespace Airom.Tests;

/// <summary>
/// Checks on the inventory screens and the prompt that asks which item.
///
/// The layout and the command mode are diffed against the C oracle - sixteen
/// scripts and ten prompts across seven seeds - so what these pin is the
/// behaviour behind them, and the cases the scripts cannot reach.
/// </summary>
public class InventoryScreenTests
{
    private static (GameState Game, MemoryScreen Screen, Display Display, GameLoop Loop)
        Fresh(string keys = "", bool floor = false)
    {
        var game = new GameState();
        game.InitSeeds(12345);
        game.MagicInit();
        game.DungeonLevel = 5;

        var screen = new MemoryScreen { TypeAheadVisible = false };
        screen.SendKeys(keys + new string((char)27, 200));

        var display = new Display(game, screen);

        new DungeonGenerator(game).Generate();
        game.Cave[game.CharacterRow, game.CharacterColumn].MonsterIndex = 1;
        display.Panel.Resize(game.Cave.Height, game.Cave.Width);

        var loop = new GameLoop(game, display);
        Player player = game.Player;

        player.Level = 20;
        player.ExperienceFactor = 100;
        player.Food = 5000;

        for (int i = 0; i < Stat.Count; i++)
        {
            player.MaxStat[i] = 18;
            player.CurrentStat[i] = 18;
            player.UseStat[i] = 18;
        }

        game.Inventory.Reset();
        game.Inventory[Inventory.WieldSlot].CopyFrom(30);    // a stiletto
        game.Inventory[Inventory.BodySlot].CopyFrom(103);    // soft leather armor
        game.Inventory[Inventory.LightSlot].CopyFrom(365);   // a wooden torch
        game.Inventory[Inventory.LightSlot].P1 = 5000;
        game.Inventory.EquipmentCount = 3;

        loop.Equipment.Recalculate();

        player.MaxHitPoints = 500;
        player.CurrentHitPoints = 500;

        game.PlayerLight = true;
        display.Panel.Invalidate();
        loop.Lighting.CheckView();

        // The square under the player is bare unless a test wants otherwise, so
        // dropping has somewhere to go.
        if (!floor)
        {
            game.Cave[game.CharacterRow, game.CharacterColumn].ObjectIndex = 0;
        }
        else
        {
            int index = game.Objects.Allocate();
            game.Objects[index].CopyFrom(30);
            game.Cave[game.CharacterRow, game.CharacterColumn].ObjectIndex = index;
        }

        display.MessageWaitingFlag = false;
        return (game, screen, display, loop);
    }

    private static void Carry(GameState game, int which, int number = 1)
    {
        var item = new InvenType();
        item.CopyFrom(which);
        item.Number = (byte)number;
        game.Inventory.Carry(item);
    }

    /// <summary>Everything said so far, newest last.</summary>
    private static string Said(Display display) =>
        string.Join(" | ", display.RecentMessages.Where(m => !string.IsNullOrEmpty(m)));

    /// <summary>The first object in the table of a kind, which is what the harness uses.</summary>
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

    // ------------------------------------------------------------ the lists

    /// <summary>
    /// The list sits as far right as its longest line allows, so one long
    /// description pushes the whole column left rather than wrapping.
    /// </summary>
    [Fact]
    public void ShowInventory_MovesLeftForTheLongestLine()
    {
        (GameState game, _, _, GameLoop loop) = Fresh();

        Carry(game, FirstOfKind(ItemCategory.Potion1));
        int narrow = loop.InventoryScreen.ShowInventory(
            0, game.Inventory.Count - 1, weight: false, 50, null);

        Carry(game, 103); // Soft Leather Armor, a longer name
        int wide = loop.InventoryScreen.ShowInventory(
            0, game.Inventory.Count - 1, weight: false, 50, null);

        Assert.True(wide <= narrow, "a longer line did not move the column left");
        Assert.InRange(narrow, 0, 79);
    }

    /// <summary>
    /// Showing the weights narrows the room left for the descriptions, so the
    /// same pack is drawn further left.
    /// </summary>
    [Fact]
    public void ShowInventory_MakesRoomForTheWeights()
    {
        (GameState game, MemoryScreen screen, _, GameLoop loop) = Fresh();

        Carry(game, 103);

        int plain = loop.InventoryScreen.ShowInventory(
            0, game.Inventory.Count - 1, weight: false, 50, null);
        int weighed = loop.InventoryScreen.ShowInventory(
            0, game.Inventory.Count - 1, weight: true, 50, null);

        Assert.True(weighed < plain, "the weights did not narrow the list");
        Assert.Contains(" lb", screen.GetRow(1), StringComparison.Ordinal);
    }

    /// <summary>
    /// A mask lists only what it marks, which is how a prompt limited to one
    /// kind of thing shows only that kind.
    /// </summary>
    [Fact]
    public void ShowInventory_ListsOnlyWhatTheMaskMarks()
    {
        (GameState game, MemoryScreen screen, _, GameLoop loop) = Fresh();

        Carry(game, FirstOfKind(ItemCategory.Potion1));
        Carry(game, FirstOfKind(ItemCategory.Scroll1));

        var mask = new bool[Inventory.Size];
        mask[1] = true;

        loop.InventoryScreen.ShowInventory(0, 1, weight: false, 50, mask);

        // One line, and it is the one the mask marked.
        Assert.Contains(
            game.Names.Describe(game.Inventory[1], withArticle: true),
            screen.GetRow(1), StringComparison.Ordinal);
        Assert.Equal(string.Empty, screen.GetRow(2).TrimEnd());
    }

    /// <summary>
    /// The equipment letters run over the slots in use, so an empty neck does
    /// not push a ring down the alphabet.
    /// </summary>
    [Fact]
    public void ShowEquipment_LettersTheSlotsInUse()
    {
        (_, MemoryScreen screen, _, GameLoop loop) = Fresh();

        loop.InventoryScreen.ShowEquipment(weight: false, 50);

        Assert.Contains("a) Wielding", screen.GetRow(1), StringComparison.Ordinal);
        Assert.Contains("b) On body", screen.GetRow(2), StringComparison.Ordinal);
        Assert.Contains("c) Light source", screen.GetRow(3), StringComparison.Ordinal);
    }

    /// <summary>
    /// A weapon too heavy to swing is only being lifted, which is the one label
    /// that depends on the player rather than the slot.
    /// </summary>
    [Fact]
    public void ShowEquipment_SaysWhenAWeaponIsOnlyBeingLifted()
    {
        (GameState game, MemoryScreen screen, _, GameLoop loop) = Fresh();

        game.Player.UseStat[Stat.Strength] = 0;
        loop.InventoryScreen.ShowEquipment(weight: false, 50);

        Assert.Contains("Just lifting", screen.GetRow(1), StringComparison.Ordinal);
    }

    // ----------------------------------------------------------- the prompt

    /// <summary>A letter picks the slot it stands for.</summary>
    [Fact]
    public void GetItem_PicksBySlotLetter()
    {
        (GameState game, _, _, GameLoop loop) = Fresh("b");

        Carry(game, FirstOfKind(ItemCategory.Potion1));
        Carry(game, FirstOfKind(ItemCategory.Food));

        Assert.Equal(1, loop.InventoryScreen.GetItem("Which one?", 0, 1));
    }

    /// <summary>Escape backs out, and gives the turn back.</summary>
    [Fact]
    public void GetItem_EscapeBacksOutAndCostsNothing()
    {
        (GameState game, _, _, GameLoop loop) = Fresh();

        Carry(game, FirstOfKind(ItemCategory.Potion1));
        loop.FreeTurn = false;

        Assert.Null(loop.InventoryScreen.GetItem("Which one?", 0, 0));
        Assert.True(loop.FreeTurn);
    }

    /// <summary>
    /// A capital letter asks first, so a scroll cannot be read by a slip of the
    /// finger. Declining backs the whole prompt out.
    /// </summary>
    [Fact]
    public void GetItem_ACapitalAsksBeforeItPicks()
    {
        (GameState game, _, _, GameLoop loop) = Fresh("An");

        Carry(game, FirstOfKind(ItemCategory.Potion1));

        Assert.Null(loop.InventoryScreen.GetItem("Which one?", 0, 0));

        (GameState confirmed, _, _, GameLoop yes) = Fresh("Ay");
        Carry(confirmed, FirstOfKind(ItemCategory.Potion1));

        Assert.Equal(0, yes.InventoryScreen.GetItem("Which one?", 0, 0));
    }

    /// <summary>
    /// A digit picks by inscription rather than by letter, which is what
    /// inscribing something with a number is for.
    /// </summary>
    [Fact]
    public void GetItem_PicksByInscription()
    {
        (GameState game, _, _, GameLoop loop) = Fresh("7");

        Carry(game, FirstOfKind(ItemCategory.Potion1));
        Carry(game, FirstOfKind(ItemCategory.Food));
        game.Inventory[1].Inscription = "7";

        Assert.Equal(1, loop.InventoryScreen.GetItem("Which one?", 0, 1));
    }

    /// <summary>
    /// A letter outside the range rings the bell and asks again, rather than
    /// picking something the player did not mean.
    /// </summary>
    [Fact]
    public void GetItem_RingsTheBellOutsideTheRange()
    {
        (GameState game, MemoryScreen screen, _, GameLoop loop) = Fresh("zb");

        Carry(game, FirstOfKind(ItemCategory.Potion1));
        Carry(game, FirstOfKind(ItemCategory.Food));

        Assert.Equal(1, loop.InventoryScreen.GetItem("Which one?", 0, 1));
        Assert.True(screen.BellCount > 0, "no bell for a letter out of range");
    }

    /// <summary>
    /// Where a message was given, it is said instead of the bell - which is how
    /// a prompt explains that it wants a different kind of thing.
    ///
    /// Putting the question again writes over the message line, which flushes
    /// the message through a -more- first, and that takes the next key with it.
    /// The answer typed straight after the refusal is therefore swallowed - the
    /// original does the same.
    /// </summary>
    [Fact]
    public void GetItem_SaysWhatItWantsWhenGivenAMessage()
    {
        (GameState game, _, Display display, GameLoop loop) = Fresh("z a");

        Carry(game, FirstOfKind(ItemCategory.Potion1));

        Assert.Equal(0, loop.InventoryScreen.GetItem(
            "Which one?", 0, 0, null, "You cannot use that."));

        Assert.Contains("You cannot use that.", Said(display));
    }

    /// <summary>An empty pack has nothing to ask about.</summary>
    [Fact]
    public void GetItem_SaysNothingIsCarried()
    {
        (_, MemoryScreen screen, _, GameLoop loop) = Fresh();

        Assert.Null(loop.InventoryScreen.GetItem("Which one?", 0, 0));
        Assert.Contains("not carrying anything", screen.GetRow(0), StringComparison.Ordinal);
    }

    // ------------------------------------------------------ the command mode

    /// <summary>
    /// Listing costs no turn, so a player can look at their pack as often as
    /// they like.
    /// </summary>
    [Fact]
    public void Command_ListingIsFree()
    {
        (GameState game, _, _, GameLoop loop) = Fresh();

        Carry(game, FirstOfKind(ItemCategory.Potion1));
        loop.InventoryScreen.Command('i');

        Assert.True(loop.FreeTurn);
        Assert.Null(loop.InventoryScreen.ContinuingCommand);
    }

    /// <summary>
    /// Wearing something takes a turn, and leaves the mode open so it can be
    /// picked up again next turn with the list still up.
    /// </summary>
    [Fact]
    public void Command_WearingTakesATurnAndKeepsTheModeOpen()
    {
        // "i" draws the pack first, so the mode has a screen to come back to.
        (GameState game, _, _, GameLoop loop) = Fresh("wa");

        Carry(game, FirstOfKind(ItemCategory.Shield));
        loop.InventoryScreen.Command('i');

        Assert.Equal(ItemCategory.Shield, game.Inventory[Inventory.ArmSlot].TVal);
        Assert.False(loop.FreeTurn);
        Assert.NotNull(loop.InventoryScreen.ContinuingCommand);
        Assert.Equal(0, game.Inventory.Count);
    }

    /// <summary>
    /// Wearing with nothing drawn ends the mode outright: there is no list to
    /// come back to, so nothing is remembered for next turn.
    /// </summary>
    [Fact]
    public void Command_WearingWithNothingDrawnLeavesTheMode()
    {
        (GameState game, _, _, GameLoop loop) = Fresh("a");

        Carry(game, FirstOfKind(ItemCategory.Shield));
        loop.InventoryScreen.Command('w');

        Assert.Equal(ItemCategory.Shield, game.Inventory[Inventory.ArmSlot].TVal);
        Assert.False(loop.FreeTurn);
        Assert.Null(loop.InventoryScreen.ContinuingCommand);
    }

    /// <summary>
    /// A second ring goes on the other hand without being asked about, since
    /// there is only one empty hand to put it on.
    /// </summary>
    [Fact]
    public void Command_ASecondRingFillsTheOtherHand()
    {
        (GameState game, _, _, GameLoop loop) = Fresh("a");

        int ring = FirstOfKind(ItemCategory.Ring);
        game.Inventory[Inventory.RightRingSlot].CopyFrom(ring);
        game.Inventory.EquipmentCount++;
        Carry(game, ring);

        loop.InventoryScreen.Command('w');

        Assert.Equal(ItemCategory.Ring, game.Inventory[Inventory.LeftRingSlot].TVal);
        Assert.Equal(0, game.Inventory.Count);
    }

    /// <summary>
    /// A cursed thing will not come off, however plainly the player asks.
    /// </summary>
    [Fact]
    public void Command_ACursedItemWillNotComeOff()
    {
        (GameState game, _, Display display, GameLoop loop) = Fresh("a");

        game.Inventory[Inventory.WieldSlot].Flags |= ItemFlags.Cursed;
        loop.InventoryScreen.Command('t');

        Assert.Contains("seems to be cursed", Said(display));
        Assert.Equal(ItemCategory.Sword, game.Inventory[Inventory.WieldSlot].TVal);
    }

    /// <summary>
    /// Swapping weapons is refused while the wielded one is cursed, and the
    /// spare stays where it is.
    /// </summary>
    [Fact]
    public void Command_ACursedWeaponCannotBeSwappedOut()
    {
        (GameState game, _, Display display, GameLoop loop) = Fresh();

        game.Inventory[Inventory.WieldSlot].Flags |= ItemFlags.Cursed;
        game.Inventory[Inventory.AuxiliarySlot].CopyFrom(34);

        loop.InventoryScreen.Command('x');

        Assert.Contains("appears to be cursed", Said(display));
        Assert.True(loop.FreeTurn);
    }

    /// <summary>
    /// Swapping puts the spare in hand and takes its bonuses with it - the
    /// spare grants nothing while it is spare.
    /// </summary>
    [Fact]
    public void Command_SwappingMovesBothWeapons()
    {
        (GameState game, _, _, GameLoop loop) = Fresh();

        game.Inventory[Inventory.AuxiliarySlot].CopyFrom(34);
        string spare = game.Names.Describe(
            game.Inventory[Inventory.AuxiliarySlot], withArticle: true);

        loop.InventoryScreen.Command('x');

        Assert.Equal(spare,
            game.Names.Describe(game.Inventory[Inventory.WieldSlot], withArticle: true));
        Assert.False(loop.FreeTurn);
    }

    /// <summary>Nothing can be dropped onto a square that is already occupied.</summary>
    [Fact]
    public void Command_RefusesToDropWhereSomethingLies()
    {
        (GameState game, _, Display display, GameLoop loop) = Fresh(floor: true);

        Carry(game, FirstOfKind(ItemCategory.Potion1));
        loop.InventoryScreen.Command('d');

        Assert.Contains("no room to drop", Said(display));
        Assert.Equal(1, game.Inventory.Count);
    }

    /// <summary>
    /// Dropping part of a pile leaves the rest carried; the question is put
    /// with its own key, so escape drops nothing at all.
    /// </summary>
    [Fact]
    public void Command_DroppingAPileAsksWhetherAllOfItGoes()
    {
        (GameState game, _, _, GameLoop loop) = Fresh("an");

        Carry(game, FirstOfKind(ItemCategory.Potion1), number: 3);
        loop.InventoryScreen.Command('d');

        Assert.Equal(1, game.Inventory.Count);
        Assert.Equal(2, game.Inventory[0].Number);

        (GameState all, _, _, GameLoop whole) = Fresh("ay");
        Carry(all, FirstOfKind(ItemCategory.Potion1), number: 3);
        whole.InventoryScreen.Command('d');

        Assert.Equal(0, all.Inventory.Count);
    }

    /// <summary>
    /// Putting the last thing down leaves nothing weighing anything, which the
    /// original makes sure of rather than trusting the arithmetic.
    /// </summary>
    [Fact]
    public void Command_TheLastThingDroppedLeavesNoWeight()
    {
        (GameState game, _, _, GameLoop loop) = Fresh("ay");

        game.Inventory.Reset();
        Carry(game, FirstOfKind(ItemCategory.Potion1));

        loop.InventoryScreen.Command('d');

        Assert.Equal(0, game.Inventory.Count);
        Assert.Equal(0, game.Inventory.EquipmentCount);
        Assert.Equal(0, game.Inventory.Weight);
    }

    /// <summary>
    /// Something that cannot be worn at all says so, rather than going into
    /// some slot or other.
    /// </summary>
    [Fact]
    public void Command_RefusesToWearWhatCannotBeWorn()
    {
        (GameState game, _, Display display, GameLoop loop) = Fresh("a");

        // Nothing wearable is carried, so the command never gets that far.
        Carry(game, FirstOfKind(ItemCategory.Potion1));
        loop.InventoryScreen.Command('w');

        Assert.Contains("nothing to wear or wield", Said(display));
    }

    /// <summary>
    /// Putting on something cursed is noticed at once, and the thing is marked
    /// so the player is not caught twice.
    /// </summary>
    [Fact]
    public void Command_ACursedThingIsFeltAsItGoesOn()
    {
        (GameState game, _, Display display, GameLoop loop) = Fresh("a");

        Carry(game, FirstOfKind(ItemCategory.Shield));
        game.Inventory[0].Flags |= ItemFlags.Cursed;

        loop.InventoryScreen.Command('w');

        Assert.Contains("deathly cold", Said(display));
        Assert.Equal(-1, game.Inventory[Inventory.ArmSlot].Cost);
    }

    /// <summary>
    /// Taking something off with a full pack is refused, since there would be
    /// nowhere to put it.
    /// </summary>
    [Fact]
    public void Command_TakingOffNeedsRoomInThePack()
    {
        (GameState game, _, Display display, GameLoop loop) = Fresh();

        // A pack with a slot for every letter it could offer.
        for (int which = 0; which < GameTables.ObjectList.Length
                            && game.Inventory.Count < Inventory.WieldSlot; which++)
        {
            if (GameTables.ObjectList[which].TVal is > ItemCategory.Nothing
                and <= ItemCategory.MaxObject)
            {
                Carry(game, which);
            }
        }

        Assert.Equal(Inventory.WieldSlot, game.Inventory.Count);

        loop.InventoryScreen.Command('t');

        Assert.Contains("have to drop something first", Said(display));
    }
}
