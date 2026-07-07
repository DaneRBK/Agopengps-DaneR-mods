using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows.Forms;
using AgIO.Controls;
using AgLibrary.Logging;

namespace AgIO
{
    public partial class FormUDP : Form
    {
        //class variables
        private readonly FormLoop mf = null;

        //used to send communication check pgn= C8 or 200
        private byte[] sendIPToModules = { 0x80, 0x81, 0x7F, 201, 5, 201, 201, 192, 168, 5, 0x47 };

        private byte[] ipCurrent = { 192, 168, 5 };
        private byte[] ipNew = { 192, 168, 5 };

        public FormUDP(Form callingForm)
        {
            //get copy of the calling main form
            mf = callingForm as FormLoop;
            InitializeComponent();

            nudFirstIP.Controls[0].Enabled = false;
            nudSecndIP.Controls[0].Enabled = false;
            nudThirdIP.Controls[0].Enabled = false;
        }

        private void FormUDp_Load(object sender, EventArgs e)
        {
            mf.ipAutoSet[0] = 99;
            mf.ipAutoSet[1] = 99;
            mf.ipAutoSet[2] = 99;

            lblHostname.Text = Dns.GetHostName(); // Retrieve the Name of HOST

            lblNetworkHelp.Text =
                Properties.Settings.Default.etIP_SubnetOne.ToString() + " . " +
                Properties.Settings.Default.etIP_SubnetTwo.ToString() + " . " +
                Properties.Settings.Default.etIP_SubnetThree.ToString();

            nudFirstIP.Value = ipNew[0] = ipCurrent[0] = Properties.Settings.Default.etIP_SubnetOne;
            nudSecndIP.Value = ipNew[1] = ipCurrent[1] = Properties.Settings.Default.etIP_SubnetTwo;
            nudThirdIP.Value = ipNew[2] = ipCurrent[2] = Properties.Settings.Default.etIP_SubnetThree;

            ScanNetwork();
        }

        private int tickCounter = 0;

        private void timer1_Tick(object sender, EventArgs e)
        {
            if (!mf.scanReply.isNewData)
            {
                mf.ipAutoSet[0] = 99;
                mf.ipAutoSet[1] = 99;
                mf.ipAutoSet[2] = 99;
                btnAutoSet.Enabled = false;
            }
            else
            {
                btnAutoSet.Enabled = true;
            }

            if (mf.scanReply.isNewSteer)
            {
                lblSteerIP.Text = mf.scanReply.steerIP;
                mf.scanReply.isNewSteer = false;
                lblNewSubnet.Text = mf.scanReply.subnetStr;
            }

            if (mf.scanReply.isNewMachine)
            {
                lblMachineIP.Text = mf.scanReply.machineIP;
                mf.scanReply.isNewMachine = false;
                lblNewSubnet.Text = mf.scanReply.subnetStr;
            }

            if (mf.scanReply.isNewIMU)
            {
                lblIMU_IP.Text = mf.scanReply.IMU_IP;
                mf.scanReply.isNewIMU = false;
                lblNewSubnet.Text = mf.scanReply.subnetStr;
            }

            if (mf.scanReply.isNewGPS)
            {
                lblGPSIP.Text = mf.scanReply.GPS_IP;
                mf.scanReply.isNewGPS = false;
                lblNewSubnet.Text = mf.scanReply.subnetStr;
            }

            if (tickCounter == 4)
            {
                if (mf.btnSteer.BackColor == Color.LimeGreen) lblBtnSteer.BackColor = Color.LimeGreen;
                else lblBtnSteer.BackColor = Color.Red;

                if (mf.btnMachine.BackColor == Color.LimeGreen) lblBtnMachine.BackColor = Color.LimeGreen;
                else lblBtnMachine.BackColor = Color.Red;

                if (mf.btnGPS.BackColor == Color.LimeGreen) lblBtnGPS.BackColor = Color.LimeGreen;
                else lblBtnGPS.BackColor = Color.Red;

                if (mf.btnIMU.BackColor == Color.LimeGreen) lblBtnIMU.BackColor = Color.LimeGreen;
                else lblBtnIMU.BackColor = Color.Red;
            }

            if (tickCounter > 5)
            {
                ScanNetwork();
                tickCounter = 0;
                lblSubTimer.Text = "Scanning";
                //FillNudsWithScan();
            }
            else
            {
                lblSubTimer.Text = "-";
            }
            tickCounter++;
        }

