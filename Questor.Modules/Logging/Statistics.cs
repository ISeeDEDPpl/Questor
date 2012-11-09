
namespace Questor.Modules.Logging
{
    using System;
    using System.Linq;
    using DirectEve;
    using System.IO;
    using System.Globalization;
    using System.Collections.Generic;
    using Questor.Modules.Caching;
    using Questor.Modules.Lookup;
    using Questor.Modules.States;

    public class Statistics
    {
        public StatisticsState State { get; set; }

        //private DateTime _lastStatisticsAction;
        public DateTime MissionLoggingStartedTimestamp { get; set; }

        public DateTime StartedMission = DateTime.UtcNow;
        public DateTime FinishedMission = DateTime.UtcNow;
        public DateTime StartedSalvaging = DateTime.UtcNow;
        public DateTime FinishedSalvaging = DateTime.UtcNow;
        public DateTime StartedPocket = DateTime.UtcNow;
        public int LootValue { get; set; }
        public int LoyaltyPoints { get; set; }
        public int LostDrones { get; set; }
        public int DroneRecalls { get; set; }
        public int AmmoConsumption { get; set; }
        public int AmmoValue { get; set; }
        public int MissionsThisSession { get; set; }
        public int MissionCompletionErrors { get; set; }

        public static int AgentLPRetrievalAttempts { get; set; }

        public bool MissionLoggingCompleted; //false
        public bool DroneLoggingCompleted; //false
        public long AgentID { get; set; }
        //private bool PocketLoggingCompleted = false;
        //private bool SessionLoggingCompleted = false;

        public bool MissionLoggingStarted = true;

        public static DateTime DateTimeForLogs;

        /// <summary>
        ///   Singleton implementation
        /// </summary>
        private static readonly Statistics _instance = new Statistics();
        public DateTime LastMissionCompletionError;

        public static Statistics Instance
        {
            get { return _instance; }
        }

        public double TimeInCurrentMission()
        {
            double missiontimeMinutes = Math.Round(DateTime.UtcNow.Subtract(Statistics.Instance.StartedMission).TotalMinutes, 0);
            return missiontimeMinutes;
        }

        public static bool WreckStatistics(IEnumerable<ItemCache> items, EntityCache containerEntity)
        {
            //if (Settings.Instance.DateTimeForLogs = EVETIme)
            //{
            //    DateTimeForLogs = DateTime.UtcNow;
            //}
            //else //assume LocalTime
            //{
            DateTimeForLogs = DateTime.UtcNow;
            //}

            if (Settings.Instance.WreckLootStatistics)
            {
                if (containerEntity != null)
                {
                    // Log all items found in the wreck
                    File.AppendAllText(Settings.Instance.WreckLootStatisticsFile,
                                       "TIME: " + string.Format("{0:dd/MM/yyyy HH:mm:ss}", DateTimeForLogs) + "\n");
                    File.AppendAllText(Settings.Instance.WreckLootStatisticsFile, "NAME: " + containerEntity.Name + "\n");
                    File.AppendAllText(Settings.Instance.WreckLootStatisticsFile, "ITEMS:" + "\n");
                    foreach (ItemCache item in items.OrderBy(i => i.TypeId))
                    {
                        File.AppendAllText(Settings.Instance.WreckLootStatisticsFile,
                                           "TypeID: " + item.TypeId.ToString(CultureInfo.InvariantCulture) + "\n");
                        File.AppendAllText(Settings.Instance.WreckLootStatisticsFile, "Name: " + item.Name + "\n");
                        File.AppendAllText(Settings.Instance.WreckLootStatisticsFile,
                                           "Quantity: " + item.Quantity.ToString(CultureInfo.InvariantCulture) + "\n");
                        File.AppendAllText(Settings.Instance.WreckLootStatisticsFile, "=\n");
                    }
                    File.AppendAllText(Settings.Instance.WreckLootStatisticsFile, ";" + "\n");
                }
            }
            return true;
        }

        public static bool PocketObjectStatistics(List<EntityCache> things)
        {
            if (Settings.Instance.PocketObjectStatisticsLog)
            {
                string currentPocketName = Cache.Instance.FilterPath("randomgrid");
                try
                {
                    if (!String.IsNullOrEmpty(Cache.Instance.MissionName))
                    {
                        currentPocketName = Cache.Instance.FilterPath(Cache.Instance.MissionName);
                    }
                }
                catch (Exception ex)
                {
                    Logging.Log("Statistics", "PocketObjectStatistics: is cache.Instance.MissionName null?: exception was [" + ex.Message + "]",
                                Logging.White);
                }

                Settings.Instance.PocketObjectStatisticsFile = Path.Combine(
                    Settings.Instance.PocketObjectStatisticsPath,
                    Cache.Instance.FilterPath(Cache.Instance.DirectEve.Me.Name) + " - " + currentPocketName + " - " +
                    Cache.Instance.PocketNumber + " - ObjectStatistics.csv");
                Logging.Log("Statistics.ObjectStatistics",
                            "Logging info on the [" + things.Count + "] objects in this pocket to [" +
                            Settings.Instance.PocketObjectStatisticsFile + "]", Logging.White);

                if (File.Exists(Settings.Instance.PocketObjectStatisticsFile))
                {
                    File.Delete(Settings.Instance.PocketObjectStatisticsFile);
                }
                //
                // build header
                //
                string objectline =
                    "Name;Distance;TypeId;GroupId;CategoryId;IsNPC;IsPlayer;TargetValue;Velocity;ID;\r\n";
                //Logging.Log("Statistics",";PocketObjectStatistics;" + objectline,Logging.White);
                File.AppendAllText(Settings.Instance.PocketObjectStatisticsFile, objectline);

                //
                // iterate through entities
                //
                foreach (EntityCache thing in things.OrderBy(i => i.Distance))
                {
                    objectline = thing.Name + ";";
                    objectline += Math.Round(thing.Distance / 1000, 0) + ";";
                    objectline += thing.TypeId + ";";
                    objectline += thing.GroupId + ";";
                    objectline += thing.CategoryId + ";";
                    objectline += thing.IsNpc + ";";
                    objectline += thing.IsPlayer + ";";
                    objectline += thing.TargetValue + ";";
                    objectline += Math.Round(thing.Velocity, 0) + ";";
                    objectline += thing.Id + ";\r\n";
                    //
                    // can we somehow get the X,Y,Z coord? If we could we could use this info to build some kind of grid layout...
                    // or at least know the distances between all the NPCs... thus be able to infer which NPCs were in which 'groups'
                    //

                    //Logging.Log("Statistics", ";PocketObjectStatistics;" + objectline, Logging.White);
                    File.AppendAllText(Settings.Instance.PocketObjectStatisticsFile, objectline);
                }
            }
            return true;
        }

