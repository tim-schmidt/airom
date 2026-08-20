using Airom.Core;
using Airom.Data;
using Airom.Terminal;

namespace Airom.Tests;

/// <summary>
/// Checks on the player's own magic.
///
/// Every spell and every prayer is cast against the C oracle - thirty-one of
/// each, twice over, across seven seeds and nine depths - so what these pin is
/// the bookkeeping around them: which spells a character knows, what they cost,
/// and what the prompts do with an answer.
/// </summary>
public class MagicTests
{
    /// <summary>A caster that picks the first book without asking.</summary>
    private sealed class Booked(GameState game, Display display, GameLoop loop, int? book)
        : Magic(game, display, loop)
    {
        protected internal override int? ChooseBook(string prompt, int first, int last) =>
            book;
    }

    /// <summary>Answers the questions the effects in spells.c ask.</summary>
    private sealed class Answering(GameState game, Display display, GameLoop loop)
        : Spells(game, display, loop)
    {
        protected internal override int? ChooseItem(string prompt, int first, int last) => 0;

        protected internal override char? ChooseSymbol(string prompt) => 'k';
    }

    private const int MageClass = 1;
    private const int PriestClass = 2;

    /// <summary>The first mage book, which holds spells 0 to 6.</summary>
    private const int BeginnersMagick = 318;

    /// <summary>The first priest book, which holds prayers 0 to 7.</summary>
    private const int BeginnersHandbook = 322;

    private static (GameState Game, Display Display, GameLoop Loop) Fresh(
        int characterClass = MageClass, string keys = "", int? book = 0, int level = 20)
    {
        var game = new GameState();
        game.InitSeeds(12345);
        game.MagicInit();
        game.DungeonLevel = 5;

        var screen = new MemoryScreen { TypeAheadVisible = false };
        screen.SendKeys(keys + new string((char)27, 200));

        var display = new Display(game, screen);

        new DungeonGenerator(game).Generate();
        display.Panel.Resize(game.Cave.Height, game.Cave.Width);

        var loop = new GameLoop(game, display);
        loop.Spells = new Answering(game, display, loop);
        loop.Magic = new Booked(game, display, loop, book);

        Player player = game.Player;
        player.Class = characterClass;
        player.Level = level;
        player.ExperienceFactor = 100;
        player.MaxHitPoints = 500;
        player.CurrentHitPoints = 500;
        player.MaxMana = 100;
        player.CurrentMana = 100;
        player.Food = 5000;

        for (int i = 0; i < Stat.Count; i++)
        {
            player.MaxStat[i] = 18;
            player.CurrentStat[i] = 18;
            player.UseStat[i] = 18;
        }

        // A light, since a spell book cannot be read in the dark.
        game.Inventory[Inventory.LightSlot].CopyFrom(365);
        game.Inventory[Inventory.LightSlot].P1 = 5000;
        game.Inventory.EquipmentCount = 1;
        loop.Equipment.Recalculate();
        game.PlayerLight = true;
        display.Panel.Invalidate();
        loop.Lighting.CheckView();

        // Recalculating works the hit points out from the class, so the
        // survivable totals go back afterwards.
        player.MaxHitPoints = 500;
        player.CurrentHitPoints = 500;
        player.MaxMana = 100;
        player.CurrentMana = 100;

        return (game, display, loop);
    }

    /// <summary>Everything said so far, newest last.</summary>
    private static string Said(Display display) =>
        string.Join(" | ", display.RecentMessages.Where(m => !string.IsNullOrEmpty(m)));

    private static void CarryBook(GameState game, int which)
    {
        var book = new InvenType();
        book.CopyFrom(which);
        game.Inventory.Carry(book);
    }

    // ------------------------------------------------------------- casting

