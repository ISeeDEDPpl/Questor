// ------------------------------------------------------------------------------
//   <copyright from='2010' to='2015' company='THEHACKERWITHIN.COM'>
//     Copyright (c) TheHackerWithin.COM. All Rights Reserved.
//
//     Please look in the accompanying license.htm file for the license that
//     applies to this source code. (a copy can also be found at:
//     http://www.thehackerwithin.com/license.htm)
//   </copyright>
// -------------------------------------------------------------------------------

namespace Questor.Modules.Lookup
{
    using System.Xml.Linq;
    using System.Globalization;
    using System;
    using global::Questor.Modules.Logging;

    public class CharSchedule
    {
        public string LoginUserName { get; private set; }

        public string LoginPassWord { get; private set; }

        public string ScheduleCharacterName { get; private set; }

        public DateTime Start1 { get; set; }

        public DateTime Stop1 { get; set; }

        public DateTime Start2 { get; set; }

        public DateTime Stop2 { get; set; }

        public DateTime Start3 { get; set; }

        public DateTime Stop3 { get; set; }

        public double RunTime { get; set; }

        public bool StopTimeSpecified { get; set; }

        public bool StartTimeSpecified { get; set; }

        public bool StopTime2Specified { get; set; }

        public bool StartTime2Specified { get; set; }

        public bool StopTime3Specified { get; set; }

        public bool StartTime3Specified { get; set; }

        public CharSchedule(XElement element)
        {
            //var timeformat = new CultureInfo("en-US");
            LoginUserName = (string)element.Attribute("user");
            LoginPassWord = (string)element.Attribute("pw");
            ScheduleCharacterName = (string)element.Attribute("name");

            StopTimeSpecified = false;
            StartTimeSpecified = false;
            StopTime2Specified = false;
            StartTime2Specified = false;
            StopTime3Specified = false;
            StartTime3Specified = false;

            var startxml1 = (string)element.Attribute("start");
            var stopxml1 = (string)element.Attribute("stop");
            var startxml2 = (string)element.Attribute("start2");
            var stopxml2 = (string)element.Attribute("stop2");
            var startxml3 = (string)element.Attribute("start3");
            var stopxml3 = (string)element.Attribute("stop3");
            DateTime startTime1 = DateTime.MaxValue;
            DateTime stopTime1 = DateTime.MinValue;
            DateTime startTime2 = DateTime.MaxValue;
            DateTime stopTime2 = DateTime.MinValue;
            DateTime startTime3 = DateTime.MaxValue;
            DateTime stopTime3 = DateTime.MinValue;

            //
            // schedule #1
            // start time parsing
            if (startxml1 != null)
            {
                if (!DateTime.TryParseExact(startxml1, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out startTime1))
                {
                    Logging.Log("CharSchedule", ScheduleCharacterName + ": Could not parse starttime.", Logging.Red);
                    startTime1 = DateTime.Now.AddSeconds(20);
                }
                else
                    StartTimeSpecified = true;

                Start1 = startTime1;
            }
            else
            {
                Logging.Log("CharSchedule", "No start time specified. Starting now.", Logging.Orange);
                startTime1 = DateTime.Now.AddSeconds(20);
                Start1 = startTime1;
            }

            // stop time parsing
            if (stopxml1 != null && StartTimeSpecified)
            {
                if (!DateTime.TryParseExact(stopxml1, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out stopTime1))
                {
                    Logging.Log("CharSchedule", ScheduleCharacterName + ": Could not parse stoptime: Assuming 4 hours", Logging.Red);
                    stopTime1 = DateTime.Now.AddHours(4);
                }
                else
                    StopTimeSpecified = true;

                Stop1 = stopTime1;
            }
            else
            {
                Logging.Log("CharSchedule", "No stop time specified: Assuming 4 hours", Logging.Red);
                stopTime1 = DateTime.Now.AddHours(4);
                Stop1 = stopTime1;
            }

            //
            // schedule #2
            // start time parsing
            if (startxml2 != null)
            {
                if (!DateTime.TryParseExact(startxml2, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out startTime2))
                {
                    Logging.Log("CharSchedule", ScheduleCharacterName + ": Could not parse starttime2.", Logging.Red);
                    startTime2 = DateTime.Now.AddSeconds(20);
                }
                else
                    StartTime2Specified = true;

                Start2 = startTime2;
            }

            // stop time parsing
            if (stopxml2 != null && StartTime2Specified)
            {
                if (!DateTime.TryParseExact(stopxml2, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out stopTime2))
                {
                    Logging.Log("CharSchedule", ScheduleCharacterName + ": Could not parse stoptime2: Assuming 4 hours.", Logging.Red);
                    stopTime2 = DateTime.Now.AddHours(4);
                }
                else
                    StopTime2Specified = true;

                Stop2 = stopTime2;
            }
            else
            {
                Logging.Log("CharSchedule", "No stop time specified: Assuming 4 hours", Logging.Red);
                stopTime2 = DateTime.Now.AddHours(4);
                Stop2 = stopTime2;
            }

            //
            // schedule #3
            // start time parsing
            if (startxml3 != null)
            {
                if (!DateTime.TryParseExact(startxml3, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out startTime3))
                {
                    Logging.Log("CharSchedule", ScheduleCharacterName + ": Could not parse starttime3", Logging.Red);
                    startTime3 = DateTime.Now.AddSeconds(20);
                }
                else
                    StartTime3Specified = true;

                Start3 = startTime3;
            }

            // stop time parsing
            if (stopxml3 != null && StartTime3Specified)
            {
                if (!DateTime.TryParseExact(stopxml3, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out stopTime3))
                {
                    Logging.Log("CharSchedule", ScheduleCharacterName + ": Could not parse stoptime3: Assuming 4 hours.", Logging.Red);
                    stopTime3 = DateTime.Now.AddHours(4);
                }
                else
                    StopTime3Specified = true;

                Stop3 = stopTime3;
            }
            else
            {
                Logging.Log("CharSchedule", "No stop time specified: Assuming 4 hours", Logging.Red);
                stopTime3 = DateTime.Now.AddHours(4);
                Stop3 = stopTime3;
            }

            if ((string)element.Attribute("runtime") != null)
            {
                Logging.Log("CharSchedule", "RunTime Attribute no longer supported: use StartTime and StopTime", Logging.Orange);
                RunTime = -1;
            }
            else
                RunTime = -1;
        }
    }
}