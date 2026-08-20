// Ported from the main loop in Umoria 5.6 source/dungeon.c, with the
// interruption helpers from source/moria1.c that it leans on.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using System.Globalization;
using Airom.Data;

namespace Airom.Core;

/// <summary>
/// The turn: everything that happens to the player between one command and the
/// next.
///
/// Umoria runs this as one long loop. A turn burns a little lamp oil and a
/// little food, counts every temporary effect down by one, regenerates a
/// fraction of a hit point, redraws whatever changed, and only then asks for a
/// command. Commands that take no time never reach the bottom of the loop,
/// which is what lets a player check their inventory for free.
/// </summary>
public class GameLoop
{
    /// <summary>Chance per turn of a new monster wandering in. Umoria's MAX_MALLOC_CHANCE.</summary>
    public const int MonsterArrivalChance = 160;

    /// <summary>Below this, the player is warned they are getting hungry.</summary>
    public const int FoodAlert = 2000;

    /// <summary>Below this, hunger starts to weaken them.</summary>
    public const int FoodWeak = 1000;

    /// <summary>Below this, they start fainting.</summary>
    public const int FoodFainting = 300;

    /// <summary>Full. Umoria's PLAYER_FOOD_FULL.</summary>
    public const int FoodFull = 10000;

    /// <summary>Anything past this is wasted. Umoria's PLAYER_FOOD_MAX.</summary>
    public const int FoodMax = 15000;

    // Regeneration rates, as a fraction of the maximum times 65536.
    public const int RegenFainting = 33;
    public const int RegenWeak = 98;
    public const int RegenNormal = 197;

    /// <summary>The least hit point regeneration per turn, times 65536.</summary>
    public const int RegenHitPointBase = 1442;

    /// <summary>The least mana regeneration per turn, times 65536.</summary>
    public const int RegenManaBase = 524;

    /// <summary>The largest value Umoria's signed short counters hold.</summary>
    public const int MaxShort = 32767;

    /// <summary>How far a creature can be seen. Umoria's MAX_SIGHT.</summary>
    public const int MaxSight = 20;

    private readonly GameState _game;
    private readonly Display _display;
    private readonly Lighting _lighting;
    private Movement _movement;
    private Equipment _equipment;
    private Stats _stats;
    private Combat _combat;
    private Levelling _levelling;
    private Damage _damage;
    private Traps _traps;
    private Doors _doors;
    private MonsterAi _monsterAi;
    private MonsterAttack _monsterAttack;
    private Spells _spells;
    private Potions _potions;
    private Food _food;
    private Scrolls _scrolls;
    private Devices _devices;
    private Magic _magic;
    private InventoryScreen _inventoryScreen;
    private Tunnelling _tunnelling;
    private Looking _looking;
    private Throwing _throwing;

    public GameLoop(GameState game, Display display)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(display);

