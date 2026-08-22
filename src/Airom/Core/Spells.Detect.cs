// Ported from the detection and lighting half of Umoria 5.6 source/spells.c.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using Airom.Data;

namespace Airom.Core;

public partial class Spells
{
    /// <summary>
    /// Every detection spell works the same way: sweep the panel, mark what
    /// matches, and report whether anything was found. Only what is on screen is
    /// ever found, which is why detection is worth casting twice from different
    /// places.
    /// </summary>
    private bool DetectOnPanel(Func<CaveSquare, bool> matches)
    {
        bool found = false;

        for (int row = _display.Panel.RowMin; row <= _display.Panel.RowMax; row++)
        {
            for (int column = _display.Panel.ColumnMin;
                 column <= _display.Panel.ColumnMax;
                 column++)
            {
                if (matches(Cave[row, column]))
                {
                    found = true;
                }
            }
        }

        return found;
    }

    private bool IsSeen(int row, int column)
    {
        CaveSquare square = Cave[row, column];
        return square.PermanentLight || square.TemporaryLight || square.FieldMark;
    }

    /// <summary>Finds gold on the panel. Mirrors detect_treasure().</summary>
    public bool DetectTreasure()
    {
        bool found = false;

        for (int row = _display.Panel.RowMin; row <= _display.Panel.RowMax; row++)
        {
            for (int column = _display.Panel.ColumnMin;
                 column <= _display.Panel.ColumnMax;
                 column++)
            {
                CaveSquare square = Cave[row, column];

                if (square.ObjectIndex != 0
                    && _game.Objects[square.ObjectIndex].TVal == ItemCategory.Gold
                    && !IsSeen(row, column))
                {
                    square.FieldMark = true;
                    _loop.Lighting.LightSpot(row, column);
                    found = true;
                }
            }
        }

        return found;
    }

    /// <summary>Finds objects on the panel. Mirrors detect_object().</summary>
    public bool DetectObject()
    {
        bool found = false;

        for (int row = _display.Panel.RowMin; row <= _display.Panel.RowMax; row++)
        {
            for (int column = _display.Panel.ColumnMin;
                 column <= _display.Panel.ColumnMax;
                 column++)
            {
                CaveSquare square = Cave[row, column];

                if (square.ObjectIndex != 0
                    && _game.Objects[square.ObjectIndex].TVal < ItemCategory.MaxObject
                    && !IsSeen(row, column))
                {
                    square.FieldMark = true;
                    _loop.Lighting.LightSpot(row, column);
                    found = true;
                }
            }
        }

        return found;
    }

    /// <summary>
    /// Finds traps on the panel, and unlocks what the player knows about a
    /// chest. Mirrors detect_trap().
    /// </summary>
    public bool DetectTrap()
    {
        bool found = false;

        for (int row = _display.Panel.RowMin; row <= _display.Panel.RowMax; row++)
        {
            for (int column = _display.Panel.ColumnMin;
                 column <= _display.Panel.ColumnMax;
                 column++)
            {
                CaveSquare square = Cave[row, column];

                if (square.ObjectIndex == 0)
                {
                    continue;
                }

                InvenType item = _game.Objects[square.ObjectIndex];

                if (item.TVal == ItemCategory.InvisibleTrap)
                {
                    square.FieldMark = true;
                    _loop.Movement.ChangeTrap(row, column);
                    found = true;
                }
                else if (item.TVal == ItemCategory.Chest)
                {
                    _game.Knowledge.LearnEnchantment(item);
                }
            }
        }

        return found;
    }

