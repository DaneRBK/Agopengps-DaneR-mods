using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using AgLibrary.Logging;
using AgOpenGPS.Controls;

namespace AgOpenGPS
{
    public class FormAutoRollOffsetWizard : Form
    {
        private const double AutoAbDistanceMeters = 10.0;
        private const double XteStartLimitMeters = 0.02;
        private const double ReverseStartToleranceMeters = 0.50;
        private const int MinimumSamplesPerPass = 8;

        private readonly FormGPS mf;
        private readonly Timer abTimer = new Timer();
        private readonly Timer measureTimer = new Timer();
        private readonly double baseRollZero;

        private bool isRecordingAbLine;
        private bool didWizardTurnPaintingOn;
        private bool areSectionPositionsClamped;
        private bool didCaptureAppliedAreaSnapshot;
        private vec2 abStart = new vec2();
        private CTrk temporaryTrack;

        private int[] appliedPatchCounts;
        private int appliedPatchSaveCount;
        private int appliedPatchCounter;
        private double appliedWorkedAreaTotal;
        private double appliedWorkedAreaTotalUser;

        private double[] originalSectionLeft;
        private double[] originalSectionRight;
        private double[] originalSectionWidth;
        private int[] originalRpSectionPosition;
        private int[] originalRpSectionWidth;

        private double forwardPassOffsetSum;
        private double reversePassOffsetSum;
        private double lastForwardPassOffset;
        private double lastReversePassOffset;
        private double firstPassStartAlong;
        private double firstPassEndAlong;
        private double lastForwardAlong;
        private int forwardPassSamples;
        private int reversePassSamples;
        private bool isMarkingActive;
        private bool firstPassStarted;
        private bool firstPassFinished;
        private bool reversePassStarted;
        private bool reversePassFinished;
        private string markingStatus = "Waiting for first pass.";

        private Label lblAbStatus;
        private Label lblMeasureStatus;
        private Label lblAntennaHeight;
        private Label lblPassError;
        private Label lblCurrentRoll;
        private Label lblCorrection;
        private Label lblNewRoll;
        private Button btnCreateAb;
        private Button btnAutoSteer;
        private Button btnResetMeasure;
        private Button btnApply;
        private PictureBox picPasses;
        private NudlessNumericUpDown nudAntennaHeight;

        public FormAutoRollOffsetWizard(Form callingForm)
        {
            mf = callingForm as FormGPS;
            baseRollZero = Properties.VehicleSettings.Default.setIMU_rollZero;

            InitializeComponent();
            ResetMeasurements();
            UpdateLabels();
            UpdateAutoSteerButton();

            abTimer.Interval = 250;
            abTimer.Tick += AbTimer_Tick;

            measureTimer.Interval = 250;
            measureTimer.Tick += MeasureTimer_Tick;
        }

