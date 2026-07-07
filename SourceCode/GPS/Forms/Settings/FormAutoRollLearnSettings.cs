using AgLibrary.Logging;
using AgOpenGPS.Controls;
using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace AgOpenGPS
{
    public sealed class FormAutoRollLearnSettings : Form
    {
        private static readonly Color AppleBlue = Color.FromArgb(0, 122, 255);
        private static readonly Color AppleGreen = Color.FromArgb(52, 199, 89);
        private static readonly Color AppleGray = Color.FromArgb(229, 229, 234);
        private static readonly Color AppleOrange = Color.FromArgb(255, 149, 0);

        private readonly FormGPS mf;
        private readonly Timer liveTimer = new Timer();

        private Button btnEnable;
        private Button btnAutoApply;
        private Button btnApply;
        private Label lblLive;
        private Label lblLastAction;
        private NudlessNumericUpDown nudMinLength;
        private NudlessNumericUpDown nudMaxXte;
        private NudlessNumericUpDown nudConfidence;
        private NudlessNumericUpDown nudMaxStep;

        public FormAutoRollLearnSettings(Form callingForm)
        {
            mf = callingForm as FormGPS;
            InitializeComponent();
            LoadSettings();
            UpdateButtons();
            UpdateLiveLabels();

            liveTimer.Interval = 300;
            liveTimer.Tick += (sender, e) => UpdateLiveLabels();
            liveTimer.Start();
        }

        private void InitializeComponent()
        {
            Name = "FormAutoRollLearnSettings";
            Text = "Auto Roll Learn";
            ClientSize = new Size(820, 520);
            MinimumSize = new Size(820, 520);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.Gainsboro;
            Font = new Font("Tahoma", 11F, FontStyle.Regular);

            Label title = new Label
            {
                Location = new Point(22, 14),
                Size = new Size(760, 64),
                Font = new Font("Tahoma", 13F, FontStyle.Bold),
                Text = "Auto Roll Learn\r\nUses adjacent marked AB passes to measure gap/overlap and suggest IMU roll zero correction."
            };

            btnEnable = MakeButton("Enable", new Point(25, 92), new Size(145, 58), AppleGray);
            btnEnable.Click += (sender, e) =>
            {
                Properties.VehicleSettings.Default.setIMU_autoRollLearnEnabled = !Properties.VehicleSettings.Default.setIMU_autoRollLearnEnabled;
                Properties.VehicleSettings.Default.Save();
                UpdateButtons();
            };

            btnAutoApply = MakeButton("Suggest only", new Point(185, 92), new Size(165, 58), AppleGray);
            btnAutoApply.Click += (sender, e) =>
            {
                Properties.VehicleSettings.Default.setIMU_autoRollLearnAutoApply = !Properties.VehicleSettings.Default.setIMU_autoRollLearnAutoApply;
                Properties.VehicleSettings.Default.Save();
                UpdateButtons();
            };

            btnApply = MakeButton("Apply suggestion", new Point(365, 92), new Size(185, 58), AppleGreen);
            btnApply.Click += (sender, e) =>
            {
                if (mf != null && mf.ApplyAutoRollLearnSuggestion())
                {
                    UpdateLiveLabels();
                }
            };

            Button btnReset = MakeButton("Reset learned", new Point(565, 92), new Size(165, 58), AppleOrange);
            btnReset.Click += (sender, e) =>
            {
                mf?.ResetAutoRollLearn();
                UpdateLiveLabels();
            };

            GroupBox gbLimits = new GroupBox
            {
                Location = new Point(25, 170),
                Size = new Size(760, 145),
                Text = "Limits"
            };

            AddSetting(gbLimits, "Min pass m", nudMinLength = MakeNud(10M, 200M, 0), 22, 30);
            AddSetting(gbLimits, "Max XTE cm", nudMaxXte = MakeNud(1M, 15M, 1), 205, 30);
            AddSetting(gbLimits, "Confidence %", nudConfidence = MakeNud(50M, 95M, 0), 388, 30);
            AddSetting(gbLimits, "Max step deg", nudMaxStep = MakeNud(0.01M, 0.50M, 2), 571, 30);

            Button btnSave = MakeButton("Save", new Point(590, 92), new Size(125, 38), AppleGreen);
            btnSave.Click += (sender, e) => SaveSettings();
            gbLimits.Controls.Add(btnSave);

            GroupBox gbLive = new GroupBox
            {
                Location = new Point(25, 335),
                Size = new Size(760, 150),
                Text = "Live"
            };

            lblLive = MakeLabel(new Point(18, 28), new Size(715, 78), FontStyle.Bold);
            lblLastAction = MakeLabel(new Point(18, 108), new Size(715, 28), FontStyle.Regular);
            gbLive.Controls.Add(lblLive);
            gbLive.Controls.Add(lblLastAction);

            Controls.Add(title);
            Controls.Add(btnEnable);
            Controls.Add(btnAutoApply);
            Controls.Add(btnApply);
            Controls.Add(btnReset);
            Controls.Add(gbLimits);
            Controls.Add(gbLive);

            FormClosing += (sender, e) => liveTimer.Stop();
        }

        private void LoadSettings()
        {
            var settings = Properties.VehicleSettings.Default;
            nudMinLength.Value = ClampDecimal((decimal)settings.setIMU_autoRollLearnMinPassLength, nudMinLength.Minimum, nudMinLength.Maximum);
            nudMaxXte.Value = ClampDecimal((decimal)settings.setIMU_autoRollLearnMaxXteCm, nudMaxXte.Minimum, nudMaxXte.Maximum);
            nudConfidence.Value = ClampDecimal((decimal)settings.setIMU_autoRollLearnMinConfidence, nudConfidence.Minimum, nudConfidence.Maximum);
            nudMaxStep.Value = ClampDecimal((decimal)settings.setIMU_autoRollLearnMaxStepDeg, nudMaxStep.Minimum, nudMaxStep.Maximum);
        }

        private void SaveSettings()
        {
            var settings = Properties.VehicleSettings.Default;
            settings.setIMU_autoRollLearnMinPassLength = (double)nudMinLength.Value;
            settings.setIMU_autoRollLearnMaxXteCm = (double)nudMaxXte.Value;
            settings.setIMU_autoRollLearnMinConfidence = (double)nudConfidence.Value;
            settings.setIMU_autoRollLearnMaxStepDeg = (double)nudMaxStep.Value;
            settings.Save();
            Log.EventWriter("Auto Roll Learn settings saved");
            UpdateLiveLabels();
        }

        private void UpdateButtons()
        {
            var settings = Properties.VehicleSettings.Default;
            btnEnable.Text = settings.setIMU_autoRollLearnEnabled ? "Enabled" : "Disabled";
            ApplyButtonColor(btnEnable, settings.setIMU_autoRollLearnEnabled ? AppleGreen : AppleGray);
            btnAutoApply.Text = settings.setIMU_autoRollLearnAutoApply ? "Auto apply" : "Suggest only";
            ApplyButtonColor(btnAutoApply, settings.setIMU_autoRollLearnAutoApply ? AppleOrange : AppleBlue);
        }

        private void UpdateLiveLabels()
        {
            if (mf == null) return;

            string mode = Properties.VehicleSettings.Default.setIMU_autoRollLearnAutoApply ? "Auto apply" : "Suggest only";
            lblLive.Text = "Mode: " + mode
                + " | Passes: " + mf.AutoRollLearnPassCount.ToString(CultureInfo.CurrentCulture)
                + " | Confidence: " + mf.AutoRollLearnConfidence.ToString("N0", CultureInfo.CurrentCulture) + "%\r\n"
                + "Measured: " + FormatGapOverlap(mf.AutoRollLearnMeasuredErrorCm)
                + " | Suggested roll: " + mf.AutoRollLearnSuggestedCorrectionDeg.ToString("N3", CultureInfo.CurrentCulture) + " deg\r\n"
                + "Status: " + mf.AutoRollLearnStatus;

            lblLastAction.Text = "Last action: " + mf.AutoRollLearnLastAction;
            btnApply.Enabled = mf.AutoRollLearnHasSuggestion;
        }

        private static string FormatGapOverlap(double errorCm)
        {
            if (Math.Abs(errorCm) < 0.05) return "0.0 cm";
            return errorCm >= 0
                ? "Overlap " + Math.Abs(errorCm).ToString("N1", CultureInfo.CurrentCulture) + " cm"
                : "Gap " + Math.Abs(errorCm).ToString("N1", CultureInfo.CurrentCulture) + " cm";
        }

        private static void AddSetting(GroupBox groupBox, string text, NudlessNumericUpDown nud, int x, int y)
        {
            Label label = MakeLabel(new Point(x, y), new Size(165, 24), FontStyle.Regular);
            label.Text = text;
            nud.Location = new Point(x, y + 28);
            nud.Click += NumericInput_Click;
            groupBox.Controls.Add(label);
            groupBox.Controls.Add(nud);
        }

        private static void NumericInput_Click(object sender, EventArgs e)
        {
            NudlessNumericUpDown nud = sender as NudlessNumericUpDown;
            nud?.ShowKeypad(nud.FindForm());
        }

        private static NudlessNumericUpDown MakeNud(decimal min, decimal max, int decimals)
        {
            return new NudlessNumericUpDown
            {
                Minimum = min,
                Maximum = max,
                DecimalPlaces = decimals,
                Increment = decimals == 0 ? 1M : 0.1M,
                Width = 130,
                Height = 36,
                Font = new Font("Tahoma", 18F, FontStyle.Bold),
                TextAlign = HorizontalAlignment.Center
            };
        }

        private static Label MakeLabel(Point location, Size size, FontStyle style)
        {
            return new Label
            {
                Location = location,
                Size = size,
                Font = new Font("Tahoma", 10.5F, style),
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        private static Button MakeButton(string text, Point location, Size size, Color color)
        {
            Button button = new Button
            {
                Text = text,
                Location = location,
                Size = size,
                BackColor = color,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
                UseVisualStyleBackColor = false
            };
            button.FlatAppearance.BorderSize = 1;
            return button;
        }

        private static void ApplyButtonColor(Button button, Color color)
        {
            button.BackColor = color;
            int brightness = (color.R * 299 + color.G * 587 + color.B * 114) / 1000;
            button.ForeColor = brightness < 145 ? Color.White : Color.Black;
        }

        private static decimal ClampDecimal(decimal value, decimal min, decimal max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
