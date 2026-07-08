using AgOpenGPS.Controls;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AgOpenGPS
{
    public sealed class FormObstacleMarker : Form
    {
        private readonly FormGPS mf;
        private readonly NudlessNumericUpDown nudWidth;
        private readonly NudlessNumericUpDown nudLength;
        private readonly NudlessNumericUpDown nudAlarmDistance;
        private readonly TextBox tboxNotes;
        private readonly Label lblStatus;
        private readonly Button btnTree;
        private readonly Button btnHole;
        private readonly Button btnHose;
        private readonly Button btnAlarm;
        private string selectedType = "POLE";
        private bool savedPending;

        public FormObstacleMarker(Form callingForm)
        {
            mf = callingForm as FormGPS;

            Text = "Obstacle";
            Name = "FormObstacleMarker";
            ClientSize = new Size(580, 500);
            MinimumSize = new Size(580, 500);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(245, 245, 247);
            Font = new Font("Segoe UI", 11F, FontStyle.Regular);
            FormClosing += FormObstacleMarker_FormClosing;

            Label title = new Label
            {
                Location = new Point(22, 14),
                Size = new Size(536, 34),
                Font = new Font("Segoe UI", 15F, FontStyle.Bold),
                ForeColor = Color.FromArgb(28, 28, 30),
                Text = "Obstacle"
            };

            btnTree = MakeTileButton("Tree", CreateTreeIcon(), new Point(22, 62));
            btnHole = MakeTileButton("Hole", CreateHoleIcon(), new Point(207, 62));
            btnHose = MakeTileButton("Hose", CreateHoseIcon(), new Point(392, 62));
            btnTree.Click += (sender, e) => SetObstacleType("POLE", true);
            btnHole.Click += (sender, e) => SetObstacleType("HOLE", true);
            btnHose.Click += (sender, e) => SetObstacleType("HOSE", true);

            Label lblWidth = MakeLabel("Width (m):", new Point(22, 202), new Size(145, 34));
            nudWidth = MakeNumeric(new Point(170, 200), 0.30M, 20M);
            nudWidth.ValueChanged += (sender, e) => UpdatePendingObstacleSize();

            Label lblLength = MakeLabel("Length (m):", new Point(320, 202), new Size(145, 34));
            nudLength = MakeNumeric(new Point(450, 200), 0.30M, 20M);
            nudLength.ValueChanged += (sender, e) => UpdatePendingObstacleSize();

            Label lblNotes = MakeLabel("Name:", new Point(22, 254), new Size(145, 34));
            tboxNotes = new TextBox
            {
                Location = new Point(170, 254),
                Size = new Size(280, 32),
                Text = "Tree",
                Font = new Font("Segoe UI", 13F, FontStyle.Regular),
                BorderStyle = BorderStyle.FixedSingle
            };
            tboxNotes.Click += (sender, e) =>
            {
                if (mf?.isKeyboardOn == true)
                {
                    tboxNotes.ShowKeyboard(this);
                }
            };
            tboxNotes.TextChanged += (sender, e) => UpdatePendingObstacleSize();

            btnAlarm = MakeActionButton("Obstacle alarm", new Point(22, 312), Color.White);
            btnAlarm.Size = new Size(178, 48);
            btnAlarm.Click += (sender, e) => ToggleObstacleAlarm();

            Label lblAlarmDistance = MakeLabel("Alarm distance (m):", new Point(224, 319), new Size(170, 34));
            nudAlarmDistance = MakeNumeric(new Point(398, 314), 10M, 30M);
            nudAlarmDistance.Value = (decimal)Math.Max(0.5, Math.Min(30.0, mf?.ObstacleAlarmDistanceMeters ?? 10.0));
            nudAlarmDistance.ValueChanged += (sender, e) => UpdateObstacleAlarmDistance();

            Button btnSave = MakeActionButton("Save", new Point(170, 382), Color.FromArgb(52, 199, 89));
            btnSave.Click += (sender, e) => SaveObstacle();

            Button btnCancel = MakeActionButton("Cancel", new Point(330, 382), Color.White);
            btnCancel.Click += (sender, e) => Close();

            lblStatus = new Label
            {
                Location = new Point(22, 442),
                Size = new Size(536, 34),
                ForeColor = Color.FromArgb(99, 99, 102),
                Text = "Select an obstacle, tap the map, drag it, then Save.",
                TextAlign = ContentAlignment.MiddleCenter
            };

            Controls.Add(title);
            Controls.Add(btnTree);
            Controls.Add(btnHole);
            Controls.Add(btnHose);
            Controls.Add(lblWidth);
            Controls.Add(nudWidth);
            Controls.Add(lblLength);
            Controls.Add(nudLength);
            Controls.Add(lblNotes);
            Controls.Add(tboxNotes);
            Controls.Add(btnAlarm);
            Controls.Add(lblAlarmDistance);
            Controls.Add(nudAlarmDistance);
            Controls.Add(btnSave);
            Controls.Add(btnCancel);
            Controls.Add(lblStatus);

            SetObstacleType("POLE", false);
            UpdateAlarmButtonStyle();
        }

        private void SetObstacleType(string type, bool startPlacement)
        {
            selectedType = type;
            UpdateSelectionStyles();

            if (selectedType == "POLE")
            {
                tboxNotes.Text = "Tree";
                nudWidth.Value = 0.30M;
                nudLength.Value = 0.30M;
                nudWidth.Enabled = false;
                nudLength.Enabled = false;
                lblStatus.Text = "Tree is fixed at 0.30 x 0.30 m. Tap map to place it.";
            }
            else if (selectedType == "HOLE")
            {
                tboxNotes.Text = "Hole";
                nudWidth.Enabled = true;
                nudLength.Enabled = true;
                if (nudWidth.Value < 0.50M) nudWidth.Value = 1.00M;
                if (nudLength.Value < 0.50M) nudLength.Value = 1.00M;
                lblStatus.Text = "Set hole size, tap map, drag it, then Save.";
            }
            else
            {
                tboxNotes.Text = "Hose";
                nudWidth.Enabled = false;
                nudLength.Enabled = false;
                lblStatus.Text = "Hose is 90 degrees to AB line and clipped to field boundary.";
            }

            if (!startPlacement) return;

            if (selectedType == "HOSE")
            {
                mf?.SetPendingHoseAtFieldCenter(GetObstacleNotes());
            }
            else
            {
                StartPlacementOnMap();
            }
        }

        private void StartPlacementOnMap()
        {
            if (mf == null) return;

            mf.StartObstacleTouchMode(
                GetObstacleNotes(),
                selectedType,
                (double)nudWidth.Value,
                (double)nudLength.Value);
            lblStatus.Text = "Tap the map, drag the marker, then Save.";
        }

        private void UpdatePendingObstacleSize()
        {
            if (mf == null) return;

            mf.UpdatePendingObstacleDetails(
                GetObstacleNotes(),
                selectedType,
                (double)nudWidth.Value,
                (double)nudLength.Value);

            if (selectedType == "HOLE")
            {
                lblStatus.Text = "Hole size is shown on map. Drag it, then Save.";
            }
        }

        private void SaveObstacle()
        {
            if (mf == null) return;

            savedPending = mf.SavePendingObstacleFlag();
            if (savedPending)
            {
                Close();
            }
        }

        private void ToggleObstacleAlarm()
        {
            if (mf == null) return;

            mf.SetObstacleAlarmSettings(
                !mf.IsObstacleAlarmEnabled,
                (double)nudAlarmDistance.Value);
            UpdateAlarmButtonStyle();
        }

        private void UpdateObstacleAlarmDistance()
        {
            if (mf == null || !mf.IsObstacleAlarmEnabled) return;

            mf.SetObstacleAlarmSettings(true, (double)nudAlarmDistance.Value);
            UpdateAlarmButtonStyle();
        }

        private void UpdateAlarmButtonStyle()
        {
            if (btnAlarm == null || mf == null) return;

            bool enabled = mf.IsObstacleAlarmEnabled;
            btnAlarm.Text = enabled ? "Alarm ON" : "Obstacle alarm";
            btnAlarm.BackColor = enabled ? Color.FromArgb(255, 204, 0) : Color.White;
            btnAlarm.ForeColor = Color.FromArgb(28, 28, 30);
            btnAlarm.FlatAppearance.BorderColor = enabled ? Color.FromArgb(255, 149, 0) : Color.FromArgb(209, 209, 214);
            btnAlarm.FlatAppearance.BorderSize = enabled ? 2 : 1;
        }

        private string GetObstacleNotes()
        {
            return string.IsNullOrWhiteSpace(tboxNotes.Text) ? "Obstacle" : tboxNotes.Text.Trim();
        }

        private void FormObstacleMarker_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!savedPending)
            {
                mf?.CancelPendingObstacleFlag();
            }
        }

        private void UpdateSelectionStyles()
        {
            StyleTile(btnTree, selectedType == "POLE");
            StyleTile(btnHole, selectedType == "HOLE");
            StyleTile(btnHose, selectedType == "HOSE");
        }

        private static Label MakeLabel(string text, Point location, Size size)
        {
            return new Label
            {
                Text = text,
                Location = location,
                Size = size,
                ForeColor = Color.FromArgb(58, 58, 60),
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        private NudlessNumericUpDown MakeNumeric(Point location, decimal value, decimal maximum)
        {
            NudlessNumericUpDown numeric = new NudlessNumericUpDown
            {
                Location = location,
                Size = new Size(100, 38),
                DecimalPlaces = 2,
                Increment = 0.10M,
                Minimum = 0.05M,
                Maximum = maximum,
                Value = value,
                Font = new Font("Segoe UI", 16F, FontStyle.Bold),
                TextAlign = HorizontalAlignment.Center,
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            numeric.Click += (sender, e) => numeric.ShowKeypad(this);
            return numeric;
        }

        private static Button MakeTileButton(string text, Image image, Point location)
        {
            Button button = new Button
            {
                Text = text,
                Image = image,
                TextImageRelation = TextImageRelation.ImageAboveText,
                ImageAlign = ContentAlignment.MiddleCenter,
                TextAlign = ContentAlignment.BottomCenter,
                Location = location,
                Size = new Size(165, 112),
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                ForeColor = Color.FromArgb(28, 28, 30),
                UseVisualStyleBackColor = false
            };
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = Color.FromArgb(209, 209, 214);
            return button;
        }

        private static Button MakeActionButton(string text, Point location, Color color)
        {
            Button button = new Button
            {
                Text = text,
                Location = location,
                Size = new Size(120, 48),
                BackColor = color,
                ForeColor = color == Color.White ? Color.FromArgb(28, 28, 30) : Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                UseVisualStyleBackColor = false
            };
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = Color.FromArgb(209, 209, 214);
            return button;
        }

        private static void StyleTile(Button button, bool selected)
        {
            button.BackColor = selected ? Color.FromArgb(232, 244, 255) : Color.White;
            button.FlatAppearance.BorderColor = selected ? Color.FromArgb(0, 122, 255) : Color.FromArgb(209, 209, 214);
            button.FlatAppearance.BorderSize = selected ? 2 : 1;
        }

        private static Bitmap CreateTreeIcon()
        {
            Bitmap bitmap = new Bitmap(72, 60);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (Brush trunk = new SolidBrush(Color.FromArgb(142, 86, 46)))
                using (Brush leaves = new SolidBrush(Color.FromArgb(52, 199, 89)))
                using (Brush leavesDark = new SolidBrush(Color.FromArgb(40, 168, 76)))
                {
                    g.FillRectangle(trunk, 32, 32, 8, 20);
                    g.FillEllipse(leavesDark, 14, 16, 28, 28);
                    g.FillEllipse(leaves, 30, 10, 30, 30);
                    g.FillEllipse(leaves, 23, 2, 28, 28);
                }
            }
            return bitmap;
        }

        private static Bitmap CreateHoleIcon()
        {
            Bitmap bitmap = new Bitmap(72, 60);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (Brush shadow = new SolidBrush(Color.FromArgb(65, 65, 70)))
                using (Brush rim = new SolidBrush(Color.FromArgb(176, 120, 65)))
                using (Pen line = new Pen(Color.FromArgb(90, 60, 40), 3))
                {
                    g.FillEllipse(rim, 10, 16, 52, 30);
                    g.FillEllipse(shadow, 17, 21, 38, 20);
                    g.DrawEllipse(line, 10, 16, 52, 30);
                }
            }
            return bitmap;
        }

        private static Bitmap CreateHoseIcon()
        {
            Bitmap bitmap = new Bitmap(72, 60);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (Pen hose = new Pen(Color.FromArgb(0, 122, 255), 8))
                using (Pen highlight = new Pen(Color.FromArgb(120, 200, 255), 3))
                {
                    hose.StartCap = LineCap.Round;
                    hose.EndCap = LineCap.Round;
                    highlight.StartCap = LineCap.Round;
                    highlight.EndCap = LineCap.Round;
                    Point[] points = { new Point(10, 40), new Point(26, 22), new Point(44, 38), new Point(62, 20) };
                    g.DrawCurve(hose, points, 0.55f);
                    g.DrawCurve(highlight, points, 0.55f);
                }
            }
            return bitmap;
        }
    }
}