        private void InitializeComponent()
        {
            Text = "Auto Roll Offset";
            Name = "FormAutoRollOffsetWizard";
            ClientSize = new Size(840, 670);
            MinimumSize = new Size(840, 670);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.Gainsboro;
            Font = new Font("Tahoma", 11.25F, FontStyle.Regular, GraphicsUnit.Point, 0);

            Label lblStep = new Label
            {
                AutoSize = false,
                Location = new Point(18, 12),
                Size = new Size(795, 72),
                Font = new Font("Tahoma", 13.5F, FontStyle.Bold),
                Text = "Auto Roll Offset\r\nPaint only the right half in both directions. The wizard measures overlap or gap automatically."
            };

            btnCreateAb = MakeButton("Create 10 m AB", new Point(22, 95), new Size(205, 72), Color.PaleGreen);
            btnCreateAb.Click += BtnCreateAb_Click;

            lblAbStatus = new Label
            {
                AutoSize = false,
                Location = new Point(245, 100),
                Size = new Size(565, 62),
                TextAlign = ContentAlignment.MiddleLeft,
                Text = "Press Create, drive straight 10 m, and a temporary AB line will be created."
            };

            btnAutoSteer = MakeButton("Autosteer On", new Point(22, 180), new Size(205, 72), Color.LightBlue);
            btnAutoSteer.Click += BtnAutoSteer_Click;

            Label lblDrive = new Label
            {
                AutoSize = false,
                Location = new Point(245, 178),
                Size = new Size(565, 78),
                TextAlign = ContentAlignment.MiddleLeft,
                Text = "Turn autosteer on and drive the AB line both ways. Painting starts only when XTE is 2 cm or less."
            };

            GroupBox gbMeasure = new GroupBox
            {
                Location = new Point(22, 275),
                Size = new Size(790, 235),
                Text = "Automatic marked pass measurement"
            };

            picPasses = new PictureBox
            {
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Location = new Point(22, 30),
                Size = new Size(300, 145)
            };
            picPasses.Paint += (sender, e) => DrawPassGuide(e.Graphics, picPasses.ClientRectangle, GetMeasuredPassErrorMeters());

            lblMeasureStatus = new Label
            {
                AutoSize = false,
                Location = new Point(345, 30),
                Size = new Size(265, 145),
                TextAlign = ContentAlignment.MiddleLeft
            };

            lblPassError = new Label
            {
                AutoSize = false,
                Location = new Point(630, 30),
                Size = new Size(140, 145),
                TextAlign = ContentAlignment.MiddleLeft
            };

            btnResetMeasure = MakeButton("Reset Measure", new Point(345, 178), new Size(170, 42), Color.WhiteSmoke);
            btnResetMeasure.Click += (sender, e) =>
            {
                ResetMeasurements();
                UpdateLabels();
            };

            gbMeasure.Controls.Add(picPasses);
            gbMeasure.Controls.Add(lblMeasureStatus);
            gbMeasure.Controls.Add(lblPassError);
            gbMeasure.Controls.Add(btnResetMeasure);

            Label lblHeightCaption = new Label
            {
                Location = new Point(25, 532),
                Size = new Size(155, 32),
                Text = "Antenna height (m):",
                TextAlign = ContentAlignment.MiddleRight
            };

            nudAntennaHeight = new NudlessNumericUpDown
            {
                Location = new Point(190, 530),
                Size = new Size(105, 32),
                DecimalPlaces = 2,
                Increment = 0.05M,
                Minimum = 0.10M,
                Maximum = 10,
                Value = ClampDecimal((decimal)Properties.VehicleSettings.Default.setVehicle_antennaHeight, 0.10M, 10M),
                TextAlign = HorizontalAlignment.Center
            };
            nudAntennaHeight.ValueChanged += MeasurementChanged;
            nudAntennaHeight.Click += NumericInput_Click;

            lblAntennaHeight = new Label
            {
                Location = new Point(310, 530),
                Size = new Size(190, 32),
                TextAlign = ContentAlignment.MiddleLeft
            };

            lblCurrentRoll = new Label
            {
                Location = new Point(25, 582),
                Size = new Size(210, 28),
                TextAlign = ContentAlignment.MiddleLeft
            };

            lblCorrection = new Label
            {
                Location = new Point(245, 582),
                Size = new Size(205, 28),
                TextAlign = ContentAlignment.MiddleLeft
            };

            lblNewRoll = new Label
            {
                Location = new Point(455, 582),
                Size = new Size(150, 28),
                TextAlign = ContentAlignment.MiddleLeft
            };

            btnApply = MakeButton("Apply", new Point(710, 532), new Size(100, 70), Color.PaleGreen);
            btnApply.Click += BtnApply_Click;

            Controls.Add(lblStep);
            Controls.Add(btnCreateAb);
            Controls.Add(lblAbStatus);
            Controls.Add(btnAutoSteer);
            Controls.Add(lblDrive);
            Controls.Add(gbMeasure);
            Controls.Add(lblHeightCaption);
            Controls.Add(nudAntennaHeight);
            Controls.Add(lblAntennaHeight);
            Controls.Add(lblCurrentRoll);
            Controls.Add(lblCorrection);
            Controls.Add(lblNewRoll);
            Controls.Add(btnApply);

            FormClosing += FormAutoRollOffsetWizard_FormClosing;
        }

        private static Button MakeButton(string text, Point location, Size size, Color backColor)
        {
            return new Button
            {
                BackColor = backColor,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Tahoma", 12F, FontStyle.Bold),
                Location = location,
                Size = size,
                Text = text,
                UseVisualStyleBackColor = false
            };
        }

        private void NumericInput_Click(object sender, EventArgs e)
        {
            if (((NudlessNumericUpDown)sender).ShowKeypad(this))
            {
                UpdateLabels();
            }
        }

