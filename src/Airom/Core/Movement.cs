// Ported from the movement half of Umoria 5.6 source/moria2.c - searching, the
// running algorithm and its commentary - with move_char() from source/moria3.c
// that they drive.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using System.Globalization;
using Airom.Data;

namespace Airom.Core;

/// <summary>
/// Walking, running and searching.
///
/// A step is more than a change of position: it moves the player record, drags
/// the light with it, may light a room, searches for what is hidden nearby, and
/// picks up whatever is underfoot. Running is a step repeated until something
/// worth stopping for appears, which is decided by looking only at the squares
/// that are newly adjacent - never by re-examining the whole corridor.
/// </summary>
public class Movement
{
    /// <summary>
    /// The directions anticlockwise, over two complete cycles, so a step either
    /// side of any direction can be taken without wrapping the index by hand.
    /// Mirrors Umoria's cycle[].
    /// </summary>
    private static readonly int[] Cycle =
        [1, 2, 3, 6, 9, 8, 7, 4, 1, 2, 3, 6, 9, 8, 7, 4, 1];

    /// <summary>Where each direction sits in <see cref="Cycle"/>. Mirrors chome[].</summary>
    private static readonly int[] CycleHome = [-1, 8, 9, 10, 7, -1, 11, 6, 5, 4];

    /// <summary>The closed door a found secret door turns into. Umoria's OBJ_CLOSED_DOOR.</summary>
    private const int ClosedDoorObject = 368;

    private readonly GameState _game;
    private readonly Display _display;
    private readonly Lighting _lighting;
    private readonly GameLoop _loop;

    public Movement(GameState game, Display display, GameLoop loop)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(loop);

