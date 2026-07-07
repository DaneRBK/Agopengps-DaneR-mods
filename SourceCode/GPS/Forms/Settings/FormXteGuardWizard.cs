using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;
using AgLibrary.Logging;
using AgOpenGPS.Controls;

namespace AgOpenGPS
{
    public class FormXteGuardWizard : Form
    {
        private static readonly Color AppleBlue = Color.FromArgb(0, 122, 255);
        private static readonly Color AppleGreen = Color.FromArgb(52, 199, 89);
        private static readonly Color AppleRed = Color.FromArgb(255, 59, 48);
        private static readonly Color AppleGray = Color.FromArgb(229, 229, 234);
        private static readonly Color AppleGrayPressed = Color.FromArgb(209, 209, 214);

        private readonly FormGPS mf;
        private readonly Timer liveTimer = new Timer();

        private Button btnEnabled;
        private Button btnDirection;
        private Button btnApply;
        private Button btnDefaults;
        private NudlessNumericUpDown nudTrigger;
        private NudlessNumericUpDown nudGain;
        private NudlessNumericUpDown nudMaxCorrection;
        private NudlessNumericUpDown nudDecay;
        private NudlessNumericUpDown nudMinSpeed;
        private Label lblLiveXte;
        private Label lblLiveRate;
        private Label lblLiveCorrection;
        private Label lblStatus;

        private bool guardEnabled;
        private bool invertDirection;

        public FormXteGuardWizard(Form callingForm)
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
            Text = "XTE Guard Wizard";
            Name = "FormXteGuardWizard";
            ClientSize = new Size(760, 500);
            MinimumSize = new Size(760, 500);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.Gainsboro;
            Font = new Font("Tahoma", 11.25F, FontStyle.Regular, GraphicsUnit.Point, 0);

            Label lblTitle = new Label
            {
                AutoSize = false,
                Location = new Point(20, 14),
                Size = new Size(710, 58),
                Font = new Font("Tahoma", 13.5F, FontStyle.Bold),
                Text = "XTE Guard Wizard\r\nAdds a short steering correction when cross track error starts moving away fast."
            };

            btnEnabled = MakeButton("Guard Off", new Point(25, 88), new Size(150, 62), AppleGrayPressed);
            btnEnabled.Click += (sender, e) =>
            {
                guardEnabled = !guardEnabled;
                UpdateButtons();
            };

            btnDirection = MakeButton("Direction Normal", new Point(195, 88), new Size(190, 62), AppleGray);
            btnDirection.Click += (sender, e) =>
            {
                invertDirection = !invertDirection;
                UpdateButtons();
            };

            btnDefaults = MakeButton("Defaults", new Point(405, 88), new Size(130, 62), AppleGray);
            btnDefaults.Click += (sender, e) =>
            {
                guardEnabled = false;
                invertDirection = false;
                nudTrigger.Value = 3.0M;
                nudGain.Value = 0.6M;
                nudMaxCorrection.Value = 2.0M;
                nudDecay.Value = 12M;
                nudMinSpeed.Value = 1.0M;
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

            AddSetting(gbSettings, "Trigger cm/s", nudTrigger = MakeNud(0.5M, 20M, 1, 3.0M), 25, 32);
            AddSetting(gbSettings, "Gain deg/cm/s", nudGain = MakeNud(0.05M, 3M, 2, 0.6M), 255, 32);
            AddSetting(gbSettings, "Max correction deg", nudMaxCorrection = MakeNud(0.1M, 8M, 1, 2.0M), 485, 32);
            AddSetting(gbSettings, "Decay %", nudDecay = MakeNud(1M, 80M, 0, 12M), 140, 105);
            AddSetting(gbSettings, "Min speed km/h", nudMinSpeed = MakeNud(0M, 10M, 1, 1.0M), 370, 105);

            GroupBox gbLive = new GroupBox
            {
                Location = new Point(25, 372),
                Size = new Size(700, 92),
                Text = "Live"
            };

            lblLiveXte = MakeLabel(new Point(20, 28), new Size(200, 26));
            lblLiveRate = MakeLabel(new Point(245, 28), new Size(210, 26));
            lblLiveCorrection = MakeLabel(new Point(480, 28), new Size(190, 26));
            lblStatus = MakeLabel(new Point(20, 58), new Size(650, 26));

            gbLive.Controls.Add(lblLiveXte);
            gbLive.Controls.Add(lblLiveRate);
            gbLive.Controls.Add(lblLiveCorrection);
            gbLive.Controls.Add(lblStatus);

            Controls.Add(lblTitle);
            Controls.Add(btnEnabled);
            Controls.Add(btnDirection);
            Controls.Add(btnDefaults);
            Controls.Add(btnApply);
            Controls.Add(gbSettings);
            Controls.Add(gbLive);

            FormClosing += (sender, e) => liveTimer.Stop();
        }

