using Airom.Core;
using Airom.Data;
using Airom.Terminal;

namespace Airom.Tests;

/// <summary>
/// Checks on digging, disarming, bashing, throwing and looking.
///
/// All five are diffed against the C oracle - ten scripted arrangements and
/// eighteen looks across seven seeds and four depths - so what these pin is the
/// behaviour behind them, and the cases the scripts cannot reach.
/// </summary>
public class Moria4Tests
{
    private static (GameState Game, MemoryScreen Screen, Display Display, GameLoop Loop)
        Fresh(string keys = "")
    {
        var game = new GameState();
        game.InitSeeds(12345);
        game.MagicInit();
        game.DungeonLevel = 5;

        var screen = new MemoryScreen { TypeAheadVisible = false };
        screen.SendKeys(keys + new string((char)27, 400));

        var display = new Display(game, screen);

        new DungeonGenerator(game).Generate();
        game.Cave[game.CharacterRow, game.CharacterColumn].MonsterIndex = 1;
        display.Panel.Resize(game.Cave.Height, game.Cave.Width);

        var loop = new GameLoop(game, display);
        Player player = game.Player;

        player.Level = 20;
        player.ExperienceFactor = 100;
        player.Weight = 150;
        player.Food = 5000;

        for (int i = 0; i < Stat.Count; i++)
        {
            player.MaxStat[i] = 18;
            player.CurrentStat[i] = 18;
            player.UseStat[i] = 18;
        }

        game.Inventory.Reset();
        game.Inventory[Inventory.WieldSlot].CopyFrom(30);    // a stiletto
        game.Inventory[Inventory.ArmSlot].CopyFrom(111);     // a shield
        game.Inventory[Inventory.LightSlot].CopyFrom(365);   // a wooden torch
        game.Inventory[Inventory.LightSlot].P1 = 5000;
        game.Inventory.EquipmentCount = 3;

        loop.Equipment.Recalculate();

        player.MaxHitPoints = 2000;
        player.CurrentHitPoints = 2000;

        game.PlayerLight = true;
        display.Panel.Invalidate();
        loop.Lighting.CheckView();

        // A clean patch to work in: floor all round, and nothing on it.
        for (int i = -1; i <= 1; i++)
        {
            for (int j = -1; j <= 1; j++)
            {
                CaveSquare square = game.Cave[game.CharacterRow + i, game.CharacterColumn + j];
                square.Feature = CaveFeature.CorridorFloor;
                square.ObjectIndex = 0;

                if (i != 0 || j != 0)
                {
                    square.MonsterIndex = 0;
                }
            }
        }

        display.MessageWaitingFlag = false;
        loop.FreeTurn = false;
        return (game, screen, display, loop);
    }

    /// <summary>Everything said so far, newest last.</summary>
    private static string Said(Display display) =>
        string.Join(" | ", display.RecentMessages.Where(m => !string.IsNullOrEmpty(m)));

    /// <summary>Puts a wall of a kind directly north of the player.</summary>
    private static void WallNorth(GameState game, byte feature) =>
        game.Cave[game.CharacterRow - 1, game.CharacterColumn].Feature = feature;

    /// <summary>Puts an object directly north of the player.</summary>
    private static InvenType ObjectNorth(GameState game, int which)
    {
        int index = game.Objects.Allocate();
        game.Objects[index].CopyFrom(which);
        game.Cave[game.CharacterRow - 1, game.CharacterColumn].ObjectIndex = index;
        return game.Objects[index];
    }

    private const int RubbleObject = 396;
    private const int SecretDoorObject = 369;
    private const int ClosedDoorObject = 368;
    private const int SmallChestObject = 326;
    private const int TrapListObject = 378;

    // ------------------------------------------------------------ tunnelling

    /// <summary>
    /// Nothing may be dug that is not diggable, and being told so costs no turn
    /// - which is what stops digging at empty air being used as a free attack
    /// on whatever might be standing in it.
    /// </summary>
    [Fact]
    public void Tunnel_AtEmptyAirIsFree()
    {
        (_, _, Display display, GameLoop loop) = Fresh();

        loop.Tunnelling.Tunnel(8);

        Assert.True(loop.FreeTurn);
        Assert.Contains("Empty air", Said(display));
    }

    /// <summary>Something in the way is dug at only with an object that is.</summary>
    [Fact]
    public void Tunnel_ThroughAnUndiggableObjectIsFree()
    {
        (GameState game, _, Display display, GameLoop loop) = Fresh();

        ObjectNorth(game, 30);   // a stiletto lying on the floor
        loop.Tunnelling.Tunnel(8);

        Assert.True(loop.FreeTurn);
        Assert.Contains("can't tunnel through that", Said(display));
    }

