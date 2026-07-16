using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using AgLibrary.Logging;

namespace AgOpenGPS
{
    public class FormHeadingOffsetWizard : Form
    {
        private const int TargetSamples = 1000;
        private const int MinSamples = 200;
        private const double MinTotalDistanceMeters = 20.0;
        private const double MinStepDistanceMeters = 0.25;
        private const double MaxXteMeters = 0.10;
        private const double MaxCorrectionDeg = 45.0;
        private const double MaxGoodStdDevDeg = 1.0;
        private const double MinApplyConfidence = 45.0;
        private const double MaxSteerAngleDeg = 1.0;
        private const double MaxSteerStdDevDeg = 1.5;
        private const double MaxSteerStepDeg = 0.6;
        private const double MaxSteerWindowRangeDeg = 0.8;
        private const double MaxSteerMedianDeviationDeg = 0.5;
        private const int WasStabilityWindowSamples = 6;
        private const int WasOscillationHoldSamples = 4;
        private static readonly Color AppleBlue = Color.FromArgb(0, 122, 255);
        private static readonly Color AppleGreen = Color.FromArgb(52, 199, 89);
        private static readonly Color AppleRed = Color.FromArgb(255, 59, 48);
        private static readonly Color AppleGray = Color.FromArgb(229, 229, 234);
        private static readonly Color AppleGrayPressed = Color.FromArgb(209, 209, 214);

        private readonly FormGPS mf;
        private readonly Timer sampleTimer = new Timer();
        private readonly Timer abPreviewTimer = new Timer();
        private double baseHeadingOffset;

        private bool isSampling;
        private bool hasLastFix;
        private bool hasAbPointA;
        private vec2 abPointA;
        private vec2 lastFix;
        private CTrk temporaryTrack;
        private readonly List<double> headingCorrections = new List<double>();
        private readonly List<HeadingOffsetSample> headingSamples = new List<HeadingOffsetSample>();
        private readonly Queue<double> recentSteerAngles = new Queue<double>();
        private int acceptedSamples;
        private int rejectedSamples;
        private int wasOscillationHoldSamples;
        private double totalDistance;
        private double meanCorrection;
        private double medianCorrection;
        private double stdDeviation;
        private double confidence;
        private double calculatedGpsHeading;
        private double calculatedDualRawHeading;
        private double calculatedSetHeading;
        private double calculatedNewOffset;
        private double steerStdDeviation;
        private double lastSteerAngle;
        private bool hasLastSteerAngle;
        private string statusText = "Press Start and drive straight.";

        private Label lblAbStatus;
        private Label lblStatus;
        private Label lblSamples;
        private Label lblCurrentOffset;
        private Label lblCorrection;
        private Label lblNewOffset;
        private Label lblDistance;
        private Label lblXte;
        private Label lblConfidence;
        private ProgressBar pbarSamples;
        private DataGridView dgvHeadingTable;
        private Button btnPointA;
        private Button btnPointB;
        private Button btnAutoSteer;
        private Button btnStartStop;
        private Button btnReset;
        private Button btnApply;

        public FormHeadingOffsetWizard(Form callingForm)
        {
            mf = callingForm as FormGPS;
            baseHeadingOffset = Properties.VehicleSettings.Default.setGPS_dualHeadingOffset;

            InitializeComponent();
            ResetMeasurements();
            UpdateLabels();

            sampleTimer.Interval = 250;
            sampleTimer.Tick += SampleTimer_Tick;

            abPreviewTimer.Interval = 250;
            abPreviewTimer.Tick += AbPreviewTimer_Tick;
        }

