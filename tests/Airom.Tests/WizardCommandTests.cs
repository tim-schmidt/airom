using Airom.Core;
using Airom.Data;
using Airom.Terminal;

namespace Airom.Tests;

/// <summary>
/// Checks on the symbol help and the debugging commands.
///
/// Both are diffed against the C oracle - every printable symbol at six depths
/// of memory, and twelve scripted runs of the editor and the item builder
/// across five seeds - so what these pin is the behaviour behind them.
/// </summary>
public class WizardCommandTests
{
    private static (GameState Game, MemoryScreen Screen, Display Display, GameLoop Loop)
        Fresh(string keys = "", bool wizard = false)
    {
        var game = new GameState();
        game.InitSeeds(12345);
        game.MagicInit();
        game.DungeonLevel = 5;
        game.Wizard = wizard;

        var screen = new MemoryScreen { TypeAheadVisible = false };
        screen.SendKeys(keys + new string((char)27, 300));

        var display = new Display(game, screen);

        new DungeonGenerator(game).Generate();
        display.Panel.Resize(game.Cave.Height, game.Cave.Width);

        var loop = new GameLoop(game, display);
        Player player = game.Player;

        player.Name = "Oracle the Bold";
        player.Level = 20;
        player.Gold = 500;
        player.Search = 20;
        player.Stealth = 3;
        player.Disarm = 30;
        player.Save = 40;
        player.BaseToHit = 50;
        player.BaseToHitBows = 45;
        player.Weight = 150;
        player.MaxHitPoints = 100;
        player.CurrentHitPoints = 60;
        player.MaxMana = 20;
        player.CurrentMana = 10;

        for (int i = 0; i < Stat.Count; i++)
        {
            player.MaxStat[i] = 16;
            player.CurrentStat[i] = 12;
            player.UseStat[i] = 12;
        }

        display.MessageWaitingFlag = false;
        return (game, screen, display, loop);
    }

    // -------------------------------------------------------- the symbol help

    /// <summary>An ordinary symbol is answered from the list.</summary>
    [Fact]
    public void IdentifySymbol_NamesWhatItStandsFor()
    {
        (_, MemoryScreen screen, _, GameLoop loop) = Fresh("!n");

        loop.SymbolHelp.IdentifySymbol();

        Assert.Contains("! - A potion.", screen.GetRow(0), StringComparison.Ordinal);
    }

    /// <summary>
    /// The player's own symbol is answered with their name, which is the one
    /// entry in the list that is not a fixed string.
    /// </summary>
    [Fact]
    public void IdentifySymbol_TheAtSignIsThePlayersName()
    {
        (_, MemoryScreen screen, _, GameLoop loop) = Fresh("@n");

        loop.SymbolHelp.IdentifySymbol();

        Assert.Contains("Oracle the Bold", screen.GetRow(0), StringComparison.Ordinal);
    }

    /// <summary>
    /// What "%" means depends on whether the player asked for mineral veins to
    /// be picked out - the only answer that turns on an option.
    /// </summary>
    [Fact]
    public void IdentifySymbol_ThePercentFollowsTheSeamOption()
    {
        (GameState plain, MemoryScreen plainScreen, _, GameLoop plainLoop) = Fresh("%n");
        plain.HighlightSeams = false;
        plainLoop.SymbolHelp.IdentifySymbol();

        (GameState seams, MemoryScreen seamScreen, _, GameLoop seamLoop) = Fresh("%n");
        seams.HighlightSeams = true;
        seamLoop.SymbolHelp.IdentifySymbol();

        Assert.Contains("% - Not used.", plainScreen.GetRow(0), StringComparison.Ordinal);
        Assert.Contains("magma or quartz vein", seamScreen.GetRow(0),
            StringComparison.Ordinal);
    }

    /// <summary>A symbol nothing uses says so rather than being left out.</summary>
    [Fact]
    public void IdentifySymbol_AnUnusedSymbolSaysSo()
    {
        (_, MemoryScreen screen, _, GameLoop loop) = Fresh("0n");

        loop.SymbolHelp.IdentifySymbol();

        Assert.Contains("Not Used.", screen.GetRow(0), StringComparison.Ordinal);
    }

    /// <summary>
    /// Every creature drawn with the symbol that the player knows anything
    /// about is offered up - and the offer is only made once, however many
    /// there are.
    /// </summary>
    [Fact]
    public void IdentifySymbol_OffersTheMemoryOfWhatIsDrawnWithIt()
    {
        int which = FirstCreatureWith('k');

        (GameState game, MemoryScreen screen, _, GameLoop loop) =
            Fresh("k" + new string('y', 40));

        game.Memories[which].Kills = 1;
        loop.SymbolHelp.IdentifySymbol();

        string page = screen.GetText();

        Assert.Contains(GameTables.CreatureList[which].Name, page,
            StringComparison.Ordinal);
    }