    /// <summary>
    /// Finds secret doors and staircases on the panel. Mirrors detect_sdoor().
    /// </summary>
    public bool DetectSecretDoors()
    {
        bool found = false;

        for (int row = _display.Panel.RowMin; row <= _display.Panel.RowMax; row++)
        {
            for (int column = _display.Panel.ColumnMin;
                 column <= _display.Panel.ColumnMax;
                 column++)
            {
                CaveSquare square = Cave[row, column];

                if (square.ObjectIndex == 0)
                {
                    continue;
                }

                InvenType item = _game.Objects[square.ObjectIndex];

                if (item.TVal == ItemCategory.SecretDoor)
                {
                    square.FieldMark = true;
                    _loop.Movement.ChangeTrap(row, column);
                    found = true;
                }
                else if ((item.TVal == ItemCategory.UpStair
                          || item.TVal == ItemCategory.DownStair)
                         && !square.FieldMark)
                {
                    square.FieldMark = true;
                    _loop.Lighting.LightSpot(row, column);
                    found = true;
                }
            }
        }

        return found;
    }

    /// <summary>
    /// Shows the invisible monsters on the panel. Mirrors detect_invisible().
    ///
    /// They are drawn and then unlit again by relighting the level, so what the
    /// player sees is a glimpse rather than a lasting view.
    /// </summary>
    public bool DetectInvisibleMonsters() =>
        RevealMonsters(invisible: true, "You sense the presence of invisible creatures!");

    /// <summary>Shows the ordinary monsters on the panel. Mirrors detect_monsters().</summary>
    public bool DetectMonsters() =>
        RevealMonsters(invisible: false, "You sense the presence of monsters!");

    private bool RevealMonsters(bool invisible, string message)
    {
        bool found = false;

        for (int i = _game.Monsters.Count - 1; i >= MonsterPool.FirstIndex; i--)
        {
            Monster monster = _game.Monsters[i];
            CreatureType creature = GameTables.CreatureList[monster.CreatureIndex];

            bool isInvisible = (creature.MoveFlags & CreatureMove.Invisible) != 0;

            if (!_display.Panel.Contains(monster.Row, monster.Column)
                || isInvisible != invisible)
            {
                continue;
            }

            monster.Visible = true;

            // Drawn from the table rather than through the usual symbol, so this
            // works even while hallucinating.
            _display.PrintAt(creature.DisplayChar, monster.Row, monster.Column);

            if (invisible)
            {
                _game.Memories[monster.CreatureIndex].Move |= CreatureMove.Invisible;
            }

            found = true;
        }

        if (found)
        {
            _display.MessagePrint(message);
            _display.MessagePrint(null);

            // Unlight everything just lit: the glimpse is over.
            _loop.LightMonsters();
        }

        return found;
    }

    /// <summary>Finds the evil on the panel. Mirrors detect_evil().</summary>
    public bool DetectEvil()
    {
        bool found = false;

        for (int i = _game.Monsters.Count - 1; i >= MonsterPool.FirstIndex; i--)
        {
            Monster monster = _game.Monsters[i];
            CreatureType creature = GameTables.CreatureList[monster.CreatureIndex];

            if (_display.Panel.Contains(monster.Row, monster.Column)
                && (creature.DefenseFlags & CreatureDefense.Evil) != 0)
            {
                monster.Visible = true;
                _display.PrintAt(creature.DisplayChar, monster.Row, monster.Column);
                found = true;
            }
        }

        if (found)
        {
            _display.MessagePrint("You sense the presence of evil!");
            _display.MessagePrint(null);
            _loop.LightMonsters();
        }

        return found;
    }

    /// <summary>
    /// Lights the area around a point. Mirrors light_area().
    ///
    /// The immediate squares are always lit, whatever else happens, because the
    /// player may be standing in a doorway or beside a blasted room where the
    /// room lighting alone would leave a hole.
    /// </summary>
    public bool LightArea(int row, int column)
    {
        if (Player.Blind < 1)
        {
            _display.MessagePrint("You are surrounded by a white light.");
        }

        if (Cave[row, column].LitRoom && _game.DungeonLevel > 0)
        {
            _loop.Lighting.LightRoom(row, column);
        }

        for (int y = row - 1; y <= row + 1; y++)
        {
            for (int x = column - 1; x <= column + 1; x++)
            {
                Cave[y, x].PermanentLight = true;
                _loop.Lighting.LightSpot(y, x);
            }
        }

        return true;
    }