        private void InitializeComponent()
        {
            Text = "Auto Heading Offset";
            Name = "FormHeadingOffsetWizard";
            ClientSize = new Size(760, 590);
            MinimumSize = new Size(760, 590);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.Gainsboro;
            Font = new Font("Tahoma", 11.25F, FontStyle.Regular, GraphicsUnit.Point, 0);

            Label lblTitle = new Label
            {
                AutoSize = false,
                Location = new Point(18, 14),
                Size = new Size(680, 76),
                Font = new Font("Tahoma", 13.5F, FontStyle.Bold),
                Text = "Auto Heading Offset\r\nDrive straight with good GPS. The wizard compares GPS travel direction with dual heading."
            };

            btnPointA = MakeButton("Point A", new Point(25, 105), new Size(125, 62), AppleGreen);
            btnPointA.Click += BtnPointA_Click;

            btnPointB = MakeButton("Point B", new Point(165, 105), new Size(125, 62), AppleGreen);
            btnPointB.Click += BtnPointB_Click;

            btnAutoSteer = MakeButton("Autosteer Off", new Point(305, 105), new Size(165, 62), AppleGray);
            btnAutoSteer.Click += BtnAutoSteer_Click;

            lblAbStatus = MakeLabel(new Point(490, 105), new Size(235, 62), ContentAlignment.MiddleLeft);

            btnStartStop = MakeButton("Start", new Point(25, 190), new Size(160, 72), AppleBlue);
            btnStartStop.Click += BtnStartStop_Click;

            btnReset = MakeButton("Reset", new Point(205, 190), new Size(130, 72), AppleGray);
            btnReset.Click += (sender, e) =>
            {
                ResetMeasurements();
                UpdateLabels();
            };

            btnApply = MakeButton("Apply", new Point(595, 190), new Size(130, 72), AppleGreen);
            btnApply.Click += BtnApply_Click;

            GroupBox gbMeasure = new GroupBox
            {
                Location = new Point(25, 285),
                Size = new Size(700, 245),
                Text = "Measurement"
            };

            lblStatus = MakeLabel(new Point(18, 28), new Size(660, 34), ContentAlignment.MiddleLeft);
            pbarSamples = new ProgressBar
            {
                Location = new Point(18, 68),
                Size = new Size(660, 22),
                Minimum = 0,
                Maximum = TargetSamples
            };
            lblSamples = MakeLabel(new Point(18, 98), new Size(190, 28), ContentAlignment.MiddleLeft);
            lblDistance = MakeLabel(new Point(225, 98), new Size(190, 28), ContentAlignment.MiddleLeft);
            lblXte = MakeLabel(new Point(430, 98), new Size(190, 28), ContentAlignment.MiddleLeft);
            lblCurrentOffset = MakeLabel(new Point(18, 136), new Size(160, 28), ContentAlignment.MiddleLeft);
            lblCorrection = MakeLabel(new Point(185, 136), new Size(160, 28), ContentAlignment.MiddleLeft);
            lblNewOffset = MakeLabel(new Point(350, 136), new Size(150, 28), ContentAlignment.MiddleLeft);
            lblConfidence = MakeLabel(new Point(510, 136), new Size(160, 28), ContentAlignment.MiddleLeft);
            dgvHeadingTable = new DataGridView
            {
                Location = new Point(18, 166),
                Size = new Size(660, 60),
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = 24,
                ReadOnly = true,
                RowHeadersVisible = false,
                ScrollBars = ScrollBars.None
            };
            dgvHeadingTable.Columns.Add("setHeading", "Set heading");
            dgvHeadingTable.Columns.Add("calcHeading", "Calculated heading");
            dgvHeadingTable.Columns.Add("newOffset", "New offset");
            dgvHeadingTable.Columns.Add("confidence", "Confidence");
            dgvHeadingTable.Columns.Add("samples", "Samples");
            dgvHeadingTable.Rows.Add("", "", "", "", "");

            gbMeasure.Controls.Add(lblStatus);
            gbMeasure.Controls.Add(pbarSamples);
            gbMeasure.Controls.Add(lblSamples);
            gbMeasure.Controls.Add(lblDistance);
            gbMeasure.Controls.Add(lblXte);
            gbMeasure.Controls.Add(lblCurrentOffset);
            gbMeasure.Controls.Add(lblCorrection);
            gbMeasure.Controls.Add(lblNewOffset);
            gbMeasure.Controls.Add(lblConfidence);
            gbMeasure.Controls.Add(dgvHeadingTable);

            Controls.Add(lblTitle);
            Controls.Add(btnPointA);
            Controls.Add(btnPointB);
            Controls.Add(btnAutoSteer);
            Controls.Add(lblAbStatus);
            Controls.Add(btnStartStop);
            Controls.Add(btnReset);
            Controls.Add(btnApply);
            Controls.Add(gbMeasure);

            FormClosing += FormHeadingOffsetWizard_FormClosing;
        }