    /// <summary>
    /// Nothing can be cast without a book to read it out of, and being told so
    /// costs no turn.
    /// </summary>
    [Fact]
    public void Cast_WithoutABookIsFreeAndSaysSo()
    {
        (GameState game, Display display, GameLoop loop) = Fresh();

        loop.Magic.Cast();

        Assert.True(loop.FreeTurn);
        Assert.Contains("not carrying any spell-books", Said(display));
        Assert.Equal(100, game.Player.CurrentMana);
    }

    /// <summary>A priest is told to try harder rather than that they cannot.</summary>
    [Fact]
    public void Pray_AMageIsToldToPrayHarder()
    {
        (_, Display display, GameLoop loop) = Fresh();

        loop.Magic.Pray();

        Assert.Contains("Pray hard enough", Said(display));
        Assert.True(loop.FreeTurn);
    }

    /// <summary>
    /// A book the character has learned nothing from cannot be cast from, and
    /// saying so costs no turn.
    /// </summary>
    [Fact]
    public void Cast_AnUnlearnedBookIsFree()
    {
        (GameState game, Display display, GameLoop loop) = Fresh();

        CarryBook(game, BeginnersMagick);
        loop.Magic.Cast();

        Assert.True(loop.FreeTurn);
        Assert.Contains("don't know any spells", Said(display));
    }

    /// <summary>
    /// Backing out of the book prompt is free too, and nothing else is asked.
    /// </summary>
    [Fact]
    public void Cast_ARefusedBookPromptCostsNothing()
    {
        (GameState game, _, GameLoop loop) = Fresh(book: null);

        CarryBook(game, BeginnersMagick);
        game.Player.SpellLearned = 0x7FFFFFFFu;
        loop.Magic.Cast();

        Assert.True(loop.FreeTurn);
        Assert.Equal(100, game.Player.CurrentMana);
    }

    /// <summary>
    /// Casting the first spell of the first book: magic missile costs one mana,
    /// and getting it to work for the first time is worth four times what the
    /// table holds.
    /// </summary>
    [Fact]
    public void Cast_AFirstSuccessIsWorthFourTimesTheTable()
    {
        // "a" picks the spell, "6" points the bolt east.
        (GameState game, _, GameLoop loop) = Fresh(keys: "a6");

        CarryBook(game, BeginnersMagick);
        game.Player.SpellLearned = 0x7FFFFFFFu;

        loop.Magic.Cast();

        Assert.False(loop.FreeTurn);
        Assert.Equal(99, game.Player.CurrentMana);
        Assert.Equal(1u, game.Player.SpellWorked & 1u);
        Assert.Equal(GameTables.MagicSpell[MageClass - 1][0].Experience * 4,
                     game.Player.Experience);
    }

    /// <summary>
    /// The second casting earns nothing, since the experience is for learning
    /// the spell rather than for using it.
    /// </summary>
    [Fact]
    public void Cast_ASecondSuccessEarnsNothing()
    {
        (GameState game, _, GameLoop loop) = Fresh(keys: "a6a6");

        CarryBook(game, BeginnersMagick);
        game.Player.SpellLearned = 0x7FFFFFFFu;

        loop.Magic.Cast();
        int earned = game.Player.Experience;
        loop.Magic.Cast();

        Assert.Equal(earned, game.Player.Experience);
    }

    /// <summary>
    /// A spell that asks which way it goes and is not answered costs neither a
    /// turn nor any mana - the escape backs out of the whole thing.
    /// </summary>
    [Fact]
    public void Cast_ACancelledDirectionCostsNothing()
    {
        // "a" picks the spell; the padding escapes back out of the direction.
        (GameState game, _, GameLoop loop) = Fresh(keys: "a");

        CarryBook(game, BeginnersMagick);
        game.Player.SpellLearned = 0x7FFFFFFFu;

        loop.Magic.Cast();

        Assert.True(loop.FreeTurn);
        Assert.Equal(100, game.Player.CurrentMana);
        Assert.Equal(0, game.Player.Experience);
    }

