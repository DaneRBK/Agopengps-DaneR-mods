using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using AgLibrary.Logging;
using AgOpenGPS.Controls;

namespace AgOpenGPS
{
    public class FormImuRollWizard : Form
    {
        private const double AutoAbDistanceMeters = 10.0;

        private readonly FormGPS mf;
        private readonly Timer abTimer = new Timer();
        private readonly double baseRollZero;

        private bool isRecordingAbLine;
        private bool didWizardTurnPaintingOn;
        private bool didCaptureAppliedAreaSnapshot;
        private vec2 abStart = new vec2();
        private CTrk temporaryTrack;

        private int[] appliedPatchCounts;
        private int appliedPatchSaveCount;
        private int appliedPatchCounter;
        private double appliedWorkedAreaTotal;
        private double appliedWorkedAreaTotalUser;

        private Label lblStep;
        private Label lblAbStatus;
        private Label lblCurrentRoll;
        private Label lblAntennaHeight;
        private Label lblCorrection;
        private Label lblNewRoll;
        private Button btnCreateAb;
        private Button btnAutoSteer;
        private Button btnApply;
        private NudlessNumericUpDown nudLineLeftOffset;
        private NudlessNumericUpDown nudWheelOnLineOffset;
        private NudlessNumericUpDown nudAntennaHeight;

        public FormImuRollWizard(Form callingForm)
        {
            mf = callingForm as FormGPS;
            baseRollZero = Properties.VehicleSettings.Default.setIMU_rollZero;

            InitializeComponent();
            UpdateLabels();
            UpdateAutoSteerButton();

            abTimer.Interval = 250;
            abTimer.Tick += AbTimer_Tick;
        }