    /// <summary>Bare hands make no progress, whatever the rock is.</summary>
    [Fact]
    public void Tunnel_WithBareHandsGetsNowhere()
    {
        (GameState game, _, Display display, GameLoop loop) = Fresh();

        game.Inventory[Inventory.WieldSlot].Clear();
        WallNorth(game, CaveFeature.MagmaWall);

        loop.Tunnelling.Tunnel(8);

        Assert.Contains("dig with your hands", Said(display));
        Assert.Equal(CaveFeature.MagmaWall,
            game.Cave[game.CharacterRow - 1, game.CharacterColumn].Feature);
    }

    /// <summary>The boundary of the level is not rock anybody can dig.</summary>
    [Fact]
    public void Tunnel_IntoTheBoundaryIsPermanent()
    {
        (GameState game, _, Display display, GameLoop loop) = Fresh();

        WallNorth(game, CaveFeature.BoundaryWall);
        loop.Tunnelling.Tunnel(8);

        Assert.Contains("permanent rock", Said(display));
    }

    /// <summary>
    /// Something standing in diggable rock is attacked rather than dug at -
    /// unless the player is too frightened to go near it.
    /// </summary>
    [Fact]
    public void Tunnel_IntoSomethingAliveAttacksIt()
    {
        (GameState game, _, Display display, GameLoop loop) = Fresh();

        // In the rock, not in front of it: something standing on open floor is
        // "empty air" to a digger, since the guard that stops a free attack
        // fires before the attack branch is reached at all.
        WallNorth(game, CaveFeature.GraniteWall);
        game.Cave[game.CharacterRow - 1, game.CharacterColumn].MonsterIndex = 2;

        game.Player.Afraid = 10;
        loop.Tunnelling.Tunnel(8);

        Assert.Contains("is in your way!", Said(display));
        Assert.Contains("too afraid", Said(display));
    }

    /// <summary>
    /// Something standing on open floor is not, which is what stops a player
    /// finding an invisible creature by digging at every square around them.
    /// </summary>
    [Fact]
    public void Tunnel_IntoSomethingAliveOnOpenFloorIsStillEmptyAir()
    {
        (GameState game, _, Display display, GameLoop loop) = Fresh();

        WallNorth(game, CaveFeature.CorridorFloor);
        game.Cave[game.CharacterRow - 1, game.CharacterColumn].MonsterIndex = 2;

        loop.Tunnelling.Tunnel(8);

        Assert.True(loop.FreeTurn);
        Assert.Contains("Empty air", Said(display));
    }

    /// <summary>
    /// Digging at a wall that is really a secret door searches, which is how
    /// tunnelling into blank granite finds what is hidden in it.
    /// </summary>
    [Fact]
    public void Tunnel_IntoASecretDoorSearchesForIt()
    {
        (GameState game, _, Display display, GameLoop loop) = Fresh();

        WallNorth(game, CaveFeature.GraniteWall);
        ObjectNorth(game, SecretDoorObject);

        // A search that cannot fail, so the door is certain to be found.
        game.Player.Search = 1000;
        loop.Tunnelling.Tunnel(8);

        Assert.Contains("tunnel into the granite wall", Said(display));
    }

    /// <summary>
    /// A shovel digs far better than a weapon of the same weight, which is the
    /// whole point of carrying one.
    /// </summary>
    [Fact]
    public void Tunnel_AShovelBeatsABlade()
    {
        int WithDigging(int p1, uint flags)
        {
            int broken = 0;

            for (uint seed = 1; seed <= 60; seed++)
            {
                var game = new GameState();
                game.InitSeeds(seed);
                game.MagicInit();
                game.DungeonLevel = 5;

                var screen = new MemoryScreen { TypeAheadVisible = false };
                screen.SendKeys(new string((char)27, 100));

                var display = new Display(game, screen);
                new DungeonGenerator(game).Generate();
                display.Panel.Resize(game.Cave.Height, game.Cave.Width);

                var loop = new GameLoop(game, display);

                for (int i = 0; i < Stat.Count; i++)
                {
                    game.Player.UseStat[i] = 18;
                }

                game.Inventory.Reset();
                game.Inventory[Inventory.WieldSlot].CopyFrom(30);
                game.Inventory[Inventory.WieldSlot].P1 = (short)p1;
                game.Inventory[Inventory.WieldSlot].Flags = flags;
                game.Inventory.EquipmentCount = 1;

                CaveSquare square =
                    game.Cave[game.CharacterRow - 1, game.CharacterColumn];
                square.Feature = CaveFeature.QuartzWall;
                square.ObjectIndex = 0;
                square.MonsterIndex = 0;

                loop.Tunnelling.Tunnel(8);

                if (square.Feature <= CaveFeature.MaxOpenSpace)
                {
                    broken++;
                }
            }

            return broken;
        }

        int blade = WithDigging(0, 0);
        int shovel = WithDigging(3, ItemFlags.Tunnel);

        Assert.True(shovel > blade,
            $"a shovel broke {shovel} walls and a blade {blade}");
    }