        private static decimal ClampDecimal(decimal value, decimal min, decimal max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static void DrawPassGuide(Graphics g, Rectangle bounds, double passErrorMeters)
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.White);

            int center = bounds.Left + bounds.Width / 2;
            int halfWidth = 72;
            int shiftPixels = (int)Math.Max(-42, Math.Min(42, passErrorMeters * 900.0));
            Rectangle firstPass = new Rectangle(center, bounds.Top + 18, halfWidth, bounds.Height - 36);
            Rectangle secondPass = new Rectangle(center - halfWidth + shiftPixels, bounds.Top + 18, halfWidth, bounds.Height - 36);

            using (Brush firstBrush = new SolidBrush(Color.FromArgb(120, Color.LightGreen)))
            using (Brush secondBrush = new SolidBrush(Color.FromArgb(120, Color.SkyBlue)))
            using (Pen edgePen = new Pen(Color.FromArgb(90, 90, 90), 2))
            using (Pen centerPen = new Pen(Color.Magenta, 3))
            using (Pen arrowPen = new Pen(Color.Orange, 4))
            using (Brush textBrush = new SolidBrush(Color.Black))
            using (Font font = new Font("Tahoma", 9F, FontStyle.Bold))
            {
                g.FillRectangle(firstBrush, firstPass);
                g.FillRectangle(secondBrush, secondPass);
                g.DrawRectangle(edgePen, firstPass);
                g.DrawRectangle(edgePen, secondPass);
                g.DrawLine(centerPen, center, bounds.Top, center, bounds.Bottom);

                int y = bounds.Top + bounds.Height / 2;
                DrawDoubleArrow(g, arrowPen, center, center + shiftPixels, y);

                string label = passErrorMeters >= 0.001 ? "Overlap" : "Gap";
                SizeF size = g.MeasureString(label, font);
                g.DrawString(label, font, textBrush, bounds.Left + (bounds.Width - size.Width) / 2, y + 12);
            }
        }

        private static void DrawDoubleArrow(Graphics g, Pen pen, int x1, int x2, int y)
        {
            if (Math.Abs(x2 - x1) < 6) x2 = x1 + 6;
            g.DrawLine(pen, x1, y, x2, y);
            int dir = x1 < x2 ? 1 : -1;
            DrawArrowHead(g, pen, x1, y, -dir);
            DrawArrowHead(g, pen, x2, y, dir);
        }

        private static void DrawArrowHead(Graphics g, Pen pen, int x, int y, int direction)
        {
            g.DrawLine(pen, x, y, x - (direction * 12), y - 8);
            g.DrawLine(pen, x, y, x - (direction * 12), y + 8);
        }

        private void BtnCreateAb_Click(object sender, EventArgs e)
        {
            if (mf == null) return;

            RemoveTemporaryTrack();
            ResetMeasurements();

            abStart = new vec2(mf.pivotAxlePos.easting, mf.pivotAxlePos.northing);
            isRecordingAbLine = true;
            btnCreateAb.Enabled = false;

            mf.ABLine.isMakingABLine = true;
            mf.ABLine.desPtA = new vec2(abStart);
            mf.ABLine.desPtB = new vec2(abStart.easting + Math.Sin(mf.pivotAxlePos.heading), abStart.northing + Math.Cos(mf.pivotAxlePos.heading));
            UpdateDesignedAbLine(mf.pivotAxlePos.heading);

            lblAbStatus.Text = "Recording A point. Drive straight until 10 m is reached.";
            abTimer.Start();
            mf.Activate();
        }

        private void AbTimer_Tick(object sender, EventArgs e)
        {
            if (!isRecordingAbLine || mf == null) return;

            double distance = GetDistanceMeters(abStart.easting, abStart.northing, mf.pivotAxlePos.easting, mf.pivotAxlePos.northing);
            double heading = Math.Atan2(mf.pivotAxlePos.easting - abStart.easting, mf.pivotAxlePos.northing - abStart.northing);
            if (heading < 0) heading += glm.twoPI;

            mf.ABLine.desPtB = new vec2(mf.pivotAxlePos.easting, mf.pivotAxlePos.northing);
            UpdateDesignedAbLine(heading);
            lblAbStatus.Text = "Temporary AB recording: " + distance.ToString("N1", CultureInfo.CurrentCulture) + " / 10.0 m";

            if (distance >= AutoAbDistanceMeters)
            {
                CreateCalibrationTrack(heading);
            }
        }

