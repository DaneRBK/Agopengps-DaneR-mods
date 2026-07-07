using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;
using AgLibrary.Logging;
using AgOpenGPS.Controls;

namespace AgOpenGPS
{
    public class FormRollCutSettings : Form
    {
        private static readonly Color AppleBlue = ModernUi.Accent;
        private static readonly Color AppleGreen = ModernUi.Success;
        private static readonly Color AppleGray = ModernUi.SurfaceAlt;
        private static readonly Color AppleGrayPressed = ModernUi.Disabled;

        private readonly FormGPS mf;
        private readonly Timer liveTimer = new Timer();

        private Button btnEnabled;
        private Button btnApply;
        private Button btnDefaults;
        private NudlessNumericUpDown nudAngle;
        private NudlessNumericUpDown nudWindow;
        private NudlessNumericUpDown nudRate;
        private NudlessNumericUpDown nudHold;
        private NudlessNumericUpDown nudRecovery;
        private Label lblLiveRaw;
        private Label lblLiveEffective;
        private Label lblLiveRate;
        private Label lblStatus;

        private bool rollCutEnabled;

        public FormRollCutSettings(Form callingForm)
        {
            mf = callingForm as FormGPS;

            InitializeComponent();
            LoadSettingsToControls();
            UpdateButtons();
            UpdateLiveLabels();

            liveTimer.Interval = 250;
            liveTimer.Tick += (sender, e) => UpdateLiveLabels();
            liveTimer.Start();
        }

        private void InitializeComponent()
        {
            Text = "Roll cut";
            Name = "FormRollCutSettings";
            ClientSize = new Size(760, 500);
            MinimumSize = new Size(760, 500);
            StartPosition = FormStartPosition.CenterParent;
            ModernUi.ApplyForm(this);

            Label lblTitle = new Label
            {
                AutoSize = false,
                Location = new Point(20, 14),
                Size = new Size(710, 58),
                Font = ModernUi.TitleFont,
                ForeColor = ModernUi.Text,
                Text = "Roll cut\r\nCuts short fast roll spikes from potholes before sidehill steering compensation uses them."
            };

            btnEnabled = MakeButton("Roll cut Off", new Point(25, 88), new Size(170, 62), AppleGrayPressed);
            btnEnabled.Click += (sender, e) =>
            {
                rollCutEnabled = !rollCutEnabled;
                UpdateButtons();
            };

            btnDefaults = MakeButton("Defaults", new Point(405, 88), new Size(130, 62), AppleGray);
            btnDefaults.Click += (sender, e) =>
            {
                rollCutEnabled = false;
                nudAngle.Value = 2.0M;
                nudWindow.Value = 0.5M;
                nudRate.Value = 4.0M;
                nudHold.Value = 0.8M;
                nudRecovery.Value = 10M;
                UpdateButtons();
                lblStatus.Text = "Defaults loaded. Press Apply to save.";
            };

            btnApply = MakeButton("Apply", new Point(565, 88), new Size(150, 62), AppleGreen);
            btnApply.Click += BtnApply_Click;

            GroupBox gbSettings = new GroupBox
            {
                Location = new Point(25, 170),
                Size = new Size(700, 185),
                Text = "Settings"
            };
            ModernUi.StyleGroupBox(gbSettings);

            AddSetting(gbSettings, "Angle deg", nudAngle = MakeNud(0.2M, 10M, 1, 2.0M), 25, 32);
            AddSetting(gbSettings, "Window sec", nudWindow = MakeNud(0.1M, 2M, 1, 0.5M), 255, 32);
            AddSetting(gbSettings, "Min rate deg/s", nudRate = MakeNud(0.5M, 30M, 1, 4.0M), 485, 32);
            AddSetting(gbSettings, "Hold sec", nudHold = MakeNud(0.1M, 3M, 1, 0.8M), 140, 105);
            AddSetting(gbSettings, "Recovery %", nudRecovery = MakeNud(1M, 100M, 0, 10M), 370, 105);

            GroupBox gbLive = new GroupBox
            {
                Location = new Point(25, 372),
                Size = new Size(700, 92),
                Text = "Live"
            };
            ModernUi.StyleGroupBox(gbLive);

            lblLiveRaw = MakeLabel(new Point(20, 28), new Size(170, 26));
            lblLiveEffective = MakeLabel(new Point(210, 28), new Size(210, 26));
            lblLiveRate = MakeLabel(new Point(445, 28), new Size(220, 26));
            lblStatus = MakeLabel(new Point(20, 58), new Size(650, 26));

            gbLive.Controls.Add(lblLiveRaw);
            gbLive.Controls.Add(lblLiveEffective);
            gbLive.Controls.Add(lblLiveRate);
            gbLive.Controls.Add(lblStatus);

            Controls.Add(lblTitle);
            Controls.Add(btnEnabled);
            Controls.Add(btnDefaults);
            Controls.Add(btnApply);
            Controls.Add(gbSettings);
            Controls.Add(gbLive);

            FormClosing += (sender, e) => liveTimer.Stop();
        }