        private void InitializeComponent()
        {
            Text = "IMU Roll Offset Wizard - Manual";
            Name = "FormImuRollWizard";
            ClientSize = new Size(820, 650);
            MinimumSize = new Size(820, 650);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.Gainsboro;
            Font = new Font("Tahoma", 11.25F, FontStyle.Regular, GraphicsUnit.Point, 0);

            lblStep = new Label
            {
                AutoSize = false,
                Location = new Point(18, 14),
                Size = new Size(775, 86),
                Font = new Font("Tahoma", 13.5F, FontStyle.Bold),
                Text = "1. Create a 10 m AB line, then steer on it in both directions. Mark the wheel on the ground in each pass.\r\nManual"
            };

            btnCreateAb = MakeButton("Create 10 m AB", new Point(22, 105), new Size(205, 72), Color.PaleGreen);
            btnCreateAb.Click += BtnCreateAb_Click;

            lblAbStatus = new Label
            {
                AutoSize = false,
                Location = new Point(245, 110),
                Size = new Size(545, 62),
                TextAlign = ContentAlignment.MiddleLeft,
                Text = "Press Create, drive straight 10 m, and a temporary AB line will be created."
            };

            btnAutoSteer = MakeButton("Autosteer On", new Point(22, 195), new Size(205, 72), Color.LightBlue);
            btnAutoSteer.Click += BtnAutoSteer_Click;

            Label lblDrive = new Label
            {
                AutoSize = false,
                Location = new Point(245, 195),
                Size = new Size(545, 72),
                TextAlign = ContentAlignment.MiddleLeft,
                Text = "Mark the right wheel, turn 180 degrees, drive the same line back, and measure the left wheel from the mark. Enter the measured value in the correct place."
            };

            GroupBox gbMeasure = new GroupBox
            {
                Location = new Point(22, 290),
                Size = new Size(768, 220),
                Text = "Measured left front wheel track offset"
            };

            PictureBox picLineLeft = MakeGuidePicture(new Point(35, 28), true);
            PictureBox picWheelOnLine = MakeGuidePicture(new Point(415, 28), false);

            Label lblLineLeft = new Label
            {
                Location = new Point(20, 142),
                Size = new Size(330, 26),
                Text = "Line/old track is left of wheel",
                TextAlign = ContentAlignment.MiddleCenter
            };

            Label lblWheelOnLine = new Label
            {
                Location = new Point(400, 142),
                Size = new Size(330, 26),
                Text = "Wheel is driving on the line/old track",
                TextAlign = ContentAlignment.MiddleCenter
            };

            nudLineLeftOffset = MakeOffsetInput(new Point(125, 174));
            nudWheelOnLineOffset = MakeOffsetInput(new Point(505, 174));

            Label lblCm1 = MakeCmLabel(new Point(246, 176));
            Label lblCm2 = MakeCmLabel(new Point(626, 176));

            gbMeasure.Controls.Add(picLineLeft);
            gbMeasure.Controls.Add(picWheelOnLine);
            gbMeasure.Controls.Add(lblLineLeft);
            gbMeasure.Controls.Add(lblWheelOnLine);
            gbMeasure.Controls.Add(nudLineLeftOffset);
            gbMeasure.Controls.Add(nudWheelOnLineOffset);
            gbMeasure.Controls.Add(lblCm1);
            gbMeasure.Controls.Add(lblCm2);

            Label lblHeightCaption = new Label
            {
                Location = new Point(25, 535),
                Size = new Size(155, 32),
                Text = "Antenna height (m):",
                TextAlign = ContentAlignment.MiddleRight
            };

            nudAntennaHeight = new NudlessNumericUpDown
            {
                Location = new Point(190, 533),
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
                Location = new Point(310, 533),
                Size = new Size(170, 32),
                TextAlign = ContentAlignment.MiddleLeft
            };

            lblCurrentRoll = new Label
            {
                Location = new Point(25, 575),
                Size = new Size(210, 28),
                TextAlign = ContentAlignment.MiddleLeft
            };

            lblCorrection = new Label
            {
                Location = new Point(245, 575),
                Size = new Size(205, 28),
                TextAlign = ContentAlignment.MiddleLeft
            };

            lblNewRoll = new Label
            {
                Location = new Point(455, 575),
                Size = new Size(150, 28),
                TextAlign = ContentAlignment.MiddleLeft
            };

            btnApply = MakeButton("Apply", new Point(690, 532), new Size(100, 70), Color.PaleGreen);
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

            FormClosing += FormImuRollWizard_FormClosing;
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

        private PictureBox MakeGuidePicture(Point location, bool lineLeftOfWheel)
        {
            PictureBox picture = new PictureBox
            {
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Location = location,
                Size = new Size(300, 108)
            };

            picture.Paint += (sender, e) => DrawWheelGuide(e.Graphics, picture.ClientRectangle, lineLeftOfWheel);
            return picture;
        }

        private NudlessNumericUpDown MakeOffsetInput(Point location)
        {
            NudlessNumericUpDown input = new NudlessNumericUpDown
            {
                Location = location,
                Size = new Size(115, 32),
                DecimalPlaces = 1,
                Increment = 1,
                Minimum = 0,
                Maximum = 500,
                TextAlign = HorizontalAlignment.Center
            };

            input.ValueChanged += MeasurementChanged;
            input.Click += NumericInput_Click;
            return input;
        }

        private void NumericInput_Click(object sender, EventArgs e)
        {
            if (((NudlessNumericUpDown)sender).ShowKeypad(this))
            {
                UpdateLabels();
            }
        }

        private static Label MakeCmLabel(Point location)
        {
            return new Label
            {
                Location = location,
                Size = new Size(35, 28),
                Text = "cm",
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        private static void DrawWheelGuide(Graphics g, Rectangle bounds, bool lineLeftOfWheel)
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.White);

            int wheelX = bounds.Left + bounds.Width / 2 + (lineLeftOfWheel ? 48 : 0);
            Rectangle wheel = new Rectangle(wheelX - 26, bounds.Top + 22, 30, 64);
            int lineX = lineLeftOfWheel ? wheelX - 95 : wheel.Left + (wheel.Width / 2);
            int top = bounds.Top;
            int bottom = bounds.Bottom;

            using (Pen linePen = new Pen(Color.RoyalBlue, 6))
            using (Brush wheelBrush = new SolidBrush(Color.FromArgb(35, 35, 35)))
            using (Brush bodyBrush = new SolidBrush(Color.FromArgb(235, 235, 235)))
            using (Pen wheelPen = new Pen(Color.Black, 2))
            {
                g.DrawLine(linePen, lineX, top, lineX, bottom);

                Rectangle tractorHalf = new Rectangle(wheelX - 4, bounds.Top + 18, bounds.Right - wheelX + 22, 72);
                g.FillRectangle(bodyBrush, tractorHalf);

                g.FillRectangle(wheelBrush, wheel);
                g.DrawRectangle(wheelPen, wheel);
                DrawTireLugs(g, wheel);
            }
        }

        private static void DrawTireLugs(Graphics g, Rectangle wheel)
        {
            using (Brush lugBrush = new SolidBrush(Color.FromArgb(80, 80, 80)))
            using (Pen lugPen = new Pen(Color.FromArgb(15, 15, 15), 1))
            {
                for (int y = wheel.Top + 5; y < wheel.Bottom - 8; y += 10)
                {
                    Point[] leftLug =
                    {
                        new Point(wheel.Left + 2, y),
                        new Point(wheel.Left + 13, y + 5),
                        new Point(wheel.Left + 13, y + 10),
                        new Point(wheel.Left + 2, y + 5)
                    };

                    Point[] rightLug =
                    {
                        new Point(wheel.Right - 2, y),
                        new Point(wheel.Right - 13, y + 5),
                        new Point(wheel.Right - 13, y + 10),
                        new Point(wheel.Right - 2, y + 5)
                    };

                    g.FillPolygon(lugBrush, leftLug);
                    g.DrawPolygon(lugPen, leftLug);
                    g.FillPolygon(lugBrush, rightLug);
                    g.DrawPolygon(lugPen, rightLug);
                }
            }
        }

        private static decimal ClampDecimal(decimal value, decimal min, decimal max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private void BtnCreateAb_Click(object sender, EventArgs e)
        {
            if (mf == null) return;

            RemoveTemporaryTrack();

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
            track.name = "IMU Roll Cal " + DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

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
                EnableCoveragePainting();
            }
            else
            {
                DisableCoveragePaintingIfWizardStartedIt();
            }

            UpdateAutoSteerButton();
            mf.Activate();
        }

        private void ForceAutoSteerOn()
        {
            mf.isBtnAutoSteerOn = true;
            mf.btnAutoSteer.Image = mf.trk.isAutoSnapToPivot ? Properties.Resources.AutoSteerOnSnapToPivot : Properties.Resources.AutoSteerOn;
            Log.EventWriter("IMU Roll Wizard forced autosteer on for temporary AB line");
        }

        private void ForceAutoSteerOff()
        {
            mf.isBtnAutoSteerOn = false;
            mf.btnAutoSteer.Image = mf.trk.isAutoSnapToPivot ? Properties.Resources.AutoSteerOffSnapToPivot : Properties.Resources.AutoSteerOff;
            Log.EventWriter("IMU Roll Wizard forced autosteer off");
            DisableCoveragePaintingIfWizardStartedIt();
        }

        private void EnableCoveragePainting()
        {
            if (mf.manualBtnState == btnStates.On || mf.autoBtnState == btnStates.Auto) return;

            CaptureAppliedAreaSnapshot();

            mf.autoBtnState = btnStates.Off;
            mf.btnSectionMasterAuto.Image = Properties.Resources.SectionMasterOff;

            mf.manualBtnState = btnStates.On;
            mf.btnSectionMasterManual.Image = Properties.Resources.ManualOn;

            if (mf.tool.isSectionsNotZones)
                mf.AllSectionsAndButtonsToState(btnStates.On);
            else
                mf.AllZonesAndButtonsToState(btnStates.On);

            ForceSectionsMappingOn();
            didWizardTurnPaintingOn = true;
        }

        private void DisableCoveragePaintingIfWizardStartedIt()
        {
            if (!didWizardTurnPaintingOn) return;

            if (mf.manualBtnState == btnStates.On)
            {
                mf.manualBtnState = btnStates.Off;
                mf.btnSectionMasterManual.Image = Properties.Resources.ManualOff;

                if (mf.tool.isSectionsNotZones)
                    mf.AllSectionsAndButtonsToState(btnStates.Off);
                else
                    mf.AllZonesAndButtonsToState(btnStates.Off);
            }

            ForceSectionsMappingOff();
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

        private void ForceSectionsMappingOn()
        {
            mf.CalculateSectionLookAhead(mf.GetGpsOnlyMappingToolNorthing(), mf.GetGpsOnlyMappingToolEasting(), mf.cosSectionHeading, mf.sinSectionHeading);

            for (int j = 0; j < mf.tool.numOfSections; j++)
            {
                mf.section[j].sectionBtnState = btnStates.On;
                mf.section[j].sectionOnRequest = true;
                mf.section[j].sectionOffRequest = false;
                mf.section[j].isSectionOn = true;
                mf.section[j].isMappingOn = true;
                mf.section[j].mappingOnTimer = 0;
                mf.section[j].mappingOffTimer = 0;
            }

            if (mf.triStrip.Count == 0)
                mf.triStrip.Add(new CPatches(mf));

            mf.triStrip[0].currentStartSectionNum = 0;
            mf.triStrip[0].currentEndSectionNum = mf.tool.numOfSections - 1;
            mf.triStrip[0].newStartSectionNum = 0;
            mf.triStrip[0].newEndSectionNum = mf.tool.numOfSections - 1;

            if (!mf.triStrip[0].isDrawing)
                mf.triStrip[0].TurnMappingOn(0);

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
            double measuredMeters = ((double)nudLineLeftOffset.Value - (double)nudWheelOnLineOffset.Value) * 0.01;
            double antennaHeight = (double)nudAntennaHeight.Value;

            return glm.toDegrees(Math.Atan(measuredMeters / antennaHeight)) * 0.5;
        }

        private void UpdateLabels()
        {
            double correction = GetSignedCorrectionDegrees();
            double newRollZero = baseRollZero + correction;

            lblAntennaHeight.Text = "Saved height: " + Properties.VehicleSettings.Default.setVehicle_antennaHeight.ToString("N2", CultureInfo.CurrentCulture) + " m";
            lblCurrentRoll.Text = "Current: " + baseRollZero.ToString("N2", CultureInfo.CurrentCulture);
            lblCorrection.Text = "Correction: " + correction.ToString("N2", CultureInfo.CurrentCulture);
            lblNewRoll.Text = "New: " + newRollZero.ToString("N2", CultureInfo.CurrentCulture);
        }

        private void BtnApply_Click(object sender, EventArgs e)
        {
            if (mf == null) return;

            double correction = GetSignedCorrectionDegrees();
            double newRollZero = baseRollZero + correction;

            Properties.VehicleSettings.Default.setIMU_rollZero = newRollZero;
            Properties.VehicleSettings.Default.Save();

            mf.ahrs.rollZero = newRollZero;

            lblCurrentRoll.Text = "Applied: " + newRollZero.ToString("N2", CultureInfo.CurrentCulture);
            Log.EventWriter("IMU Roll Wizard correction " + correction.ToString("N3", CultureInfo.InvariantCulture)
                + ", roll zero " + newRollZero.ToString("N3", CultureInfo.InvariantCulture));

            MessageBox.Show(this, "IMU roll zero updated.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void FormImuRollWizard_FormClosing(object sender, FormClosingEventArgs e)
        {
            abTimer.Stop();
            if (mf != null)
            {
                mf.ABLine.isMakingABLine = false;
                RemoveTemporaryTrack();
                DisableCoveragePaintingIfWizardStartedIt();
                RestoreAppliedAreaSnapshot();
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
