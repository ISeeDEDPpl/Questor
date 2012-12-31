using System;
using System.Windows.Forms;

namespace GridMon
{
    using DirectEve;

    public partial class FrmMain : Form
    {
        private GridMonState State { get; set; }

        private DirectEve DirectEve { get; set; }

        private static DateTime _nextAction;
        private const int WaitMillis = 10000;

        public FrmMain()
        {
            InitializeComponent();

            DirectEve = new DirectEve();
            DirectEve.OnFrame += OnFrame;
        }

        private delegate void SetButtonTextCallback(string text);

        public void SetButtonText(string text)
        {
            if (this.InvokeRequired)
            {
                SetButtonTextCallback cb = new SetButtonTextCallback(SetButtonText);
                this.Invoke(cb, new object[] { text });
            }
            else
            {
                btnStartStop.Text = text;
            }
        }

        private delegate void LogCallback(string line);

        public void Log(string line)
        {
            //InnerSpaceAPI.InnerSpace.Echo(string.Format("{0:HH:mm:ss} {1}", DateTime.UtcNow, line));

            if (this.InvokeRequired)
            {
                LogCallback cb = new LogCallback(Log);
                this.Invoke(cb, new object[] { line });
            }
            else
            {
                string output = string.Format("{0:HH:mm:ss} {1}\n", DateTime.UtcNow, line);
                tbLog.AppendText(output);
            }
        }

        private void OnFrame(object sender, EventArgs e)
        {
            if (State == GridMonState.Idle)
            {
                return;
            }

            // Wait for the next action
            if (_nextAction >= DateTime.UtcNow)
            {
                return;
            }

            switch (State)
            {
                case GridMonState.WatchGrid:
                    Log("WatchGrid...");
                    foreach (var entity in DirectEve.Entities)
                    {
                        if (entity.IsPc)
                        {
                            LogEntity("{0} {1} {2} {3} {4} {5}", entity);
                            // AppendSQL
                        }
                    }
                    State = GridMonState.WatchLocal;
                    _nextAction = DateTime.UtcNow.AddMilliseconds(WaitMillis);
                    break;

                case GridMonState.WatchLocal:
                    Log("WatchLocal...");
                    State = GridMonState.WatchGrid;
                    break;
            }
        }

        public void LogEntity(string format, DirectEntity entity)
        {
            if (entity != null)
            {
                Log(string.Format(format, entity.Id, entity.Name, entity.CorpId, entity.AllianceId, entity.TypeName, entity.GivenName));
            }
        }

        private void BtnStartStopClick(object sender, EventArgs e)
        {
            if (btnStartStop.Text == "Start")
            {
                btnStartStop.Text = "Stop";
                State = GridMonState.WatchGrid;
            }
            else
            {
                btnStartStop.Text = "Start";
                State = GridMonState.Idle;
            }
        }

        private void FrmMainFormClosed(object sender, FormClosedEventArgs e)
        {
            DirectEve.Dispose();
            DirectEve = null;
        }
    }
}