    /// <summary>Declining the offer leaves the symbol's own answer showing.</summary>
    [Fact]
    public void IdentifySymbol_DecliningTheOfferShowsNoMemory()
    {
        int which = FirstCreatureWith('k');

        (GameState game, MemoryScreen screen, _, GameLoop loop) = Fresh("kn");

        game.Memories[which].Kills = 1;
        loop.SymbolHelp.IdentifySymbol();

        Assert.Contains("k - Kobold.", screen.GetRow(0), StringComparison.Ordinal);
        Assert.DoesNotContain("battles to the death", screen.GetText(),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A creature nobody has met is not offered at all, so the question is
    /// never asked.
    /// </summary>
    [Fact]
    public void IdentifySymbol_NothingKnownMeansNoOffer()
    {
        (GameState game, MemoryScreen screen, _, GameLoop loop) = Fresh("k");

        for (int i = 0; i < game.Memories.Count; i++)
        {
            game.Memories[i].Clear();
        }

        loop.SymbolHelp.IdentifySymbol();

        Assert.DoesNotContain("You recall those details?", screen.GetText(),
            StringComparison.Ordinal);
    }

    private static int FirstCreatureWith(char symbol)
    {
        for (int i = GameTables.CreatureList.Length - 1; i >= 0; i--)
        {
            if (GameTables.CreatureList[i].DisplayChar == symbol)
            {
                return i;
            }
        }

        throw new InvalidOperationException("no creature drawn with " + symbol);
    }

    // ------------------------------------------------------ lighting the level

    /// <summary>
    /// Lighting the level is a toggle, and which way it goes depends on the
    /// square the player is standing on.
    /// </summary>
    [Fact]
    public void LightLevel_TogglesTheWholeLevel()
    {
        (GameState game, _, _, GameLoop loop) = Fresh();

        int Lit()
        {
            int lit = 0;

            for (int row = 0; row < game.Cave.Height; row++)
            {
                for (int column = 0; column < game.Cave.Width; column++)
                {
                    if (game.Cave[row, column].PermanentLight)
                    {
                        lit++;
                    }
                }
            }

            return lit;
        }

        game.Cave[game.CharacterRow, game.CharacterColumn].PermanentLight = false;

        loop.WizardCommands.LightLevel();
        int lightened = Lit();

        loop.WizardCommands.LightLevel();
        int darkened = Lit();

        Assert.True(lightened > darkened, "lighting the level lit nothing");
        Assert.Equal(0, darkened);
    }

    /// <summary>Putting it back into the dark forgets what was mapped as well.</summary>
    [Fact]
    public void LightLevel_DarkeningAlsoForgets()
    {
        (GameState game, _, _, GameLoop loop) = Fresh();

        game.Cave[game.CharacterRow, game.CharacterColumn].PermanentLight = true;
        game.Cave[game.CharacterRow, game.CharacterColumn].FieldMark = true;

        loop.WizardCommands.LightLevel();

        Assert.False(game.Cave[game.CharacterRow, game.CharacterColumn].FieldMark);
    }

    // ----------------------------------------------------- editing a character

    /// <summary>
    /// Backing out of any question abandons the rest, so nothing after it is
    /// touched.
    /// </summary>
    [Fact]
    public void ChangeCharacter_BackingOutAbandonsTheRest()
    {
        (GameState game, _, _, GameLoop loop) = Fresh("18\r");

        loop.WizardCommands.ChangeCharacter();

        Assert.Equal(18, game.Player.MaxStat[Stat.Strength]);

        // The second question was escaped, so nothing past it moved.
        Assert.Equal(16, game.Player.MaxStat[Stat.Intelligence]);
        Assert.Equal(100, game.Player.MaxHitPoints);
    }

    /// <summary>A value outside the bounds is ignored rather than clamped.</summary>
    [Fact]
    public void ChangeCharacter_RefusesAValueOutOfRange()
    {
        (GameState game, _, _, GameLoop loop) = Fresh("999\r2\r");

        loop.WizardCommands.ChangeCharacter();

        Assert.Equal(16, game.Player.MaxStat[Stat.Strength]);
        Assert.Equal(16, game.Player.MaxStat[Stat.Intelligence]);
    }

    /// <summary>
    /// The hit points and mana are set to the same number both ways, so a
    /// wizard is not left injured.
    /// </summary>
    [Fact]
    public void ChangeCharacter_SetsBothHalvesOfTheHitPoints()
    {
        (GameState game, _, _, GameLoop loop) =
            Fresh("18\r18\r18\r18\r18\r18\r250\r99\r");

        loop.WizardCommands.ChangeCharacter();

        Assert.Equal(250, game.Player.MaxHitPoints);
        Assert.Equal(250, game.Player.CurrentHitPoints);
        Assert.Equal(99, game.Player.MaxMana);
        Assert.Equal(99, game.Player.CurrentMana);
    }

    /// <summary>
    /// Nothing typed at all leaves a value alone, which is how one question in
    /// the middle is passed over.
    /// </summary>
    [Fact]
    public void ChangeCharacter_AnEmptyAnswerChangesNothing()
    {
        (GameState game, _, _, GameLoop loop) =
            Fresh("\r\r\r\r\r\r\r\r\r\r\r\r\r\r\r\r");

        loop.WizardCommands.ChangeCharacter();

        Assert.Equal(16, game.Player.MaxStat[Stat.Strength]);
        Assert.Equal(500, game.Player.Gold);
        Assert.Equal(20, game.Player.Search);
        Assert.Equal(150, game.Player.Weight);
    }

    // -------------------------------------------------------- building an item

    /// <summary>
    /// The item builder puts what was typed straight into the item, and drops
    /// it where the player stands.
    /// </summary>
    [Fact]
    public void CreateObject_BuildsWhatWasTypedAndDropsIt()
    {
        // The leading space answers the -more- that the warning puts up.
        (GameState game, _, _, GameLoop loop) =
            Fresh(" 23\r|\r1\r100\r1\r2\r6\r5\r3\r0\r0\r0\r0\r100\r5\ry");

        game.Cave[game.CharacterRow, game.CharacterColumn].ObjectIndex = 0;
        loop.WizardCommands.CreateObject();

        int index = game.Cave[game.CharacterRow, game.CharacterColumn].ObjectIndex;
        Assert.NotEqual(0, index);

        InvenType made = game.Objects[index];

        Assert.Equal(ItemCategory.Sword, made.TVal);
        Assert.Equal('|', made.DisplayChar);
        Assert.Equal(100, made.Weight);
        Assert.Equal(2, made.DamageDice);
        Assert.Equal(6, made.DamageSides);
        Assert.Equal(5, made.ToHit);
        Assert.Equal(3, made.ToDam);
        Assert.Equal(100, made.Cost);
        Assert.Equal(5, made.Level);
    }

    /// <summary>The flags are typed in hexadecimal, as the prompt says.</summary>
    [Fact]
    public void CreateObject_ReadsTheFlagsAsHex()
    {
        (GameState game, _, _, GameLoop loop) =
            Fresh(" 23\r|\r1\r100\r1\r2\r6\r5\r3\r0\r0\r0\r1f\r100\r5\ry");

        game.Cave[game.CharacterRow, game.CharacterColumn].ObjectIndex = 0;
        loop.WizardCommands.CreateObject();

        int index = game.Cave[game.CharacterRow, game.CharacterColumn].ObjectIndex;

        Assert.Equal(0x1fu, game.Objects[index].Flags);
    }

    /// <summary>Declining at the end throws the whole thing away.</summary>
    [Fact]
    public void CreateObject_DecliningAllocatesNothing()
    {
        (GameState game, _, Display display, GameLoop loop) =
            Fresh(" 23\r|\r1\r100\r1\r2\r6\r5\r3\r0\r0\r0\r0\r100\r5\rn");

        game.Cave[game.CharacterRow, game.CharacterColumn].ObjectIndex = 0;
        loop.WizardCommands.CreateObject();

        Assert.Equal(0, game.Cave[game.CharacterRow, game.CharacterColumn].ObjectIndex);

        Assert.Contains("Aborted.",
            string.Join(" | ", display.RecentMessages.Where(m => !string.IsNullOrEmpty(m))),
            StringComparison.Ordinal);
    }

    /// <summary>Backing out part-way builds nothing at all.</summary>
    [Fact]
    public void CreateObject_BackingOutPartWayBuildsNothing()
    {
        (GameState game, _, _, GameLoop loop) = Fresh(" 23\r|\r1\r100\r1\r2\r6\r5\r3\r0\r");

        game.Cave[game.CharacterRow, game.CharacterColumn].ObjectIndex = 0;
        loop.WizardCommands.CreateObject();

        Assert.Equal(0, game.Cave[game.CharacterRow, game.CharacterColumn].ObjectIndex);
    }
}
