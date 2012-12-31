//------------------------------------------------------------------------------
//  <copyright from='2010' to='2015' company='THEHACKERWITHIN.COM'>
//    Copyright (c) TheHackerWithin.COM. All Rights Reserved.
//
//    Please look in the accompanying license.htm file for the license that
//    applies to this source code. (a copy can also be found at:
//    http://www.thehackerwithin.com/license.htm)
//  </copyright>
//-------------------------------------------------------------------------------

using Questor.Modules.Lookup;

namespace QuestorManager.Actions
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using DirectEve;
    using Questor.Modules.Caching;
    using Questor.Modules.Logging;
    using Questor.Modules.Actions;
    using Questor.Modules.States;

    public class ValueDump
    {
        private readonly QuestorManagerUI _form;
        private DateTime _lastExecute = DateTime.MinValue;
        private bool _valueProcess; //false
        private readonly Market _market;

        public ValueDump(QuestorManagerUI form1)
        {
            _form = form1;
            _market = new Market();
        }

        public void ProcessState()
        {
            switch (_States.CurrentValueDumpState)
            {
                case ValueDumpState.CheckMineralPrices:
                    if (Settings.Instance.DebugValuedump) Logging.Log("ValueDump", "case ValueDumpState.CheckMineralPrices:", Logging.Debug);
                    if(!Market.CheckMineralPrices("ValueDump", false)) return; //hard coded to not refine (FIXME)
                    _States.CurrentValueDumpState = ValueDumpState.SaveMineralPrices;
                    break;

                case ValueDumpState.SaveMineralPrices:
                    if (Settings.Instance.DebugValuedump) Logging.Log("ValueDump", "case ValueDumpState.SaveMineralPrices:", Logging.Debug);
                    if (!Market.SaveMineralprices("ValueDump")) return;
                    _States.CurrentValueDumpState = ValueDumpState.Idle;    
                    break;

                case ValueDumpState.GetItems:
                    if (Settings.Instance.DebugValuedump) Logging.Log("ValueDump", "case ValueDumpState.GetItems:", Logging.Debug);
                    if (!Cache.Instance.OpenItemsHangar("ValueDump")) break;
                    Logging.Log("ValueDump", "Loading hangar items", Logging.White);

                    // Clear out the old
                    Market.Items.Clear();
                    List<DirectItem> hangarItems = Cache.Instance.ItemHangar.Items;
                    if (hangarItems != null)
                    {
                        Market.Items.AddRange(hangarItems.Where(i => i.ItemId > 0 && i.MarketGroupId > 0 && i.Quantity > 0).Select(i => new ItemCacheMarket(i, _form.RefineCheckBox.Checked)));
                    }

                    _States.CurrentValueDumpState = ValueDumpState.UpdatePrices;
                    break;

                case ValueDumpState.UpdatePrices:
                    if (Settings.Instance.DebugValuedump) Logging.Log("ValueDump", "case ValueDumpState.UpdatePrices:", Logging.Debug);
                    if (!Market.UpdatePrices("ValueDump", _form.cbxSell.Checked, _form.RefineCheckBox.Checked, _form.cbxUndersell.Checked)) return;
                    //
                    // we are out of items
                    //
                    _States.CurrentValueDumpState = ValueDumpState.Idle;
                    break;

                case ValueDumpState.Idle:
                case ValueDumpState.Done:
                    break;

                case ValueDumpState.Begin:
                    if (Settings.Instance.DebugValuedump) Logging.Log("ValueDump", "case ValueDumpState.Begin:", Logging.Debug);
                    if (_form.RefineCheckBox.Checked && _form.cbxSell.Checked)
                    {
                        _form.cbxSell.Checked = false;
                        _valueProcess = true;
                        _States.CurrentValueDumpState = ValueDumpState.GetItems;
                    }
                    else if (_form.RefineCheckBox.Checked && _valueProcess)
                    {
                        _form.RefineCheckBox.Checked = false;
                        _form.cbxSell.Checked = true;
                        _valueProcess = false;
                        _States.CurrentValueDumpState = ValueDumpState.GetItems;
                    }
                    else
                        _States.CurrentValueDumpState = ValueDumpState.GetItems;
                    break;

                case ValueDumpState.NextItem:
                    if (Settings.Instance.DebugValuedump) Logging.Log("ValueDump", "case ValueDumpState.NextItem:", Logging.Debug);
                    if (!Market.NextItem("ValueDump")) return;
                    _States.CurrentValueDumpState = ValueDumpState.StartQuickSell;
                    break;

                case ValueDumpState.StartQuickSell:
                    if (Settings.Instance.DebugValuedump) Logging.Log("ValueDump", "case ValueDumpState.StartQuickSell:", Logging.Debug);
                    if (!Market.StartQuickSell("ValueDump", _form.cbxSell.Checked)) return;
                    _States.CurrentValueDumpState = ValueDumpState.InspectOrder;
                    break;

                case ValueDumpState.InspectOrder:
                    if (Settings.Instance.DebugValuedump) Logging.Log("ValueDump", "case ValueDumpState.InspectOrder:", Logging.Debug);
                    if (!Market.Inspectorder("ValueDump", _form.cbxSell.Checked, _form.RefineCheckBox.Checked, _form.cbxUndersell.Checked, (double)_form.RefineEfficiencyInput.Value)) return;
                    _States.CurrentValueDumpState = ValueDumpState.WaitingToFinishQuickSell;
                    break;

                case ValueDumpState.InspectRefinery:
                    if (Settings.Instance.DebugValuedump) Logging.Log("ValueDump", "case ValueDumpState.InspectRefinery:", Logging.Debug);
                    if (!Market.InspectRefinery("ValueDump", (double)_form.RefineEfficiencyInput.Value))
                    _States.CurrentValueDumpState = ValueDumpState.NextItem;

                    break;

                case ValueDumpState.WaitingToFinishQuickSell:
                    if (Settings.Instance.DebugValuedump) Logging.Log("ValueDump", "case ValueDumpState.WaitingToFinishQuickSell:", Logging.Debug);
                    if (!Market.WaitingToFinishQuickSell("ValueDump")) return;
                    _States.CurrentValueDumpState = ValueDumpState.NextItem;
                    break;

                case ValueDumpState.RefineItems:
                    if (Settings.Instance.DebugValuedump) Logging.Log("ValueDump", "case ValueDumpState.RefineItems:", Logging.Debug);
                    if (Market.RefineItems("ValueDump", _form.RefineCheckBox.Checked)) return;
                    _lastExecute = DateTime.UtcNow;
                    Logging.Log("Valuedump", "Waiting 17 seconds for minerals to appear in the item hangar", Logging.White);
                    _States.CurrentValueDumpState = ValueDumpState.WaitingToBack;
                    break;

                case ValueDumpState.WaitingToBack:
                    if (Settings.Instance.DebugValuedump) Logging.Log("ValueDump", "case ValueDumpState.WaitingToBack:", Logging.Debug);
                    if (DateTime.UtcNow.Subtract(_lastExecute).TotalSeconds > 17 && _valueProcess)
                    {
                        _States.CurrentValueDumpState = _valueProcess ? ValueDumpState.Begin : ValueDumpState.Done;
                    }
                    break;
            }
        }
    }
}