using System;
using System.Collections.Generic;
using Questor.Modules.Actions;
using Questor.Modules.Activities;
using Questor.Modules.Caching;
using Questor.Modules.Logging;
using Questor.Modules.Lookup;
using Questor.Modules.States;
using System.Linq;
using DirectEve;

namespace Questor.Storylines
{
    public class GenericCourier : IStoryline
    {
        private DateTime _nextAction;
        private GenericCourierStorylineState _state;

        public StorylineState Arm(Storyline storyline)
        {
            if (_nextAction > DateTime.UtcNow)
                return StorylineState.Arm;
            if (!Cache.Instance.OpenShipsHangar("Arm"))
                return StorylineState.Arm;

            // Are we in an industrial?  Yes, goto the agent
            //var directEve = Cache.Instance.DirectEve;
            //if (directEve.ActiveShip.TypeId == 648 || directEve.ActiveShip.TypeId == 649 || directEve.ActiveShip.TypeId == 650 || directEve.ActiveShip.TypeId == 651 || directEve.ActiveShip.TypeId == 652 || directEve.ActiveShip.TypeId == 653 || directEve.ActiveShip.TypeId == 654 || directEve.ActiveShip.TypeId == 655 || directEve.ActiveShip.TypeId == 656 || directEve.ActiveShip.TypeId == 657 || directEve.ActiveShip.TypeId == 1944 || directEve.ActiveShip.TypeId == 19744)
            //    return StorylineState.GotoAgent;

            //// Open the ship hangar
            //if (!Cache.Instance.ReadyItemsHangar("GenericCourierStoryline: Arm")) return StorylineState.Arm;

            ////  Look for an industrial
            //var item = Cache.Instance.ShipHangar.Items.FirstOrDefault(i => i.Quantity == -1 && (i.TypeId == 648 || i.TypeId == 649 || i.TypeId == 650 || i.TypeId == 651 || i.TypeId == 652 || i.TypeId == 653 || i.TypeId == 654 || i.TypeId == 655 || i.TypeId == 656 || i.TypeId == 657 || i.TypeId == 1944 || i.TypeId == 19744));
            //if (item != null)
            //{
            //    Logging.Log("GenericCourier", "Switching to an industrial", Logging.White);

            //    _nextAction = DateTime.UtcNow.AddSeconds(10);

            //    item.ActivateShip();
            //    return StorylineState.Arm;
            //}
            //else
            //{
            //    Logging.Log("GenericCourier", "No industrial found, going in active ship", Logging.White);
            //    return StorylineState.GotoAgent;
            //}
            string transportshipName = Settings.Instance.TransportShipName.ToLower();

            if (string.IsNullOrEmpty(transportshipName))
            {
                _States.CurrentArmState = ArmState.NotEnoughAmmo;
                Logging.Log("Arm.ActivateTransportShip", "Could not find transportshipName: " + transportshipName + " in settings!", Logging.Orange);
                return StorylineState.BlacklistAgent;
            }
            try
            {
                if (Settings.Instance.DebugArm) Logging.Log("Arm.ActivateTransportShip", "try", Logging.White);
                if (Cache.Instance.DirectEve.ActiveShip.GivenName.ToLower() != transportshipName.ToLower())
                {
                    if (Settings.Instance.DebugArm) Logging.Log("Arm.ActivateTransportShip", "if (Cache.Instance.DirectEve.ActiveShip.GivenName.ToLower() != transportshipName.ToLower())", Logging.White);
                    if (!Cache.Instance.ShipHangar.Items.Any()) return StorylineState.Arm; //no ships?!?

                    if (Settings.Instance.DebugArm) Logging.Log("Arm.ActivateTransportShip", "if (!Cache.Instance.ShipHangar.Items.Any()) return StorylineState.Arm; done", Logging.White);

                    List<DirectItem> ships = Cache.Instance.ShipHangar.Items;
                    if (Settings.Instance.DebugArm) Logging.Log("Arm.ActivateTransportShip", "List<DirectItem> ships = Cache.Instance.ShipHangar.Items;", Logging.White);
                    foreach (DirectItem ship in ships.Where(ship => ship.GivenName != null && ship.GivenName.ToLower() == transportshipName.ToLower()))
                    {
                        Logging.Log("Arm", "Making [" + ship.GivenName + "] active", Logging.White);
                        ship.ActivateShip();
                        Cache.Instance.NextArmAction = DateTime.UtcNow.AddSeconds(Modules.Lookup.Time.Instance.SwitchShipsDelay_seconds);
                    }
                    return StorylineState.Arm;
                }
            }
            catch (Exception ex)
            {
                Logging.Log("GenericCourierStoryline", "Exception thrown while attempting to switch to transport ship:" + ex.Message, Logging.White);
                Logging.Log("GenericCourierStoryline", "blacklisting this storyline agent for this session because we could not switch to the configured transportship named [" + transportshipName + "]", Logging.White);
                return StorylineState.BlacklistAgent;
            }

            if (DateTime.UtcNow > Cache.Instance.NextArmAction) //default 7 seconds
            {
                if (Cache.Instance.DirectEve.ActiveShip.GivenName.ToLower() == transportshipName.ToLower())
                {
                    Logging.Log("Arm.ActivateTransportShip", "Done", Logging.White);
                    _States.CurrentArmState = ArmState.Done;
                    return StorylineState.GotoAgent;
                }
            }

            return StorylineState.Arm;
        }

