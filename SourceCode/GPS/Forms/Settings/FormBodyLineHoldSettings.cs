using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;
using AgLibrary.Logging;
using AgOpenGPS.Controls;

namespace AgOpenGPS
{
    public class FormBodyLineHoldSettings : Form
    {
        private static readonly Color AppleBlue = ModernUi.Accent;
        private static readonly Color AppleGreen = ModernUi.Success;
        private static readonly Color AppleGray = ModernUi.SurfaceAlt;
        private static readonly Color AppleGrayPressed = ModernUi.Disabled;

        private readonly FormGPS mf;
        private readonly Timer liveTimer = new Timer();

        private Button btnEnabled;
        private Button btnInvert;
        private Button btnApply;
        private Button btnDefaults;
        private NudlessNumericUpDown nudGain;
        private NudlessNumericUpDown nudMaxCorrection;
        private NudlessNumericUpDown nudFilter;
        private NudlessNumericUpDown nudMinSpeed;
        private Label lblLiveAb;
        private Label lblLiveDual;
        private Label lblLiveError;
        private Label lblLiveCorrection;
        private Label lblStatus;

        private bool bodyLineHoldEnabled;
        private bool invertDirection;

        public FormBodyLineHoldSettings(Form callingForm)
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
            Text = "Body line hold";
            Name = "FormBodyLineHoldSettings";
            ClientSize = new Size(780, 545);
            MinimumSize = new Size(780, 545);
            StartPosition = FormStartPosition.CenterParent;
            ModernUi.ApplyForm(this);

            Label lblTitle = new Label
            {
                AutoSize = false,
                Location = new Point(20, 14),
                Size = new Size(735, 70),
                Font = ModernUi.TitleFont,
                ForeColor = ModernUi.Text,
                Text = "Body line hold\r\nUses dual antenna heading to keep the tractor body parallel with the active AB line."
            };

            btnEnabled = MakeButton("Body Hold Off", new Point(25, 96), new Size(180, 62), AppleGrayPressed);
            btnEnabled.Click += (sender, e) =>
            {
                bodyLineHoldEnabled = !bodyLineHoldEnabled;
                UpdateButtons();
            };

            btnInvert = MakeButton("Invert Off", new Point(225, 96), new Size(150, 62), AppleGray);
            btnInvert.Click += (sender, e) =>
            {
                invertDirection = !invertDirection;
                UpdateButtons();
            };

            btnDefaults = MakeButton("Defaults", new Point(435, 96), new Size(130, 62), AppleGray);
            btnDefaults.Click += (sender, e) =>
            {
                bodyLineHoldEnabled = false;
                invertDirection = false;
                nudGain.Value = 0.40M;
                nudMaxCorrection.Value = 2.0M;
                nudFilter.Value = 20M;
                nudMinSpeed.Value = 1.0M;
                UpdateButtons();
                lblStatus.Text = "Defaults loaded. Press Apply to save.";
            };

            btnApply = MakeButton("Apply", new Point(590, 96), new Size(150, 62), AppleGreen);
            btnApply.Click += BtnApply_Click;

            GroupBox gbSettings = new GroupBox
            {
                Location = new Point(25, 178),
                Size = new Size(715, 190),
                Text = "Settings"
            };
            ModernUi.StyleGroupBox(gbSettings);

            AddSetting(gbSettings, "Body gain", nudGain = MakeNud(0.0M, 2.0M, 2, 0.40M), 25, 32);
            AddSetting(gbSettings, "Max correction deg", nudMaxCorrection = MakeNud(0.1M, 10.0M, 1, 2.0M), 265, 32);
            AddSetting(gbSettings, "Filter %", nudFilter = MakeNud(1M, 100M, 0, 20M), 505, 32);
            AddSetting(gbSettings, "Min speed kmh", nudMinSpeed = MakeNud(0M, 10M, 1, 1.0M), 25, 112);

            GroupBox gbLive = new GroupBox
            {
                Location = new Point(25, 385),
                Size = new Size(715, 118),
                Text = "Live"
            };
            ModernUi.StyleGroupBox(gbLive);

            lblLiveAb = MakeLabel(new Point(20, 28), new Size(165, 26));
            lblLiveDual = MakeLabel(new Point(195, 28), new Size(170, 26));
            lblLiveError = MakeLabel(new Point(375, 28), new Size(155, 26));
            lblLiveCorrection = MakeLabel(new Point(540, 28), new Size(155, 26));
            lblStatus = MakeLabel(new Point(20, 64), new Size(675, 30));

            gbLive.Controls.Add(lblLiveAb);
            gbLive.Controls.Add(lblLiveDual);
            gbLive.Controls.Add(lblLiveError);
            gbLive.Controls.Add(lblLiveCorrection);
            gbLive.Controls.Add(lblStatus);