        public static bool EntityStatistics(List<EntityCache> things)
        {
            string objectline = "Name;Distance;TypeId;GroupId;CategoryId;IsNPC;IsPlayer;TargetValue;Velocity;ID;\r\n";
            Logging.Log("Statistics", ";EntityStatistics;" + objectline, Logging.White);

            if (!things.Any()) //if their are no entries, return
            {
                Logging.Log("Statistics", "EntityStatistics: No entries to log", Logging.White);
                return true;
            }

            foreach (EntityCache thing in things.OrderBy(i => i.Distance))
            {
                objectline = thing.Name + ";";
                objectline += Math.Round(thing.Distance / 1000, 0) + ";";
                objectline += thing.TypeId + ";";
                objectline += thing.GroupId + ";";
                objectline += thing.CategoryId + ";";
                objectline += thing.IsNpc + ";";
                objectline += thing.IsPlayer + ";";
                objectline += thing.TargetValue + ";";
                objectline += Math.Round(thing.Velocity, 0) + ";";
                objectline += thing.Id + ";\r\n";
                //
                // can we somehow get the X,Y,Z coord? If we could we could use this info to build some kinda mission simulator...
                // or at least know the distances between all the NPCs... thus be able to infer which NPCs were in which 'groups'
                //

                Logging.Log("Statistics", ";EntityStatistics;" + objectline, Logging.White);
            }
            return true;
        }

        public static bool AmmoConsumptionStatistics()
        {
            // Ammo Consumption statistics
            // Is cargo open?
            if (!Cache.Instance.ReadyCargoHold("Statistics: AmmoConsumptionStats")) return false;

            IEnumerable<Ammo> correctAmmo1 = Settings.Instance.Ammo.Where(a => a.DamageType == Cache.Instance.DamageType);
            IEnumerable<DirectItem> ammoCargo = Cache.Instance.CargoHold.Items.Where(i => correctAmmo1.Any(a => a.TypeId == i.TypeId));
            foreach (DirectItem item in ammoCargo)
            {
                Ammo ammo1 = Settings.Instance.Ammo.FirstOrDefault(a => a.TypeId == item.TypeId);
                InvType ammoType = Cache.Instance.InvTypesById[item.TypeId];
                if (ammo1 != null) Statistics.Instance.AmmoConsumption = (ammo1.Quantity - item.Quantity);
                Statistics.Instance.AmmoValue = ((int?)ammoType.MedianSell ?? 0) * Statistics.Instance.AmmoConsumption;
            }
            return true;
        }

        public static bool WriteDroneStatsLog()
        {
            //if (Settings.Instance.DateTimeForLogs = EVETIme)
            //{
            //    DateTimeForLogs = DateTime.UtcNow;
            //}
            //else //assume LocalTime
            //{
            DateTimeForLogs = DateTime.UtcNow;
            //}

            if (Settings.Instance.DroneStatsLog && !Statistics.Instance.DroneLoggingCompleted)
            {
                // Lost drone statistics
                if (Settings.Instance.UseDrones &&
                     Cache.Instance.DirectEve.ActiveShip.GroupId != (int)Group.Capsule &&
                     Cache.Instance.DirectEve.ActiveShip.GroupId != (int)Group.Shuttle &&
                     Cache.Instance.DirectEve.ActiveShip.GroupId != (int)Group.Frigate && 
                     Cache.Instance.DirectEve.ActiveShip.GroupId != (int)Group.Industrial && 
                     Cache.Instance.DirectEve.ActiveShip.GroupId != (int)Group.TransportShip &&
                     Cache.Instance.DirectEve.ActiveShip.GroupId != (int)Group.Freighter)
                {
                    if (Cache.Instance.InvTypesById.ContainsKey(Settings.Instance.DroneTypeId))
                    {
                        if (!Cache.Instance.ReadyDroneBay("Statistics: WriteDroneStatsLog")) return false;
                        if (!Cache.Instance.DroneBay.IsValid) return true; //if the dronebay does not exist, assume we cant log any drone stats

                        InvType drone = Cache.Instance.InvTypesById[Settings.Instance.DroneTypeId];
                        Statistics.Instance.LostDrones = (int)Math.Floor((Cache.Instance.DroneBay.Capacity - Cache.Instance.DroneBay.UsedCapacity) / drone.Volume);
                        Logging.Log("Statistics: WriteDroneStatsLog", "Logging the number of lost drones: " + Statistics.Instance.LostDrones.ToString(CultureInfo.InvariantCulture), Logging.White);

                        if (!File.Exists(Settings.Instance.DroneStatslogFile))
                            File.AppendAllText(Settings.Instance.DroneStatslogFile, "Date;Mission;Number of lost drones;# of Recalls\r\n");
                        string droneline = DateTimeForLogs.ToShortDateString() + ";";
                        droneline += DateTimeForLogs.ToShortTimeString() + ";";
                        droneline += Cache.Instance.MissionName + ";";
                        droneline += Statistics.Instance.LostDrones + ";";
                        droneline += +Statistics.Instance.DroneRecalls + ";\r\n";
                        File.AppendAllText(Settings.Instance.DroneStatslogFile, droneline);
                        Statistics.Instance.DroneLoggingCompleted = true;
                    }
                    else
                    {
                        Logging.Log("DroneStats", "Could not find the drone TypeID specified in the character settings xml; this should not happen!", Logging.White);
                    }
                }
                else
                {
                    Logging.Log("DroneStats", "We do not use drones in this type of ship, skipping dronestats", Logging.White);
                    Statistics.Instance.DroneLoggingCompleted = true;
                }
            }
            // Lost drone statistics stuff ends here
            return true;
        }

