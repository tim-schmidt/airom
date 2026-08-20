// Ported from store_purchase(), store_sell() and enter_store() in Umoria 5.6
// source/store2.c.
//
// Copyright (C) 1989-2008 James E. Wilson, Robert A. Koeneke, David J. Grabiner
// Copyright (C) 2026 AIrom contributors
// Licensed under the GNU General Public License v3 or later. See LICENSE.

using Airom.Data;

namespace Airom.Core;

public partial class StoreScreen
{
    /// <summary>
    /// Buying something. Mirrors store_purchase().
    /// </summary>
    /// <returns>Whether the visit is over.</returns>
    private bool Purchase(int storeIndex, ref int top)
    {
        Store store = Shops.All[storeIndex];

        // How many lines are on the screen to choose from.
        int last = top == PageSize
            ? store.StockCount - 1 - PageSize
            : Math.Min(store.StockCount - 1, PageSize - 1);

        if (store.StockCount < 1)
        {
            _display.MessagePrint("I am currently out of stock.");
            return false;
        }

        if (GetStoreItem("Which item are you interested in? ", 0, last)
            is not int line)
        {
            return false;
        }

        int slot = line + top;

        var wanted = new InvenType();
        Inventory.TakeOne(wanted, store.Stock[slot].Item);

        if (!_game.Inventory.HasRoomFor(wanted))
        {
            _display.Print("You cannot carry that many different items.", 0, 0);
            return false;
        }

        int price;
        Deal outcome;

        if (store.Stock[slot].Cost > 0)
        {
            // A fixed price is not haggled over.
            price = store.Stock[slot].Cost;
            outcome = Deal.Struck;
        }
        else
        {
            outcome = PurchaseHaggle(storeIndex, wanted, out price);
        }

        bool thrownOut = false;

        if (outcome == Deal.Struck)
        {
            thrownOut = CompletePurchase(storeIndex, slot, price, wanted, ref top);
        }
        else if (outcome == Deal.ThrownOut)
        {
            thrownOut = true;
        }

        // Put back here rather than inside the haggling, where it would be
        // drawn and then immediately covered again.
        DisplayCommands();
        _display.EraseLine(1, 0);

        return thrownOut;
    }

    /// <summary>
    /// Paying for it, and the redrawing that follows. A player who agreed a
    /// price they cannot pay has insulted the shopkeeper.
    /// </summary>
    private bool CompletePurchase(
        int storeIndex, int slot, int price, InvenType wanted, ref int top)
    {
        Store store = Shops.All[storeIndex];

        if (Player.Gold < price)
        {
            if (IncreaseInsults(storeIndex))
            {
                return true;
            }

            SayAccepted();
            _display.MessagePrint("Liar!  You have not the gold!");
            return false;
        }

        SayAccepted();
        DecreaseInsults(storeIndex);
        Player.Gold -= price;

        int carried = _game.Inventory.Carry(wanted);
        int stockWas = store.StockCount;
        Shops.Destroy(storeIndex, slot, oneOnly: true);

        _display.Print(
            "You have " + _game.Names.Describe(_game.Inventory[carried], withArticle: true)
            + " (" + (char)('a' + carried) + ")", 0, 0);

        _loop.Equipment.CheckStrength();

        if (top >= store.StockCount)
        {
            top = 0;
            DisplayInventory(storeIndex, top);
        }
        else if (stockWas == store.StockCount)
        {
            // One out of a pile: the rest are now worth what was agreed, so
            // only the price needs redrawing.
            if (store.Stock[slot].Cost < 0)
            {
                store.Stock[slot].Cost = price;
                DisplayCost(storeIndex, slot);
            }
        }
        else
        {
            DisplayInventory(storeIndex, slot);
        }

        PrintGold();
        return false;
    }

    /// <summary>
    /// Selling something. Mirrors store_sell().
    /// </summary>
    /// <returns>Whether the visit is over.</returns>
    private bool Sell(int storeIndex, ref int top)
    {
        // Only what this shop deals in is offered, which is what the mask on
        // the item prompt is for.
        var mask = new bool[Inventory.Size];
        int first = _game.Inventory.Count;
        int last = -1;

        for (int i = 0; i < _game.Inventory.Count; i++)
        {
            mask[i] = StoreSets.Buys(storeIndex, _game.Inventory[i].TVal);

            if (!mask[i])
            {
                continue;
            }

            first = Math.Min(first, i);
            last = Math.Max(last, i);
        }

        if (last == -1)
        {
            _display.MessagePrint("You have nothing to sell to this store!");
            return false;
        }

        if (_loop.InventoryScreen.GetItem(
                "Which one? ", first, last, mask, "I do not buy such items.")
            is not int slot)
        {
            return false;
        }

        var offered = new InvenType();
        Inventory.TakeOne(offered, _game.Inventory[slot]);

        _display.MessagePrint(
            "Selling " + _game.Names.Describe(offered, withArticle: true)
            + " (" + (char)('a' + slot) + ")");

        if (!Shops.HasRoomFor(storeIndex, offered))
        {
            _display.MessagePrint("I have not the room in my store to keep it.");
            return false;
        }

        Deal outcome = SellHaggle(storeIndex, offered, out int price);
        bool thrownOut = false;

        if (outcome == Deal.Struck)
        {
            CompleteSale(storeIndex, slot, price, ref top);
        }
        else if (outcome == Deal.ThrownOut)
        {
            thrownOut = true;
        }
        else if (outcome == Deal.Worthless)
        {
            _display.MessagePrint("How dare you!");
            _display.MessagePrint("I will not buy that!");
            thrownOut = IncreaseInsults(storeIndex);
        }

        _display.EraseLine(1, 0);
        DisplayCommands();

        return thrownOut;
    }