            Controls.Add(lblTitle);
            Controls.Add(btnEnabled);
            Controls.Add(btnInvert);
            Controls.Add(btnDefaults);
            Controls.Add(btnApply);
            Controls.Add(gbSettings);
            Controls.Add(gbLive);

            FormClosing += (sender, e) => liveTimer.Stop();
        }

        private void LoadSettingsToControls()
        {
            var settings = Properties.VehicleSettings.Default;
            bodyLineHoldEnabled = settings.setAS_bodyLineHoldEnabled;
            invertDirection = settings.setAS_bodyLineHoldInvertDirection;
            nudGain.Value = ClampDecimal((decimal)settings.setAS_bodyLineHoldGain, nudGain.Minimum, nudGain.Maximum);
            nudMaxCorrection.Value = ClampDecimal((decimal)settings.setAS_bodyLineHoldMaxCorrection, nudMaxCorrection.Minimum, nudMaxCorrection.Maximum);
            nudFilter.Value = ClampDecimal((decimal)settings.setAS_bodyLineHoldFilterPercent, nudFilter.Minimum, nudFilter.Maximum);
            nudMinSpeed.Value = ClampDecimal((decimal)settings.setAS_bodyLineHoldMinSpeed, nudMinSpeed.Minimum, nudMinSpeed.Maximum);
            lblStatus.Text = "Ready.";
        }

        private void BtnApply_Click(object sender, EventArgs e)
        {
            var settings = Properties.VehicleSettings.Default;
            settings.setAS_bodyLineHoldEnabled = bodyLineHoldEnabled;
            settings.setAS_bodyLineHoldInvertDirection = invertDirection;
            settings.setAS_bodyLineHoldGain = (double)nudGain.Value;
            settings.setAS_bodyLineHoldMaxCorrection = (double)nudMaxCorrection.Value;
            settings.setAS_bodyLineHoldFilterPercent = (double)nudFilter.Value;
            settings.setAS_bodyLineHoldMinSpeed = (double)nudMinSpeed.Value;
            settings.Save();

            Log.EventWriter("Body line hold saved: enabled "
                + bodyLineHoldEnabled.ToString(CultureInfo.InvariantCulture)
                + ", invert " + invertDirection.ToString(CultureInfo.InvariantCulture)
                + ", gain " + settings.setAS_bodyLineHoldGain.ToString("N2", CultureInfo.InvariantCulture)
                + ", max correction " + settings.setAS_bodyLineHoldMaxCorrection.ToString("N1", CultureInfo.InvariantCulture)
                + ", filter " + settings.setAS_bodyLineHoldFilterPercent.ToString("N0", CultureInfo.InvariantCulture)
                + ", min speed " + settings.setAS_bodyLineHoldMinSpeed.ToString("N1", CultureInfo.InvariantCulture));

            lblStatus.Text = "Saved.";
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            btnEnabled.Text = bodyLineHoldEnabled ? "Body Hold On" : "Body Hold Off";
            ApplyButtonColor(btnEnabled, bodyLineHoldEnabled ? AppleGreen : AppleGrayPressed);

            btnInvert.Text = invertDirection ? "Invert On" : "Invert Off";
            ApplyButtonColor(btnInvert, invertDirection ? AppleBlue : AppleGray);
        }

        private void UpdateLiveLabels()
        {
            lblLiveAb.Text = "AB: " + (mf?.BodyLineHoldAbHeadingDeg ?? 0).ToString("N1", CultureInfo.CurrentCulture) + " deg";
            lblLiveDual.Text = "Dual: " + (mf?.BodyLineHoldDualHeadingDeg ?? 0).ToString("N1", CultureInfo.CurrentCulture) + " deg";
            lblLiveError.Text = "Error: " + (mf?.BodyLineHoldBodyErrorDeg ?? 0).ToString("N2", CultureInfo.CurrentCulture) + " deg";
            lblLiveCorrection.Text = "Trim: " + (mf?.BodyLineHoldCorrectionDeg ?? 0).ToString("N2", CultureInfo.CurrentCulture) + " deg";

            if (bodyLineHoldEnabled && mf?.IsBodyLineHoldActing == true)
            {
                lblStatus.Text = "Holding body parallel with AB line.";
            }
            else if (lblStatus.Text == "Holding body parallel with AB line.")
            {
                lblStatus.Text = "Ready.";
            }
        }

        private static void AddSetting(GroupBox groupBox, string text, NudlessNumericUpDown nud, int x, int y)
        {
            Label label = MakeLabel(new Point(x, y), new Size(210, 24));
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
