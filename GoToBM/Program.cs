/* Written by Noob536 */

namespace GoToBM
{
    using DirectEve;
    using System;
    using Questor.Modules.Activities;
    using Questor.Modules.Caching;
    using Questor.Modules.Logging;
    using Questor.Modules.Actions;
    using Questor.Modules.States;
    using Questor.Modules.BackgroundTasks;

    internal static class Program
    {
        private static DirectEve _directEve;
        private static Cleanup _cleanup;
        private static Defense _defense;
        private static DirectBookmark _bookmark;
        private static DateTime _lastPulse;
        private static bool _done;
        private static string _bm;
        private static bool _started;

        [STAThread]
        private static void Main(string[] args)
        {
            Logging.Log("GoToBM", "Started", Logging.White);
            if (args.Length == 0 || args[0].Length < 1)
            {
                Logging.Log("GoToBM", " You need to supply a bookmark name", Logging.White);
                Logging.Log("GoToBM", " Ended", Logging.White);
                return;
            }
            _bm = args[0];
            _bm = _bm.ToLower();

            _directEve = new DirectEve();
            Cache.Instance.DirectEve = _directEve;
            _directEve.OnFrame += OnFrame;
            _cleanup = new Cleanup();
            _defense = new Defense();

            while (!_done)
            {
                System.Threading.Thread.Sleep(50);
            }

            _directEve.Dispose();
            Logging.Log("GoToBM", " Exiting", Logging.White);
            return;
        }

        private static void OnFrame(object sender, EventArgs e)
        {
            if (DateTime.UtcNow.Subtract(_lastPulse).TotalMilliseconds < 1500)
                return;
            _lastPulse = DateTime.UtcNow;

            // New frame, invalidate old cache
            Cache.Instance.InvalidateCache();

            Cache.Instance.LastFrame = DateTime.UtcNow;

            // Session is not ready yet, do not continue
            if (!Cache.Instance.DirectEve.Session.IsReady)
                return;

            if (Cache.Instance.DirectEve.Session.IsReady)
                Cache.Instance.LastSessionIsReady = DateTime.UtcNow;

            // We are not in space or station, don't do shit yet!
            if (!Cache.Instance.InSpace && !Cache.Instance.InStation)
            {
                Cache.Instance.NextInSpaceorInStation = DateTime.UtcNow.AddSeconds(12);
                Cache.Instance.LastSessionChange = DateTime.UtcNow;
                return;
            }

            if (DateTime.UtcNow < Cache.Instance.NextInSpaceorInStation)
                return;

            // We always check our defense state if we're in space, regardless of questor state
            // We also always check panic
            if (Cache.Instance.InSpace)
            {
                if (!Cache.Instance.DoNotBreakInvul)
                {
                    _defense.ProcessState();
                }
            }

            // Start _cleanup.ProcessState
            // Description: Closes Windows, and eventually other things considered 'cleanup' useful to more than just Questor(Missions) but also Anomalies, Mining, etc
            //
            _cleanup.ProcessState();

            // Done
            // Cleanup State: ProcessState

            if (Cache.Instance.InWarp)
                return;

            if (!_started)
            {
                _started = true;
                if (!Cache.Instance.DirectEve.Session.IsReady)
                {
                    Logging.Log("GoToBM", " Not in game, exiting", Logging.White);
                    return;
                }
                Logging.Log("GoToBM", ": Attempting to find bookmark [" + _bm + "]", Logging.White);
                foreach (var bookmark in Cache.Instance.DirectEve.Bookmarks)
                {
                    if (bookmark.Title.ToLower().Equals(_bm))
                    {
                        _bookmark = bookmark;
                        break;
                    }
                    if (_bookmark == null && bookmark.Title.ToLower().Contains(_bm))
                    {
                        _bookmark = bookmark;
                    }
                }
                if (_bookmark == null)
                {
                    Logging.Log("GoToBM", ": Bookmark not found", Logging.White);
                    _done = true;
                    return;
                }
                Traveler.Destination = new BookmarkDestination(_bookmark);
            }
            Traveler.ProcessState();
            if (_States.CurrentTravelerState == TravelerState.AtDestination)
            {
                _done = true;
                Logging.Log("GoToBM", " At destination", Logging.White);
            }
            else if (_States.CurrentTravelerState == TravelerState.Error)
            {
                Logging.Log("GoToBM", " Traveler error", Logging.White);
                _done = true;
            }
        }
    }
}