        /// <summary>
        ///   There are no pre-accept actions
        /// </summary>
        /// <param name="storyline"></param>
        /// <returns></returns>
        public StorylineState PreAcceptMission(Storyline storyline)
        {
            _state = GenericCourierStorylineState.GotoPickupLocation;

            _States.CurrentTravelerState = TravelerState.Idle;
            Traveler.Destination = null;

            return StorylineState.AcceptMission;
        }

        private bool GotoMissionBookmark(long agentId, string title)
        {
            var destination = Traveler.Destination as MissionBookmarkDestination;
            if (destination == null || destination.AgentId != agentId || !destination.Title.ToLower().StartsWith(title.ToLower()))
                Traveler.Destination = new MissionBookmarkDestination(Cache.Instance.GetMissionBookmark(agentId, title));

            Traveler.ProcessState();

            if (_States.CurrentTravelerState == TravelerState.AtDestination)
            {
                Traveler.Destination = null;
                return true;
            }

            return false;
        }

        private bool MoveItem(bool pickup)
        {
            var directEve = Cache.Instance.DirectEve;

            // Open the item hangar (should still be open)
            if (!Cache.Instance.OpenItemsHangar("GenericCourierStoryline: MoveItem")) return false;

            if (!Cache.Instance.OpenCargoHold("GenericCourierStoryline: MoveItem")) return false;

            // 314 == Giant Sealed Cargo Containers
            const int containersGroupId = 314;
            const int marinesGroupId = 283;
            DirectContainer from = pickup ? Cache.Instance.ItemHangar : Cache.Instance.CargoHold;
            DirectContainer to = pickup ? Cache.Instance.CargoHold : Cache.Instance.ItemHangar;

            // We moved the item

            if (to.Items.Any(i => i.GroupId == containersGroupId || i.GroupId == marinesGroupId))
                return true;

            if (directEve.GetLockedItems().Count != 0)
                return false;

            // Move items
            foreach (var item in from.Items.Where(i => i.GroupId == containersGroupId || i.GroupId == marinesGroupId))
            {
                Logging.Log("GenericCourier", "Moving [" + item.TypeName + "][" + item.ItemId + "] to " + (pickup ? "cargo" : "hangar"), Logging.White);
                to.Add(item, item.Stacksize);
            }
            _nextAction = DateTime.UtcNow.AddSeconds(10);
            return false;
        }

        /// <summary>
        ///   Goto the pickup location
        ///   Pickup the item
        ///   Goto drop off location
        ///   Drop the item
        ///   Complete mission
        /// </summary>
        /// <param name="storyline"></param>
        /// <returns></returns>
        public StorylineState ExecuteMission(Storyline storyline)
        {
            if (_nextAction > DateTime.UtcNow)
                return StorylineState.ExecuteMission;

            switch (_state)
            {
                case GenericCourierStorylineState.GotoPickupLocation:
                    if (GotoMissionBookmark(Cache.Instance.CurrentStorylineAgentId, "Objective (Pick Up)"))
                        _state = GenericCourierStorylineState.PickupItem;
                    break;

                case GenericCourierStorylineState.PickupItem:
                    if (MoveItem(true))
                        _state = GenericCourierStorylineState.GotoDropOffLocation;
                    break;

                case GenericCourierStorylineState.GotoDropOffLocation:
                    if (GotoMissionBookmark(Cache.Instance.CurrentStorylineAgentId, "Objective (Drop Off)"))
                        _state = GenericCourierStorylineState.DropOffItem;
                    break;

                case GenericCourierStorylineState.DropOffItem:
                    if (MoveItem(false))
                        return StorylineState.CompleteMission;
                    break;
            }

            return StorylineState.ExecuteMission;
        }
    }
}