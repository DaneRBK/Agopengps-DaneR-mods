using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AgOpenGPS
{
    public partial class FormGPS
    {
        private Button btnWindowsDeviceReset;
        private Button btnFieldsMap;
        private Button btnDxfMap;
        private Button btnObstacleMarker;
        private Button btnFixRoll;
        private Button btnMarkedEdgesBoundary;

        private void InitializeWindowsDeviceResetButton()
        {
            btnWindowsDeviceReset = new Button
            {
                Name = "btnWindowsDeviceReset",
                Text = "USB RESET",
                Width = 132,
                Height = 38,
                Top = 4,
                BackColor = Color.FromArgb(185, 185, 185),
                ForeColor = Color.Black,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Tahoma", 11F, FontStyle.Bold),
                TabStop = false
            };

            btnWindowsDeviceReset.FlatAppearance.BorderColor = Color.Black;
            btnWindowsDeviceReset.FlatAppearance.BorderSize = 1;
            btnWindowsDeviceReset.Click += btnWindowsDeviceReset_Click;

            btnFieldsMap = new Button
            {
                Name = "btnFieldsMap",
                Text = "FIELDS",
                Width = 112,
                Height = 38,
                Top = 4,
                BackColor = Color.FromArgb(185, 185, 185),
                ForeColor = Color.Black,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Tahoma", 11F, FontStyle.Bold),
                TabStop = false
            };

            btnFieldsMap.FlatAppearance.BorderColor = Color.Black;
            btnFieldsMap.FlatAppearance.BorderSize = 1;
            btnFieldsMap.Click += btnFieldsMap_Click;

            btnDxfMap = new Button
            {
                Name = "btnDxfMap",
                Text = "DXF MAP",
                Width = 112,
                Height = 38,
                Top = 4,
                BackColor = Color.FromArgb(185, 185, 185),
                ForeColor = Color.Black,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Tahoma", 10F, FontStyle.Bold),
                TabStop = false
            };

            btnDxfMap.FlatAppearance.BorderColor = Color.Black;
            btnDxfMap.FlatAppearance.BorderSize = 1;
            btnDxfMap.Click += btnDxfMap_Click;

            btnObstacleMarker = new Button
            {
                Name = "btnObstacleMarker",
                Text = "OBST",
                Width = 92,
                Height = 38,
                Top = 4,
                BackColor = Color.FromArgb(230, 120, 100),
                ForeColor = Color.Black,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Tahoma", 10F, FontStyle.Bold),
                TabStop = false
            };

            btnObstacleMarker.FlatAppearance.BorderColor = Color.Black;
            btnObstacleMarker.FlatAppearance.BorderSize = 1;
            btnObstacleMarker.Click += btnObstacleMarker_Click;

            btnFixRoll = new Button
            {
                Name = "btnFixRoll",
                Text = "FIX ROLL",
                Width = 112,
                Height = 38,
                Top = 4,
                BackColor = Color.FromArgb(116, 190, 92),
                ForeColor = Color.Black,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Tahoma", 10F, FontStyle.Bold),
                TabStop = false,
                Visible = false
            };

            btnFixRoll.FlatAppearance.BorderColor = Color.FromArgb(35, 100, 35);
            btnFixRoll.FlatAppearance.BorderSize = 2;
            btnFixRoll.Click += btnFixRoll_Click;

            btnMarkedEdgesBoundary = new Button
            {
                Name = "btnMarkedEdgesBoundary",
                Text = "OUTER\r\nEDGES",
                Width = 64,
                Height = 64,
                BackColor = Color.FromArgb(116, 190, 92),
                ForeColor = Color.Black,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Tahoma", 8F, FontStyle.Bold),
                Margin = new Padding(0),
                TabStop = false,
                Visible = false
            };

            btnMarkedEdgesBoundary.FlatAppearance.BorderColor = Color.FromArgb(35, 100, 35);
            btnMarkedEdgesBoundary.FlatAppearance.BorderSize = 2;
            btnMarkedEdgesBoundary.Click += btnMarkedEdgesBoundary_Click;

            Controls.Add(btnWindowsDeviceReset);
            Controls.Add(btnFieldsMap);
            Controls.Add(btnDxfMap);
            Controls.Add(btnObstacleMarker);
            Controls.Add(btnFixRoll);
            btnWindowsDeviceReset.BringToFront();
            btnFieldsMap.BringToFront();
            btnDxfMap.BringToFront();
            btnObstacleMarker.BringToFront();
            btnFixRoll.BringToFront();
            PositionWindowsDeviceResetButton();

            Resize += (_, __) => PositionWindowsDeviceResetButton();
        }

        private void PositionWindowsDeviceResetButton()
        {
            if (btnWindowsDeviceReset == null) return;

            btnWindowsDeviceReset.Left = Math.Max(0, (ClientSize.Width - btnWindowsDeviceReset.Width) / 2);
            btnWindowsDeviceReset.Top = 4;
            btnWindowsDeviceReset.BringToFront();

            if (btnFieldsMap == null) return;

            btnFieldsMap.Left = Math.Max(0, btnWindowsDeviceReset.Left - btnFieldsMap.Width - 8);
            btnFieldsMap.Top = 4;
            btnFieldsMap.BringToFront();

            if (btnDxfMap == null) return;

            btnDxfMap.Left = Math.Max(0, btnFieldsMap.Left - btnDxfMap.Width - 8);
            btnDxfMap.Top = 4;
            btnDxfMap.BringToFront();

            if (btnObstacleMarker == null) return;

            btnObstacleMarker.Left = Math.Min(ClientSize.Width - btnObstacleMarker.Width, btnWindowsDeviceReset.Right + 8);
            btnObstacleMarker.Top = 4;
            btnObstacleMarker.BringToFront();

            if (btnFixRoll == null) return;

            btnFixRoll.Left = Math.Min(ClientSize.Width - btnFixRoll.Width, btnObstacleMarker.Right + 8);
            btnFixRoll.Top = 4;
            btnFixRoll.BringToFront();
        }

        private void UpdateFixRollButtonVisibility()
        {
            if (btnFixRoll == null) return;

            bool shouldShow = AutoRollLearnHasSuggestion
                && AutoRollLearnConfidence >= 75.0;

            btnFixRoll.Visible = shouldShow;
            btnFixRoll.Enabled = shouldShow;
            if (shouldShow)
            {
                btnFixRoll.Text = "FIX ROLL\r\n" + AutoRollLearnSuggestedCorrectionDeg.ToString("N3");
                btnFixRoll.BringToFront();
            }
        }

        private void btnFieldsMap_Click(object sender, EventArgs e)
        {
            ToggleFieldsOverlay();
        }

        private void btnDxfMap_Click(object sender, EventArgs e)
        {
            ToggleDxfMapTool();
        }

        private void btnObstacleMarker_Click(object sender, EventArgs e)
        {
            ShowObstacleMarker();
        }

        private void btnFixRoll_Click(object sender, EventArgs e)
        {
            if (!AutoRollLearnHasSuggestion || AutoRollLearnConfidence < 75.0)
            {
                TimedMessageBox(2500, "Fix roll", "Auto Roll Learn confidence is below 75%");
                UpdateFixRollButtonVisibility();
                return;
            }

            double correction = AutoRollLearnSuggestedCorrectionDeg;
            if (ApplyAutoRollLearnSuggestion())
            {
                TimedMessageBox(3000, "Fix roll", "Roll corrected " + correction.ToString("N3") + " deg");
                btnFixRoll.Visible = false;
            }

            UpdateFixRollButtonVisibility();
        }

        private async void btnWindowsDeviceReset_Click(object sender, EventArgs e)
        {
            var result = MessageBox.Show(
                this,
                "Windows ce kratko iskljuciti i ponovo ukljuciti COM/USB-serial portove i USB Ethernet adaptere. Potrebna su Administrator prava.",
                "USB/COM reset",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (result != DialogResult.Yes) return;

            btnWindowsDeviceReset.Enabled = false;
            TimedMessageBox(3000, "USB/COM reset", "Prihvati Windows Administrator prompt");

            try
            {
                await Task.Run(StartElevatedWindowsDeviceReset);
                TimedMessageBox(3000, "USB/COM reset", "Reset komanda je zavrsena");
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                TimedMessageBox(2500, "USB/COM reset", "Administrator prompt je otkazan");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "USB/COM reset", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnWindowsDeviceReset.Enabled = true;
            }
        }

        private static void StartElevatedWindowsDeviceReset()
        {
            var script = BuildWindowsDeviceResetScript();
            var encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

            var processInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -EncodedCommand " + encodedScript,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using (var process = Process.Start(processInfo))
            {
                process?.WaitForExit(30000);
            }
        }

        private static string BuildWindowsDeviceResetScript()
        {
            return string.Join(Environment.NewLine, new[]
            {
                "$ErrorActionPreference = 'Continue'",
                "$targets = Get-PnpDevice -PresentOnly | Where-Object {",
                "    $text = \"$($_.FriendlyName) $($_.Class) $($_.InstanceId)\"",
                "    $isCom = $_.Class -eq 'Ports' -or $text -match '(?i)\\(COM\\d+\\)|usb.serial|usb-serial|usb-ser|ch340|ch341|ftdi|arduino'",
                "    $isUsbNet = $_.Class -eq 'Net' -and $_.InstanceId -like 'USB\\*' -and $text -match '(?i)usb|rndis|ethernet|network|lan|realtek|asix|cdc|ndis'",
                "    $isCom -or $isUsbNet",
                "} | Sort-Object FriendlyName",
                "foreach ($device in $targets) {",
                "    try {",
                "        Disable-PnpDevice -InstanceId $device.InstanceId -Confirm:$false -ErrorAction Stop",
                "    } catch { }",
                "}",
                "Start-Sleep -Milliseconds 1400",
                "foreach ($device in $targets) {",
                "    try {",
                "        Enable-PnpDevice -InstanceId $device.InstanceId -Confirm:$false -ErrorAction Stop",
                "    } catch { }",
                "}",
                "Start-Sleep -Milliseconds 700"
            });
        }
    }
}