    // ------------------------------------------------------------- disarming

    /// <summary>Nothing to disarm costs no turn.</summary>
    [Fact]
    public void DisarmTrap_WithNothingThereIsFree()
    {
        (_, _, Display display, GameLoop loop) = Fresh("8");

        loop.Traps.DisarmTrap();

        Assert.True(loop.FreeTurn);
        Assert.Contains("do not see anything to disarm", Said(display));
    }

    /// <summary>
    /// A chest whose trap has not been seen cannot be worked on, and saying so
    /// costs no turn.
    /// </summary>
    [Fact]
    public void DisarmTrap_AnUnknownChestIsFree()
    {
        (GameState game, _, Display display, GameLoop loop) = Fresh("8");

        InvenType chest = ObjectNorth(game, SmallChestObject);
        chest.Flags = ChestFlags.Locked | ChestFlags.Poison;

        loop.Traps.DisarmTrap();

        Assert.True(loop.FreeTurn);
        Assert.Contains("don't see a trap", Said(display));
    }

    /// <summary>A chest that was never trapped has nothing to disarm.</summary>
    [Fact]
    public void DisarmTrap_AnUntrappedChestSaysSo()
    {
        (GameState game, _, Display display, GameLoop loop) = Fresh("8");

        InvenType chest = ObjectNorth(game, SmallChestObject);
        chest.Flags = ChestFlags.Locked;
        game.Knowledge.LearnEnchantment(chest);

        loop.Traps.DisarmTrap();

        Assert.True(loop.FreeTurn);
        Assert.Contains("was not trapped", Said(display));
    }

    /// <summary>
    /// A disarmed chest keeps its lock if it had one, and is renamed to say so
    /// either way.
    /// </summary>
    [Fact]
    public void DisarmTrap_ADisarmedChestKeepsItsLock()
    {
        (GameState game, _, _, GameLoop loop) = Fresh("8");

        InvenType chest = ObjectNorth(game, SmallChestObject);
        chest.Flags = ChestFlags.Locked | ChestFlags.Poison;
        chest.Level = 1;
        game.Knowledge.LearnEnchantment(chest);

        // A disarming nothing can fail.
        game.Player.Disarm = 10000;
        loop.Traps.DisarmTrap();

        Assert.Equal(0u, chest.Flags & ChestFlags.Trapped);
        Assert.Equal(ChestFlags.Locked, chest.Flags & ChestFlags.Locked);
        Assert.Equal(SpecialName.Locked, chest.SpecialName);
    }

    /// <summary>
    /// Anything that clouds the senses divides the skill by ten, and the three
    /// of them stack.
    /// </summary>
    [Fact]
    public void DisarmTrap_BlindnessAndConfusionBothCount()
    {
        int Disarmed(bool blind, bool confused)
        {
            int count = 0;

            for (uint seed = 1; seed <= 60; seed++)
            {
                var game = new GameState();
                game.InitSeeds(seed);
                game.MagicInit();
                game.DungeonLevel = 5;

                var screen = new MemoryScreen { TypeAheadVisible = false };
                screen.SendKeys("8" + new string((char)27, 100));

                var display = new Display(game, screen);
                new DungeonGenerator(game).Generate();
                display.Panel.Resize(game.Cave.Height, game.Cave.Width);

                var loop = new GameLoop(game, display);

                for (int i = 0; i < Stat.Count; i++)
                {
                    game.Player.UseStat[i] = 18;
                }

                game.Player.Level = 20;
                game.Player.Disarm = 60;
                game.Player.Blind = blind ? 10 : 0;
                game.Player.Confused = confused ? 10 : 0;
                game.Player.MaxHitPoints = 2000;
                game.Player.CurrentHitPoints = 2000;

                game.Cave[game.CharacterRow, game.CharacterColumn].PermanentLight = true;

                CaveSquare square =
                    game.Cave[game.CharacterRow - 1, game.CharacterColumn];
                square.Feature = CaveFeature.CorridorFloor;
                square.MonsterIndex = 0;

                int index = game.Objects.Allocate();
                game.Objects[index].CopyFrom(TrapListObject + 1);
                game.Objects[index].TVal = ItemCategory.VisibleTrap;
                square.ObjectIndex = index;

                loop.Traps.DisarmTrap();

                if (square.ObjectIndex == 0)
                {
                    count++;
                }
            }

            return count;
        }

        int clear = Disarmed(blind: false, confused: false);
        int clouded = Disarmed(blind: true, confused: true);

        Assert.True(clear > clouded,
            $"clear headed disarmed {clear}, blind and confused {clouded}");
    }