        public static void WriteSessionLogStarting()
        {
            //if (Settings.Instance.DateTimeForLogs = EVETIme)
            //{
            //    DateTimeForLogs = DateTime.UtcNow;
            //}
            //else //assume LocalTime
            //{
            DateTimeForLogs = DateTime.UtcNow;
            //}

            if (Settings.Instance.SessionsLog)
            {
                if ((int)Cache.Instance.DirectEve.Me.Wealth != 0 || (int)Cache.Instance.DirectEve.Me.Wealth != -2147483648) // this hopefully resolves having negative maxint in the session logs occasionally
                {
                    //
                    // prepare the Questor Session Log - keeps track of starts, restarts and exits, and hopefully the reasons
                    //
                    // Get the path
                    if (!Directory.Exists(Settings.Instance.SessionsLogPath))
                        Directory.CreateDirectory(Settings.Instance.SessionsLogPath);

                    // Write the header
                    if (!File.Exists(Settings.Instance.SessionsLogFile))
                        File.AppendAllText(Settings.Instance.SessionsLogFile, "Date;RunningTime;SessionState;LastMission;WalletBalance;MemoryUsage;Reason;IskGenerated;LootGenerated;LPGenerated;Isk/Hr;Loot/Hr;LP/HR;Total/HR;\r\n");

                    // Build the line
                    var line = DateTimeForLogs + ";";                           //Date
                    line += "0" + ";";                                       //RunningTime
                    line += Cache.Instance.SessionState + ";";               //SessionState
                    line += "" + ";";                                        //LastMission
                    line += Cache.Instance.DirectEve.Me.Wealth + ";";        //WalletBalance
                    line += Cache.Instance.TotalMegaBytesOfMemoryUsed + ";"; //MemoryUsage
                    line += "Starting" + ";";                                //Reason
                    line += ";";                                             //IskGenerated
                    line += ";";                                             //LootGenerated
                    line += ";";                                             //LPGenerated
                    line += ";";                                             //Isk/Hr
                    line += ";";                                             //Loot/Hr
                    line += ";";                                             //LP/HR
                    line += ";\r\n";                                         //Total/HR

                    // The mission is finished
                    File.AppendAllText(Settings.Instance.SessionsLogFile, line);

                    Cache.Instance.SessionState = "";
                    Logging.Log("Statistics: WriteSessionLogStarting", "Writing session data to [ " + Settings.Instance.SessionsLogFile + " ]", Logging.White);
                }
            }
        }

