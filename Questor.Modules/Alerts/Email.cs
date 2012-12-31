// ------------------------------------------------------------------------------
//   <copyright from='2010' to='2015' company='THEHACKERWITHIN.COM'>
//     Copyright (c) TheHackerWithin.COM. All Rights Reserved.
//
//     Please look in the accompanying license.htm file for the license that
//     applies to this source code. (a copy can also be found at:
//     http://www.thehackerwithin.com/license.htm)
//   </copyright>
// -------------------------------------------------------------------------------

namespace Questor.Modules.Alerts
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Net.Mail;
    using System.Net;
    using Questor.Modules.Caching;
    using Questor.Modules.Lookup;
    using Questor.Modules.Logging;
    using Questor.Modules.States;
    using InnerSpaceAPI;
    using LavishScriptAPI;

    public static class Email
    {
        public static DateTime DateTimeForLogs;
        private static bool ShipLostEmailSent;
        private static bool QuestorRestartedEmailSent;

        public static void Email_ShipLost()
        {
            try
            {
                string _locationName;
                if (Cache.Instance.InStation)
                {
                    _locationName = Cache.Instance.DirectEve.GetLocationName(Cache.Instance.DirectEve.Session.StationId ?? 0);
                }
                else
                {
                    long mySolarSystemlocation = Cache.Instance.DirectEve.ActiveShip.LocationId;
                    _locationName = Cache.Instance.DirectEve.Navigation.GetLocationName(mySolarSystemlocation);
                }
                
                if (!ShipLostEmailSent)
                {
                    string subject = "ShipLostEmail: [" + Settings.Instance.CharacterName + "] is in a pod";
                    string body = "ShipLostEmail: [" + Settings.Instance.CharacterName + "] is in a pod in [" + _locationName + "]";
                    if (SendEmail(subject, body))
                    {
                        ShipLostEmailSent = true;
                    }
                }
            }
            catch (Exception exception)
            {
                Logging.Log("Email_ShipLost", "Exception was [" + exception + "]", Logging.Debug);
            }
        }

        public static void Email_QuestorRestarted()
        {
            if (!QuestorRestartedEmailSent)
            {
                string subject = "subject test";
                string body = "body test";
                if (SendEmail(subject, body))
                {
                    QuestorRestartedEmailSent = true;
                }
            }
        }

        public static bool SendEmail(string subject, string body)
        {
            if (Settings.Instance.EmailSupport)
            {
                bool _useSSL;
                if (Settings.Instance.EmailEnableSSL != null)
                {
                    _useSSL = (bool)Settings.Instance.EmailEnableSSL;
                }
                else
                {
                    _useSSL = false;
                }

                if (!String.IsNullOrEmpty(Settings.Instance.EmailAddress) &&
                    !String.IsNullOrEmpty(Settings.Instance.EmailPassword) &&
                    !String.IsNullOrEmpty(Settings.Instance.EmailSMTPServer) &&
                    !String.IsNullOrEmpty(Settings.Instance.EmailAddressToSendAlerts)
                    )
                {
                    try
                    {
                        var smtpClient = new SmtpClient(Settings.Instance.EmailSMTPServer, Settings.Instance.EmailSMTPPort) //587
                        {
                            Credentials = new NetworkCredential(Settings.Instance.EmailAddress, Settings.Instance.EmailPassword),
                            EnableSsl = _useSSL
                        };
                        smtpClient.Send(Settings.Instance.EmailAddress, Settings.Instance.EmailAddressToSendAlerts, subject, body);
                        Logging.Log("Email.SendEmail", "Sent Email to [" + Settings.Instance.EmailAddressToSendAlerts + "]", Logging.Debug);
                    }
                    catch (Exception exception)
                    {
                        Logging.Log("Email.SendEmail", "Sending Email caused an exception [" + exception + "]",Logging.Debug);
                        return false;
                    }
                    return true;
                }
                return false;
            }
            return true;
        }
    }
}