        private void LoadSettingsToControls()
        {
            var settings = Properties.VehicleSettings.Default;
            rollCutEnabled = settings.setIMU_rollCutEnabled;
            nudAngle.Value = ClampDecimal((decimal)settings.setIMU_rollCutAngleDeg, nudAngle.Minimum, nudAngle.Maximum);
            nudWindow.Value = ClampDecimal((decimal)settings.setIMU_rollCutWindowSec, nudWindow.Minimum, nudWindow.Maximum);
            nudRate.Value = ClampDecimal((decimal)settings.setIMU_rollCutRateDegSec, nudRate.Minimum, nudRate.Maximum);
            nudHold.Value = ClampDecimal((decimal)settings.setIMU_rollCutHoldSec, nudHold.Minimum, nudHold.Maximum);
            nudRecovery.Value = ClampDecimal((decimal)settings.setIMU_rollCutRecoveryPercent, nudRecovery.Minimum, nudRecovery.Maximum);
            lblStatus.Text = "Ready.";
        }

        private void BtnApply_Click(object sender, EventArgs e)
        {
            var settings = Properties.VehicleSettings.Default;
            settings.setIMU_rollCutEnabled = rollCutEnabled;
            settings.setIMU_rollCutAngleDeg = (double)nudAngle.Value;
            settings.setIMU_rollCutWindowSec = (double)nudWindow.Value;
            settings.setIMU_rollCutRateDegSec = (double)nudRate.Value;
            settings.setIMU_rollCutHoldSec = (double)nudHold.Value;
            settings.setIMU_rollCutRecoveryPercent = (double)nudRecovery.Value;
            settings.Save();

            Log.EventWriter("Roll cut saved: enabled "
                + rollCutEnabled.ToString(CultureInfo.InvariantCulture)
                + ", angle " + settings.setIMU_rollCutAngleDeg.ToString("N2", CultureInfo.InvariantCulture)
                + ", window " + settings.setIMU_rollCutWindowSec.ToString("N2", CultureInfo.InvariantCulture)
                + ", rate " + settings.setIMU_rollCutRateDegSec.ToString("N2", CultureInfo.InvariantCulture)
                + ", hold " + settings.setIMU_rollCutHoldSec.ToString("N2", CultureInfo.InvariantCulture)
                + ", recovery " + settings.setIMU_rollCutRecoveryPercent.ToString("N1", CultureInfo.InvariantCulture));

            lblStatus.Text = "Saved.";
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            btnEnabled.Text = rollCutEnabled ? "Roll cut On" : "Roll cut Off";
            ApplyButtonColor(btnEnabled, rollCutEnabled ? AppleGreen : AppleGrayPressed);
        }