        private static Button MakeButton(string text, Point location, Size size, Color backColor)
        {
            Button button = new Button
            {
                BackColor = backColor,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
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
            int radius = 16;
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
            return Color.FromArgb(
                color.A,
                color.R + (int)((255 - color.R) * amount),
                color.G + (int)((255 - color.G) * amount),
                color.B + (int)((255 - color.B) * amount));
        }

        private static Color Darken(Color color, double amount)
        {
            return Color.FromArgb(
                color.A,
                Math.Max(0, color.R - (int)(color.R * amount)),
                Math.Max(0, color.G - (int)(color.G * amount)),
                Math.Max(0, color.B - (int)(color.B * amount)));
        }

        private static Label MakeLabel(Point location, Size size, ContentAlignment alignment)
        {
            return new Label
            {
                AutoSize = false,
                Location = location,
                Size = size,
                TextAlign = alignment
            };
        }

        private void BtnPointA_Click(object sender, EventArgs e)
        {
            if (mf == null) return;

            RemoveTemporaryTrack();
            hasAbPointA = true;
            abPointA = new vec2(mf.pivotAxlePos.easting, mf.pivotAxlePos.northing);

            mf.ABLine.isMakingABLine = true;
            mf.ABLine.desPtA = new vec2(abPointA);
            mf.ABLine.desPtB = new vec2(abPointA.easting + Math.Sin(mf.pivotAxlePos.heading), abPointA.northing + Math.Cos(mf.pivotAxlePos.heading));
            UpdateDesignedAbLine(mf.pivotAxlePos.heading);

            abPreviewTimer.Start();
            lblAbStatus.Text = "Point A saved. Drive to Point B.";
            mf.Activate();
        }

        private void BtnPointB_Click(object sender, EventArgs e)
        {
            if (mf == null) return;

            if (!hasAbPointA)
            {
                MessageBox.Show(this, "Save Point A first.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            vec2 pointB = new vec2(mf.pivotAxlePos.easting, mf.pivotAxlePos.northing);
            double distance = GetDistanceMeters(abPointA.easting, abPointA.northing, pointB.easting, pointB.northing);
            if (distance < 2.0)
            {
                MessageBox.Show(this, "Point B must be at least 2 m from Point A.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            double heading = Math.Atan2(pointB.easting - abPointA.easting, pointB.northing - abPointA.northing);
            if (heading < 0) heading += glm.twoPI;

            mf.ABLine.desPtB = new vec2(pointB);
            UpdateDesignedAbLine(heading);
            CreateTemporaryTrack(heading);
            mf.Activate();
        }

        private void AbPreviewTimer_Tick(object sender, EventArgs e)
        {
            if (!hasAbPointA || mf == null) return;

            double distance = GetDistanceMeters(abPointA.easting, abPointA.northing, mf.pivotAxlePos.easting, mf.pivotAxlePos.northing);
            if (distance < 0.25) return;

            double heading = Math.Atan2(mf.pivotAxlePos.easting - abPointA.easting, mf.pivotAxlePos.northing - abPointA.northing);
            if (heading < 0) heading += glm.twoPI;

            mf.ABLine.desPtB = new vec2(mf.pivotAxlePos.easting, mf.pivotAxlePos.northing);
            UpdateDesignedAbLine(heading);
            lblAbStatus.Text = "Point A saved. Distance: " + distance.ToString("N1", CultureInfo.CurrentCulture) + " m";
        }

        private void CreateTemporaryTrack(double heading)
        {
            RemoveTemporaryTrack();

            abPreviewTimer.Stop();
            mf.ABLine.isMakingABLine = false;
            mf.ABLine.desHeading = heading;

            temporaryTrack = new CTrk();
            mf.trk.gArr.Add(temporaryTrack);
            int idx = mf.trk.gArr.Count - 1;

            temporaryTrack.ptA = new vec2(mf.ABLine.desPtA);
            temporaryTrack.ptB = new vec2(mf.ABLine.desPtB);
            temporaryTrack.mode = TrackMode.AB;
            temporaryTrack.heading = heading;
            temporaryTrack.name = "Heading Cal " + DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

            mf.trk.idx = idx;
            mf.ABLine.isABValid = false;
            CenterTemporaryTrackOnTractor(temporaryTrack);
            mf.ABLine.BuildCurrentABLineList(mf.pivotAxlePos);

            lblAbStatus.Text = "Temporary AB line selected.";
        }

        private void CenterTemporaryTrackOnTractor(CTrk track)
        {
            double normalEast = Math.Sin(track.heading + glm.PIBy2);
            double normalNorth = Math.Cos(track.heading + glm.PIBy2);
            double widthMinusOverlap = mf.tool.width - mf.tool.overlap;
            double activeLineOffset = (0.5 * widthMinusOverlap) - mf.tool.offset;

            double distanceToCenter =
                ((mf.pivotAxlePos.easting - track.ptA.easting) * normalEast)
                + ((mf.pivotAxlePos.northing - track.ptA.northing) * normalNorth)
                - activeLineOffset;

            track.ptA.easting += normalEast * distanceToCenter;
            track.ptA.northing += normalNorth * distanceToCenter;
            track.ptB.easting += normalEast * distanceToCenter;
            track.ptB.northing += normalNorth * distanceToCenter;
            track.nudgeDistance = 0;

            mf.ABLine.isABValid = false;
        }

        private void BtnAutoSteer_Click(object sender, EventArgs e)
        {
            if (mf == null) return;

            if (temporaryTrack == null && !mf.isBtnAutoSteerOn)
            {
                MessageBox.Show(this, "Create Point A and Point B first.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            int idx = temporaryTrack == null ? -1 : mf.trk.gArr.IndexOf(temporaryTrack);
            if (idx >= 0) mf.trk.idx = idx;

            mf.btnAutoSteer.Enabled = true;
            bool requestedOn = !mf.isBtnAutoSteerOn;
            mf.btnAutoSteer.PerformClick();

            if (requestedOn && !mf.isBtnAutoSteerOn)
            {
                ForceAutoSteerOn();
            }
            else if (!requestedOn && mf.isBtnAutoSteerOn)
            {
                ForceAutoSteerOff();
            }

            UpdateAutoSteerButton();
            mf.Activate();
        }

        private void BtnStartStop_Click(object sender, EventArgs e)
        {
            isSampling = !isSampling;
            if (isSampling)
            {
                statusText = "Measuring. Keep driving straight.";
                sampleTimer.Start();
            }
            else
            {
                statusText = "Paused.";
                sampleTimer.Stop();
            }

            UpdateLabels();
        }

        private void SampleTimer_Tick(object sender, EventArgs e)
        {
            if (TryTakeSample(out string reason))
            {
                statusText = "Good sample.";
            }
            else
            {
                rejectedSamples++;
                statusText = reason;
            }

            UpdateLabels();
        }

        private bool TryTakeSample(out string reason)
        {
            reason = string.Empty;

            if (mf == null)
            {
                reason = "Main form not available.";
                return false;
            }

            if (!mf.isGPSPositionInitialized)
            {
                reason = "Waiting for GPS position.";
                return false;
            }

            double correctedDualHeading = mf.pn.headingTrueDual;
            if (!IsFinite(correctedDualHeading) || Math.Abs(correctedDualHeading) > 1000)
            {
                reason = "Waiting for dual heading.";
                return false;
            }

            double steerAngle = mf.timerSim.Enabled ? mf.sim.steerAngle : mf.mc.actualSteerAngleDegrees;
            if (!TryAcceptStableWasAngle(steerAngle, out reason))
            {
                return false;
            }

            vec2 currentFix = mf.pn.fix;
            if (!hasLastFix)
            {
                lastFix = new vec2(currentFix);
                hasLastFix = true;
                reason = "First GPS point saved.";
                return false;
            }

            double deltaEasting = currentFix.easting - lastFix.easting;
            double deltaNorthing = currentFix.northing - lastFix.northing;
            double stepDistance = Math.Sqrt(deltaEasting * deltaEasting + deltaNorthing * deltaNorthing);

            if (stepDistance < MinStepDistanceMeters)
            {
                reason = "Drive forward a little more.";
                return false;
            }

            double xte = GetXteMeters();
            if (IsFinite(xte) && xte > MaxXteMeters)
            {
                lastFix = new vec2(currentFix);
                reason = "XTE is too large for calibration.";
                return false;
            }

            lastFix = new vec2(currentFix);

            double rawDualHeading = Normalize360(correctedDualHeading - baseHeadingOffset);
            headingSamples.Add(new HeadingOffsetSample(currentFix.easting, currentFix.northing, rawDualHeading, steerAngle));
            if (headingSamples.Count > TargetSamples)
            {
                headingSamples.RemoveAt(0);
            }

            totalDistance += stepDistance;
            acceptedSamples = headingSamples.Count;

            if (!AnalyzeHeadingSamples(out reason))
            {
                return false;
            }

            return true;
        }

        private void BtnApply_Click(object sender, EventArgs e)
        {
            if (mf == null) return;

            if (!HasEnoughMeasurement())
            {
                MessageBox.Show(this, "Drive straight for at least 20 m and collect stable samples before applying.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            double correction = GetRecommendedCorrectionDeg();
            double oldOffset = baseHeadingOffset;
            double loggedDistance = totalDistance;
            int loggedSamples = acceptedSamples;
            double newOffset = Normalize360(baseHeadingOffset + correction);

            Properties.VehicleSettings.Default.setGPS_dualHeadingOffset = newOffset;
            Properties.VehicleSettings.Default.Save();
            mf.pn.headingTrueDualOffset = newOffset;
            RefreshOpenConfigHeadingOffset(newOffset);

            sampleTimer.Stop();
            isSampling = false;
            baseHeadingOffset = newOffset;
            ResetMeasurements();
            statusText = "Dual heading offset updated.";
            UpdateLabels();

            Log.EventWriter("Auto Heading Offset correction " + correction.ToString("N3", CultureInfo.InvariantCulture)
                + ", old offset " + oldOffset.ToString("N3", CultureInfo.InvariantCulture)
                + ", new offset " + newOffset.ToString("N3", CultureInfo.InvariantCulture)
                + ", distance " + loggedDistance.ToString("N2", CultureInfo.InvariantCulture)
                + ", samples " + loggedSamples.ToString(CultureInfo.InvariantCulture));

            MessageBox.Show(this, "Dual heading offset updated.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static void RefreshOpenConfigHeadingOffset(double newOffset)
        {
            if (Application.OpenForms["FormConfig"] is FormConfig configForm)
            {
                configForm.SetDualHeadingOffsetDisplay(newOffset);
            }
        }

        private void ResetMeasurements()
        {
            sampleTimer.Stop();
            isSampling = false;
            hasLastFix = false;
            acceptedSamples = 0;
            rejectedSamples = 0;
            totalDistance = 0;
            headingCorrections.Clear();
            headingSamples.Clear();
            recentSteerAngles.Clear();
            meanCorrection = 0;
            medianCorrection = 0;
            stdDeviation = 0;
            confidence = 0;
            wasOscillationHoldSamples = 0;
            calculatedGpsHeading = 0;
            calculatedDualRawHeading = 0;
            calculatedSetHeading = Normalize360(baseHeadingOffset);
            calculatedNewOffset = Normalize360(baseHeadingOffset);
            steerStdDeviation = 0;
            lastSteerAngle = 0;
            hasLastSteerAngle = false;
            statusText = "Press Start and drive straight.";
        }

        private void UpdateLabels()
        {
            double correction = GetRecommendedCorrectionDeg();
            double newOffset = Normalize360(baseHeadingOffset + correction);
            double xte = GetXteMeters();
            if (acceptedSamples == 0 && mf != null && IsFinite(mf.pn.headingTrueDual))
            {
                calculatedSetHeading = Normalize360(mf.pn.headingTrueDual);
            }

            btnStartStop.Text = isSampling ? "Stop" : "Start";
            ApplyButtonColor(btnStartStop, isSampling ? AppleRed : AppleBlue);
            btnApply.Enabled = HasEnoughMeasurement();
            pbarSamples.Value = Math.Min(TargetSamples, acceptedSamples);

            lblStatus.Text = statusText;
            lblSamples.Text = "Samples: " + acceptedSamples.ToString(CultureInfo.CurrentCulture)
                + " / " + TargetSamples.ToString(CultureInfo.CurrentCulture);
            lblDistance.Text = "Distance: " + totalDistance.ToString("N1", CultureInfo.CurrentCulture) + " m";
            lblXte.Text = IsFinite(xte) ? "XTE: " + (xte * 100.0).ToString("N1", CultureInfo.CurrentCulture) + " cm" : "XTE: no line";
            lblCurrentOffset.Text = "Current: " + baseHeadingOffset.ToString("N2", CultureInfo.CurrentCulture);
            lblCorrection.Text = "Correction: " + correction.ToString("N2", CultureInfo.CurrentCulture);
            lblNewOffset.Text = "New: " + newOffset.ToString("N2", CultureInfo.CurrentCulture);
            lblConfidence.Text = "Conf: " + confidence.ToString("N0", CultureInfo.CurrentCulture) + "%";
            UpdateHeadingTable();
            UpdateAutoSteerButton();
        }

        private void UpdateHeadingTable()
        {
            if (dgvHeadingTable == null || dgvHeadingTable.Rows.Count == 0) return;

            DataGridViewRow row = dgvHeadingTable.Rows[0];
            row.Cells[0].Value = calculatedSetHeading.ToString("N2", CultureInfo.CurrentCulture);
            row.Cells[1].Value = calculatedGpsHeading.ToString("N2", CultureInfo.CurrentCulture);
            row.Cells[2].Value = calculatedNewOffset.ToString("N2", CultureInfo.CurrentCulture);
            row.Cells[3].Value = confidence.ToString("N0", CultureInfo.CurrentCulture) + "%";
            row.Cells[4].Value = acceptedSamples.ToString(CultureInfo.CurrentCulture);
        }

        private void UpdateAutoSteerButton()
        {
            if (btnAutoSteer == null || mf == null) return;

            if (mf.isBtnAutoSteerOn)
            {
                btnAutoSteer.Text = "Autosteer On";
                ApplyButtonColor(btnAutoSteer, AppleBlue);
            }
            else
            {
                btnAutoSteer.Text = "Autosteer Off";
                ApplyButtonColor(btnAutoSteer, AppleGrayPressed);
            }

            if (string.IsNullOrEmpty(lblAbStatus.Text))
            {
                lblAbStatus.Text = "Save A and B to create a temporary AB line.";
            }
        }

        private bool HasEnoughMeasurement()
        {
            return acceptedSamples >= MinSamples
                && totalDistance >= MinTotalDistanceMeters
                && confidence >= MinApplyConfidence
                && Math.Abs(GetRecommendedCorrectionDeg()) <= MaxCorrectionDeg;
        }

        private double GetRecommendedCorrectionDeg()
        {
            return acceptedSamples < MinSamples ? 0 : medianCorrection;
        }

        private bool TryAcceptStableWasAngle(double steerAngle, out string reason)
        {
            reason = string.Empty;

            if (!IsFinite(steerAngle))
            {
                reason = "Waiting for WAS angle.";
                return false;
            }

            if (Math.Abs(steerAngle) > MaxSteerAngleDeg)
            {
                recentSteerAngles.Clear();
                wasOscillationHoldSamples = WasOscillationHoldSamples;
                hasLastSteerAngle = true;
                lastSteerAngle = steerAngle;
                reason = "WAS angle is too large. Drive straighter.";
                return false;
            }

            if (hasLastSteerAngle && Math.Abs(steerAngle - lastSteerAngle) > MaxSteerStepDeg)
            {
                recentSteerAngles.Clear();
                recentSteerAngles.Enqueue(steerAngle);
                wasOscillationHoldSamples = WasOscillationHoldSamples;
                lastSteerAngle = steerAngle;
                reason = "WAS changed too quickly. Waiting to settle.";
                return false;
            }

            hasLastSteerAngle = true;
            lastSteerAngle = steerAngle;

            recentSteerAngles.Enqueue(steerAngle);
            while (recentSteerAngles.Count > WasStabilityWindowSamples)
            {
                recentSteerAngles.Dequeue();
            }

            if (wasOscillationHoldSamples > 0)
            {
                wasOscillationHoldSamples--;
                reason = "Waiting after WAS oscillation.";
                return false;
            }

            if (recentSteerAngles.Count < WasStabilityWindowSamples)
            {
                reason = "Collecting stable WAS window.";
                return false;
            }

            double minSteer = recentSteerAngles.Min();
            double maxSteer = recentSteerAngles.Max();
            if (maxSteer - minSteer > MaxSteerWindowRangeDeg)
            {
                wasOscillationHoldSamples = WasOscillationHoldSamples;
                reason = "WAS range is oscillating.";
                return false;
            }

            List<double> sortedSteer = recentSteerAngles.OrderBy(angle => angle).ToList();
            double medianSteer = sortedSteer.Count % 2 == 0
                ? (sortedSteer[sortedSteer.Count / 2 - 1] + sortedSteer[sortedSteer.Count / 2]) * 0.5
                : sortedSteer[sortedSteer.Count / 2];

            if (Math.Abs(steerAngle - medianSteer) > MaxSteerMedianDeviationDeg)
            {
                reason = "WAS sample is outside stable range.";
                return false;
            }

            return true;
        }

        private bool AnalyzeHeadingSamples(out string reason)
        {
            reason = "Good sample.";

            if (headingSamples.Count < 2)
            {
                confidence = 0;
                return true;
            }

            if (!TryCalculateGpsFitHeading(out calculatedGpsHeading))
            {
                reason = "GPS path is too short.";
                confidence = Math.Min(25.0, headingSamples.Count * 25.0 / MinSamples);
                return true;
            }

            calculatedDualRawHeading = CircularMean(headingSamples.Select(sample => sample.RawDualHeadingDeg));
            calculatedNewOffset = Normalize360(calculatedGpsHeading - calculatedDualRawHeading);
            calculatedSetHeading = Normalize360(calculatedDualRawHeading + baseHeadingOffset);

            double correction = NormalizeDelta180(calculatedNewOffset - baseHeadingOffset);
            headingCorrections.Clear();
            foreach (HeadingOffsetSample sample in headingSamples)
            {
                double sampleSetHeading = Normalize360(sample.RawDualHeadingDeg + baseHeadingOffset);
                double sampleCorrection = NormalizeDelta180(calculatedGpsHeading - sampleSetHeading);
                headingCorrections.Add(sampleCorrection);
            }

            AnalyzeCorrections();
            medianCorrection = correction;

            if (Math.Abs(correction) > MaxCorrectionDeg)
            {
                reason = "Heading difference is too large.";
                return false;
            }

            if (headingSamples.Count >= MinSamples && steerStdDeviation > MaxSteerStdDevDeg)
            {
                reason = "WAS is oscillating too much.";
                return false;
            }

            return true;
        }

        private bool TryCalculateGpsFitHeading(out double headingDeg)
        {
            headingDeg = 0;
            int count = headingSamples.Count;
            if (count < 2) return false;

            HeadingOffsetSample first = headingSamples[0];
            HeadingOffsetSample last = headingSamples[count - 1];
            double displacementEast = last.Easting - first.Easting;
            double displacementNorth = last.Northing - first.Northing;
            double displacement = Math.Sqrt((displacementEast * displacementEast) + (displacementNorth * displacementNorth));
            if (displacement < MinTotalDistanceMeters * 0.25) return false;

            double meanEast = headingSamples.Average(sample => sample.Easting);
            double meanNorth = headingSamples.Average(sample => sample.Northing);
            double sxx = 0;
            double syy = 0;
            double sxy = 0;

            foreach (HeadingOffsetSample sample in headingSamples)
            {
                double east = sample.Easting - meanEast;
                double north = sample.Northing - meanNorth;
                sxx += east * east;
                syy += north * north;
                sxy += east * north;
            }

            if (sxx + syy < 0.0001) return false;

            double thetaFromEast = 0.5 * Math.Atan2(2.0 * sxy, sxx - syy);
            double dirEast = Math.Cos(thetaFromEast);
            double dirNorth = Math.Sin(thetaFromEast);

            if ((dirEast * displacementEast) + (dirNorth * displacementNorth) < 0)
            {
                dirEast = -dirEast;
                dirNorth = -dirNorth;
            }

            headingDeg = Normalize360(Math.Atan2(dirEast, dirNorth) * 180.0 / Math.PI);
            return true;
        }

        private void AnalyzeCorrections()
        {
            if (headingCorrections.Count < MinSamples)
            {
                meanCorrection = 0;
                medianCorrection = 0;
                stdDeviation = 0;
                steerStdDeviation = CalculateSteerStdDeviation();
                confidence = Math.Min(25.0, headingCorrections.Count * 25.0 / MinSamples);
                return;
            }

            meanCorrection = headingCorrections.Average();

            List<double> sorted = headingCorrections.OrderBy(correction => correction).ToList();
            int count = sorted.Count;
            if (count % 2 == 0)
            {
                medianCorrection = (sorted[count / 2 - 1] + sorted[count / 2]) * 0.5;
            }
            else
            {
                medianCorrection = sorted[count / 2];
            }

            double sumSquares = headingCorrections.Sum(correction => (correction - meanCorrection) * (correction - meanCorrection));
            stdDeviation = count > 1 ? Math.Sqrt(sumSquares / (count - 1)) : 0;
            steerStdDeviation = CalculateSteerStdDeviation();
            confidence = CalculateConfidence();
        }

        private double CalculateConfidence()
        {
            if (headingCorrections.Count < MinSamples) return 0;

            double sampleScore = Math.Min(1.0, headingCorrections.Count / (double)TargetSamples);
            double stabilityScore = Math.Max(0, 1.0 - (stdDeviation / MaxGoodStdDevDeg));
            double agreementScore = Math.Max(0, 1.0 - (Math.Abs(meanCorrection - medianCorrection) / MaxGoodStdDevDeg));
            double steerScore = Math.Max(0, 1.0 - (steerStdDeviation / MaxSteerStdDevDeg));

            int closeSamples = headingCorrections.Count(correction => Math.Abs(correction - medianCorrection) <= MaxGoodStdDevDeg);
            double closeScore = closeSamples / (double)headingCorrections.Count;

            double result = ((sampleScore * 0.25) + (stabilityScore * 0.30) + (agreementScore * 0.15) + (closeScore * 0.15) + (steerScore * 0.15)) * 100.0;
            return Math.Max(0, Math.Min(100, result));
        }

        private double CalculateSteerStdDeviation()
        {
            if (headingSamples.Count < 2) return 0;

            double meanSteer = headingSamples.Average(sample => sample.SteerAngleDeg);
            double sumSquares = headingSamples.Sum(sample => (sample.SteerAngleDeg - meanSteer) * (sample.SteerAngleDeg - meanSteer));
            return Math.Sqrt(sumSquares / (headingSamples.Count - 1));
        }

        private double GetXteMeters()
        {
            if (mf == null) return double.NaN;

            if (mf.guidanceLineDistanceOff != 32000 && mf.guidanceLineDistanceOff != 32020 && Math.Abs(mf.guidanceLineDistanceOff) < 29000)
            {
                return Math.Abs(mf.guidanceLineDistanceOff) / 1000.0;
            }

            if (IsFinite(mf.vehicle.modeActualXTE) && Math.Abs(mf.vehicle.modeActualXTE) < 100)
            {
                return Math.Abs(mf.vehicle.modeActualXTE);
            }

            return double.NaN;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static double Normalize360(double degrees)
        {
            degrees %= 360.0;
            if (degrees < 0) degrees += 360.0;
            return degrees;
        }

        private static double NormalizeDelta180(double degrees)
        {
            degrees = Normalize360(degrees);
            if (degrees > 180.0) degrees -= 360.0;
            return degrees;
        }

        private static double CircularMean(IEnumerable<double> anglesDeg)
        {
            double sumSin = 0;
            double sumCos = 0;
            int count = 0;

            foreach (double angleDeg in anglesDeg)
            {
                double angleRad = angleDeg * Math.PI / 180.0;
                sumSin += Math.Sin(angleRad);
                sumCos += Math.Cos(angleRad);
                count++;
            }

            return count == 0 ? 0 : Normalize360(Math.Atan2(sumSin, sumCos) * 180.0 / Math.PI);
        }

        private void UpdateDesignedAbLine(double heading)
        {
            mf.ABLine.desHeading = heading;
            mf.ABLine.desLineEndA.easting = mf.ABLine.desPtA.easting - (Math.Sin(heading) * 1000);
            mf.ABLine.desLineEndA.northing = mf.ABLine.desPtA.northing - (Math.Cos(heading) * 1000);
            mf.ABLine.desLineEndB.easting = mf.ABLine.desPtA.easting + (Math.Sin(heading) * 1000);
            mf.ABLine.desLineEndB.northing = mf.ABLine.desPtA.northing + (Math.Cos(heading) * 1000);
        }

        private static double GetDistanceMeters(double easting1, double northing1, double easting2, double northing2)
        {
            double deltaEast = easting2 - easting1;
            double deltaNorth = northing2 - northing1;
            return Math.Sqrt(deltaEast * deltaEast + deltaNorth * deltaNorth);
        }

        private void ForceAutoSteerOn()
        {
            mf.isBtnAutoSteerOn = true;
            mf.btnAutoSteer.Image = mf.trk.isAutoSnapToPivot ? Properties.Resources.AutoSteerOnSnapToPivot : Properties.Resources.AutoSteerOn;
            Log.EventWriter("Auto Heading Offset Wizard forced autosteer on for temporary AB line");
        }

        private void ForceAutoSteerOff()
        {
            mf.isBtnAutoSteerOn = false;
            mf.btnAutoSteer.Image = mf.trk.isAutoSnapToPivot ? Properties.Resources.AutoSteerOffSnapToPivot : Properties.Resources.AutoSteerOff;
            Log.EventWriter("Auto Heading Offset Wizard forced autosteer off");
        }

        private void RemoveTemporaryTrack()
        {
            if (mf == null || temporaryTrack == null) return;

            int idx = mf.trk.gArr.IndexOf(temporaryTrack);
            if (idx < 0)
            {
                temporaryTrack = null;
                return;
            }

            if (mf.isBtnAutoSteerOn && mf.trk.idx == idx)
            {
                mf.btnAutoSteer.Enabled = true;
                mf.btnAutoSteer.PerformClick();
                if (mf.isBtnAutoSteerOn)
                {
                    ForceAutoSteerOff();
                }
            }

            mf.trk.gArr.RemoveAt(idx);

            if (mf.trk.idx == idx)
            {
                mf.trk.idx = -1;
                mf.ABLine.isABValid = false;
            }
            else if (mf.trk.idx > idx)
            {
                mf.trk.idx--;
            }

            temporaryTrack = null;
        }

        private void FormHeadingOffsetWizard_FormClosing(object sender, FormClosingEventArgs e)
        {
            sampleTimer.Stop();
            abPreviewTimer.Stop();
            if (mf != null)
            {
                mf.ABLine.isMakingABLine = false;
                RemoveTemporaryTrack();
            }
        }

        private struct HeadingOffsetSample
        {
            public HeadingOffsetSample(double easting, double northing, double rawDualHeadingDeg, double steerAngleDeg)
            {
                Easting = easting;
                Northing = northing;
                RawDualHeadingDeg = rawDualHeadingDeg;
                SteerAngleDeg = steerAngleDeg;
            }

            public double Easting { get; }
            public double Northing { get; }
            public double RawDualHeadingDeg { get; }
            public double SteerAngleDeg { get; }
        }
    }
}
