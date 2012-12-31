namespace UpdateInvTypes
{
    using System.IO;
    using System.Xml.Linq;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Windows.Forms;
    using global::Questor.Modules.Lookup;
    using global::Questor.Modules.Logging;

    public partial class UpdateInvTypesUI : Form
    {
        private bool _doUpdate;
        private bool _updating;
        private bool _eveCentralInvalidItemType;
        private bool _debugEVECentralURL = true;
        private readonly int _numOfItemIDsToCheckAtOnce;
        private readonly List<InvType> _invTypes;
        private readonly List<InvType> _nonMarketItems;
        private DateTime _nextEVECentralQuery;

        public string InvTypesPath = Settings.Instance.Path + "\\InvTypes.xml";
        public string NonMarketItemsPath = Settings.Instance.Path + "\\NonMarketItems.xml";
        
        public UpdateInvTypesUI()
        {
            InitializeComponent();

            _invTypes = new List<InvType>();
            _nonMarketItems = new List<InvType>();

            try
            {
                XDocument invTypes = XDocument.Load(InvTypesPath);
                if (invTypes.Root != null)
                {
                    foreach (XElement element in invTypes.Root.Elements("invtype"))
                    {
                        _invTypes.Add(new InvType(element));
                    }
                }
            }
            catch (Exception)
            {
                Logging.Log("UpdateInvTypes","Unable to load [" + InvTypesPath + "]",Logging.Debug);
            }
            
            try
            {
                XDocument NonMarketItems = XDocument.Load(NonMarketItemsPath);
                if (NonMarketItems.Root != null)
                {
                    foreach (XElement element in NonMarketItems.Root.Elements("invtype"))
                    {
                        _nonMarketItems.Add(new InvType(element));
                    }
                }
            }
            catch (Exception)
            {
                Logging.Log("UpdateInvTypes", "Unable to load [" + NonMarketItemsPath + "]", Logging.Debug);
            }
            
            _numOfItemIDsToCheckAtOnce = 10;
            Progress.Step = _numOfItemIDsToCheckAtOnce;
            Progress.Value = 0;
            Progress.Minimum = 0;
            Progress.Maximum = _invTypes.Count;
        }

        private void UpdateClick(object sender, EventArgs e)
        {
            _doUpdate ^= true;
            UpdateButton.Text = _doUpdate ? "Stop" : "Update";

            if (!_doUpdate)
            {
                XDocument xdoc = new XDocument(new XElement("invtypes"));
                foreach (InvType type in _invTypes)
                {
                    if (xdoc.Root != null) xdoc.Root.Add(type.Save());
                }
                xdoc.Save(InvTypesPath);
            }
        }

        private void UpdateTick(object sender, EventArgs e)
        {
            if (_nextEVECentralQuery > DateTime.UtcNow)
            {
                return;
            }

            // This is what you get if your too bored to setup an actual thread and do UI-invoke shit
            if (!_doUpdate)
            {
                return;
            }

            if (_updating)
            {
                return;
            }

            _updating = true;
            try
            {
                IEnumerable<InvType> types;
                if (_eveCentralInvalidItemType) //if eve-central did not like the previous list, try one item
                {
                    types = _invTypes.Skip(Progress.Value).Take(1).ToList();
                    _eveCentralInvalidItemType = false;
                }
                else //if eve-central did like the previous list, keep going querying Progress.Step at a time
                {
                    types = _invTypes.Skip(Progress.Value).Take(Progress.Step).ToList();
                }

                try
                {
                    IEnumerable<InvType> needUpdating = types.Where(type => !type.LastUpdate.HasValue || DateTime.UtcNow.Subtract(type.LastUpdate.Value).TotalDays > 4).ToList();
                    if (chkfast.Checked)
                    {
                        needUpdating = types.Where(type => !type.LastUpdate.HasValue || DateTime.UtcNow.Subtract(type.LastUpdate.Value).TotalMinutes > 2);
                    }

                    if (!needUpdating.Any()) return;

                    string queryString = string.Join("&", types.Select(type => "typeid=" + type.Id).ToArray());
                    Logging.Log("UpdateInvTypes", "Checking Invtypes: " + string.Join(",", types.Select(type => type.Name + "(" + type.Id + ")").ToArray()), Logging.White);

                    queryString += "&usesystem=30000142"; //jita

                    string url = "http://api.eve-central.com/api/marketstat?" + queryString;
                    try
                    {
                        XDocument prices = XDocument.Load(url);
                        _nextEVECentralQuery = DateTime.UtcNow.AddMilliseconds(300);

                        if (prices.Root != null && (string)prices.Root.Attribute("method") != "marketstat_xml")
                        {
                            Logging.Log("UpdateInvTypes", "Invalid XML Method", Logging.Red);
                            throw new Exception("Invalid XML method");
                        }

                        if (prices.Root != null)
                        {
                            foreach (XElement type in prices.Root.Element("marketstat").Elements("type"))
                            {
                                int id = (int) type.Attribute("id");
                                InvType invType = types.Single(t => t.Id == id);

                                XElement all = type.Element("all");
                                if (all != null)
                                    invType.MedianAll = (double?) all.Element("median");

                                XElement buy = type.Element("buy");
                                if (buy != null)
                                {
                                    invType.MedianBuy = (double?) buy.Element("median");
                                    invType.MaxBuy = (double?) buy.Element("max");
                                }

                                XElement sell = type.Element("sell");
                                if (sell != null)
                                {
                                    invType.MedianSell = (double?) sell.Element("median");
                                    invType.MinSell = (double?) sell.Element("min");
                                }

                                invType.LastUpdate = DateTime.UtcNow;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        if (_debugEVECentralURL) Logging.Log("UpdateInvTypes", "Invalid XML Method in marketstat_xml [" + ex.Message + "]", Logging.Red);
                        if (_debugEVECentralURL) Logging.Log("UpdateInvTypes", "URL was: " + url, Logging.Yellow); // Test marketstat lookup string
                        _eveCentralInvalidItemType = true;
                        if (Progress.Step == 1) //we previously queried only one item and eve central was still unhappy, skip this item
                        {
                            Logging.Log("UpdateInvTypes", "Skipping [" + types.Select(type => "typeid=" + type.Id) + "]", Logging.White);
                            //
                            // todo add item to list here
                            //
                            Progress.Value = Progress.Value + Progress.Step;
                            _nextEVECentralQuery = DateTime.UtcNow.AddMilliseconds(300);
                        }
                        else
                        {
                            Logging.Log("UpdateInvTypes", "_eveCentralInvalidItemType is true but Progress.Step == [ " + Progress.Step + " ]", Logging.White);
                        }
                        return;
                    }
                }
                finally
                {
                    Progress.Value = Progress.Value + types.Count();
                    Progress.Step = _numOfItemIDsToCheckAtOnce;
                    _nextEVECentralQuery = DateTime.UtcNow.AddMilliseconds(300);

                    if (Progress.Value >= _invTypes.Count - 1)
                    {
                        _doUpdate = false;

                        XDocument xdoc = new XDocument(new XElement("invtypes"));
                        foreach (InvType type in _invTypes)
                        {
                            if (xdoc.Root != null) xdoc.Root.Add(type.Save());
                        }
                        xdoc.Save(InvTypesPath);

                        UpdateButton.Text = _doUpdate ? "Stop" : "Update";
                    }
                }
            }
            finally
            {
                _updating = false;
            }
        }
    }
}