        private void CreateCalibrationTrack(double heading)
        {
            abTimer.Stop();
            isRecordingAbLine = false;
            btnCreateAb.Enabled = true;

            mf.ABLine.isMakingABLine = false;
            mf.ABLine.desHeading = heading;

            temporaryTrack = new CTrk();
            mf.trk.gArr.Add(temporaryTrack);
            int idx = mf.trk.gArr.Count - 1;
            CTrk track = temporaryTrack;
            track.ptA = new vec2(mf.ABLine.desPtA);
            track.ptB = new vec2(mf.ABLine.desPtB);
            track.mode = TrackMode.AB;
            track.heading = heading;
            track.name = "Auto Roll Cal " + DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

            mf.trk.idx = idx;
            mf.ABLine.isABValid = false;
            CenterTemporaryTrackOnTractor(track);

            lblAbStatus.Text = "Temporary AB line created and selected: " + track.name;
            mf.Activate();
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
            mf.ABLine.BuildCurrentABLineList(mf.pivotAxlePos);
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

        private void BtnAutoSteer_Click(object sender, EventArgs e)
        {
            if (mf == null) return;

            if (mf.trk.idx < 0 && !mf.isBtnAutoSteerOn)
            {
                MessageBox.Show(this, "Create or select an AB line before enabling autosteer.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

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

            if (mf.isBtnAutoSteerOn)
            {
                EnableRightHalfCoveragePainting();
                measureTimer.Start();
            }
            else
            {
                measureTimer.Stop();
                DisableCoveragePaintingIfWizardStartedIt();
            }

            UpdateAutoSteerButton();
            mf.Activate();
        }

        private void ForceAutoSteerOn()
        {
            mf.isBtnAutoSteerOn = true;
            mf.btnAutoSteer.Image = mf.trk.isAutoSnapToPivot ? Properties.Resources.AutoSteerOnSnapToPivot : Properties.Resources.AutoSteerOn;
            Log.EventWriter("Auto Roll Offset Wizard forced autosteer on for temporary AB line");
        }

        private void ForceAutoSteerOff()
        {
            mf.isBtnAutoSteerOn = false;
            mf.btnAutoSteer.Image = mf.trk.isAutoSnapToPivot ? Properties.Resources.AutoSteerOffSnapToPivot : Properties.Resources.AutoSteerOff;
            Log.EventWriter("Auto Roll Offset Wizard forced autosteer off");
            measureTimer.Stop();
            DisableCoveragePaintingIfWizardStartedIt();
        }

        private void EnableRightHalfCoveragePainting()
        {
            if (mf.tool.numOfSections <= 0) return;

            CaptureAppliedAreaSnapshot();

            mf.autoBtnState = btnStates.Off;
            mf.btnSectionMasterAuto.Image = Properties.Resources.SectionMasterOff;

            mf.manualBtnState = btnStates.On;
            mf.btnSectionMasterManual.Image = Properties.Resources.ManualOn;

            ClampSectionsToRightHalf();
            SetWizardPaintingActive(false);
            didWizardTurnPaintingOn = true;
        }

        private void DisableCoveragePaintingIfWizardStartedIt()
        {
            if (!didWizardTurnPaintingOn) return;

            mf.manualBtnState = btnStates.Off;
            mf.btnSectionMasterManual.Image = Properties.Resources.ManualOff;
            SetWizardPaintingActive(false);
            RestoreSectionPositions();
            didWizardTurnPaintingOn = false;
        }

        private void CaptureAppliedAreaSnapshot()
        {
            if (didCaptureAppliedAreaSnapshot) return;

            appliedPatchCounts = new int[mf.triStrip.Count];
            for (int j = 0; j < mf.triStrip.Count; j++)
            {
                appliedPatchCounts[j] = mf.triStrip[j].patchList.Count;
            }

            appliedPatchSaveCount = mf.patchSaveList.Count;
            appliedPatchCounter = mf.patchCounter;
            appliedWorkedAreaTotal = mf.fd.workedAreaTotal;
            appliedWorkedAreaTotalUser = mf.fd.workedAreaTotalUser;
            didCaptureAppliedAreaSnapshot = true;
        }

        private void RestoreAppliedAreaSnapshot()
        {
            if (!didCaptureAppliedAreaSnapshot) return;

            for (int j = 0; j < mf.triStrip.Count; j++)
            {
                if (mf.triStrip[j].isDrawing)
                    mf.triStrip[j].TurnMappingOff();
            }

            for (int j = 0; j < mf.triStrip.Count; j++)
            {
                int keepCount = j < appliedPatchCounts.Length ? appliedPatchCounts[j] : 0;
                while (mf.triStrip[j].patchList.Count > keepCount)
                {
                    mf.triStrip[j].patchList.RemoveAt(mf.triStrip[j].patchList.Count - 1);
                }
            }

            while (mf.patchSaveList.Count > appliedPatchSaveCount)
            {
                mf.patchSaveList.RemoveAt(mf.patchSaveList.Count - 1);
            }

            mf.patchCounter = appliedPatchCounter;
            mf.fd.workedAreaTotal = appliedWorkedAreaTotal;
            mf.fd.workedAreaTotalUser = appliedWorkedAreaTotalUser;
            mf.oglMain.Refresh();
        }

        private void SetWizardPaintingActive(bool active)
        {
            SetRightHalfSectionStates(active);

            if (active && !isMarkingActive)
            {
                ForceRightHalfMappingOn();
            }
            else if (!active && isMarkingActive)
            {
                ForceSectionsMappingOff();
            }

            isMarkingActive = active;
        }

        private void ClampSectionsToRightHalf()
        {
            if (areSectionPositionsClamped) return;

            int count = mf.tool.numOfSections;
            originalSectionLeft = new double[count];
            originalSectionRight = new double[count];
            originalSectionWidth = new double[count];
            originalRpSectionPosition = new int[count];
            originalRpSectionWidth = new int[count];

            for (int j = 0; j < count; j++)
            {
                originalSectionLeft[j] = mf.section[j].positionLeft;
                originalSectionRight[j] = mf.section[j].positionRight;
                originalSectionWidth[j] = mf.section[j].sectionWidth;
                originalRpSectionPosition[j] = mf.section[j].rpSectionPosition;
                originalRpSectionWidth[j] = mf.section[j].rpSectionWidth;

                if (mf.section[j].positionLeft < 0 && mf.section[j].positionRight > 0)
                {
                    mf.section[j].positionLeft = 0;
                    mf.section[j].sectionWidth = mf.section[j].positionRight - mf.section[j].positionLeft;
                    mf.section[j].rpSectionPosition = 250 + (int)Math.Round(mf.section[j].positionLeft * 10, 0, MidpointRounding.AwayFromZero);
                    mf.section[j].rpSectionWidth = Math.Max(1, (int)Math.Round(mf.section[j].sectionWidth * 10, 0, MidpointRounding.AwayFromZero));
                }
            }

            areSectionPositionsClamped = true;
            CalculateGpsOnlySectionLookAhead();
        }

        private void RestoreSectionPositions()
        {
            if (!areSectionPositionsClamped || originalSectionLeft == null) return;

            for (int j = 0; j < mf.tool.numOfSections && j < originalSectionLeft.Length; j++)
            {
                mf.section[j].positionLeft = originalSectionLeft[j];
                mf.section[j].positionRight = originalSectionRight[j];
                mf.section[j].sectionWidth = originalSectionWidth[j];
                mf.section[j].rpSectionPosition = originalRpSectionPosition[j];
                mf.section[j].rpSectionWidth = originalRpSectionWidth[j];
            }

            areSectionPositionsClamped = false;
            CalculateGpsOnlySectionLookAhead();
        }

        private void SetRightHalfSectionStates(bool isOn)
        {
            GetRightHalfSectionRange(out int startSection, out int endSection);

            for (int j = 0; j < mf.tool.numOfSections; j++)
            {
                bool enabled = isOn && j >= startSection && j <= endSection && mf.section[j].positionRight > 0;
                mf.section[j].sectionBtnState = enabled ? btnStates.On : btnStates.Off;
                mf.section[j].sectionOnRequest = enabled;
                mf.section[j].sectionOffRequest = !enabled;
                mf.section[j].isSectionOn = enabled;
                mf.section[j].isMappingOn = enabled;
                mf.section[j].mappingOnTimer = 0;
                mf.section[j].mappingOffTimer = 0;
            }
        }

        private void ForceRightHalfMappingOn()
        {
            GetRightHalfSectionRange(out int startSection, out int endSection);
            CalculateGpsOnlySectionLookAhead();

            if (mf.triStrip.Count == 0)
                mf.triStrip.Add(new CPatches(mf));

            mf.triStrip[0].currentStartSectionNum = startSection;
            mf.triStrip[0].currentEndSectionNum = endSection;
            mf.triStrip[0].newStartSectionNum = startSection;
            mf.triStrip[0].newEndSectionNum = endSection;

            if (!mf.triStrip[0].isDrawing)
                mf.triStrip[0].TurnMappingOn(startSection);

            mf.oglMain.Refresh();
        }

        private void ForceSectionsMappingOff()
        {
            for (int j = 0; j < mf.tool.numOfSections; j++)
            {
                mf.section[j].sectionOnRequest = false;
                mf.section[j].sectionOffRequest = true;
                mf.section[j].isSectionOn = false;
                mf.section[j].isMappingOn = false;
                mf.section[j].mappingOnTimer = 0;
                mf.section[j].mappingOffTimer = 0;
            }

            for (int j = 0; j < mf.triStrip.Count; j++)
            {
                if (mf.triStrip[j].isDrawing)
                    mf.triStrip[j].TurnMappingOff();
            }

            mf.oglMain.Refresh();
        }

        private void GetRightHalfSectionRange(out int startSection, out int endSection)
        {
            startSection = mf.tool.numOfSections - 1;
            endSection = mf.tool.numOfSections - 1;

            for (int j = 0; j < mf.tool.numOfSections; j++)
            {
                if (mf.section[j].positionRight > 0)
                {
                    startSection = j;
                    break;
                }
            }

            for (int j = mf.tool.numOfSections - 1; j >= 0; j--)
            {
                if (mf.section[j].positionRight > 0)
                {
                    endSection = j;
                    break;
                }
            }
        }

        private void MeasureTimer_Tick(object sender, EventArgs e)
        {
            if (mf == null || temporaryTrack == null || !mf.isBtnAutoSteerOn) return;

            ClampSectionsToRightHalf();
            UpdateMarkingGate();

            if (Math.Abs(mf.avgSpeed) < 0.5) return;
            if (!isMarkingActive) return;

            double directionDot = Math.Cos(mf.pivotAxlePos.heading - temporaryTrack.heading);
            if (directionDot > 0.45)
            {
                lastForwardPassOffset = GetToolCenterSignedDistanceFromTrack();
                forwardPassOffsetSum += lastForwardPassOffset;
                forwardPassSamples++;
            }
            else if (directionDot < -0.45)
            {
                lastReversePassOffset = GetToolCenterSignedDistanceFromTrack();
                reversePassOffsetSum += lastReversePassOffset;
                reversePassSamples++;
            }

            UpdateLabels();
        }

        private void UpdateMarkingGate()
        {
            double xte = Math.Abs(GetActiveLineXteMeters());
            double along = GetToolAlongTrackMeters();
            double directionDot = Math.Cos(mf.pivotAxlePos.heading - temporaryTrack.heading);

            if (Math.Abs(mf.avgSpeed) < 0.5)
            {
                markingStatus = "Waiting for movement.";
                SetWizardPaintingActive(false);
                UpdateLabels();
                return;
            }

            if (xte > XteStartLimitMeters)
            {
                markingStatus = "XTE too high: " + (xte * 100.0).ToString("N1", CultureInfo.CurrentCulture) + " cm";
                SetWizardPaintingActive(false);
                UpdateLabels();
                return;
            }

            if (!firstPassStarted)
            {
                if (directionDot > 0.45)
                {
                    firstPassStarted = true;
                    firstPassStartAlong = along;
                    lastForwardAlong = along;
                    markingStatus = "First pass marking.";
                    SetWizardPaintingActive(true);
                }
                else
                {
                    markingStatus = "Drive first pass forward on AB line.";
                    SetWizardPaintingActive(false);
                }

                UpdateLabels();
                return;
            }

            if (!firstPassFinished)
            {
                if (directionDot > 0.45)
                {
                    lastForwardAlong = along;
                    markingStatus = "First pass marking.";
                    SetWizardPaintingActive(true);
                }
                else
                {
                    firstPassFinished = true;
                    firstPassEndAlong = lastForwardAlong;
                    markingStatus = "First pass saved. Turn 180 and return to the end mark.";
                    SetWizardPaintingActive(false);
                }

                UpdateLabels();
                return;
            }

            if (!reversePassStarted)
            {
                if (directionDot < -0.45 && Math.Abs(along - firstPassEndAlong) <= ReverseStartToleranceMeters)
                {
                    reversePassStarted = true;
                    markingStatus = "Reverse pass marking.";
                    SetWizardPaintingActive(true);
                }
                else
                {
                    markingStatus = "Return to first pass end mark.";
                    SetWizardPaintingActive(false);
                }

                UpdateLabels();
                return;
            }

            if (!reversePassFinished)
            {
                if (directionDot < -0.45 && along > firstPassStartAlong)
                {
                    markingStatus = "Reverse pass marking.";
                    SetWizardPaintingActive(true);
                }
                else
                {
                    reversePassFinished = true;
                    markingStatus = "Reverse pass complete.";
                    SetWizardPaintingActive(false);
                }

                UpdateLabels();
                return;
            }

            markingStatus = "Measurement complete.";
            SetWizardPaintingActive(false);
            UpdateLabels();
        }

        private double GetActiveLineXteMeters()
        {
            double dx = mf.ABLine.currentLinePtB.easting - mf.ABLine.currentLinePtA.easting;
            double dy = mf.ABLine.currentLinePtB.northing - mf.ABLine.currentLinePtA.northing;
            double length = Math.Sqrt((dx * dx) + (dy * dy));

            if (length < 0.001) return Math.Abs(mf.ABLine.distanceFromCurrentLinePivot);

            return ((dy * mf.pivotAxlePos.easting) - (dx * mf.pivotAxlePos.northing)
                + (mf.ABLine.currentLinePtB.easting * mf.ABLine.currentLinePtA.northing)
                - (mf.ABLine.currentLinePtB.northing * mf.ABLine.currentLinePtA.easting)) / length;
        }

        private double GetToolAlongTrackMeters()
        {
            double alongEast = Math.Sin(temporaryTrack.heading);
            double alongNorth = Math.Cos(temporaryTrack.heading);

            return ((GetGpsOnlyToolEasting() - temporaryTrack.ptA.easting) * alongEast)
                + ((GetGpsOnlyToolNorthing() - temporaryTrack.ptA.northing) * alongNorth);
        }

        private double GetToolCenterSignedDistanceFromTrack()
        {
            double normalEast = Math.Sin(temporaryTrack.heading + glm.PIBy2);
            double normalNorth = Math.Cos(temporaryTrack.heading + glm.PIBy2);

            return ((GetGpsOnlyToolEasting() - temporaryTrack.ptA.easting) * normalEast)
                + ((GetGpsOnlyToolNorthing() - temporaryTrack.ptA.northing) * normalNorth);
        }

        private void CalculateGpsOnlySectionLookAhead()
        {
            mf.CalculateSectionLookAhead(GetGpsOnlyToolNorthing(), GetGpsOnlyToolEasting(), mf.cosSectionHeading, mf.sinSectionHeading);
        }

        private double GetGpsOnlyToolEasting()
        {
            return mf.GetGpsOnlyMappingToolEasting();
        }

        private double GetGpsOnlyToolNorthing()
        {
            return mf.GetGpsOnlyMappingToolNorthing();
        }

        private void ResetMeasurements()
        {
            forwardPassOffsetSum = 0;
            reversePassOffsetSum = 0;
            lastForwardPassOffset = 0;
            lastReversePassOffset = 0;
            firstPassStartAlong = 0;
            firstPassEndAlong = 0;
            lastForwardAlong = 0;
            forwardPassSamples = 0;
            reversePassSamples = 0;
            isMarkingActive = false;
            firstPassStarted = false;
            firstPassFinished = false;
            reversePassStarted = false;
            reversePassFinished = false;
            markingStatus = "Waiting for first pass.";
            if (btnApply != null) btnApply.Enabled = false;
        }

        private bool HasEnoughMeasurement()
        {
            return forwardPassSamples >= MinimumSamplesPerPass && reversePassSamples >= MinimumSamplesPerPass;
        }

        private double GetMeasuredPassErrorMeters()
        {
            if (forwardPassSamples == 0 || reversePassSamples == 0) return 0;

            double forward = forwardPassOffsetSum / forwardPassSamples;
            double reverse = reversePassOffsetSum / reversePassSamples;
            return reverse - forward;
        }

        private void UpdateAutoSteerButton()
        {
            if (mf != null && mf.isBtnAutoSteerOn)
            {
                btnAutoSteer.Text = "Autosteer Off";
                btnAutoSteer.BackColor = Color.LightCoral;
            }
            else
            {
                btnAutoSteer.Text = "Autosteer On";
                btnAutoSteer.BackColor = Color.LightBlue;
            }
        }

        private void MeasurementChanged(object sender, EventArgs e)
        {
            UpdateLabels();
        }

        private double GetSignedCorrectionDegrees()
        {
            double measuredMeters = GetMeasuredPassErrorMeters();
            double antennaHeight = (double)nudAntennaHeight.Value;

            return glm.toDegrees(Math.Atan(measuredMeters / antennaHeight)) * -0.5;
        }

        private void UpdateLabels()
        {
            double passErrorMeters = GetMeasuredPassErrorMeters();
            double correction = GetSignedCorrectionDegrees();
            double newRollZero = baseRollZero + correction;
            string gapMode = passErrorMeters >= 0.001 ? "Overlap" : "Gap";

            lblMeasureStatus.Text =
                markingStatus + "\r\n\r\n"
                + "Forward pass samples: " + forwardPassSamples.ToString(CultureInfo.CurrentCulture) + "\r\n"
                + "Reverse pass samples: " + reversePassSamples.ToString(CultureInfo.CurrentCulture) + "\r\n\r\n"
                + "Forward offset: " + (lastForwardPassOffset * 100.0).ToString("N1", CultureInfo.CurrentCulture) + " cm\r\n"
                + "Reverse offset: " + (lastReversePassOffset * 100.0).ToString("N1", CultureInfo.CurrentCulture) + " cm";

            lblPassError.Text =
                gapMode + ":\r\n" + Math.Abs(passErrorMeters * 100.0).ToString("N1", CultureInfo.CurrentCulture) + " cm\r\n\r\n"
                + "Measured:\r\n" + (passErrorMeters * 100.0).ToString("N1", CultureInfo.CurrentCulture) + " cm";

            lblAntennaHeight.Text = "Saved height: " + Properties.VehicleSettings.Default.setVehicle_antennaHeight.ToString("N2", CultureInfo.CurrentCulture) + " m";
            lblCurrentRoll.Text = "Current: " + baseRollZero.ToString("N2", CultureInfo.CurrentCulture);
            lblCorrection.Text = "Correction: " + correction.ToString("N2", CultureInfo.CurrentCulture);
            lblNewRoll.Text = "New: " + newRollZero.ToString("N2", CultureInfo.CurrentCulture);
            btnApply.Enabled = HasEnoughMeasurement();
            picPasses.Invalidate();
        }

        private void BtnApply_Click(object sender, EventArgs e)
        {
            if (mf == null) return;

            if (!HasEnoughMeasurement())
            {
                MessageBox.Show(this, "Drive both directions on the AB line before applying.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            double correction = GetSignedCorrectionDegrees();
            double newRollZero = baseRollZero + correction;

            Properties.VehicleSettings.Default.setIMU_rollZero = newRollZero;
            Properties.VehicleSettings.Default.Save();

            mf.ahrs.rollZero = newRollZero;

            lblCurrentRoll.Text = "Applied: " + newRollZero.ToString("N2", CultureInfo.CurrentCulture);
            Log.EventWriter("Auto Roll Offset measured pass error " + (GetMeasuredPassErrorMeters() * 100.0).ToString("N2", CultureInfo.InvariantCulture)
                + " cm, correction " + correction.ToString("N3", CultureInfo.InvariantCulture)
                + ", roll zero " + newRollZero.ToString("N3", CultureInfo.InvariantCulture));

            MessageBox.Show(this, "IMU roll zero updated.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void FormAutoRollOffsetWizard_FormClosing(object sender, FormClosingEventArgs e)
        {
            abTimer.Stop();
            measureTimer.Stop();
            if (mf != null)
            {
                mf.ABLine.isMakingABLine = false;
                RemoveTemporaryTrack();
                DisableCoveragePaintingIfWizardStartedIt();
                RestoreAppliedAreaSnapshot();
                RestoreSectionPositions();
            }
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
                else
                {
                    DisableCoveragePaintingIfWizardStartedIt();
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
    }
}