    /// <summary>
    /// A spell nobody has learned is refused by letter, however plainly it is
    /// printed in the book.
    /// </summary>
    [Fact]
    public void Cast_ALetterOutsideWhatIsKnownIsRefused()
    {
        // "b" is the second spell in the book, which is not learned here.
        (GameState game, Display display, GameLoop loop) = Fresh(keys: "b");

        CarryBook(game, BeginnersMagick);
        game.Player.SpellLearned = 1u;

        loop.Magic.Cast();

        Assert.Contains("don't know that spell", Said(display));
    }

    /// <summary>
    /// The lettering follows the book rather than what is known, so learning
    /// more spells never moves the ones already learned.
    /// </summary>
    [Fact]
    public void CastSpell_LettersFollowTheBookNotWhatIsKnown()
    {
        // "d" is the fourth spell of the first book, and the only one known.
        (GameState game, _, GameLoop loop) = Fresh(keys: "d6");

        CarryBook(game, BeginnersMagick);
        game.Player.SpellLearned = 1u << 3;

        int result = loop.Magic.CastSpell("Cast which spell?", 0,
                                          out int choice, out int chance);

        Assert.Equal(1, result);
        Assert.Equal(3, choice);
        Assert.InRange(chance, 5, 95);
    }

    /// <summary>
    /// Casting what cannot be afforded asks first, and a refusal leaves the
    /// turn free.
    /// </summary>
    [Fact]
    public void CastSpell_AnUnaffordableSpellIsConfirmed()
    {
        // "a" picks the spell, "n" declines the warning.
        (GameState game, _, GameLoop loop) = Fresh(keys: "an");

        CarryBook(game, BeginnersMagick);
        game.Player.SpellLearned = 1u;
        game.Player.CurrentMana = 0;

        // The warning goes to the prompt line rather than the message line, so
        // what is checked here is that declining it calls the whole thing off.
        int result = loop.Magic.CastSpell("Cast which spell?", 0, out _, out _);

        Assert.Equal(0, result);
        Assert.Equal(0, game.Player.Paralysis);
    }

    /// <summary>
    /// Casting it anyway costs consciousness rather than mana, and sometimes
    /// health as well.
    /// </summary>
    [Fact]
    public void Cast_ReachingForManaThatIsNotThereFaints()
    {
        // "a" picks the spell, "y" presses on, "6" points the bolt.
        (GameState game, Display display, GameLoop loop) = Fresh(keys: "ay6");

        CarryBook(game, BeginnersMagick);
        game.Player.SpellLearned = 1u;
        game.Player.CurrentMana = 0;

        loop.Magic.Cast();

        Assert.Contains("faint from the effort", Said(display));
        Assert.True(game.Player.Paralysis > 0);
        Assert.Equal(0, game.Player.CurrentMana);
    }

    /// <summary>
    /// A prayer works the same way, down to the wording of the faint - a priest
    /// tires rather than strains.
    /// </summary>
    [Fact]
    public void Pray_APriestFaintsFromFatigue()
    {
        (GameState game, Display display, GameLoop loop) = Fresh(PriestClass, "ay");

        CarryBook(game, BeginnersHandbook);
        game.Player.SpellLearned = 1u;
        game.Player.CurrentMana = 0;

        loop.Magic.Pray();

        Assert.Contains("faint from fatigue", Said(display));
    }

    // ------------------------------------------------------------ the table

    /// <summary>
    /// A spell gets easier as the character outgrows it, and impossible mana
    /// makes it harder - but never certain and never hopeless.
    /// </summary>
    [Fact]
    public void SpellChance_IsClampedAtBothEnds()
    {
        (GameState game, _, GameLoop loop) = Fresh(level: 40);

        Assert.Equal(5, loop.Magic.SpellChance(0));

        game.Player.Level = 1;
        game.Player.CurrentMana = 0;

        Assert.Equal(95, loop.Magic.SpellChance(30));
    }

