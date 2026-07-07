using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;
using AgLibrary.Logging;
using AgOpenGPS.Controls;

namespace AgOpenGPS
{
    public class FormAdaptiveSteeringSettings : Form
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
        private NudlessNumericUpDown nudCalmBand;
        private NudlessNumericUpDown nudCalmResponse;
        private NudlessNumericUpDown nudTrigger;
        private NudlessNumericUpDown nudBoostGain;
        private NudlessNumericUpDown nudMaxBoost;
        private NudlessNumericUpDown nudSmoothing;
        private NudlessNumericUpDown nudMinSpeed;
        private NudlessNumericUpDown nudBoostDelay;
        private NudlessNumericUpDown nudSamples;
        private Label lblLiveScale;
        private Label lblLiveRate;
        private Label lblLiveCommand;
        private Label lblStatus;

        private bool adaptiveEnabled;

        public FormAdaptiveSteeringSettings(Form callingForm)
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
            Text = "Adaptive steering response";
            Name = "FormAdaptiveSteeringSettings";
            ClientSize = new Size(810, 625);
            MinimumSize = new Size(810, 625);
            StartPosition = FormStartPosition.CenterParent;
            ModernUi.ApplyForm(this);

            Label lblTitle = new Label
            {
                AutoSize = false,
                Location = new Point(20, 14),
                Size = new Size(760, 70),
                Font = ModernUi.TitleFont,
                ForeColor = ModernUi.Text,
                Text = "Adaptive steering response\r\nSoftens steering when XTE is stable and boosts response when the tractor starts moving away from the line."
            };

            btnEnabled = MakeButton("Adaptive Off", new Point(25, 96), new Size(180, 62), AppleGrayPressed);
            btnEnabled.Click += (sender, e) =>
            {
                adaptiveEnabled = !adaptiveEnabled;
                UpdateButtons();
            };

            btnDefaults = MakeButton("Defaults", new Point(455, 96), new Size(130, 62), AppleGray);
            btnDefaults.Click += (sender, e) =>
            {
                adaptiveEnabled = false;
                nudCalmBand.Value = 3.0M;
                nudCalmResponse.Value = 80M;
                nudTrigger.Value = 2.0M;
                nudBoostGain.Value = 4.0M;
                nudMaxBoost.Value = 35M;
                nudSmoothing.Value = 25M;
                nudMinSpeed.Value = 0.5M;
                nudBoostDelay.Value = 0.35M;
                nudSamples.Value = 3M;
                UpdateButtons();
                lblStatus.Text = "Defaults loaded. Press Apply to save.";
            };

            btnApply = MakeButton("Apply", new Point(615, 96), new Size(150, 62), AppleGreen);
            btnApply.Click += BtnApply_Click;

            GroupBox gbSettings = new GroupBox
            {
                Location = new Point(25, 178),
                Size = new Size(750, 305),
                Text = "Settings"
            };
            ModernUi.StyleGroupBox(gbSettings);

            AddSetting(gbSettings, "Calm band cm", nudCalmBand = MakeNud(0.5M, 20M, 1, 3.0M), 25, 32);
            AddSetting(gbSettings, "Calm response %", nudCalmResponse = MakeNud(30M, 120M, 0, 80M), 265, 32);
            AddSetting(gbSettings, "Trigger cm/s", nudTrigger = MakeNud(0.5M, 20M, 1, 2.0M), 505, 32);
            AddSetting(gbSettings, "Boost gain %", nudBoostGain = MakeNud(0.5M, 20M, 1, 4.0M), 25, 118);
            AddSetting(gbSettings, "Max boost %", nudMaxBoost = MakeNud(0M, 100M, 0, 35M), 265, 118);
            AddSetting(gbSettings, "Smoothing %", nudSmoothing = MakeNud(1M, 100M, 0, 25M), 505, 118);
            AddSetting(gbSettings, "Min speed kmh", nudMinSpeed = MakeNud(0M, 10M, 1, 0.5M), 25, 202);
            AddSetting(gbSettings, "Boost delay s", nudBoostDelay = MakeNud(0M, 2M, 2, 0.35M), 265, 202);
            AddSetting(gbSettings, "Samples", nudSamples = MakeNud(1M, 10M, 0, 3M), 505, 202);