    /// <summary>
    /// Puts the lights out. Mirrors unlight_area().
    ///
    /// In a room the whole block goes dark and its floor is demoted to the unlit
    /// kind; in a corridor only what the player lit themselves is undone.
    /// </summary>
    public bool UnlightArea(int row, int column)
    {
        bool darkened = false;

        if (Cave[row, column].LitRoom && _game.DungeonLevel > 0)
        {
            int blockHeight = Panel.BlockRows / 2;
            int blockWidth = Panel.BlockColumns / 2;

            int startRow = (row / blockHeight * blockHeight) + 1;
            int startColumn = (column / blockWidth * blockWidth) + 1;

            for (int y = startRow; y <= startRow + blockHeight - 1; y++)
            {
                for (int x = startColumn; x <= startColumn + blockWidth - 1; x++)
                {
                    // The block is worked out from the panel rather than from
                    // the grid, so its last row and column can lie one past the
                    // end of a level. The original reads them anyway; here they
                    // are skipped, which is the one place this file knowingly
                    // departs from it.
                    if (!Cave.InBounds(y, x))
                    {
                        continue;
                    }

                    CaveSquare square = Cave[y, x];

                    if (square.LitRoom && square.Feature <= CaveFeature.MaxCaveFloor)
                    {
                        square.PermanentLight = false;
                        square.Feature = CaveFeature.DarkFloor;
                        _loop.Lighting.LightSpot(y, x);

                        if (!IsSeen(y, x))
                        {
                            darkened = true;
                        }
                    }
                }
            }
        }
        else
        {
            for (int y = row - 1; y <= row + 1; y++)
            {
                for (int x = column - 1; x <= column + 1; x++)
                {
                    CaveSquare square = Cave[y, x];

                    // The permanent light may have been put there by a wand
                    // rather than by the level.
                    if (square.Feature == CaveFeature.CorridorFloor && square.PermanentLight)
                    {
                        square.PermanentLight = false;
                        darkened = true;
                    }
                }
            }
        }

        if (darkened && Player.Blind < 1)
        {
            _display.MessagePrint("Darkness surrounds you.");
        }

        return darkened;
    }

    /// <summary>
    /// Maps the surrounding country. Mirrors map_area().
    ///
    /// The area mapped is the panel plus a ragged margin, rolled rather than
    /// fixed, so two castings from the same spot do not reveal quite the same
    /// ground.
    /// </summary>
    public void MapArea()
    {
        int top = _display.Panel.RowMin - Rng.RandInt(10);
        int bottom = _display.Panel.RowMax + Rng.RandInt(10);
        int left = _display.Panel.ColumnMin - Rng.RandInt(20);
        int right = _display.Panel.ColumnMax + Rng.RandInt(20);

        for (int row = top; row <= bottom; row++)
        {
            for (int column = left; column <= right; column++)
            {
                if (!Cave.InBounds(row, column)
                    || Cave[row, column].Feature > CaveFeature.MaxCaveFloor)
                {
                    continue;
                }

                // Floor found: remember the walls around it, and anything
                // standing on them worth drawing.
                for (int y = row - 1; y <= row + 1; y++)
                {
                    for (int x = column - 1; x <= column + 1; x++)
                    {
                        CaveSquare square = Cave[y, x];

                        if (square.Feature >= CaveFeature.MinCaveWall)
                        {
                            square.PermanentLight = true;
                        }
                        else if (square.ObjectIndex != 0
                                 && _game.Objects[square.ObjectIndex].TVal
                                    >= ItemCategory.MinVisible
                                 && _game.Objects[square.ObjectIndex].TVal
                                    <= ItemCategory.MaxVisible)
                        {
                            square.FieldMark = true;
                        }
                    }
                }
            }
        }

        _display.PrintMap();
    }