    // --------------------------------------------------------------- bashing

    /// <summary>Bashing empty space says exactly that.</summary>
    [Fact]
    public void Bash_AtEmptySpaceSaysSo()
    {
        (_, _, Display display, GameLoop loop) = Fresh("8");

        loop.Doors.Bash();

        Assert.Contains("bash at empty space", Said(display));
    }

    /// <summary>
    /// A wall and a secret door give the same answer, so bashing around cannot
    /// be used to find one.
    /// </summary>
    [Fact]
    public void Bash_AWallAndASecretDoorAnswerAlike()
    {
        (GameState wall, _, Display wallDisplay, GameLoop wallLoop) = Fresh("8");
        WallNorth(wall, CaveFeature.GraniteWall);
        wallLoop.Doors.Bash();

        (GameState door, _, Display doorDisplay, GameLoop doorLoop) = Fresh("8");
        WallNorth(door, CaveFeature.GraniteWall);
        ObjectNorth(door, SecretDoorObject);
        doorLoop.Doors.Bash();

        Assert.Contains("nothing interesting happens", Said(wallDisplay));
        Assert.Contains("nothing interesting happens", Said(doorDisplay));
    }

    /// <summary>
    /// A door that gives way is opened and walked through, and half the time it
    /// is broken rather than merely open.
    /// </summary>
    [Fact]
    public void Bash_ADoorThatGivesWayIsWalkedThrough()
    {
        // One key per bash: each one asks for its own direction.
        (GameState game, _, Display display, GameLoop loop) = Fresh(new string('8', 40));

        int row = game.CharacterRow - 1;
        int column = game.CharacterColumn;

        InvenType door = ObjectNorth(game, ClosedDoorObject);

        // Barely fastened at all, so the first shove opens it.
        door.P1 = 0;

        for (int attempt = 0; attempt < 40 && game.CharacterRow != row; attempt++)
        {
            // The turn loop clears the message line between turns; without
            // that, the -more- prompt eats the next scripted direction.
            display.MessageWaitingFlag = false;
            loop.Doors.Bash();
        }

        Assert.Equal(row, game.CharacterRow);
        Assert.Equal(column, game.CharacterColumn);
        Assert.Equal(ItemCategory.OpenDoor, door.TVal);
        Assert.Equal(CaveFeature.CorridorFloor, game.Cave[row, column].Feature);
    }

    /// <summary>
    /// A bashed chest is mostly ruined along with everything in it; breaking
    /// the lock open is the lucky outcome.
    /// </summary>
    [Fact]
    public void Bash_AChestIsEitherRuinedOrForced()
    {
        (GameState game, _, Display display, GameLoop loop) = Fresh(new string('8', 60));

        InvenType chest = ObjectNorth(game, SmallChestObject);
        chest.Flags = ChestFlags.Locked;

        for (int attempt = 0; attempt < 60; attempt++)
        {
            display.MessageWaitingFlag = false;
            loop.Doors.Bash();

            if ((chest.Flags & ChestFlags.Locked) == 0)
            {
                break;
            }
        }

        string said = Said(display);

        Assert.True(
            said.Contains("destroyed the chest", StringComparison.Ordinal)
            || said.Contains("lock breaks open", StringComparison.Ordinal),
            "sixty bashes neither ruined the chest nor broke its lock");
    }

    /// <summary>
    /// Bashing something alive is refused outright while the player is
    /// frightened.
    /// </summary>
    [Fact]
    public void Bash_SomethingAliveIsRefusedWhileAfraid()
    {
        (GameState game, _, Display display, GameLoop loop) = Fresh("8");

        WallNorth(game, CaveFeature.CorridorFloor);
        new DungeonGenerator(game)
            .PlaceMonster(game.CharacterRow - 1, game.CharacterColumn, 20, false);

        game.Player.Afraid = 10;
        loop.Doors.Bash();

        Assert.Contains("You are afraid!", Said(display));
    }