        _game = game;
        _display = display;
        _lighting = new Lighting(game, display);
        _movement = new Movement(game, display, this);
        _equipment = new Equipment(game, display, this);
        _stats = new Stats(game, display, this);
        _combat = new Combat(game, display, this);
        _levelling = new Levelling(game, display, this);
        _damage = new Damage(game, display, this);
        _traps = new Traps(game, display, this);
        _doors = new Doors(game, display, this);
        _monsterAi = new MonsterAi(game, display, this);
        _monsterAttack = new MonsterAttack(game, display, this);
        _spells = new Spells(game, display, this);
        _potions = new Potions(game, display, this);
        _food = new Food(game, display, this);
        _scrolls = new Scrolls(game, display, this);
        _devices = new Devices(game, display, this);
        _magic = new Magic(game, display, this);
        _inventoryScreen = new InventoryScreen(game, display, this);
        _tunnelling = new Tunnelling(game, display, this);
        _looking = new Looking(game, display, this);
        _throwing = new Throwing(game, display, this);
    }

    /// <summary>What the player can see, and how the screen hears about it.</summary>
    public Lighting Lighting => _lighting;

    /// <summary>Walking, running and searching.</summary>
    public Movement Movement => _movement;

    /// <summary>What worn equipment does for the player.</summary>
    public Equipment Equipment => _equipment;

    /// <summary>The six stats and the arithmetic that moves them.</summary>
    public Stats Stats => _stats;

    /// <summary>Hitting things, and what happens when they die.</summary>
    public Combat Combat => _combat;

    /// <summary>Experience, levels and hit points.</summary>
    public Levelling Levelling => _levelling;

    /// <summary>The elements, and what they do to a person and their belongings.</summary>
    public Damage Damage => _damage;

    /// <summary>Traps and chests.</summary>
    public Traps Traps => _traps;

    /// <summary>Opening and closing what is in the way.</summary>
    public Doors Doors => _doors;

    /// <summary>What the monsters do with their turn.</summary>
    public MonsterAi MonsterAi => _monsterAi;

    /// <summary>What a monster does when it reaches the player.</summary>
    public MonsterAttack MonsterAttack => _monsterAttack;

    /// <summary>Spells, and the three shapes they come in.</summary>
    public Spells Spells
    {
        get => _spells;
        set => _spells = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>Drinking things.</summary>
    public Potions Potions => _potions;

    /// <summary>Eating things.</summary>
    public Food Food => _food;

    /// <summary>Reading scrolls.</summary>
    public Scrolls Scrolls
    {
        get => _scrolls;
        set => _scrolls = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>Aiming wands and using staffs.</summary>
    public Devices Devices => _devices;

    /// <summary>Casting spells and reciting prayers.</summary>
    public Magic Magic
    {
        get => _magic;
        set => _magic = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>The inventory screens, and the prompt that asks which item.</summary>
    public InventoryScreen InventoryScreen
    {
        get => _inventoryScreen;
        set => _inventoryScreen = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>Digging through rock and rubble.</summary>
    public Tunnelling Tunnelling => _tunnelling;

    /// <summary>Looking around, with peripheral vision.</summary>
    public Looking Looking
    {
        get => _looking;
        set => _looking = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>Throwing and firing.</summary>
    public Throwing Throwing => _throwing;

    /// <summary>
    /// Lights the monsters without moving them, which is what creatures(FALSE)
    /// is for. Public so the parts that relight the level can ask for it.
    /// </summary>
    public void LightMonsters() => MoveMonsters(false);

    private Player Player => _game.Player;

    /// <summary>Set when a command took no time, so the turn does not advance.</summary>
    public bool FreeTurn { get; set; }

    /// <summary>Set when the player is leaving the level, which ends the loop.</summary>
    public bool NewLevel { get; set; }

    /// <summary>Set when the player has died.</summary>
    public bool Dead { get; set; }

    /// <summary>
    /// Whether the player is running rather than stepping. Umoria's find_flag,
    /// which lives with the rest of the shared state because the drawing reads
    /// it too.
    /// </summary>
    public bool Running
    {
        get => _game.Running;
        set => _game.Running = value;
    }

    /// <summary>
    /// Whether the player is carrying a lit light source. Lives with the rest of
    /// the shared state because the lighting reads it too.
    /// </summary>
    public bool PlayerLight
    {
        get => _game.PlayerLight;
        set => _game.PlayerLight = value;
    }

    /// <summary>What killed the player, recorded for the tombstone.</summary>
    public string DiedFrom { get; set; } = string.Empty;

    // -------------------------------------------------------- interruptions

    /// <summary>
    /// Interrupts whatever the player was doing. Mirrors disturb().
    ///
    /// Anything that deserves attention - a monster appearing, damage taken, a
    /// message worth reading - calls this, which is what stops a rest or a run
    /// from carrying on into danger.
    /// </summary>
    /// <param name="stopSearching">Whether searching should also be switched off.</param>
    /// <param name="stopRunning">Whether a run should end even if nothing else did.</param>
    public void Disturb(bool stopSearching, bool stopRunning)
    {
        _display.CommandCount = 0;

        if (stopSearching && (Player.Status & PlayerStatus.Searching) != 0)
        {
            SearchOff();
        }

        if (Player.Rest != 0)
        {
            RestOff();
        }

        if (stopRunning || Running)
        {
            Running = false;
            CheckView();
        }

        _display.FlushInput();
    }

    /// <summary>
    /// Stops searching. Mirrors search_off().
    ///
    /// Searching costs a point of speed and a point of food a turn, so both are
    /// handed back here rather than being recomputed from scratch.
    /// </summary>
    public void SearchOff()
    {
        CheckView();
        ChangeSpeed(-1);
        Player.Status &= ~PlayerStatus.Searching;
        _display.PrintState(Player);
        _display.PrintSpeed(Player);
        Player.FoodDigested--;
    }

    /// <summary>Stops resting. Mirrors rest_off().</summary>
    public void RestOff()
    {
        Player.Rest = 0;
        Player.Status &= ~PlayerStatus.Resting;
        _display.PrintState(Player);

        // Flushes the last message, so a "press any key" does not sit there.
        _display.MessagePrint(null);

        Player.FoodDigested++;
    }

    /// <summary>
    /// Changes the player's speed. Mirrors change_speed().
    ///
    /// Every monster is changed by the same amount rather than the player being
    /// compared against them, which is what keeps the rest of the game free of
    /// relative-speed arithmetic.
    /// </summary>
    public void ChangeSpeed(int amount)
    {
        Player.Speed += amount;
        Player.Status |= PlayerStatus.SpeedChanged;

        for (int i = MonsterPool.FirstIndex; i < _game.Monsters.Count; i++)
        {
            _game.Monsters[i].Speed += amount;
        }
    }

    // -------------------------------------------------------- regeneration

    /// <summary>
    /// Regenerates hit points. Mirrors regenhp().
    ///
    /// A character regenerates far less than a point a turn, so the remainder is
    /// carried in 1/65536ths between turns. <paramref name="percent"/> is that
    /// fraction of the maximum, already scaled.
    /// </summary>
    public void RegenerateHitPoints(int percent)
    {
        int old = Player.CurrentHitPoints;
        long gained = ((long)Player.MaxHitPoints * percent) + RegenHitPointBase;

        Player.CurrentHitPoints += (int)(gained >> 16);

        // The original's counters are signed shorts, so a very high maximum can
        // wrap them negative here; the clamp below lands on the same value
        // either way, since a wrapped count is still above the maximum.
        if (Player.CurrentHitPoints < 0 && old > 0)
        {
            Player.CurrentHitPoints = MaxShort;
        }

        long fraction = (gained & 0xFFFF) + Player.HitPointFraction;
        if (fraction >= 0x10000L)
        {
            Player.HitPointFraction = (int)(fraction - 0x10000L);
            Player.CurrentHitPoints++;
        }
        else
        {
            Player.HitPointFraction = (int)fraction;
        }

        // The fraction is cleared even when the totals are already equal, so a
        // character at full health does not bank progress towards nothing.
        if (Player.CurrentHitPoints >= Player.MaxHitPoints)
        {
            Player.CurrentHitPoints = Player.MaxHitPoints;
            Player.HitPointFraction = 0;
        }

        if (old != Player.CurrentHitPoints)
        {
            _display.PrintCurrentHitPoints(Player);
        }
    }

    /// <summary>Regenerates spell points. Mirrors regenmana().</summary>
    public void RegenerateMana(int percent)
    {
        int old = Player.CurrentMana;
        long gained = ((long)Player.MaxMana * percent) + RegenManaBase;

        Player.CurrentMana += (int)(gained >> 16);

        if (Player.CurrentMana < 0 && old > 0)
        {
            Player.CurrentMana = MaxShort;
        }

        long fraction = (gained & 0xFFFF) + Player.ManaFraction;
        if (fraction >= 0x10000L)
        {
            Player.ManaFraction = (int)(fraction - 0x10000L);
            Player.CurrentMana++;
        }
        else
        {
            Player.ManaFraction = (int)fraction;
        }

        if (Player.CurrentMana >= Player.MaxMana)
        {
            Player.CurrentMana = Player.MaxMana;
            Player.ManaFraction = 0;
        }

        if (old != Player.CurrentMana)
        {
            _display.PrintCurrentMana(Player);
        }
    }



    // ---------------------------------------------------------------- the loop

    /// <summary>The last command given, which "^P" reads to decide how far back to go.</summary>
    public char LastCommand { get; private set; }

    /// <summary>
    /// Plays one dungeon level, from arriving on it to leaving it. Mirrors
    /// dungeon().
    ///
    /// The shape is: a turn passes, then a command is asked for, then the
    /// monsters move. A command that takes no time loops straight back for
    /// another without a turn passing, which is what makes checking the
    /// inventory or the map free. Death and the stairs both leave by the same
    /// door, since both mean this level is over.
    /// </summary>
    public void Run()
    {
        if (_game.DungeonLevel > Player.MaxDungeonLevel)
        {
            Player.MaxDungeonLevel = _game.DungeonLevel;
        }

        _display.CommandCount = 0;
        int runCount = 0;
        NewLevel = false;
        Running = false;
        Teleporting = false;

        _game.Cave[_game.CharacterRow, _game.CharacterColumn].MonsterIndex = 1;

        // Forcing the panel to an impossible index is what makes the first
        // check_view() draw the map rather than deciding nothing moved.
        _display.Panel.Invalidate();
        CheckView();

        // Searching is switched off on arrival, and only after the panel is
        // invalid, because switching it off looks at the view.
        if ((Player.Status & PlayerStatus.Searching) != 0)
        {
            SearchOff();
        }

        // Light the level without letting anything move yet.
        MoveMonsters(false);
        _display.PrintDepth();

        do
        {
            TurnUpkeep();

            if (Player.Paralysis < 1 && Player.Rest == 0 && !Dead)
            {
                // Commands that take no time come straight back here.
                do
                {
                    if ((Player.Status & PlayerStatus.Repeating) != 0)
                    {
                        _display.PrintState(Player);
                    }

                    DefaultDirection = false;
                    FreeTurn = false;

                    if (Running)
                    {
                        TakeRunStep();
                        runCount--;
                        if (runCount == 0)
                        {
                            EndRun();
                        }

                        _display.Refresh();
                    }
                    else if (_inventoryScreen.ContinuingCommand is char resumed)
                    {
                        // The inventory mode gave the world a turn and is still
                        // standing open, so it takes the next key itself.
                        _inventoryScreen.Command(resumed);
                    }
                    else
                    {
                        _display.MoveCursorRelative(_game.CharacterRow, _game.CharacterColumn);

                        char command;
                        if (_display.CommandCount > 0)
                        {
                            // A counted command repeats without being retyped,
                            // and keeps the direction it started with.
                            _display.MessageWaitingFlag = false;
                            DefaultDirection = true;
                            command = LastCommand;
                        }
                        else
                        {
                            command = ReadCommand();
                        }

                        // Flash the message line, so a message from last turn
                        // does not look like a message about this one.
                        _display.EraseLine(Display.MessageLine, 0);
                        _display.MoveCursorRelative(_game.CharacterRow, _game.CharacterColumn);
                        _display.Refresh();

                        DoCommand(command);

                        // A run is counted differently, because the command
                        // itself changes into the run.
                        if (Running)
                        {
                            runCount = _display.CommandCount - 1;
                            _display.CommandCount = 0;
                        }
                        else if (FreeTurn)
                        {
                            _display.CommandCount = 0;
                        }
                        else if (_display.CommandCount > 0)
                        {
                            _display.CommandCount--;
                        }
                    }
                }
                while (FreeTurn && !NewLevel);
            }
            else
            {
                // Paralysed, resting or dead: nothing to ask, so just show
                // where the player is.
                _display.MoveCursorRelative(_game.CharacterRow, _game.CharacterColumn);
                _display.Refresh();
            }

            if (Teleporting)
            {
                Teleport(100);
            }

            if (!NewLevel)
            {
                MoveMonsters(true);
            }
        }
        while (!NewLevel);
    }

    /// <summary>Set when something has thrown the player across the level.</summary>
    public bool Teleporting { get; set; }

    /// <summary>
    /// Carries out one command. Mirrors do_command().
    ///
    /// Pending: most of the dispatch reaches into moria2.c, moria3.c, spells.c
    /// and the inventory, none of which are ported. The commands here are the
    /// ones whose whole implementation already exists; anything else says so
    /// rather than doing nothing, so a missing command is never mistaken for a
    /// command that did nothing.
    /// </summary>
    protected virtual void DoCommand(char command)
    {
        // "-" is a movement command that leaves whatever is on the floor where
        // it is. It asks for a direction and then becomes the walk command for
        // it, which is why the count has to be put back afterwards.
        bool pickUp = true;

        if (command == '-')
        {
            pickUp = false;
            int saved = _display.CommandCount;

            (bool taken, int direction) = ReadDirection();
            if (taken)
            {
                _display.CommandCount = saved;
                command = direction switch
                {
                    1 => 'b',
                    2 => 'j',
                    3 => 'n',
                    4 => 'h',
                    6 => 'l',
                    7 => 'y',
                    8 => 'k',
                    9 => 'u',
                    _ => Commands.Illegal,
                };
            }
            else
            {
                command = Commands.Nothing;
            }
        }

        if (command == 'Q')
        {
            _display.FlushInput();

            if (_display.GetCheck("Do you really want to quit?"))
            {
                NewLevel = true;
                Dead = true;
                DiedFrom = "Quitting";
            }

            FreeTurn = true;
        }
        else if (command == Keys.Control('P'))
        {
            ShowPreviousMessages();
            FreeTurn = true;
        }
        else if (command == Keys.Escape || command == ' ')
        {
            FreeTurn = true;
        }
        else if (command == 'M')
        {
            _display.ScreenMap();
            _display.ReadKey();
            _display.RestoreScreen();
            FreeTurn = true;
        }
        else if (command == 'o')
        {
            _doors.OpenObject();
        }
        else if (command == 'c')
        {
            _doors.CloseObject();
        }
        else if (command == 'G')
        {
            _magic.GainSpells();
        }
        else if (command is 'i' or 'e' or 'd' or 'w')
        {
            _inventoryScreen.Command(command);
        }
        else if (command == 'T')
        {
            _inventoryScreen.Command('t');
        }
        else if (command == 'X')
        {
            _inventoryScreen.Command('x');
        }
        else if (command == 't')
        {
            _throwing.ThrowObject();
        }
        else if (command == 'x')
        {
            _looking.Look();
            FreeTurn = true;
        }
        else if (command == 'f')
        {
            _doors.Bash();
        }
        else if (command == 'D')
        {
            _traps.DisarmTrap();
        }
        else if (TunnelDirection(command) is int dig)
        {
            _tunnelling.Tunnel(dig);
        }
        else if (command == 'm')
        {
            _magic.Cast();
        }
        else if (command == 'p')
        {
            _magic.Pray();
        }
        else if (command == 'b')
        {
            _magic.ExamineBook();
        }
        else if (command == 'q')
        {
            _potions.Quaff();
        }
        else if (command == 'r')
        {
            _scrolls.Read();
        }
        else if (command == 'E')
        {
            _food.EatCommand();
        }
        else if (command == 'a')
        {
            _devices.Aim();
        }
        else if (command == 'u')
        {
            _devices.Use();
        }
        else if (WalkDirection(command) is int walk)
        {
            _movement.MoveChar(walk, pickUp);
        }
        else if (RunDirection(command) is int run)
        {
            _movement.FindInit(run);
        }
        else
        {
            _display.Print("That command is not ported yet.", 0, 0);
            FreeTurn = true;
        }

        LastCommand = command;
    }

    /// <summary>
    /// The direction a tunnel command digs in, or null if it is not one. The
    /// control keys spell the direction into the command, as the walk and run
    /// commands do.
    /// </summary>
    private static int? TunnelDirection(char command) => command switch
    {
        var c when c == Keys.Control('B') => 1,

        // A carriage return has to be treated the same as a line feed, since
        // which one a terminal sends is not something the game can choose.
        var c when c == Keys.Control('M') => 2,
        var c when c == Keys.Control('J') => 2,
        var c when c == Keys.Control('N') => 3,
        var c when c == Keys.Control('H') => 4,
        var c when c == Keys.Control('L') => 6,
        var c when c == Keys.Control('Y') => 7,
        var c when c == Keys.Control('K') => 8,
        var c when c == Keys.Control('U') => 9,
        _ => null,
    };

    /// <summary>The direction a walk command steps in, or null if it is not one.</summary>
    private static int? WalkDirection(char command) => command switch
    {
        'b' => 1,
        'j' => 2,
        'n' => 3,
        'h' => 4,
        'l' => 6,
        'y' => 7,
        'k' => 8,
        'u' => 9,
        _ => null,
    };

    /// <summary>The direction a run command sets off in, or null if it is not one.</summary>
    private static int? RunDirection(char command) => command switch
    {
        'B' => 1,
        'J' => 2,
        'N' => 3,
        'H' => 4,
        'L' => 6,
        'Y' => 7,
        'K' => 8,
        'U' => 9,
        _ => null,
    };

    /// <summary>
    /// Shows the messages already gone by. Mirrors the ^P branch of
    /// do_command().
    ///
    /// One press shows the last message, and pressing again shows the whole
    /// ring at once. A recovered message is marked with a "&gt;" so it cannot be
    /// mistaken for something that just happened.
    /// </summary>
    private void ShowPreviousMessages()
    {
        int wanted;

        if (_display.CommandCount > 0)
        {
            wanted = Math.Min(_display.CommandCount, Display.SavedMessageCount);
            _display.CommandCount = 0;
        }
        else if (LastCommand != Keys.Control('P'))
        {
            wanted = 1;
        }
        else
        {
            wanted = Display.SavedMessageCount;
        }

        int index = _display.LastMessageIndex;

        if (wanted > 1)
        {
            _display.SaveScreen();

            int rows = wanted;
            while (wanted > 0)
            {
                wanted--;
                _display.Print(_display.RecentMessages[index] ?? string.Empty, wanted, 0);
                index = index == 0 ? Display.SavedMessageCount - 1 : index - 1;
            }

            _display.EraseLine(rows, 0);
            _display.PauseLine(rows);
            _display.RestoreScreen();
        }
        else
        {
            _display.PutBuffer(">", 0, 0);
            _display.Print(_display.RecentMessages[index] ?? string.Empty, 0, 1);
        }
    }

    /// <summary>Takes one step of a run. Mirrors find_run().</summary>
    protected virtual void TakeRunStep() => _movement.FindRun();

    // ------------------------------------------------------------ the turn

    /// <summary>
    /// Everything that happens to the player in one turn, before a command is
    /// asked for. Mirrors the top half of dungeon()'s loop.
    ///
    /// The order matters and is the original's. Heroism comes first because it
    /// raises the maximum hit points that everything after it measures against;
    /// hunger is settled before regeneration, because how hungry the player is
    /// decides how fast they heal; and the redraw block comes last, so a turn
    /// only repaints what actually changed.
    /// </summary>
    public void TurnUpkeep()
    {
        _game.Turn++;

        // The shop stock turns over slowly, and only while the player is out of
        // town to notice it.
        if (_game.DungeonLevel != 0 && _game.Turn % 1000 == 0)
        {
            _game.Stores.Maintain();
        }

        // Something wanders in, out of sight.
        if (_game.Rng.RandInt(MonsterArrivalChance) == 1)
        {
            new DungeonGenerator(_game, _display).AllocMonster(1, MaxSight, asleep: false);
        }

        BurnLight();
        AgeHeroism();
        int regeneration = ApplyHunger();
        Regenerate(regeneration);
        AgeConditions();
        AgeRest();
        CheckForInterruption();
        AgeDelayedEffects();

        // A cursed item that teleports at random, which is what makes one worth
        // taking off even when the bonus is good.
        if (Player.RandomTeleport && _game.Rng.RandInt(100) == 1)
        {
            Disturb(false, false);
            Teleport(40);
        }

        RedrawChanged();

        // A slim chance of noticing that something carried is enchanted. The
        // odds improve with level: about once every 2160 turns at first, and
        // once every 416 by the last.
        if ((_game.Turn & 0xF) == 0
            && Player.Confused == 0
            && _game.Rng.RandInt(10 + (750 / (5 + Player.Level))) == 1)
        {
            NoticeEnchantment();
        }

        // Keeping room in the monster list here, rather than when a monster
        // breeds, is what makes the compaction likely to succeed.
        if (MonsterPool.Capacity - _game.Monsters.Count < 10)
        {
            CompactMonsters();
        }
    }

    /// <summary>
    /// Burns a turn of lamp oil, and reports it running low or out. Mirrors the
    /// light block at the top of dungeon()'s loop.
    ///
    /// The warning that the light is growing faint is a one-in-five chance per
    /// turn over the last forty, so it comes as a nagging reminder rather than a
    /// single notice that could be missed.
    ///
    /// Pending: the fuel belongs to the lamp, which needs the inventory; until
    /// then it is held on the game state.
    /// </summary>
    protected virtual void BurnLight()
    {
        if (_game.PlayerLight)
        {
            if (_game.LightFuel > 0)
            {
                _game.LightFuel--;

                if (_game.LightFuel == 0)
                {
                    _game.PlayerLight = false;
                    _display.MessagePrint("Your light has gone out!");
                    Disturb(false, true);

                    // Unlight the creatures.
                    MoveMonsters(false);
                }
                else if (_game.LightFuel < 40 && _game.Rng.RandInt(5) == 1
                         && Player.Blind < 1)
                {
                    Disturb(false, false);
                    _display.MessagePrint("Your light is growing faint.");
                }
            }
            else
            {
                _game.PlayerLight = false;
                Disturb(false, true);
                MoveMonsters(false);
            }
        }
        else if (_game.LightFuel > 0)
        {
            _game.LightFuel--;
            _game.PlayerLight = true;
            Disturb(false, true);
            MoveMonsters(false);
        }
    }

    /// <summary>
    /// Counts down heroism and super heroism. Mirrors the first two blocks of
    /// the loop.
    ///
    /// Both raise the maximum hit points as well as the current ones, and hand
    /// the maximum back when they expire. The current total is only trimmed if
    /// it would be left above the new maximum, so a hero who spent the bonus
    /// does not lose real health twice.
    /// </summary>
    private void AgeHeroism()
    {
        if (Player.Hero > 0)
        {
            if ((Player.Status & PlayerStatus.Heroism) == 0)
            {
                Player.Status |= PlayerStatus.Heroism;
                Disturb(false, false);
                Player.MaxHitPoints += 10;
                Player.CurrentHitPoints += 10;
                Player.BaseToHit += 12;
                Player.BaseToHitBows += 12;
                _display.MessagePrint("You feel like a HERO!");
                _display.PrintMaxHitPoints(Player);
                _display.PrintCurrentHitPoints(Player);
            }

            Player.Hero--;
            if (Player.Hero == 0)
            {
                Player.Status &= ~PlayerStatus.Heroism;
                Disturb(false, false);
                Player.MaxHitPoints -= 10;

                if (Player.CurrentHitPoints > Player.MaxHitPoints)
                {
                    Player.CurrentHitPoints = Player.MaxHitPoints;
                    Player.HitPointFraction = 0;
                    _display.PrintCurrentHitPoints(Player);
                }

                Player.BaseToHit -= 12;
                Player.BaseToHitBows -= 12;
                _display.MessagePrint("The heroism wears off.");
                _display.PrintMaxHitPoints(Player);
            }
        }

        if (Player.SuperHero > 0)
        {
            if ((Player.Status & PlayerStatus.SuperHeroism) == 0)
            {
                Player.Status |= PlayerStatus.SuperHeroism;
                Disturb(false, false);
                Player.MaxHitPoints += 20;
                Player.CurrentHitPoints += 20;
                Player.BaseToHit += 24;
                Player.BaseToHitBows += 24;
                _display.MessagePrint("You feel like a SUPER HERO!");
                _display.PrintMaxHitPoints(Player);
                _display.PrintCurrentHitPoints(Player);
            }

            Player.SuperHero--;
            if (Player.SuperHero == 0)
            {
                Player.Status &= ~PlayerStatus.SuperHeroism;
                Disturb(false, false);
                Player.MaxHitPoints -= 20;

                if (Player.CurrentHitPoints > Player.MaxHitPoints)
                {
                    Player.CurrentHitPoints = Player.MaxHitPoints;
                    Player.HitPointFraction = 0;
                    _display.PrintCurrentHitPoints(Player);
                }

                Player.BaseToHit -= 24;
                Player.BaseToHitBows -= 24;
                _display.MessagePrint("The super heroism wears off.");
                _display.PrintMaxHitPoints(Player);
            }
        }
    }

    /// <summary>
    /// Eats a turn's food and reports the consequences. Mirrors the food block.
    ///
    /// Being sped up burns food by the square of the speed, which is what stops
    /// a hasted character crossing the whole dungeon on one ration. Below the
    /// fainting mark the player starts blacking out, and past zero they take
    /// damage every turn.
    /// </summary>
    /// <returns>The regeneration rate that hunger leaves them.</returns>
    private int ApplyHunger()
    {
        int regeneration = RegenNormal;

        if (Player.Food < FoodAlert)
        {
            if (Player.Food < FoodWeak)
            {
                if (Player.Food < 0)
                {
                    regeneration = 0;
                }
                else if (Player.Food < FoodFainting)
                {
                    regeneration = RegenFainting;
                }
                else
                {
                    regeneration = RegenWeak;
                }

                if ((Player.Status & PlayerStatus.Weak) == 0)
                {
                    Player.Status |= PlayerStatus.Weak;
                    _display.MessagePrint("You are getting weak from hunger.");
                    Disturb(false, false);
                    _display.PrintHunger(Player);
                }

                if (Player.Food < FoodFainting && _game.Rng.RandInt(8) == 1)
                {
                    Player.Paralysis += _game.Rng.RandInt(5);
                    _display.MessagePrint("You faint from the lack of food.");
                    Disturb(true, false);
                }
            }
            else if ((Player.Status & PlayerStatus.Hungry) == 0)
            {
                Player.Status |= PlayerStatus.Hungry;
                _display.MessagePrint("You are getting hungry.");
                Disturb(false, false);
                _display.PrintHunger(Player);
            }
        }

        if (Player.Speed < 0)
        {
            Player.Food -= Player.Speed * Player.Speed;
        }

        Player.Food -= Player.FoodDigested;

        if (Player.Food < 0)
        {
            TakeHit(-Player.Food / 16, "starvation");
            Disturb(true, false);
        }

        return regeneration;
    }

    /// <summary>
    /// Heals a turn's worth. Mirrors the regeneration block.
    ///
    /// Resting or searching doubles the rate, which is what makes resting worth
    /// the turns it costs. Poison stops healing outright.
    /// </summary>
    private void Regenerate(int regeneration)
    {
        if (Player.Regenerates)
        {
            regeneration = regeneration * 3 / 2;
        }

        if ((Player.Status & PlayerStatus.Searching) != 0 || Player.Rest != 0)
        {
            regeneration *= 2;
        }

        if (Player.Poisoned < 1 && Player.CurrentHitPoints < Player.MaxHitPoints)
        {
            RegenerateHitPoints(regeneration);
        }

        if (Player.CurrentMana < Player.MaxMana)
        {
            RegenerateMana(regeneration);
        }
    }

    /// <summary>
    /// Counts down the conditions that show in the sidebar. Mirrors the
    /// blindness through slowness blocks.
    ///
    /// Each one sets its flag on the first turn, counts down, and clears the
    /// flag on the last - so the sidebar is only redrawn twice however long the
    /// effect lasts.
    /// </summary>
    private void AgeConditions()
    {
        if (Player.Blind > 0)
        {
            if ((Player.Status & PlayerStatus.Blind) == 0)
            {
                Player.Status |= PlayerStatus.Blind;
                _display.PrintMap();
                _display.PrintBlind(Player);
                Disturb(false, true);
                MoveMonsters(false);
            }

            Player.Blind--;
            if (Player.Blind == 0)
            {
                Player.Status &= ~PlayerStatus.Blind;
                _display.PrintBlind(Player);
                _display.PrintMap();
                Disturb(false, true);
                MoveMonsters(false);
                _display.MessagePrint("The veil of darkness lifts.");
            }
        }

        if (Player.Confused > 0)
        {
            if ((Player.Status & PlayerStatus.Confused) == 0)
            {
                Player.Status |= PlayerStatus.Confused;
                _display.PrintConfused(Player);
            }

            Player.Confused--;
            if (Player.Confused == 0)
            {
                Player.Status &= ~PlayerStatus.Confused;
                _display.PrintConfused(Player);
                _display.MessagePrint("You feel less confused now.");

                if (Player.Rest != 0)
                {
                    RestOff();
                }
            }
        }

        if (Player.Afraid > 0)
        {
            if ((Player.Status & PlayerStatus.Afraid) == 0)
            {
                // Heroism cancels fear outright rather than counting it down.
                if (Player.SuperHero + Player.Hero > 0)
                {
                    Player.Afraid = 0;
                }
                else
                {
                    Player.Status |= PlayerStatus.Afraid;
                    _display.PrintAfraid(Player);
                }
            }
            else if (Player.SuperHero + Player.Hero > 0)
            {
                Player.Afraid = 1;
            }

            Player.Afraid--;
            if (Player.Afraid == 0)
            {
                Player.Status &= ~PlayerStatus.Afraid;
                _display.PrintAfraid(Player);
                _display.MessagePrint("You feel bolder now.");
                Disturb(false, false);
            }
        }

        if (Player.Poisoned > 0)
        {
            if ((Player.Status & PlayerStatus.Poisoned) == 0)
            {
                Player.Status |= PlayerStatus.Poisoned;
                _display.PrintPoisoned(Player);
            }

            Player.Poisoned--;
            if (Player.Poisoned == 0)
            {
                Player.Status &= ~PlayerStatus.Poisoned;
                _display.PrintPoisoned(Player);
                _display.MessagePrint("You feel better.");
                Disturb(false, false);
            }
            else
            {
                // A tough character takes a point every other turn, or every
                // third or fourth; a frail one takes several a turn.
                int damage = CharacterCreation.ConstitutionBonus(Player) switch
                {
                    -4 => 4,
                    -3 or -2 => 3,
                    -1 => 2,
                    0 => 1,
                    1 or 2 or 3 => _game.Turn % 2 == 0 ? 1 : 0,
                    4 or 5 => _game.Turn % 3 == 0 ? 1 : 0,
                    _ => _game.Turn % 4 == 0 ? 1 : 0,
                };

                TakeHit(damage, "poison");
                Disturb(true, false);
            }
        }

        if (Player.Hasted > 0)
        {
            if ((Player.Status & PlayerStatus.Hasted) == 0)
            {
                Player.Status |= PlayerStatus.Hasted;
                ChangeSpeed(-1);
                _display.MessagePrint("You feel yourself moving faster.");
                Disturb(false, false);
            }

            Player.Hasted--;
            if (Player.Hasted == 0)
            {
                Player.Status &= ~PlayerStatus.Hasted;
                ChangeSpeed(1);
                _display.MessagePrint("You feel yourself slow down.");
                Disturb(false, false);
            }
        }

        if (Player.Slowed > 0)
        {
            if ((Player.Status & PlayerStatus.Slowed) == 0)
            {
                Player.Status |= PlayerStatus.Slowed;
                ChangeSpeed(1);
                _display.MessagePrint("You feel yourself moving slower.");
                Disturb(false, false);
            }

            Player.Slowed--;
            if (Player.Slowed == 0)
            {
                Player.Status &= ~PlayerStatus.Slowed;
                ChangeSpeed(-1);
                _display.MessagePrint("You feel yourself speed up.");
                Disturb(false, false);
            }
        }
    }

    /// <summary>
    /// Counts down a rest. Mirrors the resting block.
    ///
    /// A negative count means resting until healed rather than for a set number
    /// of turns, which is why it counts up.
    /// </summary>
    private void AgeRest()
    {
        if (Player.Rest > 0)
        {
            Player.Rest--;
            if (Player.Rest == 0)
            {
                RestOff();
            }
        }
        else if (Player.Rest < 0)
        {
            Player.Rest++;

            if ((Player.CurrentHitPoints == Player.MaxHitPoints
                 && Player.CurrentMana == Player.MaxMana)
                || Player.Rest == 0)
            {
                RestOff();
            }
        }
    }

    /// <summary>
    /// Lets a keypress interrupt a rest, a run or a counted command. Mirrors the
    /// check_input() call.
    ///
    /// The key is thrown away: it means stop, not anything in particular.
    /// </summary>
    private void CheckForInterruption()
    {
        if ((_display.CommandCount > 0 || Running || Player.Rest != 0)
            && _display.KeyAvailable)
        {
            Disturb(false, false);
        }
    }

    /// <summary>
    /// Counts down everything that does not show in the sidebar. Mirrors the
    /// hallucination through word-of-recall blocks.
    /// </summary>
    private void AgeDelayedEffects()
    {
        if (Player.Hallucinating > 0)
        {
            EndRun();
            Player.Hallucinating--;
            if (Player.Hallucinating == 0)
            {
                _display.PrintMap();
            }
        }

        if (Player.Paralysis > 0)
        {
            // Nothing that happens while paralysed can be watched.
            Player.Paralysis--;
            Disturb(true, false);
        }

        if (Player.ProtectionFromEvil > 0)
        {
            Player.ProtectionFromEvil--;
            if (Player.ProtectionFromEvil == 0)
            {
                _display.MessagePrint("You no longer feel safe from evil.");
            }
        }

        if (Player.Invulnerable > 0)
        {
            if ((Player.Status & PlayerStatus.Invulnerable) == 0)
            {
                Player.Status |= PlayerStatus.Invulnerable;
                Disturb(false, false);
                Player.ArmourClass += 100;
                Player.DisplayedArmourClass += 100;
                _display.PrintArmourClass(Player);
                _display.MessagePrint("Your skin turns into steel!");
            }

            Player.Invulnerable--;
            if (Player.Invulnerable == 0)
            {
                Player.Status &= ~PlayerStatus.Invulnerable;
                Disturb(false, false);
                Player.ArmourClass -= 100;
                Player.DisplayedArmourClass -= 100;
                _display.PrintArmourClass(Player);
                _display.MessagePrint("Your skin returns to normal.");
            }
        }

        if (Player.Blessed > 0)
        {
            if ((Player.Status & PlayerStatus.Blessed) == 0)
            {
                Player.Status |= PlayerStatus.Blessed;
                Disturb(false, false);
                Player.BaseToHit += 5;
                Player.BaseToHitBows += 5;
                Player.ArmourClass += 2;
                Player.DisplayedArmourClass += 2;
                _display.MessagePrint("You feel righteous!");
                _display.PrintArmourClass(Player);
            }

            Player.Blessed--;
            if (Player.Blessed == 0)
            {
                Player.Status &= ~PlayerStatus.Blessed;
                Disturb(false, false);
                Player.BaseToHit -= 5;
                Player.BaseToHitBows -= 5;
                Player.ArmourClass -= 2;
                Player.DisplayedArmourClass -= 2;
                _display.MessagePrint("The prayer has expired.");
                _display.PrintArmourClass(Player);
            }
        }

        if (Player.ResistHeat > 0)
        {
            Player.ResistHeat--;
            if (Player.ResistHeat == 0)
            {
                _display.MessagePrint("You no longer feel safe from flame.");
            }
        }

        if (Player.ResistCold > 0)
        {
            Player.ResistCold--;
            if (Player.ResistCold == 0)
            {
                _display.MessagePrint("You no longer feel safe from cold.");
            }
        }

        if (Player.DetectInvisible > 0)
        {
            if ((Player.Status & PlayerStatus.DetectInvisible) == 0)
            {
                Player.Status |= PlayerStatus.DetectInvisible;
                Player.SeeInvisible = true;
                MoveMonsters(false);
            }

            Player.DetectInvisible--;
            if (Player.DetectInvisible == 0)
            {
                Player.Status &= ~PlayerStatus.DetectInvisible;

                // A worn item may still grant it, so the bonuses decide rather
                // than the counter.
                RecalculateBonuses();
                MoveMonsters(false);
            }
        }

        if (Player.TimedInfravision > 0)
        {
            if ((Player.Status & PlayerStatus.TimedInfravision) == 0)
            {
                Player.Status |= PlayerStatus.TimedInfravision;
                Player.SeeInfrared++;
                MoveMonsters(false);
            }

            Player.TimedInfravision--;
            if (Player.TimedInfravision == 0)
            {
                Player.Status &= ~PlayerStatus.TimedInfravision;
                Player.SeeInfrared--;
                MoveMonsters(false);
            }
        }

        // Word of recall fires when the countdown reaches one, and pulls the
        // player either out of the dungeon or back down to the deepest level
        // they have reached.
        if (Player.WordOfRecall > 0)
        {
            if (Player.WordOfRecall == 1)
            {
                NewLevel = true;
                Player.Paralysis++;
                Player.WordOfRecall = 0;

                if (_game.DungeonLevel > 0)
                {
                    _game.DungeonLevel = 0;
                    _display.MessagePrint("You feel yourself yanked upwards!");
                }
                else if (Player.MaxDungeonLevel != 0)
                {
                    _game.DungeonLevel = Player.MaxDungeonLevel;
                    _display.MessagePrint("You feel yourself yanked downwards!");
                }
            }
            else
            {
                Player.WordOfRecall--;
            }
        }
    }

    /// <summary>
    /// Repaints whatever changed. Mirrors the block of status tests near the end
    /// of the loop.
    ///
    /// Anything that changes a displayed number raises a flag rather than
    /// redrawing on the spot, so a turn that changes the same number five times
    /// still only draws it once. Each test clears the flag it acted on.
    /// </summary>
    private void RedrawChanged()
    {
        if ((Player.Status & PlayerStatus.WeightChanged) != 0)
        {
            CheckStrength();
        }

        if ((Player.Status & PlayerStatus.CanStudy) != 0)
        {
            _display.PrintStudy(Player);
        }

        if ((Player.Status & PlayerStatus.SpeedChanged) != 0)
        {
            Player.Status &= ~PlayerStatus.SpeedChanged;
            _display.PrintSpeed(Player);
        }

        if ((Player.Status & PlayerStatus.Paralysed) != 0 && Player.Paralysis < 1)
        {
            _display.PrintState(Player);
            Player.Status &= ~PlayerStatus.Paralysed;
        }
        else if (Player.Paralysis > 0)
        {
            _display.PrintState(Player);
            Player.Status |= PlayerStatus.Paralysed;
        }
        else if (Player.Rest != 0)
        {
            _display.PrintState(Player);
        }

        if ((Player.Status & PlayerStatus.ArmourChanged) != 0)
        {
            _display.PrintArmourClass(Player);
            Player.Status &= ~PlayerStatus.ArmourChanged;
        }

        if ((Player.Status & PlayerStatus.AllStats) != 0)
        {
            for (int i = 0; i < Stat.Count; i++)
            {
                if (((PlayerStatus.Strength << i) & Player.Status) != 0)
                {
                    _display.PrintStat(Player, i);
                }
            }

            Player.Status &= ~PlayerStatus.AllStats;
        }

        if ((Player.Status & PlayerStatus.HitPointsChanged) != 0)
        {
            _display.PrintMaxHitPoints(Player);
            _display.PrintCurrentHitPoints(Player);
            Player.Status &= ~PlayerStatus.HitPointsChanged;
        }

        if ((Player.Status & PlayerStatus.ManaChanged) != 0)
        {
            _display.PrintCurrentMana(Player);
            Player.Status &= ~PlayerStatus.ManaChanged;
        }
    }

    /// <summary>
    /// Wounds the player, and kills them if it is enough. Mirrors take_hit().
    /// </summary>
    public void TakeHit(int damage, string source)
    {
        if (Player.Invulnerable > 0)
        {
            damage = 0;
        }

        Player.CurrentHitPoints -= damage;

        if (Player.CurrentHitPoints < 0)
        {
            if (!Dead)
            {
                Dead = true;
                DiedFrom = source;
                _game.TotalWinner = false;
            }

            NewLevel = true;
        }
        else
        {
            _display.PrintCurrentHitPoints(Player);
        }
    }

    /// <summary>Ends a run. Mirrors end_find().</summary>
    public void EndRun() => _movement.EndFind();

    // ------------------------------------------------------ pending subsystems
    //
    // The loop reaches into parts of the game that are not ported yet. They are
    // named here rather than left out, so the shape of the turn stays visible
    // and each one has somewhere to land.

    /// <summary>
    /// Lights the player's surroundings and moves the monsters. Mirrors
    /// creatures().
    /// </summary>
    protected virtual void MoveMonsters(bool move) => _monsterAi.Creatures(move);

    /// <summary>
    /// Recomputes what worn equipment grants. Mirrors calc_bonuses(). Pending:
    /// the inventory half of moria1.c.
    ///
    /// With nothing worn there is nothing to grant, so the one thing this can
    /// still do correctly is take back what only equipment could have given.
    /// </summary>
    protected virtual void RecalculateBonuses() => _equipment.Recalculate();

    /// <summary>
    /// Checks whether the player can still carry what they are carrying. Mirrors
    /// check_strength(). Pending: the inventory.
    /// </summary>
    protected virtual void CheckStrength() => _equipment.CheckStrength();

    /// <summary>
    /// Looks over what the player is carrying for an enchantment they have not
    /// noticed. Mirrors the sweep at the end of dungeon()'s loop. Pending: the
    /// inventory, so there is nothing to look over yet.
    ///
    /// The roll that decides whether to look at all is in the loop rather than
    /// here, because it happens whether or not anything is carried.
    /// </summary>
    protected virtual void NoticeEnchantment()
    {
    }

    /// <summary>
    /// Frees room in the monster list. Mirrors compact_monsters(). Pending:
    /// the compaction itself, which is misc1.c.
    /// </summary>
    protected virtual void CompactMonsters()
    {
    }

    /// <summary>Throws the player somewhere else on the level. Mirrors teleport().</summary>
    protected virtual void Teleport(int distance) => _combat.Teleport(distance);

    // ------------------------------------------------------- reading a command

    /// <summary>
    /// Reads one command, resolving counts and control keys. Mirrors the input
    /// block inside dungeon().
    ///
    /// Three things are settled before the command is dispatched. A leading "#"
    /// - or a digit under the rogue-like keys - collects a repeat count, which a
    /// bare space then separates from a command that is itself a digit. A "^"
    /// introduces a control character for terminals that will not send one. And
    /// the whole thing is translated into the rogue-like set, so only one
    /// vocabulary reaches the dispatch.
    ///
    /// A count on a command that cannot take one is refused rather than ignored,
    /// because silently doing it once would be the wrong ninety-nine times out
    /// of a hundred.
    /// </summary>
    public char ReadCommand()
    {
        _display.MessageWaitingFlag = false;

        char command = _display.ReadKey();
        int count = 0;

        if ((_game.RogueLikeCommands && command >= '0' && command <= '9')
            || (!_game.RogueLikeCommands && command == '#'))
        {
            _display.Print("Repeat count:", 0, 0);

            if (command == '#')
            {
                command = '0';
            }

            while (true)
            {
                if (command == Keys.Delete || command == Keys.Backspace)
                {
                    count /= 10;
                    _display.Print(count.ToString(CultureInfo.InvariantCulture), 0, 14);
                }
                else if (command >= '0' && command <= '9')
                {
                    if (count > 99)
                    {
                        _display.Bell();
                    }
                    else
                    {
                        count = (count * 10) + (command - '0');
                        _display.Print(count.ToString(CultureInfo.InvariantCulture), 0, 14);
                    }
                }
                else
                {
                    break;
                }

                command = _display.ReadKey();
            }

            if (count == 0)
            {
                count = 99;
                _display.Print(count.ToString(CultureInfo.InvariantCulture), 0, 14);
            }

            // A space separates the count from a command that is itself a digit.
            if (command == ' ')
            {
                _display.Print("Command:", 0, 20);
                command = _display.ReadKey();
            }
        }

        if (command == '^')
        {
            if (_display.CommandCount > 0)
            {
                _display.PrintState(Player);
            }

            if (_display.GetCommand("Control-", out char letter))
            {
                if (letter is >= 'A' and <= 'Z')
                {
                    command = (char)(letter - ('A' - 1));
                }
                else if (letter is >= 'a' and <= 'z')
                {
                    command = (char)(letter - ('a' - 1));
                }
                else
                {
                    _display.MessagePrint("Type ^ <letter> for a control char");
                    command = Commands.Nothing;
                }
            }
            else
            {
                command = Commands.Nothing;
            }
        }

        if (!_game.RogueLikeCommands)
        {
            command = Commands.ToRogueLike(command, ReadDirection);
        }

        if (count > 0)
        {
            if (!Commands.AllowsCount(command))
            {
                FreeTurn = true;
                _display.MessagePrint("Invalid command with a count.");
                command = Commands.Nothing;
            }
            else
            {
                _display.CommandCount = count;
                _display.PrintState(Player);
            }
        }

        return command;
    }

    /// <summary>
    /// Asks for a direction. Mirrors get_dir().
    ///
    /// The last direction is remembered, so a counted command keeps going the
    /// way it started without asking again. Anything that is not a direction
    /// rings the bell and asks again, since a mistyped direction in a corridor
    /// is worth a second chance.
    /// </summary>
    public (bool Taken, int Direction) ReadDirection()
    {
        if (DefaultDirection)
        {
            return (true, _previousDirection);
        }

        while (true)
        {
            // A prompt must not end a counted command, so the count is put back.
            int saved = _display.CommandCount;

            if (!_display.GetCommand("Which direction?", out char command))
            {
                FreeTurn = true;
                return (false, 0);
            }

            _display.CommandCount = saved;

            if (_game.RogueLikeCommands)
            {
                command = Commands.MapRogueDirection(command);
            }

            if (command is >= '1' and <= '9' && command != '5')
            {
                _previousDirection = command - '0';
                return (true, _previousDirection);
            }

            _display.Bell();
        }
    }

    /// <summary>
    /// Whether a direction should be taken from memory rather than asked for.
    /// Umoria's default_dir, set while a counted command repeats.
    /// </summary>
    public bool DefaultDirection { get; set; }

    private int _previousDirection;

    /// <summary>
    /// Redraws what the player can see from where they stand. Mirrors
    /// check_view().
    /// </summary>
    protected virtual void CheckView() => _lighting.CheckView();
}