        public static bool WriteSessionLogClosing()
        {
            //if (Settings.Instance.DateTimeForLogs = EVETIme)
            //{
            //    DateTimeForLogs = DateTime.UtcNow;
            //}
            //else //assume LocalTime
            //{
            DateTimeForLogs = DateTime.UtcNow;
            //}

            if (Settings.Instance.SessionsLog) // if false we do not write a sessionlog, doubles as a flag so we don't write the sessionlog more than once
            {
                //
                // prepare the Questor Session Log - keeps track of starts, restarts and exits, and hopefully the reasons
                //

                // Get the path

                if (!Directory.Exists(Settings.Instance.SessionsLogPath))
                    Directory.CreateDirectory(Settings.Instance.SessionsLogPath);

                Cache.Instance.SessionIskPerHrGenerated = ((int)Cache.Instance.SessionIskGenerated / (DateTime.UtcNow.Subtract(Cache.Instance.QuestorStarted_DateTime).TotalMinutes / 60));
                Cache.Instance.SessionLootPerHrGenerated = ((int)Cache.Instance.SessionLootGenerated / (DateTime.UtcNow.Subtract(Cache.Instance.QuestorStarted_DateTime).TotalMinutes / 60));
                Cache.Instance.SessionLPPerHrGenerated = (((int)Cache.Instance.SessionLPGenerated * (int)Settings.Instance.IskPerLP) / (DateTime.UtcNow.Subtract(Cache.Instance.QuestorStarted_DateTime).TotalMinutes / 60));
                Cache.Instance.SessionTotalPerHrGenerated = ((int)Cache.Instance.SessionIskPerHrGenerated + (int)Cache.Instance.SessionLootPerHrGenerated + (int)Cache.Instance.SessionLPPerHrGenerated);
                Logging.Log("QuestorState.CloseQuestor", "Writing Session Data [1]", Logging.White);

                // Write the header
                if (!File.Exists(Settings.Instance.SessionsLogFile))
                    File.AppendAllText(Settings.Instance.SessionsLogFile, "Date;RunningTime;SessionState;LastMission;WalletBalance;MemoryUsage;Reason;IskGenerated;LootGenerated;LPGenerated;Isk/Hr;Loot/Hr;LP/HR;Total/HR;\r\n");

                // Build the line
                var line = DateTimeForLogs + ";";                                  // Date
                line += Cache.Instance.SessionRunningTime + ";";                // RunningTime
                line += Cache.Instance.SessionState + ";";                      // SessionState
                line += Cache.Instance.MissionName + ";";                       // LastMission
                line += ((int)Cache.Instance.DirectEve.Me.Wealth + ";");        // WalletBalance
                line += ((int)Cache.Instance.TotalMegaBytesOfMemoryUsed + ";"); // MemoryUsage
                line += Cache.Instance.ReasonToStopQuestor + ";";               // Reason to Stop Questor
                line += Cache.Instance.SessionIskGenerated + ";";               // Isk Generated This Session
                line += Cache.Instance.SessionLootGenerated + ";";              // Loot Generated This Session
                line += Cache.Instance.SessionLPGenerated + ";";                // LP Generated This Session
                line += Cache.Instance.SessionIskPerHrGenerated + ";";          // Isk Generated per hour this session
                line += Cache.Instance.SessionLootPerHrGenerated + ";";         // Loot Generated per hour This Session
                line += Cache.Instance.SessionLPPerHrGenerated + ";";           // LP Generated per hour This Session
                line += Cache.Instance.SessionTotalPerHrGenerated + ";\r\n";    // Total Per Hour This Session

                // The mission is finished
                Logging.Log("Statistics: WriteSessionLogClosing", line, Logging.White);
                File.AppendAllText(Settings.Instance.SessionsLogFile, line);

                Logging.Log("Statistics: WriteSessionLogClosing", "Writing to session log [ " + Settings.Instance.SessionsLogFile, Logging.White);
                Logging.Log("Statistics: WriteSessionLogClosing", "Questor is stopping because: " + Cache.Instance.ReasonToStopQuestor, Logging.White);
                Settings.Instance.SessionsLog = false; //so we don't write the sessionlog more than once per session
            }
            return true;
        }

        public static void WritePocketStatistics()
        {
            //if (Settings.Instance.DateTimeForLogs = EVETIme)
            //{
            //    DateTimeForLogs = DateTime.UtcNow;
            //}
            //else //assume LocalTime
            //{
            DateTimeForLogs = DateTime.UtcNow;
            //}

            // We are not supposed to create bookmarks
            //if (!Settings.Instance.LogBounties)
            //    return;

            //agentID needs to change if its a storyline mission - so its assigned in storyline.cs to the various modules directly.
            string currentPocketName = Cache.Instance.FilterPath(Cache.Instance.MissionName);
            if (Settings.Instance.PocketStatistics)
            {
                if (Settings.Instance.PocketStatsUseIndividualFilesPerPocket)
                {
                    Settings.Instance.PocketStatisticsFile = Path.Combine(Settings.Instance.PocketStatisticsPath, Cache.Instance.FilterPath(Cache.Instance.DirectEve.Me.Name) + " - " + currentPocketName + " - " + Cache.Instance.PocketNumber + " - PocketStatistics.csv");
                }
                if (!Directory.Exists(Settings.Instance.PocketStatisticsPath))
                    Directory.CreateDirectory(Settings.Instance.PocketStatisticsPath);

                //
                // this is writing down stats from the PREVIOUS pocket (if any?!)
                //

                // Write the header
                if (!File.Exists(Settings.Instance.PocketStatisticsFile))
                    File.AppendAllText(Settings.Instance.PocketStatisticsFile, "Date and Time;Mission Name ;Pocket;Time to complete;Isk;panics;LowestShields;LowestArmor;LowestCapacitor;RepairCycles;Wrecks\r\n");

                // Build the line
                string pocketstatsLine = DateTimeForLogs + ";";                                          //Date
                pocketstatsLine += currentPocketName + ";";                                           //Mission Name
                pocketstatsLine += "pocket" + (Cache.Instance.PocketNumber) + ";";                                        //Pocket number
                pocketstatsLine += ((int)DateTime.UtcNow.Subtract(Statistics.Instance.StartedMission).TotalMinutes) + ";";    //Time to Complete
                pocketstatsLine += ((long)(Cache.Instance.DirectEve.Me.Wealth - Cache.Instance.WealthatStartofPocket)) + ";";       //Isk
                pocketstatsLine += Cache.Instance.PanicAttemptsThisPocket + ";";               //Panics
                pocketstatsLine += ((int)Cache.Instance.LowestShieldPercentageThisPocket) + ";";      //LowestShields
                pocketstatsLine += ((int)Cache.Instance.LowestArmorPercentageThisPocket) + ";";       //LowestArmor
                pocketstatsLine += ((int)Cache.Instance.LowestCapacitorPercentageThisPocket) + ";";   //LowestCapacitor
                pocketstatsLine += Cache.Instance.RepairCycleTimeThisPocket + ";";             //repairCycles
                pocketstatsLine += Cache.Instance.WrecksThisPocket + ";";
                pocketstatsLine += "\r\n";

                // The old pocket is finished
                Logging.Log("Statistics: WritePocketStatistics", "Writing pocket statistics to [ " + Settings.Instance.PocketStatisticsFile + " ] and clearing stats for next pocket", Logging.White);
                File.AppendAllText(Settings.Instance.PocketStatisticsFile, pocketstatsLine);
            }
            // Update statistic values for next pocket stats
            Cache.Instance.WealthatStartofPocket = Cache.Instance.DirectEve.Me.Wealth;
            Statistics.Instance.StartedPocket = DateTime.UtcNow;
            Cache.Instance.PanicAttemptsThisPocket = 0;
            Cache.Instance.LowestShieldPercentageThisPocket = 101;
            Cache.Instance.LowestArmorPercentageThisPocket = 101;
            Cache.Instance.LowestCapacitorPercentageThisPocket = 101;
            Cache.Instance.RepairCycleTimeThisPocket = 0;
            Cache.Instance.WrecksThisMission += Cache.Instance.WrecksThisPocket;
            Cache.Instance.WrecksThisPocket = 0;
        }