        private void LoadSettingsToControls()
        {
            var settings = Properties.VehicleSettings.Default;
            guardEnabled = settings.setAS_xteGuardEnabled;
            invertDirection = settings.setAS_xteGuardInvertDirection;
            nudTrigger.Value = ClampDecimal((decimal)settings.setAS_xteGuardTriggerCmSec, nudTrigger.Minimum, nudTrigger.Maximum);
            nudGain.Value = ClampDecimal((decimal)settings.setAS_xteGuardGain, nudGain.Minimum, nudGain.Maximum);
            nudMaxCorrection.Value = ClampDecimal((decimal)settings.setAS_xteGuardMaxCorrection, nudMaxCorrection.Minimum, nudMaxCorrection.Maximum);
            nudDecay.Value = ClampDecimal((decimal)(settings.setAS_xteGuardDecay * 100.0), nudDecay.Minimum, nudDecay.Maximum);
            nudMinSpeed.Value = ClampDecimal((decimal)settings.setAS_xteGuardMinSpeed, nudMinSpeed.Minimum, nudMinSpeed.Maximum);
            lblStatus.Text = "Ready.";
        }

        private void BtnApply_Click(object sender, EventArgs e)
        {
            var settings = Properties.VehicleSettings.Default;
            settings.setAS_xteGuardEnabled = guardEnabled;
            settings.setAS_xteGuardInvertDirection = invertDirection;
            settings.setAS_xteGuardTriggerCmSec = (double)nudTrigger.Value;
            settings.setAS_xteGuardGain = (double)nudGain.Value;
            settings.setAS_xteGuardMaxCorrection = (double)nudMaxCorrection.Value;
            settings.setAS_xteGuardDecay = (double)nudDecay.Value * 0.01;
            settings.setAS_xteGuardMinSpeed = (double)nudMinSpeed.Value;
            settings.Save();

            Log.EventWriter("XTE Guard saved: enabled "
                + guardEnabled.ToString(CultureInfo.InvariantCulture)
                + ", trigger " + settings.setAS_xteGuardTriggerCmSec.ToString("N2", CultureInfo.InvariantCulture)
                + ", gain " + settings.setAS_xteGuardGain.ToString("N2", CultureInfo.InvariantCulture)
                + ", max " + settings.setAS_xteGuardMaxCorrection.ToString("N2", CultureInfo.InvariantCulture)
                + ", decay " + settings.setAS_xteGuardDecay.ToString("N2", CultureInfo.InvariantCulture)
                + ", invert " + invertDirection.ToString(CultureInfo.InvariantCulture));

            lblStatus.Text = "Saved.";
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            btnEnabled.Text = guardEnabled ? "Guard On" : "Guard Off";
            ApplyButtonColor(btnEnabled, guardEnabled ? AppleGreen : AppleGrayPressed);

            btnDirection.Text = invertDirection ? "Direction Invert" : "Direction Normal";
            ApplyButtonColor(btnDirection, invertDirection ? AppleBlue : AppleGray);
        }

        private void UpdateLiveLabels()
        {
            double xteCm = GetXteCm();
            lblLiveXte.Text = double.IsNaN(xteCm) ? "XTE: no line" : "XTE: " + xteCm.ToString("N1", CultureInfo.CurrentCulture) + " cm";
            lblLiveRate.Text = "Rate: " + (mf?.XteGuardRateCmSec ?? 0).ToString("N1", CultureInfo.CurrentCulture) + " cm/s";
            lblLiveCorrection.Text = "Add: " + (mf?.XteGuardCorrectionDeg ?? 0).ToString("N2", CultureInfo.CurrentCulture) + " deg";
        }

        private double GetXteCm()
        {
            if (mf == null) return double.NaN;
            if (mf.guidanceLineDistanceOff == 32000 || mf.guidanceLineDistanceOff == 32020 || Math.Abs(mf.guidanceLineDistanceOff) > 29000)
            {
                return double.NaN;
            }

            return mf.guidanceLineDistanceOff * 0.1;
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
                BackColor = Color.AliceBlue,
                DecimalPlaces = decimalPlaces,
                Font = new Font("Tahoma", 21.75F, FontStyle.Bold),
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
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        private static Button MakeButton(string text, Point location, Size size, Color backColor)
        {
            Button button = new Button
            {
                BackColor = backColor,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = GetButtonForeColor(backColor),
                Location = location,
                Size = size,
                Text = text,
                UseVisualStyleBackColor = false,
                Cursor = Cursors.Hand
            };

            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseDownBackColor = Darken(backColor, 0.10);
            button.FlatAppearance.MouseOverBackColor = Lighten(backColor, 0.08);
            button.Resize += (sender, e) => ApplyRoundedRegion((Button)sender);
            button.HandleCreated += (sender, e) => ApplyRoundedRegion((Button)sender);

            return button;
        }

        private static void ApplyButtonColor(Button button, Color backColor)
        {
            button.BackColor = backColor;
            button.ForeColor = GetButtonForeColor(backColor);
            button.FlatAppearance.MouseDownBackColor = Darken(backColor, 0.10);
            button.FlatAppearance.MouseOverBackColor = Lighten(backColor, 0.08);
            ApplyRoundedRegion(button);
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