    /// <summary>
    /// Lights a line, which some creatures cannot bear. Mirrors light_line().
    ///
    /// The move comes last rather than first, so the square the caster is
    /// standing on is lit as well.
    /// </summary>
    public void LightLine(int direction, int row, int column)
    {
        int distance = -1;
        bool finished = false;

        do
        {
            distance++;
            CaveSquare square = Cave[row, column];

            if (distance > BoltRange || square.Feature >= CaveFeature.MinClosedSpace)
            {
                finished = true;
            }
            else
            {
                if (!square.PermanentLight && !square.TemporaryLight)
                {
                    // Set first, so that the redraw has something to draw.
                    square.PermanentLight = true;

                    if (square.Feature == CaveFeature.LightFloor)
                    {
                        if (_display.Panel.Contains(row, column))
                        {
                            _loop.Lighting.LightRoom(row, column);
                        }
                    }
                    else
                    {
                        _loop.Lighting.LightSpot(row, column);
                    }
                }

                square.PermanentLight = true;

                if (square.MonsterIndex > 1)
                {
                    BurnWithLight(square.MonsterIndex);
                }
            }

            Cave.Move(direction, ref row, ref column);
        }
        while (!finished);
    }

    private void BurnWithLight(int index)
    {
        Monster monster = _game.Monsters[index];
        CreatureType creature = GameTables.CreatureList[monster.CreatureIndex];

        _loop.MonsterAi.UpdateMonster(index);
        string name = MonsterName(monster);

        if ((creature.DefenseFlags & CreatureDefense.HurtByLight) == 0)
        {
            return;
        }

        if (monster.Visible)
        {
            _game.Memories[monster.CreatureIndex].Defense |= CreatureDefense.HurtByLight;
        }

        if (_loop.Combat.MonsterTakeHit(index, Rng.DamRoll(2, 8)) >= 0)
        {
            _display.MessagePrint(name + " shrivels away in the light!");
            _loop.Levelling.PrintExperience();
        }
        else
        {
            _display.MessagePrint(name + " cringes from the light!");
        }
    }

    /// <summary>Lights every direction at once. Mirrors starlite().</summary>
    public void Starlight(int row, int column)
    {
        if (Player.Blind < 1)
        {
            _display.MessagePrint(
                "The end of the staff bursts into a blue shimmering light.");
        }

        for (int direction = 1; direction <= 9; direction++)
        {
            if (direction != 5)
            {
                LightLine(direction, row, column);
            }
        }
    }

    /// <summary>
    /// Disarms everything along a line. Mirrors disarm_all().
    ///
    /// A locked or jammed door is merely closed afterwards, which is the same
    /// idea as a trap being removed: what made it awkward is gone.
    /// </summary>
    public bool DisarmAll(int direction, int row, int column)
    {
        bool disarmed = false;
        int distance = -1;
        CaveSquare square;

        do
        {
            // The move comes last, in case the player is standing on a trap.
            distance++;
            square = Cave[row, column];

            if (square.ObjectIndex != 0)
            {
                InvenType item = _game.Objects[square.ObjectIndex];

                if (item.TVal == ItemCategory.InvisibleTrap
                    || item.TVal == ItemCategory.VisibleTrap)
                {
                    if (_loop.Movement.DeleteObject(row, column))
                    {
                        disarmed = true;
                    }
                }
                else if (item.TVal == ItemCategory.ClosedDoor)
                {
                    item.P1 = 0;
                }
                else if (item.TVal == ItemCategory.SecretDoor)
                {
                    square.FieldMark = true;
                    _loop.Movement.ChangeTrap(row, column);
                    disarmed = true;
                }
                else if (item.TVal == ItemCategory.Chest && item.Flags != 0)
                {
                    _display.MessagePrint("Click!");
                    item.Flags &= ~(ChestFlags.Trapped | ChestFlags.Locked);
                    item.SpecialName = SpecialName.Unlocked;
                    _game.Knowledge.LearnEnchantment(item);
                    disarmed = true;
                }
            }

            Cave.Move(direction, ref row, ref column);
        }
        while (distance <= BoltRange && square.Feature <= CaveFeature.MaxOpenSpace);

        return disarmed;
    }