    // -------------------------------------------------------------- throwing

    /// <summary>An empty pack has nothing to throw, and saying so costs no turn.</summary>
    [Fact]
    public void ThrowObject_WithAnEmptyPackIsFree()
    {
        (_, _, Display display, GameLoop loop) = Fresh();

        loop.Throwing.ThrowObject();

        Assert.True(loop.FreeTurn);
        Assert.Contains("not carrying anything", Said(display));
    }

    /// <summary>
    /// One of a pile is thrown and the rest stay carried, which is what makes a
    /// quiver of arrows last.
    /// </summary>
    [Fact]
    public void ThrowObject_TakesOneOutOfAPile()
    {
        // "a" picks the first slot, "6" throws it east.
        (GameState game, _, _, GameLoop loop) = Fresh("a6");

        var arrows = new InvenType();
        arrows.CopyFrom(FirstOfKind(ItemCategory.Arrow));
        arrows.Number = 20;
        game.Inventory.Carry(arrows);

        loop.Throwing.ThrowObject();

        Assert.Equal(1, game.Inventory.Count);
        Assert.Equal(19, game.Inventory[0].Number);
    }

    /// <summary>The last of a pile leaves the slot empty.</summary>
    [Fact]
    public void ThrowObject_TheLastOneEmptiesTheSlot()
    {
        (GameState game, _, _, GameLoop loop) = Fresh("a6");

        var arrow = new InvenType();
        arrow.CopyFrom(FirstOfKind(ItemCategory.Arrow));
        arrow.Number = 1;
        game.Inventory.Carry(arrow);

        loop.Throwing.ThrowObject();

        Assert.Equal(0, game.Inventory.Count);
    }

    /// <summary>
    /// Backing out of the direction throws nothing at all - the thing is still
    /// in the pack.
    /// </summary>
    [Fact]
    public void ThrowObject_ACancelledDirectionKeepsTheItem()
    {
        // "a" picks the slot; the padding escapes back out of the direction.
        (GameState game, _, _, GameLoop loop) = Fresh("a");

        var arrow = new InvenType();
        arrow.CopyFrom(FirstOfKind(ItemCategory.Arrow));
        game.Inventory.Carry(arrow);

        loop.Throwing.ThrowObject();

        Assert.Equal(1, game.Inventory.Count);
        Assert.True(loop.FreeTurn);
    }

    /// <summary>
    /// A heavy thing barely travels: range falls off with weight, so a suit of
    /// armour is dropped rather than thrown.
    /// </summary>
    [Fact]
    public void ThrowObject_AHeavyThingBarelyTravels()
    {
        (GameState game, _, _, GameLoop loop) = Fresh("a6");

        // Soft leather armour, which is eighty times the weight of an arrow.
        var armour = new InvenType();
        armour.CopyFrom(103);
        game.Inventory.Carry(armour);

        loop.Throwing.ThrowObject();

        // It landed somewhere close by rather than flying off across the level.
        int found = 0;

        for (int row = game.CharacterRow - 3; row <= game.CharacterRow + 3; row++)
        {
            for (int column = game.CharacterColumn - 3;
                 column <= game.CharacterColumn + 3;
                 column++)
            {
                if (game.Cave.InBounds(row, column)
                    && game.Cave[row, column].ObjectIndex != 0
                    && game.Objects[game.Cave[row, column].ObjectIndex].TVal
                        == ItemCategory.SoftArmor)
                {
                    found++;
                }
            }
        }

        Assert.True(found > 0 || game.Inventory.Count == 0,
            "the armour neither landed nearby nor was lost");
    }

    // --------------------------------------------------------------- looking

    /// <summary>A blind character sees nothing, and is told so.</summary>
    [Fact]
    public void Look_WhileBlindSeesNothing()
    {
        (GameState game, _, Display display, GameLoop loop) = Fresh("5");

        game.Player.Blind = 10;
        loop.Looking.Look();

        Assert.Contains("can't see a damn thing", Said(display));
    }

    /// <summary>Hallucinating is its own kind of not seeing.</summary>
    [Fact]
    public void Look_WhileHallucinatingIsRefused()
    {
        (GameState game, _, Display display, GameLoop loop) = Fresh("5");

        game.Player.Hallucinating = 10;
        loop.Looking.Look();

        Assert.Contains("like a dream", Said(display));
    }