        private void ScanNetwork()
        {
            tboxNets.Text = "";

            lblSteerIP.Text = lblMachineIP.Text = lblGPSIP.Text = lblIMU_IP.Text = lblNewSubnet.Text = "";
            mf.scanReply.isNewData = false;

            bool isSubnetMatchCard = false;

            byte[] scanModules = { 0x80, 0x81, 0x7F, 202, 3, 202, 202, 5, 0x47 };

            //Send out 255x4 to each installed network interface
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.Supports(NetworkInterfaceComponent.IPv4))
                {
                    foreach (var info in nic.GetIPProperties().UnicastAddresses)
                    {
                        // Only InterNetwork and not loopback which have a subnetmask
                        if (info.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(info.Address))
                        {
                            Socket scanSocket;
                            try
                            {
                                //create list of interface properties
                                if ((cboxUp.Checked && nic.OperationalStatus == OperationalStatus.Up) || !cboxUp.Checked)
                                {
                                    var properties = nic.GetIPStatistics();
                                    tboxNets.Text +=
                                            info.Address + "  - " + nic.OperationalStatus + "\r\n";

                                    tboxNets.Text += info.IPv4Mask.ToString() + "  " + nic.Name.ToString() + "\r\n";
                                    tboxNets.Text +=
                                        "->" + (properties.NonUnicastPacketsSent
                                        + properties.UnicastPacketsSent).ToString()

                                        + "  <-" + (properties.NonUnicastPacketsReceived
                                        + properties.UnicastPacketsReceived).ToString() + "\r\n"
                                        + "\r\n";
                                }

                                if (nic.OperationalStatus == OperationalStatus.Up && info.IPv4Mask != null)
                                {
                                    byte[] data = info.Address.GetAddressBytes();
                                    if (data[0] == ipCurrent[0] && data[1] == ipCurrent[1] && data[2] == ipCurrent[2])
                                    {
                                        isSubnetMatchCard = true;
                                    }

                                    //send scan reply out each network interface
                                    scanSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                                    scanSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);
                                    scanSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                                    scanSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.DontRoute, true);

                                    try
                                    {
                                        scanSocket.Bind(new IPEndPoint(info.Address, 9999));
                                        scanSocket.SendTo(scanModules, 0, scanModules.Length, SocketFlags.None, mf.epModuleSet);
                                    }
                                    catch (Exception ex)
                                    {
                                        Log.EventWriter("Catch - > Socket Bind Error Scan UDP" + ex.ToString());
                                    }

                                    scanSocket.Dispose();
                                }
                            }
                            catch (Exception ex)
                            {
                                Log.EventWriter("Catch - > Nic Loop exception in Scan" + ex.ToString());
                            }
                        }
                    }
                }
            }

            if (isSubnetMatchCard)
            {
                lblNetworkHelp.BackColor = System.Drawing.Color.LightGreen;
                lblNoAdapter.Visible = false;
            }
            else
            {
                lblNetworkHelp.BackColor = System.Drawing.Color.Salmon;
                lblNoAdapter.Visible = true;
            }
        }

        private void btnSendSubnet_Click(object sender, EventArgs e)
        {
            {
                sendIPToModules[7] = ipNew[0];
                sendIPToModules[8] = ipNew[1];
                sendIPToModules[9] = ipNew[2];

                //loop thru all interfaces
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.Supports(NetworkInterfaceComponent.IPv4) && nic.OperationalStatus == OperationalStatus.Up)
                    {
                        foreach (var info in nic.GetIPProperties().UnicastAddresses)
                        {
                            // Only InterNetwork and not loopback which have a subnetmask
                            if (info.Address.AddressFamily == AddressFamily.InterNetwork &&
                                !IPAddress.IsLoopback(info.Address) &&
                                info.IPv4Mask != null)
                            {
                                Socket scanSocket;
                                try
                                {
                                    if (nic.OperationalStatus == OperationalStatus.Up
                                        && info.IPv4Mask != null)
                                    {
                                        scanSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                                        scanSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);
                                        scanSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                                        scanSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.DontRoute, true);

                                        try
                                        {
                                            scanSocket.Bind(new IPEndPoint(info.Address, 9999));
                                            scanSocket.SendTo(sendIPToModules, 0, sendIPToModules.Length, SocketFlags.None, mf.epModuleSet);
                                        }
                                        catch (Exception ex)
                                        {
                                            Log.EventWriter("Catch - > Send Subnet Bind and Send: " + ex.ToString());
                                        }

                                        scanSocket.Dispose();
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Log.EventWriter("Catch - > Nic Loop Send Subnet: " + ex.ToString());
                                }
                            }
                        }
                    }
                }

                Properties.Settings.Default.etIP_SubnetOne = ipCurrent[0] = ipNew[0];
                Properties.Settings.Default.etIP_SubnetTwo = ipCurrent[1] = ipNew[1];
                Properties.Settings.Default.etIP_SubnetThree = ipCurrent[2] = ipNew[2];

                Properties.Settings.Default.Save();

                mf.epModule = new IPEndPoint(IPAddress.Parse(
                    Properties.Settings.Default.etIP_SubnetOne.ToString() + "." +
                    Properties.Settings.Default.etIP_SubnetTwo.ToString() + "." +
                    Properties.Settings.Default.etIP_SubnetThree.ToString() + ".255"), 8888);

                lblNetworkHelp.Text =
                    Properties.Settings.Default.etIP_SubnetOne.ToString() + " . " +
                    Properties.Settings.Default.etIP_SubnetTwo.ToString() + " . " +
                    Properties.Settings.Default.etIP_SubnetThree.ToString();
            }

            pboxSendSteer.Visible = false;
            btnSerialCancel.Image = Properties.Resources.back_button;

            Log.EventWriter("Subnet Uploaded: " + lblNetworkHelp.Text);
        }

        private void nudFirstIP_Click(object sender, EventArgs e)
        {
            ((NumericUpDown)sender).ShowKeypad(this);
            ipNew[0] = (byte)nudFirstIP.Value;
            ipNew[1] = (byte)nudSecndIP.Value;
            ipNew[2] = (byte)nudThirdIP.Value;
            btnSendSubnet.Enabled = true;
            pboxSendSteer.Visible = true;
            btnSerialCancel.Image = Properties.Resources.Cancel64;
        }

        private void cboxUp_Click(object sender, EventArgs e)
        {
            if (cboxUp.Checked)
            {
                cboxUp.Text = "Up";
            }
            else
            {
                cboxUp.Text = "Up + Down";
            }
        }

        private void btnNetworkCPL_Click(object sender, EventArgs e)
        {
            Process.Start("ncpa.cpl");
        }

        private void AddSetIpButton()
        {
            Button btnLteFix = new Button
            {
                BackColor = Color.WhiteSmoke,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Tahoma", 13.5F, FontStyle.Bold, GraphicsUnit.Point, 0),
                Location = new Point(500, 500),
                Name = "btnLteFix",
                Size = new Size(120, 79),
                Text = "LTE fix",
                UseVisualStyleBackColor = false
            };

            Button btnSetIp = new Button
            {
                BackColor = Color.WhiteSmoke,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Tahoma", 14.25F, FontStyle.Bold, GraphicsUnit.Point, 0),
                Location = new Point(629, 500),
                Name = "btnSetWindowsIp",
                Size = new Size(125, 79),
                Text = "Set IP",
                UseVisualStyleBackColor = false
            };

            btnLteFix.Click += btnLteFix_Click;
            btnSetIp.Click += btnSetWindowsIp_Click;
            Controls.Add(btnLteFix);
            Controls.Add(btnSetIp);
            btnLteFix.BringToFront();
            btnSetIp.BringToFront();
        }

        private void btnLteFix_Click(object sender, EventArgs e)
        {
            DialogResult result = MessageBox.Show(
                this,
                "This will set Windows registry:\r\n\r\n"
                    + @"HKLM\SOFTWARE\Policies\Microsoft\Windows\WcmSvc\GroupPolicy"
                    + "\r\nfMinimizeConnections = 0\r\n\r\n"
                    + "This can help keep Ethernet and LTE/WiFi active at the same time.\r\n"
                    + "Windows may ask for administrator permission.",
                "LTE fix",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Information);

            if (result != DialogResult.OK) return;

            if (ApplyLteRegistryFix())
            {
                MessageBox.Show(this, "LTE fix applied. Restart Windows if the network still drops.", "LTE fix", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private bool ApplyLteRegistryFix()
        {
            const string args = @"ADD HKLM\SOFTWARE\Policies\Microsoft\Windows\WcmSvc\GroupPolicy /v fMinimizeConnections /t REG_DWORD /d 0 /f";

            try
            {
                ProcessStartInfo info = new ProcessStartInfo("reg.exe", args)
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using (Process process = Process.Start(info))
                {
                    process?.WaitForExit(15000);
                    if (process != null && process.ExitCode != 0)
                    {
                        MessageBox.Show(this, "Windows did not accept the registry change. Try running AgIO as administrator.", "LTE fix", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return false;
                    }
                }

                Log.EventWriter("LTE fix applied: fMinimizeConnections=0");
                return true;
            }
            catch (Exception ex)
            {
                Log.EventWriter("LTE fix failed: " + ex);
                MessageBox.Show(this, "Could not apply LTE fix.\r\n\r\n" + ex.Message, "LTE fix", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
        }

        private void btnSetWindowsIp_Click(object sender, EventArgs e)
        {
            List<NetworkInterface> ethernetAdapters = GetEthernetAdapters();
            if (ethernetAdapters.Count == 0)
            {
                MessageBox.Show(this, "No Ethernet adapter found.", "Set IP", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using (Form form = new Form())
            using (ListBox listAdapters = new ListBox())
            using (TextBox tboxIp = new TextBox())
            using (Button btnOk = new Button())
            using (Button btnCancel = new Button())
            using (Label lblAdapter = new Label())
            using (Label lblIp = new Label())
            using (Label lblMask = new Label())
            {
                form.Text = "Set Ethernet IP";
                form.StartPosition = FormStartPosition.CenterParent;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.ClientSize = new Size(560, 360);
                form.MinimizeBox = false;
                form.MaximizeBox = false;
                form.Font = new Font("Tahoma", 12F, FontStyle.Regular);

                lblAdapter.Text = "Ethernet adapter";
                lblAdapter.Left = 18;
                lblAdapter.Top = 18;
                lblAdapter.Width = 230;
                lblAdapter.Height = 26;

                listAdapters.Left = 18;
                listAdapters.Top = 48;
                listAdapters.Width = 524;
                listAdapters.Height = 132;
                listAdapters.Font = new Font("Tahoma", 11F, FontStyle.Bold);
                listAdapters.ItemHeight = 28;
                foreach (NetworkInterface nic in ethernetAdapters)
                {
                    listAdapters.Items.Add(new EthernetAdapterItem(nic));
                }
                listAdapters.SelectedIndex = Math.Max(0, GetPreferredEthernetAdapterIndex(ethernetAdapters));

                lblIp.Text = "IP address";
                lblIp.Left = 18;
                lblIp.Top = 196;
                lblIp.Width = 150;
                lblIp.Height = 26;

                tboxIp.Left = 18;
                tboxIp.Top = 226;
                tboxIp.Width = 230;
                tboxIp.Height = 34;
                tboxIp.Font = new Font("Tahoma", 16F, FontStyle.Bold);
                tboxIp.Text = Properties.Settings.Default.etIP_SubnetOne.ToString() + "."
                    + Properties.Settings.Default.etIP_SubnetTwo.ToString() + "."
                    + Properties.Settings.Default.etIP_SubnetThree.ToString() + ".10";
                tboxIp.Click += (_, __) => tboxIp.ShowKeyboard(form);

                lblMask.Text = "Mask: 255.255.255.0";
                lblMask.Left = 275;
                lblMask.Top = 230;
                lblMask.Width = 220;
                lblMask.Height = 28;

                btnOk.Text = "OK";
                btnOk.Left = 326;
                btnOk.Top = 300;
                btnOk.Width = 100;
                btnOk.Height = 38;
                btnOk.DialogResult = DialogResult.OK;

                btnCancel.Text = "Cancel";
                btnCancel.Left = 442;
                btnCancel.Top = 300;
                btnCancel.Width = 100;
                btnCancel.Height = 38;
                btnCancel.DialogResult = DialogResult.Cancel;

                form.Controls.Add(lblAdapter);
                form.Controls.Add(listAdapters);
                form.Controls.Add(lblIp);
                form.Controls.Add(tboxIp);
                form.Controls.Add(lblMask);
                form.Controls.Add(btnOk);
                form.Controls.Add(btnCancel);
                form.AcceptButton = btnOk;
                form.CancelButton = btnCancel;

                if (form.ShowDialog(this) != DialogResult.OK) return;

                if (!IPAddress.TryParse(tboxIp.Text.Trim(), out IPAddress ipAddress)
                    || ipAddress.AddressFamily != AddressFamily.InterNetwork)
                {
                    MessageBox.Show(this, "Invalid IP address.", "Set IP", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                EthernetAdapterItem selectedAdapter = listAdapters.SelectedItem as EthernetAdapterItem;
                if (selectedAdapter == null) return;

                if (SetEthernetStaticIp(selectedAdapter.Name, ipAddress.ToString()))
                {
                    byte[] ipBytes = ipAddress.GetAddressBytes();
                    Properties.Settings.Default.etIP_SubnetOne = ipCurrent[0] = ipNew[0] = ipBytes[0];
                    Properties.Settings.Default.etIP_SubnetTwo = ipCurrent[1] = ipNew[1] = ipBytes[1];
                    Properties.Settings.Default.etIP_SubnetThree = ipCurrent[2] = ipNew[2] = ipBytes[2];
                    Properties.Settings.Default.Save();

                    nudFirstIP.Value = ipBytes[0];
                    nudSecndIP.Value = ipBytes[1];
                    nudThirdIP.Value = ipBytes[2];
                    lblNetworkHelp.Text = ipBytes[0] + " . " + ipBytes[1] + " . " + ipBytes[2];
                    mf.epModule = new IPEndPoint(IPAddress.Parse(ipBytes[0] + "." + ipBytes[1] + "." + ipBytes[2] + ".255"), 8888);

                    Log.EventWriter("Ethernet IP set: " + selectedAdapter.Name + " -> " + ipAddress + " / 255.255.255.0");
                    MessageBox.Show(this, "IP set to " + ipAddress + "\r\nMask 255.255.255.0", "Set IP", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    ScanNetwork();
                }
            }
        }

        private bool SetEthernetStaticIp(string adapterName, string ipAddress)
        {
            string args = "interface ip set address name=\"" + adapterName + "\" static " + ipAddress + " 255.255.255.0";

            try
            {
                ProcessStartInfo info = new ProcessStartInfo("netsh", args)
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using (Process process = Process.Start(info))
                {
                    process?.WaitForExit(15000);
                    if (process != null && process.ExitCode != 0)
                    {
                        MessageBox.Show(this, "Windows did not accept the IP change. Try running AgIO as administrator.", "Set IP", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.EventWriter("Set Ethernet IP failed: " + ex);
                MessageBox.Show(this, "Could not set IP. Run AgIO as administrator.\r\n\r\n" + ex.Message, "Set IP", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
        }

        private int GetPreferredEthernetAdapterIndex(List<NetworkInterface> adapters)
        {
            for (int i = 0; i < adapters.Count; i++)
            {
                if (adapters[i].OperationalStatus == OperationalStatus.Up && IsAdapterOnCurrentSubnet(adapters[i])) return i;
            }

            for (int i = 0; i < adapters.Count; i++)
            {
                if (adapters[i].OperationalStatus == OperationalStatus.Up) return i;
            }

            return 0;
        }

        private bool IsAdapterOnCurrentSubnet(NetworkInterface nic)
        {
            foreach (UnicastIPAddressInformation info in nic.GetIPProperties().UnicastAddresses)
            {
                if (info.Address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(info.Address)) continue;

                byte[] data = info.Address.GetAddressBytes();
                if (data[0] == ipCurrent[0] && data[1] == ipCurrent[1] && data[2] == ipCurrent[2]) return true;
            }

            return false;
        }

        private static List<NetworkInterface> GetEthernetAdapters()
        {
            List<NetworkInterface> adapters = new List<NetworkInterface>();
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (IsEthernetAdapter(nic)) adapters.Add(nic);
            }

            return adapters;
        }

        private static bool IsEthernetAdapter(NetworkInterface nic)
        {
            if (nic == null) return false;
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211
                || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback
                || nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
            {
                return false;
            }

            string name = (nic.Name + " " + nic.Description).ToLowerInvariant();
            if (name.Contains("wi-fi") || name.Contains("wifi") || name.Contains("wireless") || name.Contains("bluetooth")) return false;

            return nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet
                || nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet3Megabit
                || nic.NetworkInterfaceType == NetworkInterfaceType.FastEthernetFx
                || nic.NetworkInterfaceType == NetworkInterfaceType.FastEthernetT
                || nic.NetworkInterfaceType == NetworkInterfaceType.GigabitEthernet
                || name.Contains("ethernet");
        }

        private sealed class EthernetAdapterItem
        {
            public EthernetAdapterItem(NetworkInterface adapter)
            {
                Name = adapter.Name;
                Description = adapter.Description;
                Status = adapter.OperationalStatus;
            }

            public string Name { get; }
            private string Description { get; }
            private OperationalStatus Status { get; }

            public override string ToString()
            {
                return Name + " - " + Status + " - " + Description;
            }
        }

        private void btnSerialCancel_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void btnSerialMonitor_Click(object sender, EventArgs e)
        {
            mf.ShowUDPMonitor();
        }

        private void btnUDPOff_Click(object sender, EventArgs e)
        {
            Properties.Settings.Default.setUDP_isOn = false;
            Properties.Settings.Default.setUDP_isSendNMEAToUDP = false;

            Properties.Settings.Default.Save();

            mf.YesMessageBox("AgIO will Restart to Disable UDP Networking Features");
            Log.EventWriter("Program Reset: Turning UDP OFF");

            Program.Restart();

            Close();
        }

        private void btnAutoSet_Click(object sender, EventArgs e)
        {
            nudFirstIP.Value = mf.scanReply.subnet[0];
            nudSecndIP.Value = mf.scanReply.subnet[1];
            nudThirdIP.Value = mf.scanReply.subnet[2];
            ipNew[0] = mf.scanReply.subnet[0];
            ipNew[1] = mf.scanReply.subnet[1];
            ipNew[2] = mf.scanReply.subnet[2];
            btnSerialCancel.Image = Properties.Resources.Cancel64;
            pboxSendSteer.Visible = true;
        }

        ////get the ipv4 address only
        //public void GetIP4AddressList()
        //{
        //    tboxNets.Text = "";
        //    foreach (IPAddress IPA in Dns.GetHostAddresses(Dns.GetHostName()))
        //    {
        //        if (IPA.AddressFamily == AddressFamily.InterNetwork)
        //        {
        //            tboxNets.Text += IPA.ToString() + "\r\n";
        //        }
        //    }
        //}

        //public void IsValidNetworkFound()
        //{
        //    foreach (IPAddress IPA in Dns.GetHostAddresses(Dns.GetHostName()))
        //    {
        //        if (IPA.AddressFamily == AddressFamily.InterNetwork)
        //        {
        //            byte[] data = IPA.GetAddressBytes();
        //            //  Split string by ".", check that array length is 3
        //            if (data[0] == 192 && data[1] == 168 && data[2] == 1)
        //            {
        //                if (data[3] < 255 && data[3] > 1)
        //                {
        //                    break;
        //                }
        //            }
        //        }
        //    }
        //}
    }
}
