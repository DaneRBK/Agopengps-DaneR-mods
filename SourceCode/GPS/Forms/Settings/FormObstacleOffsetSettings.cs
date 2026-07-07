using AgLibrary.Logging;
using AgOpenGPS.Controls;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace AgOpenGPS
{
    public class FormObstacleOffsetSettings : Form
    {
        private readonly FormGPS mf;
        private NudlessNumericUpDown nudOffset;
        private Panel previewPanel;
        private Label lblStatus;

        public FormObstacleOffsetSettings(Form callingForm)
        {
            mf = callingForm as FormGPS;
            InitializeComponent();
            LoadSettings();
        }

        private void InitializeComponent()
        {
            Text = "Obstacle Offset";
            Name = "FormObstacleOffsetSettings";
            ClientSize = new Size(610, 430);
            MinimumSize = new Size(610, 430);
            StartPosition = FormStartPosition.CenterParent;
            ModernUi.ApplyForm(this);

            Label lblTitle = new Label
            {
                Location = new Point(20, 14),
                Size = new Size(560, 46),
                Font = ModernUi.TitleFont,
                ForeColor = ModernUi.Text,
                Text = "Obstacle Offset",
                TextAlign = ContentAlignment.MiddleCenter
            };

            Label lblValue = new Label
            {
                Location = new Point(34, 70),
                Size = new Size(190, 32),
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                ForeColor = ModernUi.Text,
                Text = "Extra clearance",
                TextAlign = ContentAlignment.MiddleCenter
            };

            nudOffset = new NudlessNumericUpDown
            {
                Location = new Point(55, 108),
                Size = new Size(150, 46),
                DecimalPlaces = 1,
                Increment = 0.1M,
                Minimum = 0.0M,
                Maximum = 20.0M,
                ReadOnly = true,
                TextAlign = HorizontalAlignment.Center,
                Font = ModernUi.InputFont,
                BackColor = ModernUi.Surface,
                ForeColor = ModernUi.Text
            };
            nudOffset.Controls[0].Enabled = false;
            nudOffset.Click += (sender, e) => nudOffset.ShowKeypad(this);
            nudOffset.ValueChanged += (sender, e) =>
            {
                UpdateStatus();
                previewPanel.Invalidate();
            };

            Label lblUnits = new Label
            {
                Location = new Point(208, 116),
                Size = new Size(44, 32),
                Font = new Font("Segoe UI Semibold", 14F, FontStyle.Bold),
                ForeColor = ModernUi.MutedText,
                Text = "m",
                TextAlign = ContentAlignment.MiddleLeft
            };

            previewPanel = new Panel
            {
                Location = new Point(275, 70),
                Size = new Size(300, 250),
                BackColor = ModernUi.Surface,
                BorderStyle = BorderStyle.FixedSingle
            };
            previewPanel.Paint += PreviewPanel_Paint;

            lblStatus = new Label
            {
                Location = new Point(28, 172),
                Size = new Size(224, 110),
                Font = ModernUi.BaseFont,
                ForeColor = ModernUi.MutedText,
                TextAlign = ContentAlignment.TopCenter
            };

            Button btnDefaults = MakeButton("Default", new Point(35, 330), new Size(120, 58), ModernUi.SurfaceAlt);
            btnDefaults.Click += (sender, e) => nudOffset.Value = 1.0M;

            Button btnCancel = MakeButton("Cancel", new Point(315, 330), new Size(120, 58), ModernUi.SurfaceAlt);
            btnCancel.Click += (sender, e) => Close();

            Button btnSave = MakeButton("Save", new Point(455, 330), new Size(120, 58), ModernUi.Success);
            btnSave.Click += BtnSave_Click;

            Controls.Add(lblTitle);
            Controls.Add(lblValue);
            Controls.Add(nudOffset);
            Controls.Add(lblUnits);
            Controls.Add(previewPanel);
            Controls.Add(lblStatus);
            Controls.Add(btnDefaults);
            Controls.Add(btnCancel);
            Controls.Add(btnSave);
        }

        private Button MakeButton(string text, Point location, Size size, Color color)
        {
            Button button = new Button
            {
                Text = text,
                Location = location,
                Size = size,
                BackColor = color
            };
            ModernUi.StyleButton(button, color);
            return button;
        }

        private void LoadSettings()
        {
            decimal value = (decimal)Properties.ToolSettings.Default.setVehicle_obstacleOffset;
            if (value < nudOffset.Minimum) value = nudOffset.Minimum;
            if (value > nudOffset.Maximum) value = nudOffset.Maximum;
            nudOffset.Value = value;
            UpdateStatus();
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            Properties.ToolSettings.Default.setVehicle_obstacleOffset = (double)nudOffset.Value;
            Properties.ToolSettings.Default.Save();

            Log.EventWriter("Obstacle offset saved: "
                + Properties.ToolSettings.Default.setVehicle_obstacleOffset.ToString("N1", CultureInfo.InvariantCulture)
                + " m");

            Close();
        }

        private void UpdateStatus()
        {
            double toolHalfWidth = mf?.tool?.width * 0.5 ?? 0.0;
            double totalClearance = toolHalfWidth + 1.0 + (double)nudOffset.Value;
            lblStatus.Text = "The machine avoids the obstacle by this extra distance.\r\n\r\n"
                + "Approx. centerline clearance:\r\n"
                + totalClearance.ToString("N1", CultureInfo.CurrentCulture) + " m";
        }

        private void PreviewPanel_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(ModernUi.Surface);

            Rectangle bounds = previewPanel.ClientRectangle;
            int centerY = bounds.Height / 2;
            int obstacleX = 150;
            int toolHalf = 28;
            int offsetPixels = 26 + (int)Math.Round((double)nudOffset.Value * 12.0);
            int pathY = centerY - toolHalf - offsetPixels;

            using (Pen abPen = new Pen(ModernUi.Border, 3))
            using (Pen pathPen = new Pen(ModernUi.Accent, 5))
            using (Pen offsetPen = new Pen(ModernUi.Warning, 3))
            using (Brush obstacleBrush = new SolidBrush(ModernUi.Danger))
            using (Brush toolBrush = new SolidBrush(ModernUi.Accent))
            using (Brush textBrush = new SolidBrush(ModernUi.Text))
            using (Font smallFont = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold))
            {
                abPen.DashPattern = new float[] { 8, 6 };
                g.DrawLine(abPen, 20, centerY, bounds.Width - 20, centerY);

                Rectangle obstacle = new Rectangle(obstacleX - 15, centerY - 15, 30, 30);
                g.FillEllipse(obstacleBrush, obstacle);
                g.DrawEllipse(Pens.Black, obstacle);

                Point[] path = {
                    new Point(20, centerY),
                    new Point(82, centerY),
                    new Point(116, pathY),
                    new Point(184, pathY),
                    new Point(218, centerY),
                    new Point(bounds.Width - 20, centerY)
                };
                g.DrawCurve(pathPen, path, 0.35F);

                Rectangle tool = new Rectangle(178, pathY - 13, 48, 26);
                g.FillRectangle(toolBrush, tool);
                g.DrawRectangle(Pens.Black, tool);

                g.DrawLine(offsetPen, obstacleX, centerY - 16, obstacleX, pathY + 13);
                g.DrawString("Offset", smallFont, textBrush, obstacleX + 8, ((centerY + pathY) / 2) - 10);
                g.DrawString("Obstacle", smallFont, textBrush, obstacleX - 38, centerY + 22);
                g.DrawString("Machine path", smallFont, textBrush, 96, pathY - 30);
                g.DrawString("AB line", smallFont, textBrush, 22, centerY + 10);
            }
        }
    }
}