    /// <summary>Being short of mana is what makes a spell harder.</summary>
    [Fact]
    public void SpellChance_RisesWhenTheManaIsShort()
    {
        (GameState game, _, GameLoop loop) = Fresh(level: 5);

        int affordable = loop.Magic.SpellChance(12);
        game.Player.CurrentMana = 0;

        Assert.True(loop.Magic.SpellChance(12) > affordable);
    }

    // ------------------------------------------------------------ the mana

    /// <summary>A character who knows nothing has no mana at all.</summary>
    [Fact]
    public void CalcMana_IsNothingUntilSomethingIsLearned()
    {
        (GameState game, _, GameLoop loop) = Fresh();

        loop.Magic.CalcMana(Stat.Intelligence);
        Assert.Equal(0, game.Player.MaxMana);

        game.Player.SpellLearned = 1u;
        loop.Magic.CalcMana(Stat.Intelligence);

        Assert.True(game.Player.MaxMana > 0);
    }

    /// <summary>
    /// The pool moves in proportion when the maximum changes, so gaining a
    /// level does not refill an empty caster.
    /// </summary>
    [Fact]
    public void CalcMana_MovesTheCurrentPoolInProportion()
    {
        (GameState game, _, GameLoop loop) = Fresh();

        game.Player.SpellLearned = 1u;
        loop.Magic.CalcMana(Stat.Intelligence);

        int full = game.Player.MaxMana;
        game.Player.CurrentMana = full / 2;

        game.Player.Level = 40;
        loop.Magic.CalcMana(Stat.Intelligence);

        Assert.True(game.Player.MaxMana > full);
        Assert.InRange(game.Player.CurrentMana,
                       (game.Player.MaxMana / 2) - 2, (game.Player.MaxMana / 2) + 2);
    }

    /// <summary>
    /// A first level caster gets two points rather than one, which is the extra
    /// the original adds on purpose.
    /// </summary>
    [Fact]
    public void CalcMana_AFirstLevelCasterGetsTwo()
    {
        (GameState game, _, GameLoop loop) = Fresh(level: 1);

        game.Player.SpellLearned = 1u;

        // An unremarkable intelligence, worth one point a level.
        game.Player.UseStat[Stat.Intelligence] = 14;
        loop.Magic.CalcMana(Stat.Intelligence);

        Assert.Equal(2, game.Player.MaxMana);
    }

    // ----------------------------------------------------------- the spells

    /// <summary>
    /// A lost level puts the higher spells out of reach, and they are forgotten
    /// rather than lost - coming back in the order they were learned.
    /// </summary>
    [Fact]
    public void CalcSpells_ForgetsAndRemembersWithTheLevel()
    {
        (GameState game, _, GameLoop loop) = Fresh(level: 40);

        // Three spells of rising level, learned in this order.
        game.Player.SpellLearned = (1u << 0) | (1u << 8) | (1u << 16);
        game.Player.SpellOrder[0] = 0;
        game.Player.SpellOrder[1] = 8;
        game.Player.SpellOrder[2] = 16;

        game.Player.Level = 5;
        loop.Magic.CalcSpells(Stat.Intelligence);

        // The ninth level spell is gone; the first and the fifth remain.
        Assert.Equal(0u, game.Player.SpellLearned & (1u << 16));
        Assert.NotEqual(0u, game.Player.SpellForgotten & (1u << 16));
        Assert.NotEqual(0u, game.Player.SpellLearned & (1u << 8));

        game.Player.Level = 40;
        loop.Magic.CalcSpells(Stat.Intelligence);

        Assert.NotEqual(0u, game.Player.SpellLearned & (1u << 16));
        Assert.Equal(0u, game.Player.SpellForgotten);
    }

    /// <summary>
    /// A caster with room to learn is told so, and the count is what the study
    /// command later hands out.
    /// </summary>
    [Fact]
    public void CalcSpells_AnnouncesRoomToLearn()
    {
        (GameState game, Display display, GameLoop loop) = Fresh();

        loop.Magic.CalcSpells(Stat.Intelligence);

        Assert.True(game.Player.NewSpells > 0);
        Assert.Contains("learn some new spells", Said(display));
    }

