using AgIO.Controls;
using AgIO.Properties;
using AgLibrary.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows.Forms;

namespace AgIO
{
    public partial class FormLoop
    {
        private void AddIpSettingsMenuItem()
        {
            if (toolStripDropDownButton1 == null || isobusToolStripMenuItem == null) return;
            if (toolStripDropDownButton1.DropDownItems.ContainsKey("ipToolStripMenuItem")) return;

            ToolStripMenuItem ipToolStripMenuItem = new ToolStripMenuItem
            {
                Font = isobusToolStripMenuItem.Font,
                Image = Properties.Resources.EthernetSetup,
                Name = "ipToolStripMenuItem",
                Size = isobusToolStripMenuItem.Size,
                Text = "IP"
            };

            ipToolStripMenuItem.Click += ipToolStripMenuItem_Click;

            int index = toolStripDropDownButton1.DropDownItems.IndexOf(isobusToolStripMenuItem);
            if (index >= 0) toolStripDropDownButton1.DropDownItems.Insert(index + 1, ipToolStripMenuItem);
            else toolStripDropDownButton1.DropDownItems.Add(ipToolStripMenuItem);
        }

        private void ipToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ShowIpSettingsDialog();
        }

        private void ShowIpSettingsDialog()
        {
            List<NetworkInterface> ethernetAdapters = GetEthernetAdapters();

            using (Form form = new Form())
            using (ListBox listAdapters = new ListBox())
            using (TextBox tboxIp = new TextBox())
            using (Button btnSetIp = new Button())
            using (Button btnLteFix = new Button())
            using (Button btnClose = new Button())
            using (Label lblAdapter = new Label())
            using (Label lblIp = new Label())
            using (Label lblMask = new Label())
            {
                form.Text = "IP";
                form.StartPosition = FormStartPosition.CenterParent;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.ClientSize = new Size(590, 390);
                form.MinimizeBox = false;
                form.MaximizeBox = false;
                form.Font = new Font("Tahoma", 12F, FontStyle.Regular);

                lblAdapter.Text = "Ethernet adapter";
                lblAdapter.SetBounds(18, 16, 250, 26);

                listAdapters.SetBounds(18, 46, 554, 150);
                listAdapters.Font = new Font("Tahoma", 11F, FontStyle.Bold);
                listAdapters.ItemHeight = 30;
                foreach (NetworkInterface nic in ethernetAdapters)
                {
                    listAdapters.Items.Add(new EthernetAdapterItem(nic));
                }

                if (listAdapters.Items.Count > 0)
                {
                    listAdapters.SelectedIndex = Math.Max(0, GetPreferredEthernetAdapterIndex(ethernetAdapters));
                }

                lblIp.Text = "IP address";
                lblIp.SetBounds(18, 216, 150, 26);

                tboxIp.SetBounds(18, 246, 240, 38);
                tboxIp.Font = new Font("Tahoma", 16F, FontStyle.Bold);
                tboxIp.Text = Settings.Default.etIP_SubnetOne.ToString() + "."
                    + Settings.Default.etIP_SubnetTwo.ToString() + "."
                    + Settings.Default.etIP_SubnetThree.ToString() + ".10";
                tboxIp.Click += (_, __) => tboxIp.ShowKeyboard(form);

                lblMask.Text = "Mask: 255.255.255.0";
                lblMask.SetBounds(286, 251, 240, 28);

                btnSetIp.Text = "Set IP";
                btnSetIp.SetBounds(18, 316, 132, 52);
                btnSetIp.Font = new Font("Tahoma", 13F, FontStyle.Bold);
                btnSetIp.Click += (_, __) =>
                {
                    if (listAdapters.SelectedItem == null)
                    {
                        MessageBox.Show(form, "No Ethernet adapter selected.", "IP", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    if (!IPAddress.TryParse(tboxIp.Text.Trim(), out IPAddress ipAddress)
                        || ipAddress.AddressFamily != AddressFamily.InterNetwork)
                    {
                        MessageBox.Show(form, "Invalid IP address.", "IP", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    EthernetAdapterItem selectedAdapter = listAdapters.SelectedItem as EthernetAdapterItem;
                    if (selectedAdapter == null) return;

                    if (SetEthernetStaticIp(selectedAdapter.Name, ipAddress.ToString()))
                    {
                        UpdateAgIoSubnetFromIp(ipAddress);
                        MessageBox.Show(form, "IP set to " + ipAddress + "\r\nMask 255.255.255.0", "IP", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                };

                btnLteFix.Text = "LTE fix";
                btnLteFix.SetBounds(166, 316, 132, 52);
                btnLteFix.Font = new Font("Tahoma", 13F, FontStyle.Bold);
                btnLteFix.Click += (_, __) => ApplyLteFixWithPrompt(form);

                btnClose.Text = "Close";
                btnClose.SetBounds(440, 316, 132, 52);
                btnClose.Font = new Font("Tahoma", 13F, FontStyle.Bold);
                btnClose.Click += (_, __) => form.Close();

                form.Controls.Add(lblAdapter);
                form.Controls.Add(listAdapters);
                form.Controls.Add(lblIp);
                form.Controls.Add(tboxIp);
                form.Controls.Add(lblMask);
                form.Controls.Add(btnSetIp);
                form.Controls.Add(btnLteFix);
                form.Controls.Add(btnClose);

                if (ethernetAdapters.Count == 0)
                {
                    listAdapters.Items.Add("No Ethernet adapter found");
                    btnSetIp.Enabled = false;
                }

                form.ShowDialog(this);
            }
        }

        private void UpdateAgIoSubnetFromIp(IPAddress ipAddress)
        {
            byte[] ipBytes = ipAddress.GetAddressBytes();
            Settings.Default.etIP_SubnetOne = ipBytes[0];
            Settings.Default.etIP_SubnetTwo = ipBytes[1];
            Settings.Default.etIP_SubnetThree = ipBytes[2];
            Settings.Default.Save();
            epModule = new IPEndPoint(IPAddress.Parse(ipBytes[0] + "." + ipBytes[1] + "." + ipBytes[2] + ".255"), 8888);
            Log.EventWriter("AgIO subnet updated from Set IP: " + epModule);
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
                        MessageBox.Show(this, "Windows did not accept the IP change. Try running AgIO as administrator.", "IP", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return false;
                    }
                }

                Log.EventWriter("Ethernet IP set: " + adapterName + " -> " + ipAddress + " / 255.255.255.0");
                return true;
            }
            catch (Exception ex)
            {
                Log.EventWriter("Set Ethernet IP failed: " + ex);
                MessageBox.Show(this, "Could not set IP.\r\n\r\n" + ex.Message, "IP", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
        }

        private void ApplyLteFixWithPrompt(IWin32Window owner)
        {
            DialogResult result = MessageBox.Show(
                owner,
                "This will set Windows registry:\r\n\r\n"
                    + @"HKLM\SOFTWARE\Policies\Microsoft\Windows\WcmSvc\GroupPolicy"
                    + "\r\nfMinimizeConnections = 0\r\n\r\n"
                    + "This can help keep Ethernet and LTE/WiFi active at the same time.",
                "LTE fix",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Information);

            if (result != DialogResult.OK) return;

            if (ApplyLteRegistryFix())
            {
                MessageBox.Show(owner, "LTE fix applied. Restart Windows if the network still drops.", "LTE fix", MessageBoxButtons.OK, MessageBoxIcon.Information);
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

        private int GetPreferredEthernetAdapterIndex(List<NetworkInterface> adapters)
        {
            for (int i = 0; i < adapters.Count; i++)
            {
                if (adapters[i].OperationalStatus == OperationalStatus.Up) return i;
            }

            return 0;
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
    }
}
