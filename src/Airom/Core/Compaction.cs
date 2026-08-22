// Ported from compact_objects() and compact_monsters() in Umoria 5.6
// source/misc1.c, together with the popt() and popm() calls that reach them.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using Airom.Data;

namespace Airom.Core;

/// <summary>
/// Making room when a level holds all it can.
///
/// A level may hold 175 objects and 125 monsters, and a busy one reaches those
/// numbers: breeders multiply, summoning fills a room, and everything a player
/// drops stays where it fell. When the list is full the game does not stop -
/// it throws away what is furthest from the player and out of their sight,
/// starting sixty-six squares out and working inwards until something gives.
///
/// The two are not symmetrical. Objects always yield eventually, since the
/// search closes in until it finds something to delete. Monsters may not: the
/// Balrog is never compacted away, and neither is whatever the monster loop is
/// part way through, so a level packed with those simply refuses the new
/// arrival - which is what a monster failing to breed looks like from inside.
/// </summary>
public sealed class Compaction
{
    private readonly GameState _game;
    private readonly Display? _display;
    private readonly Lighting? _lighting;

    public Compaction(GameState game, Display? display, Lighting? lighting)
    {
        ArgumentNullException.ThrowIfNull(game);

        _game = game;
        _display = display;
        _lighting = lighting;
    }

    /// <summary>
    /// How far out the search starts, in squares. Sixty-six is the width of the
    /// visible map, so the first pass only ever considers what is off-screen.
    /// </summary>
    private const int StartingDistance = 66;

    /// <summary>How much closer each pass comes when the last one found nothing.</summary>
    private const int Step = 6;

    /// <summary>
    /// Attaches these to a game's lists, so anything that claims a slot gets
    /// compaction for free. Mirrors popt() and popm() calling out to them.
    /// </summary>
    public void Attach()
    {
        _game.Objects.Compactor = CompactObjects;
        _game.Monsters.Compactor = CompactMonsters;
    }

    /// <summary>
    /// Throws away distant objects until at least one is gone. Mirrors
    /// compact_objects().
    ///
    /// What goes is decided by a roll per object, weighted by what it is: a
    /// visible trap is likelier to vanish than an ordinary item, a secret door
    /// less likely, and stairs and shop doors never go at all - losing those
    /// would strand the player or unmake the town.
    /// </summary>
    public void CompactObjects()
    {
        _display?.MessagePrint("Compacting objects...");

        int deleted = 0;
        int distance = StartingDistance;

        do
        {
            for (int row = 0; row < _game.Cave.Height; row++)
            {
                for (int column = 0; column < _game.Cave.Width; column++)
                {
                    CaveSquare square = _game.Cave[row, column];

                    if (square.ObjectIndex == 0
                        || Cave.Distance(row, column, _game.CharacterRow, _game.CharacterColumn)
                            <= distance)
                    {
                        continue;
                    }

                    int chance = _game.Objects[square.ObjectIndex].TVal switch
                    {
                        ItemCategory.VisibleTrap => 15,
                        ItemCategory.InvisibleTrap => 5,
                        ItemCategory.Rubble => 5,
                        ItemCategory.OpenDoor => 5,
                        ItemCategory.ClosedDoor => 5,
                        ItemCategory.UpStair => 0,
                        ItemCategory.DownStair => 0,
                        ItemCategory.StoreDoor => 0,
                        ItemCategory.SecretDoor => 3,
                        _ => 10,
                    };

                    if (_game.Rng.RandInt(100) <= chance)
                    {
                        DeleteObject(row, column);
                        deleted++;
                    }
                }
            }

            if (deleted == 0)
            {
                distance -= Step;
            }
        }
        while (deleted <= 0);

        // Anything deleted from inside the visible map has left a hole in it.
        if (distance < StartingDistance)
        {
            _display?.PrintMap();
        }
    }

    /// <summary>
    /// Throws away distant monsters. Mirrors compact_monsters().
    /// </summary>
    /// <returns>
    /// False when nothing could be spared, which is how a monster fails to
    /// arrive rather than the game failing.
    /// </returns>
    public bool CompactMonsters()
    {
        _display?.MessagePrint("Compacting monsters...");

        int distance = StartingDistance;
        bool deletedAny = false;

        do
        {
            // Backwards, because deleting packs the list by moving the last
            // monster down into the hole.
            for (int i = _game.Monsters.Count - 1; i >= MonsterPool.FirstIndex; i--)
            {
                Monster monster = _game.Monsters[i];

                if (distance >= monster.DistanceToPlayer || _game.Rng.RandInt(3) != 1)
                {
                    continue;
                }

                // Never compact away the Balrog: the game cannot be won
                // without something to win it against.
                if (GameTables.CreatureList[monster.CreatureIndex].WinsGameWhenKilled)
                {
                    continue;
                }

                if (_game.Monsters.ScanIndex < i)
                {
                    _game.Monsters.Delete(i, _game.Cave, _lighting);
                    deletedAny = true;
                }
                else
                {
                    // The monster loop is part way through this one. Taking it
                    // off the map is safe; closing the hole under the loop is
                    // not, so the record stays until the sweep is over - which
                    // is also why this does not count as having made room.
                    _game.Monsters.MarkDead(i, _game.Cave, _lighting);
                }
            }

            if (!deletedAny)
            {
                distance -= Step;

                if (distance < 0)
                {
                    return false;
                }
            }
        }
        while (!deletedAny);

        return true;
    }

    /// <summary>
    /// Takes one object off the level. Mirrors delete_object(), less the
    /// question of whether the player saw it go, which nothing here asks.
    /// </summary>
    private void DeleteObject(int row, int column)
    {
        CaveSquare square = _game.Cave[row, column];

        // A square that was blocked by what stood on it becomes ordinary
        // corridor again.
        if (square.Feature == CaveFeature.BlockedFloor)
        {
            square.Feature = CaveFeature.CorridorFloor;
        }

        _game.Objects.Release(square.ObjectIndex, _game.Cave);
        square.ObjectIndex = 0;
        square.FieldMark = false;

        _lighting?.LightSpot(row, column);
    }
}