    /// <summary>
    /// A mage can only learn what is written in a book they are carrying, and
    /// is told when one is missing.
    /// </summary>
    [Fact]
    public void GainSpells_AMageNeedsTheBookInHand()
    {
        (GameState game, Display display, GameLoop loop) = Fresh();

        game.Player.NewSpells = 3;
        loop.Magic.GainSpells();

        Assert.Contains("missing a book", Said(display));
        Assert.Equal(3, game.Player.NewSpells);
        Assert.Equal(0u, game.Player.SpellLearned);
    }

    /// <summary>
    /// With the book in hand the mage picks, and what they pick is remembered
    /// in the order they picked it.
    /// </summary>
    [Fact]
    public void GainSpells_AMagePicksFromTheBook()
    {
        // "c" then "a": the third spell on offer, then the first of what is left.
        (GameState game, _, GameLoop loop) = Fresh(keys: "ca");

        CarryBook(game, BeginnersMagick);
        game.Player.NewSpells = 2;
        loop.Magic.GainSpells();

        Assert.Equal(0, game.Player.NewSpells);
        Assert.NotEqual(0u, game.Player.SpellLearned & (1u << 2));
        Assert.NotEqual(0u, game.Player.SpellLearned & (1u << 0));
        Assert.Equal(2, game.Player.SpellOrder[0]);
        Assert.Equal(0, game.Player.SpellOrder[1]);
    }

    /// <summary>
    /// A priest needs no book: the prayer comes from their god rather than the
    /// page, and which one arrives is not theirs to choose.
    /// </summary>
    [Fact]
    public void GainSpells_APriestNeedsNoBook()
    {
        (GameState game, Display display, GameLoop loop) = Fresh(PriestClass);

        game.Player.NewSpells = 2;
        loop.Magic.GainSpells();

        Assert.Equal(0, game.Player.NewSpells);
        Assert.Equal(2, System.Numerics.BitOperations.PopCount(game.Player.SpellLearned));
        Assert.Contains("learned the prayer of", Said(display));
    }

    /// <summary>
    /// Nothing earned means nothing to learn, and being told so gives the turn
    /// back.
    /// </summary>
    [Fact]
    public void GainSpells_WithNothingEarnedIsFree()
    {
        (GameState game, Display display, GameLoop loop) = Fresh();

        game.Player.NewSpells = 0;
        loop.Magic.GainSpells();

        Assert.True(loop.FreeTurn);
        Assert.Contains("can't learn any new spells", Said(display));
    }

    /// <summary>
    /// A confused caster cannot study at all, whichever kind of magic they use.
    /// </summary>
    [Fact]
    public void GainSpells_AConfusedCasterCannotStudy()
    {
        (GameState game, Display display, GameLoop loop) = Fresh(PriestClass);

        game.Player.Confused = 10;
        game.Player.NewSpells = 2;
        loop.Magic.GainSpells();

        Assert.Contains("too confused", Said(display));
        Assert.Equal(0u, game.Player.SpellLearned);
    }

    /// <summary>
    /// Changing the casting stat reaches all the way through: raising the
    /// intelligence of a mage who knows a spell gives them more mana for it.
    /// </summary>
    [Fact]
    public void SetUseStat_ReachesTheSpellsAndTheMana()
    {
        (GameState game, _, GameLoop loop) = Fresh();

        game.Player.SpellLearned = 1u;
        game.Player.CurrentStat[Stat.Intelligence] = 10;
        loop.Stats.SetUseStat(Stat.Intelligence);

        int poor = game.Player.MaxMana;

        game.Player.CurrentStat[Stat.Intelligence] = 18;
        loop.Stats.SetUseStat(Stat.Intelligence);

        Assert.True(game.Player.MaxMana > poor);
    }
}