            GroupBox gbLive = new GroupBox
            {
                Location = new Point(25, 500),
                Size = new Size(750, 88),
                Text = "Live"
            };
            ModernUi.StyleGroupBox(gbLive);

            lblLiveScale = MakeLabel(new Point(20, 28), new Size(210, 26));
            lblLiveRate = MakeLabel(new Point(250, 28), new Size(210, 26));
            lblLiveCommand = MakeLabel(new Point(480, 28), new Size(230, 26));
            lblStatus = MakeLabel(new Point(20, 56), new Size(690, 24));

            gbLive.Controls.Add(lblLiveScale);
            gbLive.Controls.Add(lblLiveRate);
            gbLive.Controls.Add(lblLiveCommand);
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
            adaptiveEnabled = settings.setAS_adaptiveSteerEnabled;
            nudCalmBand.Value = ClampDecimal((decimal)settings.setAS_adaptiveSteerCalmBandCm, nudCalmBand.Minimum, nudCalmBand.Maximum);
            nudCalmResponse.Value = ClampDecimal((decimal)settings.setAS_adaptiveSteerCalmResponsePercent, nudCalmResponse.Minimum, nudCalmResponse.Maximum);
            nudTrigger.Value = ClampDecimal((decimal)settings.setAS_adaptiveSteerTriggerCmSec, nudTrigger.Minimum, nudTrigger.Maximum);
            nudBoostGain.Value = ClampDecimal((decimal)settings.setAS_adaptiveSteerBoostGain, nudBoostGain.Minimum, nudBoostGain.Maximum);
            nudMaxBoost.Value = ClampDecimal((decimal)settings.setAS_adaptiveSteerMaxBoostPercent, nudMaxBoost.Minimum, nudMaxBoost.Maximum);
            nudSmoothing.Value = ClampDecimal((decimal)settings.setAS_adaptiveSteerSmoothingPercent, nudSmoothing.Minimum, nudSmoothing.Maximum);
            nudMinSpeed.Value = ClampDecimal((decimal)settings.setAS_adaptiveSteerMinSpeed, nudMinSpeed.Minimum, nudMinSpeed.Maximum);
            nudBoostDelay.Value = ClampDecimal((decimal)settings.setAS_adaptiveSteerBoostDelaySec, nudBoostDelay.Minimum, nudBoostDelay.Maximum);
            nudSamples.Value = ClampDecimal(settings.setAS_adaptiveSteerRequiredSamples, nudSamples.Minimum, nudSamples.Maximum);
            lblStatus.Text = "Ready.";
        }

        private void BtnApply_Click(object sender, EventArgs e)
        {
            var settings = Properties.VehicleSettings.Default;
            settings.setAS_adaptiveSteerEnabled = adaptiveEnabled;
            settings.setAS_adaptiveSteerCalmBandCm = (double)nudCalmBand.Value;
            settings.setAS_adaptiveSteerCalmResponsePercent = (double)nudCalmResponse.Value;
            settings.setAS_adaptiveSteerTriggerCmSec = (double)nudTrigger.Value;
            settings.setAS_adaptiveSteerBoostGain = (double)nudBoostGain.Value;
            settings.setAS_adaptiveSteerMaxBoostPercent = (double)nudMaxBoost.Value;
            settings.setAS_adaptiveSteerSmoothingPercent = (double)nudSmoothing.Value;
            settings.setAS_adaptiveSteerMinSpeed = (double)nudMinSpeed.Value;
            settings.setAS_adaptiveSteerBoostDelaySec = (double)nudBoostDelay.Value;
            settings.setAS_adaptiveSteerRequiredSamples = (int)nudSamples.Value;
            settings.Save();

            Log.EventWriter("Adaptive steering response saved: enabled "
                + adaptiveEnabled.ToString(CultureInfo.InvariantCulture)
                + ", calm band " + settings.setAS_adaptiveSteerCalmBandCm.ToString("N1", CultureInfo.InvariantCulture)
                + ", calm response " + settings.setAS_adaptiveSteerCalmResponsePercent.ToString("N1", CultureInfo.InvariantCulture)
                + ", trigger " + settings.setAS_adaptiveSteerTriggerCmSec.ToString("N1", CultureInfo.InvariantCulture)
                + ", boost gain " + settings.setAS_adaptiveSteerBoostGain.ToString("N1", CultureInfo.InvariantCulture)
                + ", max boost " + settings.setAS_adaptiveSteerMaxBoostPercent.ToString("N1", CultureInfo.InvariantCulture)
                + ", smoothing " + settings.setAS_adaptiveSteerSmoothingPercent.ToString("N1", CultureInfo.InvariantCulture)
                + ", min speed " + settings.setAS_adaptiveSteerMinSpeed.ToString("N1", CultureInfo.InvariantCulture)
                + ", boost delay " + settings.setAS_adaptiveSteerBoostDelaySec.ToString("N2", CultureInfo.InvariantCulture)
                + ", samples " + settings.setAS_adaptiveSteerRequiredSamples.ToString(CultureInfo.InvariantCulture));

            lblStatus.Text = "Saved.";
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            btnEnabled.Text = adaptiveEnabled ? "Adaptive On" : "Adaptive Off";
            ApplyButtonColor(btnEnabled, adaptiveEnabled ? AppleGreen : AppleGrayPressed);
        }

        private void UpdateLiveLabels()
        {
            lblLiveScale.Text = "Response: " + (mf?.AdaptiveSteerScalePercent ?? 100).ToString("N0", CultureInfo.CurrentCulture) + "%";
            lblLiveRate.Text = "XTE rate: " + (mf?.AdaptiveSteerRateCmSec ?? 0).ToString("N1", CultureInfo.CurrentCulture) + " cm/s";
            lblLiveCommand.Text = "Samples: " + (mf?.AdaptiveSteerAwaySampleCount ?? 0).ToString(CultureInfo.CurrentCulture);

            double scale = mf?.AdaptiveSteerScalePercent ?? 100;
            if (adaptiveEnabled && mf?.IsAdaptiveSteerBoostBlocked == true)
            {
                lblStatus.Text = "Boost blocked by roll spike.";
            }
            else if (adaptiveEnabled && scale > 105)
            {
                lblStatus.Text = "Boosting response.";
            }
            else if (adaptiveEnabled && scale < 95)
            {
                lblStatus.Text = "Softening response.";
            }
            else if (lblStatus.Text == "Boosting response."
                || lblStatus.Text == "Softening response."
                || lblStatus.Text == "Boost blocked by roll spike.")
            {
                lblStatus.Text = "Ready.";
            }
        }

        private static void AddSetting(GroupBox groupBox, string text, NudlessNumericUpDown nud, int x, int y)
        {
            Label label = MakeLabel(new Point(x, y), new Size(200, 24));
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

        private static Color GetButtonForeColor(Color backColor)
        {
            int brightness = (backColor.R * 299 + backColor.G * 587 + backColor.B * 114) / 1000;
            return brightness < 145 ? Color.White : Color.Black;
        }

        private static Color Lighten(Color color, double amount)
        {
            return Color.FromArgb(
                color.A,
                (int)Math.Min(255, color.R + (255 - color.R) * amount),
                (int)Math.Min(255, color.G + (255 - color.G) * amount),
                (int)Math.Min(255, color.B + (255 - color.B) * amount));
        }

        private static Color Darken(Color color, double amount)
        {
            return Color.FromArgb(
                color.A,
                (int)Math.Max(0, color.R * (1 - amount)),
                (int)Math.Max(0, color.G * (1 - amount)),
                (int)Math.Max(0, color.B * (1 - amount)));
        }

        private static void ApplyRoundedRegion(Button button)
        {
            if (button.Width <= 0 || button.Height <= 0) return;

            int radius = 14;
            Rectangle rect = new Rectangle(0, 0, button.Width, button.Height);
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddArc(rect.Left, rect.Top, radius, radius, 180, 90);
                path.AddArc(rect.Right - radius, rect.Top, radius, radius, 270, 90);
                path.AddArc(rect.Right - radius, rect.Bottom - radius, radius, radius, 0, 90);
                path.AddArc(rect.Left, rect.Bottom - radius, radius, radius, 90, 90);
                path.CloseFigure();
                button.Region = new Region(path);
            }
        }

        private static decimal ClampDecimal(decimal value, decimal min, decimal max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