    /// <summary>
    /// Rings the player with traps. Mirrors trap_creation().
    ///
    /// Never underfoot: a trap under the player leads to absurdities, like
    /// falling through a trap door while trying to rest and landing under the
    /// rock of a second trap.
    /// </summary>
    public bool TrapCreation()
    {
        var generator = new DungeonGenerator(_game, _display);

        for (int row = _game.CharacterRow - 1; row <= _game.CharacterRow + 1; row++)
        {
            for (int column = _game.CharacterColumn - 1;
                 column <= _game.CharacterColumn + 1;
                 column++)
            {
                if (row == _game.CharacterRow && column == _game.CharacterColumn)
                {
                    continue;
                }

                CaveSquare square = Cave[row, column];

                if (square.Feature > CaveFeature.MaxCaveFloor)
                {
                    continue;
                }

                if (square.ObjectIndex != 0)
                {
                    _loop.Movement.DeleteObject(row, column);
                }

                generator.PlaceTrap(row, column, Rng.RandInt(TrapKinds) - 1);

                // No experience for the traps the player made themselves.
                _game.Objects[square.ObjectIndex].P1 = 0;

                // An open pit shows at once, so it is drawn.
                _loop.Lighting.LightSpot(row, column);
            }
        }

        return true;
    }

    /// <summary>How many traps there are in the table. Umoria's MAX_TRAP.</summary>
    public const int TrapKinds = 18;

    /// <summary>Rings the player with doors. Mirrors door_creation().</summary>
    public bool DoorCreation()
    {
        bool made = false;

        for (int row = _game.CharacterRow - 1; row <= _game.CharacterRow + 1; row++)
        {
            for (int column = _game.CharacterColumn - 1;
                 column <= _game.CharacterColumn + 1;
                 column++)
            {
                if (row == _game.CharacterRow && column == _game.CharacterColumn)
                {
                    continue;
                }

                CaveSquare square = Cave[row, column];

                if (square.Feature > CaveFeature.MaxCaveFloor)
                {
                    continue;
                }

                made = true;

                if (square.ObjectIndex != 0)
                {
                    _loop.Movement.DeleteObject(row, column);
                }

                int slot = _game.Objects.Allocate();
                square.Feature = CaveFeature.BlockedFloor;
                square.ObjectIndex = slot;
                _game.Objects[slot].CopyFrom(ClosedDoorObject);
                _loop.Lighting.LightSpot(row, column);
            }
        }

        return made;
    }

    /// <summary>The closed door that door creation makes. Umoria's OBJ_CLOSED_DOOR.</summary>
    private const int ClosedDoorObject = 368;

    /// <summary>
    /// Destroys the doors and traps beside the player. Mirrors td_destroy().
    /// </summary>
    public bool DestroyDoorsAndTraps()
    {
        bool destroyed = false;

        for (int row = _game.CharacterRow - 1; row <= _game.CharacterRow + 1; row++)
        {
            for (int column = _game.CharacterColumn - 1;
                 column <= _game.CharacterColumn + 1;
                 column++)
            {
                CaveSquare square = Cave[row, column];

                if (square.ObjectIndex == 0)
                {
                    continue;
                }

                InvenType item = _game.Objects[square.ObjectIndex];

                bool isDoorOrTrap =
                    (item.TVal >= ItemCategory.InvisibleTrap
                     && item.TVal <= ItemCategory.ClosedDoor
                     && item.TVal != ItemCategory.Rubble)
                    || item.TVal == ItemCategory.SecretDoor;

                if (isDoorOrTrap)
                {
                    if (_loop.Movement.DeleteObject(row, column))
                    {
                        destroyed = true;
                    }
                }
                else if (item.TVal == ItemCategory.Chest && item.Flags != 0)
                {
                    // The chest is disarmed and unlocked rather than destroyed.
                    item.Flags &= ~(ChestFlags.Trapped | ChestFlags.Locked);
                    item.SpecialName = SpecialName.Unlocked;
                    _display.MessagePrint("You have disarmed the chest.");
                    _game.Knowledge.LearnEnchantment(item);
                    destroyed = true;
                }
            }
        }

        return destroyed;
    }
}