        private void UpdateLiveLabels()
        {
            double raw = mf?.ahrs.imuRoll ?? 88888;
            double effective = mf?.RollCutEffectiveRoll ?? double.NaN;
            lblLiveRaw.Text = raw == 88888 ? "Raw: no roll" : "Raw: " + raw.ToString("N2", CultureInfo.CurrentCulture);
            lblLiveEffective.Text = double.IsNaN(effective) ? "Steer roll: --" : "Steer roll: " + effective.ToString("N2", CultureInfo.CurrentCulture);
            lblLiveRate.Text = "Rate: " + (mf?.RollCutRateDegSec ?? 0).ToString("N1", CultureInfo.CurrentCulture) + " deg/s";

            if (mf?.IsRollCutActing == true)
            {
                lblStatus.Text = "Cutting fast roll spike.";
            }
            else if (lblStatus.Text == "Cutting fast roll spike.")
            {
                lblStatus.Text = "Ready.";
            }
        }

        private static void AddSetting(GroupBox groupBox, string text, NudlessNumericUpDown nud, int x, int y)
        {
            Label label = MakeLabel(new Point(x, y), new Size(185, 24));
            label.Text = text;
            nud.Location = new Point(x, y + 28);
            groupBox.Controls.Add(label);
            groupBox.Controls.Add(nud);
        }

        private static NudlessNumericUpDown MakeNud(decimal minimum, decimal maximum, int decimalPlaces, decimal value)
        {
            NudlessNumericUpDown nud = new NudlessNumericUpDown
            {
                BackColor = ModernUi.Surface,
                DecimalPlaces = decimalPlaces,
                Font = ModernUi.InputFont,
                ForeColor = ModernUi.Text,
                InterceptArrowKeys = false,
                Minimum = minimum,
                Maximum = maximum,
                ReadOnly = true,
                Size = new Size(150, 42),
                TextAlign = HorizontalAlignment.Center,
                Value = value
            };

            nud.Click += Nud_Click;
            return nud;
        }

        private static void Nud_Click(object sender, EventArgs e)
        {
            ((NudlessNumericUpDown)sender).ShowKeypad(((Control)sender).FindForm());
        }

        private static Label MakeLabel(Point location, Size size)
        {
            return new Label
            {
                AutoSize = false,
                Location = location,
                Size = size,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = ModernUi.Text,
                Font = ModernUi.BaseFont
            };
        }

        private static Button MakeButton(string text, Point location, Size size, Color backColor)
        {
            Button button = new Button
            {
                BackColor = backColor,
                Location = location,
                Size = size,
                Text = text,
                UseVisualStyleBackColor = false
            };

            ModernUi.StyleButton(button, backColor);

            return button;
        }

        private static void ApplyButtonColor(Button button, Color backColor)
        {
            ModernUi.UpdateButtonColor(button, backColor);
        }

        private static void ApplyRoundedRegion(Button button)
        {
            int radius = 14;
            Rectangle bounds = new Rectangle(0, 0, button.Width, button.Height);
            using (GraphicsPath path = new GraphicsPath())
            {
                int diameter = radius * 2;
                path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
                path.AddArc(bounds.Right - diameter - 1, bounds.Top, diameter, diameter, 270, 90);
                path.AddArc(bounds.Right - diameter - 1, bounds.Bottom - diameter - 1, diameter, diameter, 0, 90);
                path.AddArc(bounds.Left, bounds.Bottom - diameter - 1, diameter, diameter, 90, 90);
                path.CloseFigure();
                button.Region = new Region(path);
            }
        }

        private static Color GetButtonForeColor(Color backColor)
        {
            double brightness = (backColor.R * 0.299) + (backColor.G * 0.587) + (backColor.B * 0.114);
            return brightness > 170 ? Color.FromArgb(28, 28, 30) : Color.White;
        }

        private static Color Lighten(Color color, double amount)
        {
            return Color.FromArgb(color.A,
                color.R + (int)((255 - color.R) * amount),
                color.G + (int)((255 - color.G) * amount),
                color.B + (int)((255 - color.B) * amount));
        }

        private static Color Darken(Color color, double amount)
        {
            return Color.FromArgb(color.A,
                Math.Max(0, color.R - (int)(color.R * amount)),
                Math.Max(0, color.G - (int)(color.G * amount)),
                Math.Max(0, color.B - (int)(color.B * amount)));
        }

        private static decimal ClampDecimal(decimal value, decimal min, decimal max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