    /// <summary>
    /// Handing it over. Selling identifies the thing on the way out, so the
    /// shop knows what it has and the player learns what they had.
    /// </summary>
    private void CompleteSale(int storeIndex, int slot, int price, ref int top)
    {
        SayAccepted();
        DecreaseInsults(storeIndex);
        Player.Gold += price;

        // Identified in the pack first, so that what the player knows is
        // recorded, then taken again so the copy handed over is identified too.
        slot = _game.Inventory.Identify(slot, _display);

        var sold = new InvenType();
        Inventory.TakeOne(sold, _game.Inventory[slot]);

        // Known outright, so the shop's copy shows its charges and pluses.
        _game.Knowledge.LearnEnchantment(sold);
        _game.Inventory.Destroy(slot);

        _display.MessagePrint(
            "You've sold " + _game.Names.Describe(sold, withArticle: true));

        int placed = Shops.Carry(storeIndex, sold);
        _loop.Equipment.CheckStrength();

        if (placed >= 0)
        {
            // Whichever page the new line landed on is the one now shown.
            if (placed < PageSize)
            {
                if (top < PageSize)
                {
                    DisplayInventory(storeIndex, placed);
                }
                else
                {
                    top = 0;
                    DisplayInventory(storeIndex, top);
                }
            }
            else if (top > PageSize - 1)
            {
                DisplayInventory(storeIndex, placed);
            }
            else
            {
                top = PageSize;
                DisplayInventory(storeIndex, top);
            }
        }

        PrintGold();
    }

    /// <summary>
    /// A whole visit to a shop. Mirrors enter_store().
    ///
    /// A shop thrown out of stays shut for a while, which is what the locked
    /// door means.
    /// </summary>
    public void Enter(int storeIndex)
    {
        Store store = Shops.All[storeIndex];

        if (store.OpenAgainAt >= _game.Turn)
        {
            _display.MessagePrint("The doors are locked.");
            return;
        }

        int top = 0;
        bool leaving = false;

        DisplayStore(storeIndex, top);

        do
        {
            _display.MoveCursor(20, 9);

            // Cleared as the turn loop does, so a message from the last command
            // does not turn this prompt into a -more-.
            _display.MessageWaitingFlag = false;

            if (!_display.GetCommand(null, out char command))
            {
                break;
            }

            switch (command)
            {
                case 'b':
                    leaving = false;
                    Browse(storeIndex, ref top);
                    break;

                case 'E' or 'e' or 'I' or 'i' or 'T' or 't' or 'W' or 'w' or 'X' or 'x':
                    UseInventory(storeIndex, command, top);
                    break;

                case 'p':
                    leaving = Purchase(storeIndex, ref top);
                    break;

                case 's':
                    leaving = Sell(storeIndex, ref top);
                    break;

                default:
                    _display.Bell();
                    break;
            }
        }
        while (!leaving);

        // The screen cannot be saved and put back around this, because the
        // inventory commands save and restore it themselves.
        _display.DrawCave(Player);
    }

    /// <summary>Turns to the other page of the stock, if there is one.</summary>
    private void Browse(int storeIndex, ref int top)
    {
        Store store = Shops.All[storeIndex];

        if (top != 0)
        {
            top = 0;
            DisplayInventory(storeIndex, top);
            return;
        }

        if (store.StockCount > PageSize)
        {
            top = PageSize;
            DisplayInventory(storeIndex, top);
        }
        else
        {
            _display.MessagePrint("Entire inventory is shown.");
        }
    }

    /// <summary>
    /// The pack commands, which work inside a shop as they do outside it.
    ///
    /// Wearing something can change the charisma, and the charisma is what the
    /// prices are worked out from, so the stock is redrawn when it does.
    /// </summary>
    private void UseInventory(int storeIndex, char command, int top)
    {
        int charisma = Player.UseStat[Stat.Charisma];

        do
        {
            _loop.InventoryScreen.Command(command);
            command = _loop.InventoryScreen.ContinuingCommand ?? '\0';
        }
        while (command != '\0');

        if (charisma != Player.UseStat[Stat.Charisma])
        {
            DisplayInventory(storeIndex, top);
        }

        // No free moves in here.
        _loop.FreeTurn = false;
    }
}
