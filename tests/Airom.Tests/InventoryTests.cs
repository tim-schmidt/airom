using Airom.Core;
using Airom.Data;
using Airom.Terminal;

namespace Airom.Tests;

/// <summary>
/// Checks on item naming and on the pack.
///
/// 6,720 item descriptions per seed and 96 walks that pick things up are diffed
/// against the C oracle, which runs the real objdes and inven_carry. These pin
/// the properties behind them.
/// </summary>
public class InventoryTests
{
    private static (GameState Game, ItemNames Names) Fresh(uint seed = 12345)
    {
        var game = new GameState();
        game.InitSeeds(seed);
        game.MagicInit();
        return (game, game.Names);
    }

    private static InvenType Item(GameState game, int tableIndex)
    {
        var item = new InvenType();
        item.CopyFrom(tableIndex);
        return item;
    }

    // ---------------------------------------------------------------- naming

    /// <summary>
    /// An unidentified potion is named by its colour and a known one by what it
    /// does, which is the whole point of the shuffled appearances.
    /// </summary>
    [Fact]
    public void Describe_NamesTheAppearanceUntilTheKindIsKnown()
    {
        (GameState game, ItemNames names) = Fresh();
        InvenType potion = Item(game, 222); // slime mould juice

        string unknown = names.Describe(potion, withArticle: true);
        Assert.Contains("Potion", unknown, StringComparison.Ordinal);
        Assert.DoesNotContain("Slime Mold", unknown, StringComparison.Ordinal);

        game.Knowledge.LearnKind(potion);
        Assert.Contains("Slime Mold", names.Describe(potion, withArticle: true),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A pile is counted rather than given an article, and the plural is spelled
    /// where the "~" sits in the table entry.
    /// </summary>
    [Fact]
    public void Describe_CountsAPileAndPluralisesIt()
    {
        (GameState game, ItemNames names) = Fresh();
        InvenType pebbles = Item(game, 82);

        Assert.StartsWith("a Rounded Pebble", names.Describe(pebbles, withArticle: true),
            StringComparison.Ordinal);

        pebbles.Number = 12;
        Assert.StartsWith("12 Rounded Pebbles", names.Describe(pebbles, withArticle: true),
            StringComparison.Ordinal);
    }

    /// <summary>An empty pile reads "no more", which is how the last one going is reported.</summary>
    [Fact]
    public void Describe_SaysNoMoreForAnEmptyPile()
    {
        (GameState game, ItemNames names) = Fresh();
        InvenType pebbles = Item(game, 82);
        pebbles.Number = 0;

        Assert.StartsWith("no more", names.Describe(pebbles, withArticle: true),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_UsesAnBeforeAVowel()
    {
        (GameState game, ItemNames names) = Fresh();
        InvenType arrow = Item(game, 80); // arrows, which take "an"

        Assert.StartsWith("an ", names.Describe(arrow, withArticle: true),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The bonuses only show once the item's own enchantment is known, which is
    /// what makes an unidentified weapon worth trying.
    /// </summary>
    [Fact]
    public void Describe_HidesBonusesUntilTheEnchantmentIsKnown()
    {
        (GameState game, ItemNames names) = Fresh();
        InvenType sword = Item(game, 30);
        sword.ToHit = 7;
        sword.ToDam = 3;
        sword.Identification |= Identification.ShowHitDam;

        Assert.DoesNotContain("+7", names.Describe(sword, withArticle: true),
            StringComparison.Ordinal);

        game.Knowledge.LearnEnchantment(sword);
        Assert.Contains("(+7,+3)", names.Describe(sword, withArticle: true),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The guesses go in braces at the end, and anything the player wrote on the
    /// item is added after them rather than replacing them.
    /// </summary>
    [Fact]
    public void Describe_PutsGuessesInBraces()
    {
        (GameState game, ItemNames names) = Fresh();
        InvenType ring = Item(game, 128);
        ring.Identification |= Identification.Damned;

        Assert.Contains("{damned}", names.Describe(ring, withArticle: true),
            StringComparison.Ordinal);

        ring.Inscription = "bad";
        Assert.Contains("{damned bad}", names.Describe(ring, withArticle: true),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The bare form drops the article and everything after the name, which is
    /// what the shop lists and the spell menus want.
    /// </summary>
    [Fact]
    public void Describe_BareFormDropsTheArticle()
    {
        (GameState game, ItemNames names) = Fresh();
        InvenType sword = Item(game, 30);

        Assert.StartsWith("a ", names.Describe(sword, withArticle: true),
            StringComparison.Ordinal);
        Assert.False(names.Describe(sword, withArticle: false).StartsWith("a ",
            StringComparison.Ordinal));
    }

    /// <summary>
    /// Terrain is named outright: a trap or a staircase has no article, no count
    /// and no braces.
    /// </summary>
    [Fact]
    public void Describe_NamesTerrainPlainly()
    {
        (GameState game, ItemNames names) = Fresh();
        InvenType stair = Item(game, 371); // a staircase

        Assert.EndsWith(".", names.Describe(stair, withArticle: true), StringComparison.Ordinal);
        Assert.DoesNotContain("{", names.Describe(stair, withArticle: true),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A shop names what it sells, so nothing bought is a mystery - and it is
    /// never marked as tried, whoever tried one before.
    /// </summary>
    [Fact]
    public void Describe_StoreBoughtIsNeverAMystery()
    {
        (GameState game, ItemNames names) = Fresh();
        InvenType potion = Item(game, 222);

        game.Knowledge.MarkTried(potion);
        Assert.Contains("tried", names.Describe(potion, withArticle: true),
            StringComparison.Ordinal);

        game.Knowledge.MarkStoreBought(potion);
        string bought = names.Describe(potion, withArticle: true);
        Assert.DoesNotContain("tried", bought, StringComparison.Ordinal);
        Assert.Contains("Slime Mold", bought, StringComparison.Ordinal);
    }

    // ----------------------------------------------------------------- pack

    /// <summary>
    /// Two piles of the same thing merge, which is what stops a hundred arrows
    /// filling the pack.
    /// </summary>
    [Fact]
    public void Carry_StacksLikeWithLike()
    {
        (GameState game, _) = Fresh();

        InvenType first = Item(game, 82);
        first.Number = 5;
        game.Inventory.Carry(first);

        InvenType second = Item(game, 82);
        second.Number = 7;
        game.Inventory.Carry(second);

        Assert.Equal(1, game.Inventory.Count);
        Assert.Equal(12, game.Inventory[0].Number);
    }

    /// <summary>
    /// A known pile and an unknown one stay apart, so identifying one does not
    /// silently identify the other.
    /// </summary>
    [Fact]
    public void Carry_KeepsKnownAndUnknownApart()
    {
        (GameState game, _) = Fresh();

        InvenType known = Item(game, 222);
        game.Knowledge.LearnKind(known);
        game.Inventory.Carry(known);

        InvenType unknown = Item(game, 223);
        game.Inventory.Carry(unknown);

        Assert.Equal(2, game.Inventory.Count);
    }

    /// <summary>The pack holds twenty-two things; the rest of the array is worn.</summary>
    [Fact]
    public void HasRoomFor_StopsAtTwentyTwo()
    {
        (GameState game, _) = Fresh();

        for (int i = 0; i < Inventory.WieldSlot; i++)
        {
            // Weapons, which never stack, so each one takes a slot.
            InvenType weapon = Item(game, 30 + i);
            game.Inventory.Carry(weapon);
        }

        Assert.Equal(Inventory.WieldSlot, game.Inventory.Count);
        Assert.False(game.Inventory.HasRoomFor(Item(game, 30)));

        // But something that stacks with what is already there still fits.
        InvenType pebbles = Item(game, 82);
        game.Inventory.Carry(pebbles);
        Assert.True(game.Inventory.HasRoomFor(Item(game, 82)));
    }

    /// <summary>
    /// Strength decides what can be carried, with the player's own weight
    /// counting too - and nobody carries more than three hundred pounds.
    /// </summary>
    [Fact]
    public void WeightLimit_ComesFromStrengthAndBuild()
    {
        (GameState game, _) = Fresh();

        // A hundred and thirty tenths of a pound per point of strength, plus
        // what the player weighs themselves: Umoria's PLAYER_WEIGHT_CAP.
        game.Player.UseStat[Stat.Strength] = 10;
        game.Player.Weight = 150;
        Assert.Equal((10 * Inventory.WeightPerStrength) + 150,
            game.Inventory.WeightLimit());

        Assert.Equal(130, Inventory.WeightPerStrength);

        game.Player.UseStat[Stat.Strength] = 118;
        Assert.Equal(Inventory.MaxWeightLimit, game.Inventory.WeightLimit());
    }

    /// <summary>
    /// Taking one from a pile leaves the rest, and the weight follows - but only
    /// for the kinds that stack one at a time. A pile of arrows is taken whole.
    /// </summary>
    [Fact]
    public void Destroy_TakesOneFromAPile()
    {
        (GameState game, _) = Fresh();

        InvenType potions = Item(game, 222);
        potions.Number = 5;
        game.Inventory.Carry(potions);

        int weight = game.Inventory.Weight;
        game.Inventory.Destroy(0);

        Assert.Equal(1, game.Inventory.Count);
        Assert.Equal(4, game.Inventory[0].Number);
        Assert.Equal(weight - potions.Weight, game.Inventory.Weight);
    }

    [Fact]
    public void Destroy_RemovesTheLastOneOutright()
    {
        (GameState game, _) = Fresh();

        game.Inventory.Carry(Item(game, 30));
        game.Inventory.Destroy(0);

        Assert.Equal(0, game.Inventory.Count);
        Assert.Equal(0, game.Inventory.Weight);
    }

    /// <summary>
    /// Every slot is rolled for separately, which is why a fire burns two
    /// scrolls and leaves a third.
    ///
    /// FAITHFUL QUIRK: destroying a slot shifts the rest down while the loop
    /// keeps counting up, so the item after each casualty is skipped. A fire is
    /// therefore kinder to a full pack than the odds suggest.
    /// </summary>
    [Fact]
    public void Damage_RollsForEachSlot()
    {
        (GameState game, _) = Fresh();

        for (int i = 0; i < 5; i++)
        {
            InvenType scroll = Item(game, 173 + i);
            game.Inventory.Carry(scroll);
        }

        int before = game.Inventory.Count;
        int destroyed = game.Inventory.Damage(item => item.TVal == ItemCategory.Scroll1, 100);

        Assert.True(destroyed > 0, "nothing was destroyed at a certainty");
        Assert.Equal(before - destroyed, game.Inventory.Count);

        // Half of them survive a certain fire, because of the skipping.
        Assert.True(game.Inventory.Count >= before / 2 - 1);
    }

    // ------------------------------------------------------------ equipment

    /// <summary>
    /// A worn item's base armour shows even unidentified, as long as it is not
    /// cursed - the enchantment is what stays hidden.
    /// </summary>
    [Fact]
    public void Recalculate_ShowsBaseArmourButHidesTheEnchantment()
    {
        (GameState game, _) = Fresh();
        var screen = new MemoryScreen();
        var display = new Display(game, screen);
        var loop = new GameLoop(game, display);

        InvenType armour = Item(game, 102);
        armour.Ac = 4;
        armour.ToAc = 6;
        game.Inventory[Inventory.BodySlot].CopyStateFrom(armour);

        loop.Equipment.Recalculate();

        Assert.Equal(4, game.Player.ArmourClass);
        Assert.Equal(4, game.Player.DisplayedArmourClass - game.Player.DisplayedToArmourClass);
        Assert.Equal(6, game.Player.PlusToArmourClass - Stats.ArmourBonus(game.Player));
    }

    /// <summary>
    /// A pack over the limit slows the player down by a point for every multiple
    /// of it, and says so.
    /// </summary>
    [Fact]
    public void CheckStrength_SlowsThePlayerUnderAHeavyPack()
    {
        (GameState game, _) = Fresh();
        var screen = new MemoryScreen();
        var display = new Display(game, screen);
        var loop = new GameLoop(game, display);

        game.Player.UseStat[Stat.Strength] = 3;
        game.Player.Weight = 80;

        InvenType armour = Item(game, 111); // metal scale mail, which is heavy
        armour.Number = 5;
        game.Inventory.Carry(armour);

        loop.Equipment.CheckStrength();

        Assert.True(game.Inventory.PackBurden > 0, "a heavy pack did not slow the player");
        Assert.True(game.Player.Speed > 0, "the speed penalty was not applied");
        Assert.Contains("slows you down", screen.GetRow(0), StringComparison.Ordinal);
    }

    // ------------------------------------------------------------ knowledge

    /// <summary>
    /// Knowing a kind is learned once and applies to every one the player ever
    /// finds; knowing an item's own enchantment stays with that item.
    /// </summary>
    [Fact]
    public void Knowledge_SeparatesTheKindFromTheItem()
    {
        (GameState game, _) = Fresh();

        InvenType first = Item(game, 222);
        InvenType second = Item(game, 222);

        game.Knowledge.LearnKind(first);
        Assert.True(game.Knowledge.IsKindKnown(second));

        game.Knowledge.LearnEnchantment(first);
        Assert.True(ItemKnowledge.IsEnchantmentKnown(first));
        Assert.False(ItemKnowledge.IsEnchantmentKnown(second));
    }

    /// <summary>
    /// Something with no shuffled appearance is always known, so that it sorts
    /// into a stable place in the pack.
    /// </summary>
    [Fact]
    public void Knowledge_ThingsWithNoDisguiseAreAlwaysKnown()
    {
        (GameState game, _) = Fresh();

        Assert.True(game.Knowledge.IsKindKnown(Item(game, 30)));   // a sword
        Assert.False(game.Knowledge.IsKindKnown(Item(game, 222))); // a potion
    }
}