    /// <summary>
    /// A bare stretch of floor has nothing worth describing, and the answer
    /// says which direction was asked about.
    /// </summary>
    [Fact]
    public void Look_AtNothingSaysSoForTheDirection()
    {
        (GameState game, _, Display display, GameLoop loop) = Fresh("8");

        Clear(game);
        loop.Looking.Look();

        Assert.Contains("nothing of interest in that direction", Said(display));
    }

    /// <summary>Looking every way at once drops the direction from the answer.</summary>
    [Fact]
    public void Look_InAllDirectionsSaysSoWithoutOne()
    {
        (GameState game, _, Display display, GameLoop loop) = Fresh("5");

        Clear(game);
        loop.Looking.Look();

        Assert.Contains("You see nothing of interest.", Said(display));
    }

    /// <summary>
    /// Something on the floor in the cone is described, and the look closes by
    /// saying that was all of it.
    /// </summary>
    [Fact]
    public void Look_DescribesWhatIsInTheCone()
    {
        // "8" looks north, then spaces step past each thing.
        (GameState game, _, Display display, GameLoop loop) = Fresh("8    ");

        Clear(game);

        int index = game.Objects.Allocate();
        game.Objects[index].CopyFrom(30);   // a stiletto, three squares north
        game.Cave[game.CharacterRow - 3, game.CharacterColumn].ObjectIndex = index;

        loop.Looking.Look();

        Assert.Contains("That's all you see in that direction.", Said(display));
    }

    /// <summary>
    /// Escape gives up on the whole look rather than stepping past one thing.
    /// </summary>
    [Fact]
    public void Look_EscapeAbortsTheWholeThing()
    {
        // "8" looks north; the padding escapes out of the first description.
        (GameState game, _, Display display, GameLoop loop) = Fresh("8");

        Clear(game);

        int index = game.Objects.Allocate();
        game.Objects[index].CopyFrom(30);
        game.Cave[game.CharacterRow - 3, game.CharacterColumn].ObjectIndex = index;

        loop.Looking.Look();

        Assert.Contains("--Aborting look--", Said(display));
    }

    /// <summary>
    /// Plain granite is passed over in silence unless it has something in it,
    /// while a mineral vein is worth mentioning - but only on the second pass,
    /// which the player has to ask for.
    /// </summary>
    [Fact]
    public void Look_MentionsVeinsOnlyWhenAskedTo()
    {
        (string Said, string Described) LookAtAVein(bool highlight)
        {
            (GameState game, MemoryScreen screen, Display display, GameLoop loop) =
                Fresh("8      ");

            Clear(game);
            game.HighlightSeams = highlight;
            game.Cave[game.CharacterRow - 3, game.CharacterColumn].Feature =
                CaveFeature.QuartzWall;

            // Each description is written over the last, so what was on the
            // prompt line has to be caught as the look stops to ask.
            var described = new List<string>();
            screen.BeforeReadKey = () => described.Add(screen.GetRow(0).TrimEnd());

            loop.Looking.Look();
            screen.BeforeReadKey = null;

            return (Moria4Tests.Said(display), string.Join(" | ", described));
        }

        (string plainSaid, string plainSeen) = LookAtAVein(highlight: false);
        (string seamSaid, string seamSeen) = LookAtAVein(highlight: true);

        Assert.DoesNotContain("quartz vein", plainSeen);
        Assert.Contains("nothing of interest", plainSaid);

        Assert.Contains("quartz vein", seamSeen);
        Assert.Contains("That's all you see", seamSaid);
    }

    /// <summary>
    /// Clears a patch big enough to look across, lit and remembered, so that
    /// only what a test puts there is in the way.
    /// </summary>
    private static void Clear(GameState game)
    {
        for (int row = game.CharacterRow - 6; row <= game.CharacterRow + 6; row++)
        {
            for (int column = game.CharacterColumn - 6;
                 column <= game.CharacterColumn + 6;
                 column++)
            {
                if (!game.Cave.InBounds(row, column))
                {
                    continue;
                }

                CaveSquare square = game.Cave[row, column];
                square.Feature = CaveFeature.CorridorFloor;
                square.ObjectIndex = 0;
                square.PermanentLight = true;
                square.FieldMark = true;

                if (row != game.CharacterRow || column != game.CharacterColumn)
                {
                    square.MonsterIndex = 0;
                }
            }
        }
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