        public static void WriteMissionStatistics(long statisticsForThisAgent)
        {
            //if (Settings.Instance.DateTimeForLogs = EVETIme)
            //{
            //    DateTimeForLogs = DateTime.UtcNow;
            //}
            //else //assume LocalTime
            //{
            DateTimeForLogs = DateTime.UtcNow;
            //}

            if (Cache.Instance.InSpace)
            {
                Logging.Log("Statistics","We have started questor in space, assume we do not need to write any statistics at the moment.",Logging.Teal);
                Statistics.Instance.MissionLoggingCompleted = true; //if the mission was completed more than 10 min ago assume the logging has been done already.
                return;
            }

            Cache.Instance.Mission = Cache.Instance.GetAgentMission(statisticsForThisAgent, true);
            if (Settings.Instance.DebugStatistics) // we only need to see the following wall of comments if debugging mission statistics
            {
                Logging.Log("Statistics", "...Checking to see if we should create a mission log now...", Logging.White);
                Logging.Log("Statistics", " ", Logging.White);
                Logging.Log("Statistics", " ", Logging.White);
                Logging.Log("Statistics", "The Rules for After Mission Logging are as Follows...", Logging.White);
                Logging.Log("Statistics", "1)  we must have loyalty points with the current agent (disabled at the moment)", Logging.White); //which we already verified if we got this far
                Logging.Log("Statistics", "2) Cache.Instance.MissionName must not be empty - we must have had a mission already this session", Logging.White);
                Logging.Log("Statistics", "AND", Logging.White);
                Logging.Log("Statistics", "3a Cache.Instance.mission == null - their must not be a current mission OR", Logging.White);
                Logging.Log("Statistics", "3b Cache.Instance.mission.State != (int)MissionState.Accepted) - the missionstate is not 'Accepted'", Logging.White);
                Logging.Log("Statistics", " ", Logging.White);
                Logging.Log("Statistics", " ", Logging.White);
                Logging.Log("Statistics", "If those are all met then we get to create a log for the previous mission.", Logging.White);

                if (!string.IsNullOrEmpty(Cache.Instance.MissionName)) //condition 1
                {
                    Logging.Log("Statistics", "1 We must have a mission because MissionName is filled in", Logging.White);
                    Logging.Log("Statistics", "1 Mission is: " + Cache.Instance.MissionName, Logging.White);

                    if (Cache.Instance.Mission != null) //condition 2
                    {
                        Logging.Log("Statistics", "2 Cache.Instance.mission is: " + Cache.Instance.Mission, Logging.White);
                        Logging.Log("Statistics", "2 Cache.Instance.mission.Name is: " + Cache.Instance.Mission.Name, Logging.White);
                        Logging.Log("Statistics", "2 Cache.Instance.mission.State is: " + Cache.Instance.Mission.State, Logging.White);

                        if (Cache.Instance.Mission.State != (int)MissionState.Accepted) //condition 3
                        {
                            Logging.Log("Statistics", "MissionState is NOT Accepted: which is correct if we want to do logging", Logging.White);
                        }
                        else
                        {
                            Logging.Log("Statistics", "MissionState is Accepted: which means the mission is not yet complete", Logging.White);
                            Statistics.Instance.MissionLoggingCompleted = true; //if it is not true - this means we should not be trying to log mission stats atm
                        }
                    }
                    else
                    {
                        Logging.Log("Statistics", "mission is NULL - which means we have no current mission", Logging.White);
                        Statistics.Instance.MissionLoggingCompleted = true; //if it is not true - this means we should not be trying to log mission stats atm
                    }
                }
                else
                {
                    Logging.Log("Statistics", "1 We must NOT have had a mission yet because MissionName is not filled in", Logging.White);
                    Statistics.Instance.MissionLoggingCompleted = true; //if it is not true - this means we should not be trying to log mission stats atm
                }
            }
            
            if (AgentLPRetrievalAttempts > 20)
            {
                Logging.Log("Statistics", "WriteMissionStatistics: We do not have loyalty points with the current agent yet, still -1, attempt # [" +  AgentLPRetrievalAttempts + "] giving up", Logging.White);
                AgentLPRetrievalAttempts = 0;
                Statistics.Instance.MissionLoggingCompleted = true; //if it is not true - this means we should not be trying to log mission stats atm
                return;
            }

            // Seeing as we completed a mission, we will have loyalty points for this agent
            if (Cache.Instance.Agent.LoyaltyPoints == -1)
            {
                AgentLPRetrievalAttempts++;
                Logging.Log("Statistics", "WriteMissionStatistics: We do not have loyalty points with the current agent yet, still -1, attempt # [" + AgentLPRetrievalAttempts + "] retrying...", Logging.White);
                return;
            }
            AgentLPRetrievalAttempts = 0;

            Statistics.Instance.MissionsThisSession++;
            if (Settings.Instance.DebugStatistics) Logging.Log("Statistics", "We jumped through all the hoops: now do the mission logging", Logging.White);
            Cache.Instance.SessionIskGenerated = (Cache.Instance.SessionIskGenerated + (Cache.Instance.DirectEve.Me.Wealth - Cache.Instance.Wealth));
            Cache.Instance.SessionLootGenerated = (Cache.Instance.SessionLootGenerated + Statistics.Instance.LootValue);
            Cache.Instance.SessionLPGenerated = (Cache.Instance.SessionLPGenerated + (Cache.Instance.Agent.LoyaltyPoints - Statistics.Instance.LoyaltyPoints));
            Logging.Log("Statistics", "Printing All Statistics Related Variables to the console log:", Logging.White);
            Logging.Log("Statistics", "Mission Name: [" + Cache.Instance.MissionName + "]", Logging.White);
            Logging.Log("Statistics", "Faction: [" + Cache.Instance.FactionName + "]", Logging.White);
            Logging.Log("Statistics", "System: [" + Cache.Instance.MissionSolarSystem + "]", Logging.White);
            Logging.Log("Statistics", "Total Missions completed this session: [" + Statistics.Instance.MissionsThisSession + "]", Logging.White);
            Logging.Log("Statistics", "StartedMission: [ " + Statistics.Instance.StartedMission + "]", Logging.White);
            Logging.Log("Statistics", "FinishedMission: [ " + Statistics.Instance.FinishedMission + "]", Logging.White);
            Logging.Log("Statistics", "StartedSalvaging: [ " + Statistics.Instance.StartedSalvaging + "]", Logging.White);
            Logging.Log("Statistics", "FinishedSalvaging: [ " + Statistics.Instance.FinishedSalvaging + "]", Logging.White);
            Logging.Log("Statistics", "Wealth before mission: [ " + Cache.Instance.Wealth + "]", Logging.White);
            Logging.Log("Statistics", "Wealth after mission: [ " + Cache.Instance.DirectEve.Me.Wealth + "]", Logging.White);
            Logging.Log("Statistics", "Value of Loot from the mission: [" + Statistics.Instance.LootValue + "]", Logging.White);
            Logging.Log("Statistics", "Total LP after mission:  [" + Cache.Instance.Agent.LoyaltyPoints + "]", Logging.White);
            Logging.Log("Statistics", "Total LP before mission: [" + Statistics.Instance.LoyaltyPoints + "]", Logging.White);
            Logging.Log("Statistics", "LostDrones: [" + Statistics.Instance.LostDrones + "]", Logging.White);
            Logging.Log("Statistics", "DroneRecalls: [" + Statistics.Instance.DroneRecalls + "]", Logging.White);
            Logging.Log("Statistics", "AmmoConsumption: [" + Statistics.Instance.AmmoConsumption + "]", Logging.White);
            Logging.Log("Statistics", "AmmoValue: [" + Statistics.Instance.AmmoConsumption + "]", Logging.White);
            Logging.Log("Statistics", "Panic Attempts: [" + Cache.Instance.PanicAttemptsThisMission + "]", Logging.White);
            Logging.Log("Statistics", "Lowest Shield %: [" + Math.Round(Cache.Instance.LowestShieldPercentageThisMission, 0) + "]", Logging.White);
            Logging.Log("Statistics", "Lowest Armor %: [" + Math.Round(Cache.Instance.LowestArmorPercentageThisMission, 0) + "]", Logging.White);
            Logging.Log("Statistics", "Lowest Capacitor %: [" + Math.Round(Cache.Instance.LowestCapacitorPercentageThisMission, 0) + "]", Logging.White);
            Logging.Log("Statistics", "Repair Cycle Time: [" + Cache.Instance.RepairCycleTimeThisMission + "]", Logging.White);
            Logging.Log("Statistics", "MissionXMLIsAvailable: [" + Cache.Instance.MissionXMLIsAvailable + "]", Logging.White);
            Logging.Log("Statistics", "MissionCompletionerrors: [" + Statistics.Instance.MissionCompletionErrors + "]", Logging.White);
            Logging.Log("Statistics", "the stats below may not yet be correct and need some TLC", Logging.White);
            var weaponNumber = 0;
            foreach (ModuleCache weapon in Cache.Instance.Weapons)
            {
                weaponNumber++;
                Logging.Log("Statistics", "Time Spent Reloading: [" + weaponNumber + "][" + weapon.ReloadTimeThisMission + "]", Logging.White);
            }
            Logging.Log("Statistics", "Time Spent IN Mission: [" + Cache.Instance.TimeSpentInMission_seconds + "sec]", Logging.White);
            Logging.Log("Statistics", "Time Spent In Range: [" + Cache.Instance.TimeSpentInMissionInRange + "]", Logging.White);
            Logging.Log("Statistics", "Time Spent Out of Range: [" + Cache.Instance.TimeSpentInMissionOutOfRange + "]", Logging.White);

            if (Settings.Instance.MissionStats1Log)
            {
                if (!Directory.Exists(Settings.Instance.MissionStats1LogPath))
                    Directory.CreateDirectory(Settings.Instance.MissionStats1LogPath);

                // Write the header
                if (!File.Exists(Settings.Instance.MissionStats1LogFile))
                    File.AppendAllText(Settings.Instance.MissionStats1LogFile, "Date;Mission;TimeMission;TimeSalvage;TotalTime;Isk;Loot;LP;\r\n");

                // Build the line
                string line = DateTimeForLogs + ";";                                                                                           // Date
                line += Cache.Instance.MissionName + ";";                                                                                   // Mission
                line += ((int)Statistics.Instance.FinishedMission.Subtract(Statistics.Instance.StartedMission).TotalMinutes) + ";";         // TimeMission
                line += ((int)Statistics.Instance.FinishedSalvaging.Subtract(Statistics.Instance.StartedSalvaging).TotalMinutes) + ";";     // Time Doing After Mission Salvaging
                line += ((int)DateTime.UtcNow.Subtract(Statistics.Instance.StartedMission).TotalMinutes) + ";";                                // Total Time doing Mission
                line += ((int)(Cache.Instance.DirectEve.Me.Wealth - Cache.Instance.Wealth)) + ";";                                          // Isk (balance difference from start and finish of mission: is not accurate as the wallet ticks from bounty kills are every x minuts)
                line += Statistics.Instance.LootValue + ";";                                                                         // Loot
                line += (Cache.Instance.Agent.LoyaltyPoints - Statistics.Instance.LoyaltyPoints) + ";\r\n";                                 // LP

                // The mission is finished
                File.AppendAllText(Settings.Instance.MissionStats1LogFile, line);
                Logging.Log("Statistics", "writing mission log1 to  [ " + Settings.Instance.MissionStats1LogFile + " ]", Logging.White);
                //Logging.Log("Date;Mission;TimeMission;TimeSalvage;TotalTime;Isk;Loot;LP;");
                //Logging.Log(line);
            }
            if (Settings.Instance.MissionStats2Log)
            {
                if (!Directory.Exists(Settings.Instance.MissionStats2LogPath))
                    Directory.CreateDirectory(Settings.Instance.MissionStats2LogPath);

                // Write the header
                if (!File.Exists(Settings.Instance.MissionStats2LogFile))
                    File.AppendAllText(Settings.Instance.MissionStats2LogFile, "Date;Mission;Time;Isk;Loot;LP;LostDrones;AmmoConsumption;AmmoValue\r\n");

                // Build the line
                string line2 = string.Format("{0:MM/dd/yyyy HH:mm:ss}", DateTimeForLogs) + ";";                                                // Date
                line2 += Cache.Instance.MissionName + ";";                                                                                  // Mission
                line2 += ((int)Statistics.Instance.FinishedMission.Subtract(Statistics.Instance.StartedMission).TotalMinutes) + ";";        // TimeMission
                line2 += ((int)(Cache.Instance.DirectEve.Me.Wealth - Cache.Instance.Wealth)) + ";";                                         // Isk
                line2 += Statistics.Instance.LootValue + ";";                                                                        // Loot
                line2 += (Cache.Instance.Agent.LoyaltyPoints - Statistics.Instance.LoyaltyPoints) + ";";                                    // LP
                line2 += Statistics.Instance.LostDrones + ";";                                                                       // Lost Drones
                line2 += Statistics.Instance.AmmoConsumption + ";";                                                                  // Ammo Consumption
                line2 += Statistics.Instance.AmmoValue + ";\r\n";                                                                    // Ammo Value

                // The mission is finished
                Logging.Log("Statistics", "writing mission log2 to [ " + Settings.Instance.MissionStats2LogFile + " ]", Logging.White);
                File.AppendAllText(Settings.Instance.MissionStats2LogFile, line2);
                //Logging.Log("Date;Mission;Time;Isk;Loot;LP;LostDrones;AmmoConsumption;AmmoValue;");
                //Logging.Log(line2);
            }
            if (Settings.Instance.MissionStats3Log)
            {
                if (!Directory.Exists(Settings.Instance.MissionStats3LogPath))
                    Directory.CreateDirectory(Settings.Instance.MissionStats3LogPath);

                // Write the header
                if (!File.Exists(Settings.Instance.MissionStats3LogFile))
                    File.AppendAllText(Settings.Instance.MissionStats3LogFile, "Date;Mission;Time;Isk;Loot;LP;DroneRecalls;LostDrones;AmmoConsumption;AmmoValue;Panics;LowestShield;LowestArmor;LowestCap;RepairCycles;AfterMissionsalvageTime;TotalMissionTime;MissionXMLAvailable;Faction;SolarSystem;DungeonID;\r\n");

                // Build the line
                string line3 = DateTimeForLogs + ";";                                                                                           // Date
                line3 += Cache.Instance.MissionName + ";";                                                                                   // Mission
                line3 += ((int)Statistics.Instance.FinishedMission.Subtract(Statistics.Instance.StartedMission).TotalMinutes) + ";";         // TimeMission
                line3 += ((long)(Cache.Instance.DirectEve.Me.Wealth - Cache.Instance.Wealth)) + ";";                                         // Isk
                line3 += ((long)Statistics.Instance.LootValue) + ";";                                                                        // Loot
                line3 += ((long)Cache.Instance.Agent.LoyaltyPoints - Statistics.Instance.LoyaltyPoints) + ";";                               // LP
                line3 += Statistics.Instance.DroneRecalls + ";";                                                                             // Lost Drones
                line3 += "LostDrones:" + Statistics.Instance.LostDrones + ";";                                                               // Lost Drones
                line3 += Statistics.Instance.AmmoConsumption + ";";                                                                          // Ammo Consumption
                line3 += Statistics.Instance.AmmoValue + ";";                                                                                // Ammo Value
                line3 += "Panics:" + Cache.Instance.PanicAttemptsThisMission + ";";                                                          // Panics
                line3 += ((int)Cache.Instance.LowestShieldPercentageThisMission) + ";";                                                      // Lowest Shield %
                line3 += ((int)Cache.Instance.LowestArmorPercentageThisMission) + ";";                                                       // Lowest Armor %
                line3 += ((int)Cache.Instance.LowestCapacitorPercentageThisMission) + ";";                                                   // Lowest Capacitor %
                line3 += Cache.Instance.RepairCycleTimeThisMission + ";";                                                                    // repair Cycle Time
                line3 += ((int)Statistics.Instance.FinishedSalvaging.Subtract(Statistics.Instance.StartedSalvaging).TotalMinutes) + ";";     // After Mission Salvaging Time
                line3 += ((int)Statistics.Instance.FinishedSalvaging.Subtract(Statistics.Instance.StartedSalvaging).TotalMinutes) + ((int)Statistics.Instance.FinishedMission.Subtract(Statistics.Instance.StartedMission).TotalMinutes) + ";"; // Total Time, Mission + After Mission Salvaging (if any)
                line3 += Cache.Instance.MissionXMLIsAvailable.ToString(CultureInfo.InvariantCulture) + ";";
                line3 += Cache.Instance.FactionName + ";";                                                                                   // FactionName that the mission is against
                line3 += Cache.Instance.MissionSolarSystem + ";";                                                                            // SolarSystem the mission was located in
                line3 += Cache.Instance.DungeonId + ";";                                                                                     // DungeonID - the unique identifier for this mission 
                line3 += "\r\n";

                // The mission is finished
                Logging.Log("Statistics", "writing mission log3 to  [ " + Settings.Instance.MissionStats3LogFile + " ]", Logging.White);
                File.AppendAllText(Settings.Instance.MissionStats3LogFile, line3);
                //Logging.Log("Date;Mission;Time;Isk;Loot;LP;LostDrones;AmmoConsumption;AmmoValue;Panics;LowestShield;LowestArmor;LowestCap;RepairCycles;AfterMissionsalvageTime;TotalMissionTime;");
                //Logging.Log(line3);
            }
            if (Settings.Instance.MissionDungeonIdLog)
            {
                if (!Directory.Exists(Settings.Instance.MissionDungeonIdLogPath))
                    Directory.CreateDirectory(Settings.Instance.MissionDungeonIdLogPath);

                // Write the header
                if (!File.Exists(Settings.Instance.MissionDungeonIdLogFile))
                    File.AppendAllText(Settings.Instance.MissionDungeonIdLogFile, "Mission;Faction;DungeonID;\r\n");

                // Build the line
                string line4 = DateTimeForLogs + ";";              // Date
                line4 += Cache.Instance.MissionName + ";";      // Mission
                line4 += Cache.Instance.FactionName + ";";      // FactionName that the mission is against
                line4 += Cache.Instance.DungeonId + ";";        // DungeonID - the unique identifier for this mission (parsed from the mission HTML)
                line4 += "\r\n";

                // The mission is finished
                Logging.Log("Statistics", "writing mission dungeonID log to  [ " + Settings.Instance.MissionDungeonIdLogFile + " ]", Logging.White);
                File.AppendAllText(Settings.Instance.MissionDungeonIdLogFile, line4);
                //Logging.Log("Date;Mission;Time;Isk;Loot;LP;LostDrones;AmmoConsumption;AmmoValue;Panics;LowestShield;LowestArmor;LowestCap;RepairCycles;AfterMissionsalvageTime;TotalMissionTime;");
                //Logging.Log(line3);
            }
            // Disable next log line
            Statistics.Instance.MissionLoggingCompleted = true;
            Statistics.Instance.LootValue = 0;
            Statistics.Instance.LoyaltyPoints = Cache.Instance.Agent.LoyaltyPoints;
            Statistics.Instance.StartedMission = DateTime.UtcNow;
            Statistics.Instance.FinishedMission = DateTime.UtcNow; //this may need to be reset to DateTime.MinValue, but that was causing other issues...
            Cache.Instance.MissionName = string.Empty;
            Statistics.Instance.DroneRecalls = 0;
            Statistics.Instance.LostDrones = 0;
            Statistics.Instance.AmmoConsumption = 0;
            Statistics.Instance.AmmoValue = 0;
            Statistics.Instance.DroneLoggingCompleted = false;
            Statistics.Instance.MissionCompletionErrors = 0;
            foreach (ModuleCache weapon in Cache.Instance.Weapons)
            {
                weapon.ReloadTimeThisMission = 0;
            }

            Cache.Instance.PanicAttemptsThisMission = 0;
            Cache.Instance.LowestShieldPercentageThisMission = 101;
            Cache.Instance.LowestArmorPercentageThisMission = 101;
            Cache.Instance.LowestCapacitorPercentageThisMission = 101;
            Cache.Instance.RepairCycleTimeThisMission = 0;
            Cache.Instance.TimeSpentReloading_seconds = 0;             // this will need to be added to whenever we reload or switch ammo
            Cache.Instance.TimeSpentInMission_seconds = 0;             // from landing on grid (loading mission actions) to going to base (changing to gotobase state)
            Cache.Instance.TimeSpentInMissionInRange = 0;              // time spent totally out of range, no targets
            Cache.Instance.TimeSpentInMissionOutOfRange = 0;           // time spent in range - with targets to kill (or no targets?!)
            Cache.Instance.MissionSolarSystem = null;
            Cache.Instance.DungeonId = "n/a";
        }
        
        public void ProcessState()
        {
            switch (State)
            {
                case StatisticsState.Idle:
                    Logging.Log("Statistics", "State=StatisticsState.Idle", Logging.White);
                    //This State should only start every 20 seconds
                    //if (DateTime.UtcNow.Subtract(_lastCleanupAction).TotalSeconds < 20)
                    //    break;

                    //State = StatisticsState.CheckModalWindows;
                    break;

                case StatisticsState.PocketLog:
                    State = StatisticsState.Idle;
                    break;

                case StatisticsState.SessionLog:
                    State = StatisticsState.Idle;
                    break;

                case StatisticsState.Done:
                    //_lastStatisticsAction = DateTime.UtcNow;
                    State = StatisticsState.Idle;
                    break;

                default:
                    // Next state
                    State = StatisticsState.Idle;
                    break;
            }
        }
    }
}