        _game = game;
        _display = display;
        _loop = loop;
        _lighting = loop.Lighting;
    }

    private Player Player => _game.Player;

    private Rng Rng => _game.Rng;

    // ------------------------------------------------------------ searching

    /// <summary>
    /// Reveals a trap or a secret door. Mirrors change_trap().
    ///
    /// A found trap becomes a visible one and a found secret door becomes a
    /// closed one, so both go on being ordinary features from then on.
    /// </summary>
    public void ChangeTrap(int row, int column)
    {
        CaveSquare square = _game.Cave[row, column];
        InvenType item = _game.Objects[square.ObjectIndex];

        if (item.TVal == ItemCategory.InvisibleTrap)
        {
            item.TVal = ItemCategory.VisibleTrap;
            _lighting.LightSpot(row, column);
        }
        else if (item.TVal == ItemCategory.SecretDoor)
        {
            item.Index = ClosedDoorObject;
            item.TVal = GameTables.ObjectList[ClosedDoorObject].TVal;
            item.DisplayChar = GameTables.ObjectList[ClosedDoorObject].DisplayChar;
            _lighting.LightSpot(row, column);
        }
    }

    /// <summary>
    /// Looks for what is hidden in the eight squares around one. Mirrors
    /// search().
    ///
    /// Every square is rolled for separately, so a search can find one of two
    /// adjacent traps and miss the other. Confusion, blindness, darkness and
    /// hallucination each cut the chance to a tenth, and they stack - a blind,
    /// confused character is essentially searching by luck.
    /// </summary>
    public void Search(int row, int column, int chance)
    {
        if (Player.Confused > 0)
        {
            chance /= 10;
        }

        if (Player.Blind > 0 || _lighting.NoLight())
        {
            chance /= 10;
        }

        if (Player.Hallucinating > 0)
        {
            chance /= 10;
        }

        for (int y = row - 1; y <= row + 1; y++)
        {
            for (int x = column - 1; x <= column + 1; x++)
            {
                // Always in bounds here: the player never stands on the edge.
                if (Rng.RandInt(100) >= chance)
                {
                    continue;
                }

                CaveSquare square = _game.Cave[y, x];
                if (square.ObjectIndex == 0)
                {
                    continue;
                }

                InvenType item = _game.Objects[square.ObjectIndex];

                if (item.TVal == ItemCategory.InvisibleTrap)
                {
                    _display.MessagePrint("You have found " + DescribeTrap(item));
                    ChangeTrap(y, x);
                    EndFind();
                }
                else if (item.TVal == ItemCategory.SecretDoor)
                {
                    _display.MessagePrint("You have found a secret door.");
                    ChangeTrap(y, x);
                    EndFind();
                }
                else if (item.TVal == ItemCategory.Chest)
                {
                    // The trap bits sit above the treasure bits, so anything
                    // past the first bit means the chest is trapped.
                    if ((item.Flags & ChestFlags.Trapped) > 1)
                    {
                        if ((item.Identification & Identification.Known) == 0)
                        {
                            item.Identification |= Identification.Known;
                            _display.MessagePrint(
                                "You have discovered a trap on the chest!");
                        }
                        else
                        {
                            _display.MessagePrint("The chest is trapped!");
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Names a trap. Mirrors the branch objdes() takes for one: the plain table
    /// name and a full stop, with none of the pluralising or identification the
    /// rest of an item description goes through.
    /// </summary>
    private static string DescribeTrap(InvenType item) =>
        GameTables.ObjectList[item.Index].Name + ".";

    // -------------------------------------------------------------- running
    //
    // The running algorithm, from the original's own commentary:
    //
    // You keep moving until something interesting happens. In an enclosed space
    // you follow corners, which is the usual corridor scheme. In an open space
    // you go straight, but stop before entering enclosed space - which is what
    // reaching a doorway looks like. With enclosed space on one side only, that
    // is, running alongside a wall, you stop if the wall opens out or the open
    // space closes in. Either case is a doorway.
    //
    // What happens depends on what can really be seen: with no light, running
    // along a dark corridor is just like running in a dark room. The same rules
    // therefore work in corridors, rooms, mine tailings and rubble alike.

    /// <summary>Whether there is open space on at least one side. Umoria's find_openarea.</summary>
    private bool _openArea;

    /// <summary>Whether a wall on the left will stop the run when it opens.</summary>
    private bool _breakLeft;

    /// <summary>Whether a wall on the right will stop the run when it opens.</summary>
    private bool _breakRight;

    /// <summary>The direction the run is treated as having come from.</summary>
    private int _previousDirection;

    /// <summary>The direction the run is going.</summary>
    private int _direction;

    /// <summary>Steps taken so far, which is what stops a run going round forever.</summary>
    private int _steps;

    /// <summary>How far a run goes before the player has to stop for breath.</summary>
    public const int MaxRunSteps = 100;

    /// <summary>
    /// Starts a run. Mirrors find_init().
    ///
    /// The two squares either side of the step decide what kind of run this is.
    /// If either side is seen to be closed, that side is closed; if both are, it
    /// is a corridor run rather than an open one.
    /// </summary>
    public void FindInit(int direction)
    {
        int row = _game.CharacterRow;
        int column = _game.CharacterColumn;

        if (!_game.Cave.Move(direction, ref row, ref column))
        {
            _loop.Running = false;
        }
        else
        {
            _direction = direction;
            _loop.Running = true;
            _steps = 1;
            _breakRight = false;
            _breakLeft = false;
            _previousDirection = direction;

            if (Player.Blind < 1)
            {
                int home = CycleHome[direction];
                bool deepLeft = false;
                bool deepRight = false;
                bool shortLeft = false;
                bool shortRight = false;

                if (SeeWall(Cycle[home + 1], _game.CharacterRow, _game.CharacterColumn))
                {
                    _breakLeft = true;
                    shortLeft = true;
                }
                else if (SeeWall(Cycle[home + 1], row, column))
                {
                    _breakLeft = true;
                    deepLeft = true;
                }

                if (SeeWall(Cycle[home - 1], _game.CharacterRow, _game.CharacterColumn))
                {
                    _breakRight = true;
                    shortRight = true;
                }
                else if (SeeWall(Cycle[home - 1], row, column))
                {
                    _breakRight = true;
                    deepRight = true;
                }

                if (_breakLeft && _breakRight)
                {
                    _openArea = false;

                    if ((direction & 1) != 0)
                    {
                        // A hack to allow angled corridor entry: the runner moves
                        // diagonally but remembers going straight into the gap,
                        // so the tail of the other entry is not seen as a choice
                        // on the next step.
                        if (deepLeft && !deepRight)
                        {
                            _previousDirection = Cycle[home - 1];
                        }
                        else if (deepRight && !deepLeft)
                        {
                            _previousDirection = Cycle[home + 1];
                        }
                    }
                    else if (SeeWall(Cycle[home], row, column))
                    {
                        // A wall two squares ahead in what looks like a corridor:
                        // force the turn into the side corridor.
                        if (shortLeft && !shortRight)
                        {
                            _previousDirection = Cycle[home - 2];
                        }
                        else if (shortRight && !shortLeft)
                        {
                            _previousDirection = Cycle[home + 2];
                        }
                    }
                }
                else
                {
                    _openArea = true;
                }
            }
        }

        // The player symbol has to be erased here: while running with the light
        // off and without being drawn, nothing erases the square left behind, so
        // the first step of a run would leave an "@" sitting where it started.
        if (!_game.TemporaryLightOn && !_game.ShowSelfWhileRunning)
        {
            _display.PrintAt(
                _display.SymbolAt(_game.CharacterRow, _game.CharacterColumn),
                _game.CharacterRow,
                _game.CharacterColumn);
        }

        MoveChar(direction, pickUp: true);

        if (!_loop.Running)
        {
            _display.CommandCount = 0;
        }
    }

    /// <summary>
    /// Takes one more step of a run. Mirrors find_run().
    ///
    /// A hundred steps is as far as anyone runs at once, which stops a run round
    /// a loop of corridor from going on forever.
    /// </summary>
    public void FindRun()
    {
        _steps++;
        if (_steps > MaxRunSteps + 1)
        {
            _display.MessagePrint("You stop running to catch your breath.");
            EndFind();
        }
        else
        {
            MoveChar(_direction, pickUp: true);
        }
    }

    /// <summary>
    /// Stops a run and puts the light back. Mirrors end_find().
    /// </summary>
    public void EndFind()
    {
        if (!_loop.Running)
        {
            return;
        }

        _loop.Running = false;
        _steps = 0;
        _lighting.MoveLight(
            _game.CharacterRow, _game.CharacterColumn,
            _game.CharacterRow, _game.CharacterColumn);
    }

    /// <summary>
    /// Whether a wall is seen in a direction. Mirrors see_wall().
    ///
    /// The edge of the map counts as a wall, and so does anything that is drawn
    /// like one - which is the point: the runner reacts to what the player can
    /// see, not to what is really there.
    /// </summary>
    private bool SeeWall(int direction, int row, int column)
    {
        if (!_game.Cave.Move(direction, ref row, ref column))
        {
            return true;
        }

        char symbol = _display.SymbolAt(row, column);
        return symbol is '#' or '%';
    }

    /// <summary>
    /// Whether nothing at all is seen in a direction. Mirrors see_nothing().
    /// </summary>
    private bool SeeNothing(int direction, int row, int column)
    {
        if (!_game.Cave.Move(direction, ref row, ref column))
        {
            return false;
        }

        return _display.SymbolAt(row, column) == ' ';
    }

    /// <summary>
    /// Decides where the run goes next, or whether it stops at all. Mirrors
    /// area_affect().
    ///
    /// Only the newly adjacent squares are examined - three after a straight
    /// step, five after a diagonal one - which is what makes a long run cheap.
    /// The run stops when one of them holds something worth seeing, when the
    /// walls it was following open out, or when there is more than one way to go.
    /// </summary>
    public void AreaAffect(int direction, int row, int column)
    {
        if (Player.Blind >= 1)
        {
            return;
        }

        int option = 0;
        int option2 = 0;
        int checkDirection = 0;

        direction = _previousDirection;
        int max = (direction & 1) + 1;

        for (int i = -max; i <= max; i++)
        {
            int newDirection = Cycle[CycleHome[direction] + i];
            int y = row;
            int x = column;

            if (!_game.Cave.Move(newDirection, ref y, ref x))
            {
                continue;
            }

            CaveSquare square = _game.Cave[y, x];
            bool unseen;

            if (_game.PlayerLight || square.TemporaryLight || square.PermanentLight
                || square.FieldMark)
            {
                if (square.ObjectIndex != 0)
                {
                    int category = _game.Objects[square.ObjectIndex].TVal;
                    if (category != ItemCategory.InvisibleTrap
                        && category != ItemCategory.SecretDoor
                        && (category != ItemCategory.OpenDoor || !_game.IgnoreDoorsWhileRunning))
                    {
                        EndFind();
                        return;
                    }
                }

                // A visible monster stops the run. It is visible by definition
                // here, because update_mon() treats running as a special case.
                if (square.MonsterIndex > 1 && _game.Monsters[square.MonsterIndex].Visible)
                {
                    EndFind();
                    return;
                }

                unseen = false;
            }
            else
            {
                // The square has not been seen. Treat it as open.
                unseen = true;
            }

            if (square.Feature <= CaveFeature.MaxOpenSpace || unseen)
            {
                if (_openArea)
                {
                    // Has the wall being followed opened out?
                    if (i < 0)
                    {
                        if (_breakRight)
                        {
                            EndFind();
                            return;
                        }
                    }
                    else if (i > 0)
                    {
                        if (_breakLeft)
                        {
                            EndFind();
                            return;
                        }
                    }
                }
                else if (option == 0)
                {
                    // The first new direction.
                    option = newDirection;
                }
                else if (option2 != 0)
                {
                    // Three new directions: stop.
                    EndFind();
                    return;
                }
                else if (option != Cycle[CycleHome[direction] + i - 1])
                {
                    // Not adjacent to the previous one: stop.
                    EndFind();
                    return;
                }
                else
                {
                    // Two adjacent choices. Make option2 the diagonal, and
                    // remember the other diagonal beside the first option.
                    if ((newDirection & 1) == 1)
                    {
                        checkDirection = Cycle[CycleHome[direction] + i - 2];
                        option2 = newDirection;
                    }
                    else
                    {
                        checkDirection = Cycle[CycleHome[direction] + i + 1];
                        option2 = option;
                        option = newDirection;
                    }
                }
            }
            else if (_openArea)
            {
                // An obstacle, in the open. Stop if this side was open before.
                if (i < 0)
                {
                    if (_breakLeft)
                    {
                        EndFind();
                        return;
                    }

                    _breakRight = true;
                }
                else if (i > 0)
                {
                    if (_breakRight)
                    {
                        EndFind();
                        return;
                    }

                    _breakLeft = true;
                }
            }
        }

        if (_openArea)
        {
            return;
        }

        if (option2 == 0 || (_game.ExamineCorners && !_game.CutCorners))
        {
            // One option, or two with corners always examined and never cut, so
            // the straight one is taken.
            if (option != 0)
            {
                _direction = option;
            }

            _previousDirection = option2 == 0 ? option : option2;
            return;
        }

        // Two options.
        int aheadRow = row;
        int aheadColumn = column;
        _game.Cave.Move(option, ref aheadRow, ref aheadColumn);

        if (!SeeWall(option, aheadRow, aheadColumn)
            || !SeeWall(checkDirection, aheadRow, aheadColumn))
        {
            // Not seen to be closed off: this could be a corner, or it could be
            // an intersection or the mouth of a room.
            if (_game.ExamineCorners
                && SeeNothing(option, aheadRow, aheadColumn)
                && SeeNothing(option2, aheadRow, aheadColumn))
            {
                // Nothing visible ahead or the way we are turning, so assume a
                // corner.
                _direction = option;
                _previousDirection = option2;
            }
            else
            {
                EndFind();
            }
        }
        else if (_game.CutCorners)
        {
            // The corner is seen to be enclosed, so cut it.
            _direction = option2;
            _previousDirection = option2;
        }
        else
        {
            // Enclosed, but go the long way round deliberately.
            _direction = option;
            _previousDirection = option2;
        }
    }

    // ------------------------------------------------------------ walking

    /// <summary>
    /// Takes one step. Mirrors move_char().
    ///
    /// Walking into a monster is an attack; walking into a wall wastes no turn,
    /// which is why tunnelling is a command of its own rather than something a
    /// blocked step falls back on. Confusion sends the step somewhere else three
    /// times in four - but never when standing still, so waiting is always safe.
    /// </summary>
    public void MoveChar(int direction, bool pickUp)
    {
        // 75% random movement while confused, and never random when sitting
        // still.
        if (Player.Confused > 0 && Rng.RandInt(4) > 1 && direction != 5)
        {
            direction = Rng.RandInt(9);
            EndFind();
        }

        int y = _game.CharacterRow;
        int x = _game.CharacterColumn;

        if (!_game.Cave.Move(direction, ref y, ref x))
        {
            return;
        }

        CaveSquare target = _game.Cave[y, x];

        // An unlit creature inside a wall is not attacked: moving into a wall is
        // normally a free turn, and attacking one square of rock after another
        // to find an invisible monster should cost something. Tunnelling does.
        bool attacking = target.MonsterIndex >= 2
            && (_game.Monsters[target.MonsterIndex].Visible
                || target.Feature < CaveFeature.MinClosedSpace);

        if (attacking)
        {
            bool wasRunning = _loop.Running;
            EndFind();

            // Running into a monster that was already visible does nothing at
            // all: the run stops, and the step is not spent attacking.
            if (_game.Monsters[target.MonsterIndex].Visible && wasRunning)
            {
                _loop.FreeTurn = true;
            }
            else if (Player.Afraid < 1)
            {
                PlayerAttack(y, x);
            }
            else
            {
                _display.MessagePrint("You are too afraid!");
            }

            return;
        }

        if (target.Feature > CaveFeature.MaxOpenSpace)
        {
            // Blocked. Say why, unless the player is running, in which case the
            // run simply ends.
            if (!_loop.Running && target.ObjectIndex != 0)
            {
                int category = _game.Objects[target.ObjectIndex].TVal;
                if (category == ItemCategory.Rubble)
                {
                    _display.MessagePrint("There is rubble blocking your way.");
                }
                else if (category == ItemCategory.ClosedDoor)
                {
                    _display.MessagePrint("There is a closed door blocking your way.");
                }
            }
            else
            {
                EndFind();
            }

            _loop.FreeTurn = true;
            return;
        }

        int oldRow = _game.CharacterRow;
        int oldColumn = _game.CharacterColumn;
        _game.CharacterRow = y;
        _game.CharacterColumn = x;

        _lighting.MoveRecord(oldRow, oldColumn, y, x);

        if (_display.Panel.Follow(y, x, force: false))
        {
            _display.PrintMap();
        }

        if (_loop.Running)
        {
            AreaAffect(direction, y, x);
        }

        // Searching happens on the way past, whether or not the player asked for
        // it. The frequency can go negative with good rings, hence the guard.
        if (Player.SearchFrequency <= 1
            || Rng.RandInt(Player.SearchFrequency) == 1
            || (Player.Status & PlayerStatus.Searching) != 0)
        {
            Search(y, x, Player.Search);
        }

        // A room of light should be lit, and standing in its doorway lights it
        // too.
        if (target.Feature == CaveFeature.LightFloor)
        {
            if (!target.PermanentLight && Player.Blind == 0)
            {
                _lighting.LightRoom(y, x);
            }
        }
        else if (target.LitRoom && Player.Blind < 1)
        {
            for (int i = y - 1; i <= y + 1; i++)
            {
                for (int j = x - 1; j <= x + 1; j++)
                {
                    CaveSquare beside = _game.Cave[i, j];
                    if (beside.Feature == CaveFeature.LightFloor && !beside.PermanentLight)
                    {
                        _lighting.LightRoom(i, j);
                    }
                }
            }
        }

        _lighting.MoveLight(oldRow, oldColumn, y, x);

        if (target.ObjectIndex == 0)
        {
            return;
        }

        Carry(y, x, pickUp);

        // Stepping on a falling rock trap leaves rubble underfoot, and the
        // player is pushed back the way they came - possibly onto another trap,
        // which then goes off.
        if (_game.Objects[target.ObjectIndex].TVal != ItemCategory.Rubble)
        {
            return;
        }

        _lighting.MoveRecord(y, x, oldRow, oldColumn);
        _lighting.MoveLight(y, x, oldRow, oldColumn);
        _game.CharacterRow = oldRow;
        _game.CharacterColumn = oldColumn;

        CaveSquare back = _game.Cave[oldRow, oldColumn];
        if (back.ObjectIndex != 0)
        {
            int category = _game.Objects[back.ObjectIndex].TVal;
            if (category is ItemCategory.InvisibleTrap or ItemCategory.VisibleTrap
                or ItemCategory.StoreDoor)
            {
                HitTrap(oldRow, oldColumn);
            }
        }
    }

    // ------------------------------------------------------ pending subsystems

    /// <summary>
    /// Picks up, or steps over, whatever is on the floor here. Mirrors carry().
    ///
    /// Gold goes straight into the purse. Anything else is offered, with two
    /// questions the player may have asked for: whether to pick it up at all,
    /// and whether to exceed the weight limit doing so. Stepping onto a trap is
    /// not a pickup at all, and springs it instead.
    /// </summary>
    protected virtual void Carry(int row, int column, bool pickUp)
    {
        CaveSquare square = _game.Cave[row, column];
        InvenType item = _game.Objects[square.ObjectIndex];
        int category = item.TVal;

        if (category > ItemCategory.MaxPickUp)
        {
            if (category is ItemCategory.InvisibleTrap or ItemCategory.VisibleTrap
                or ItemCategory.StoreDoor)
            {
                HitTrap(row, column);
            }

            return;
        }

        EndFind();

        if (category == ItemCategory.Gold)
        {
            Player.Gold += item.Cost;

            string found = "You have found "
                + item.Cost.ToString(CultureInfo.InvariantCulture)
                + " gold pieces worth of " + _game.Names.Describe(item, withArticle: true);

            _display.PrintGold(Player);
            DeleteObject(row, column);
            _display.MessagePrint(found);
            return;
        }

        if (!_game.Inventory.HasRoomFor(item))
        {
            _display.MessagePrint(
                "You can't carry " + _game.Names.Describe(item, withArticle: true));
            return;
        }

        if (pickUp && _game.PromptBeforeCarrying)
        {
            pickUp = _display.GetCheck("Pick up " + AsQuestion(item));
        }

        if (pickUp && !_game.Inventory.CanCarryWithoutSlowing(item))
        {
            pickUp = _display.GetCheck(
                "Exceed your weight limit to pick up " + AsQuestion(item));
        }

        if (!pickUp)
        {
            return;
        }

        int slot = _game.Inventory.Carry(item);

        _display.MessagePrint(
            "You have " + _game.Names.Describe(_game.Inventory[slot], withArticle: true)
            + " (" + (char)(slot + 'a') + ")");

        DeleteObject(row, column);
    }

    /// <summary>
    /// The description with its full stop turned into a question mark, which is
    /// how the original asks about an item without describing it twice.
    /// </summary>
    private string AsQuestion(InvenType item)
    {
        string described = _game.Names.Describe(item, withArticle: true);
        return described.Length == 0 ? described : described[..^1] + "?";
    }

    /// <summary>
    /// Takes an object off the floor. Mirrors delete_object().
    ///
    /// A square that was blocked by what stood on it becomes ordinary corridor
    /// again, which is how clearing rubble opens a way through.
    /// </summary>
    /// <returns>Whether the square is one the player can see.</returns>
    public bool DeleteObject(int row, int column)
    {
        CaveSquare square = _game.Cave[row, column];

        if (square.Feature == CaveFeature.BlockedFloor)
        {
            square.Feature = CaveFeature.CorridorFloor;
        }

        _game.Objects.Release(square.ObjectIndex, _game.Cave);
        square.ObjectIndex = 0;
        square.FieldMark = false;

        _lighting.LightSpot(row, column);

        return square.PermanentLight || square.TemporaryLight || square.FieldMark;
    }

    /// <summary>
    /// Springs a trap. Mirrors hit_trap(). Pending: moria3.c and the spell
    /// effects it leans on.
    /// </summary>
    protected virtual void HitTrap(int row, int column)
    {
    }

    /// <summary>
    /// Attacks whatever is on a square. Mirrors py_attack(). Pending: the combat
    /// half of moria3.c.
    /// </summary>
    protected virtual void PlayerAttack(int row, int column)
    {
    }
}
