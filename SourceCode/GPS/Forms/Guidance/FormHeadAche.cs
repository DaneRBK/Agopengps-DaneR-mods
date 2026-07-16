using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using AgLibrary.Logging;
using AgOpenGPS.Controls;
using AgOpenGPS.Core.Models;
using AgOpenGPS.Core.Translations;
using AgOpenGPS.Forms;
using AgOpenGPS.Helpers;
using OpenTK;
using OpenTK.Graphics.OpenGL;

namespace AgOpenGPS
{
    public partial class FormHeadAche : Form
    {
        //access to the main GPS form and all its variables
        private readonly FormGPS mf = null;

        private Point fixPt;

        private bool isA = true;
        private int start = 99999, end = 99999;
        private int bndSelect = 0;

        private bool zoomToggle;
        private double zoom = 1, sX = 0, sY = 0;

        public vec3 pint = new vec3(0.0, 1.0, 0.0);

        private bool isLinesVisible = true;
        private bool isHydLiftLineStartSet;
        private vec3 hydLiftLineStartPoint = new vec3();

        public FormHeadAche(Form callingForm)
        {
            //get copy of the calling main form
            mf = callingForm as FormGPS;

            InitializeComponent();
            mf.CalculateMinMax();
        }

        private void FormHeadLine_Load(object sender, EventArgs e)
        {
            this.Text = "1: Set distance, 2: Tap Build, 3: Create Clip Lines";

            mf.hdl.idx = -1;

            mf.FileLoadHeadLines();
            FixLabelsCurve();

            lblToolWidth.Text = "( " + mf.unitsFtM + " )      Tool: "
                + ((mf.tool.width - mf.tool.overlap) * mf.m2FtOrM).ToString("N1") + mf.unitsFtM + " ";

            mf.bnd.bndList[0].hdLine?.Clear();

            cboxIsSectionControlled.Checked = Properties.ToolSettings.Default.setHeadland_isSectionControlled;
            if (cboxIsSectionControlled.Checked) cboxIsSectionControlled.Image = Properties.Resources.HeadlandSectionOn;
            else cboxIsSectionControlled.Image = Properties.Resources.HeadlandSectionOff;

            Size = Properties.Settings.Default.setWindow_HeadAcheSize;

            Screen myScreen = Screen.FromControl(this);
            Rectangle area = myScreen.WorkingArea;

            this.Top = (area.Height - this.Height) / 2;
            this.Left = (area.Width - this.Width) / 2;
            FormHeadAche_ResizeEnd(this, e);

            if (!ScreenHelper.IsOnScreen(Bounds))
            {
                Top = 0;
                Left = 0;
            }
            //translate
            this.Text = gStr.gsHeadlandForm;
            btnBndLoop.Text = gStr.gsBuild;
            btnDeleteHeadland.Text = gStr.gsReset;

        }

        private void FormHeadLine_FormClosing(object sender, FormClosingEventArgs e)
        {
            mf.FileSaveHeadLines();

            if (mf.hdl.tracksArr.Count > 0)
            {
                mf.hdl.idx = 0;
            }
            else mf.hdl.idx = -1;

            Properties.Settings.Default.setWindow_HeadAcheSize = Size;
            Properties.Settings.Default.Save();
        }

        private void FormHeadAche_ResizeEnd(object sender, EventArgs e)
        {
            Width = (Height * 4 / 3);

            oglSelf.Height = oglSelf.Width = Height - 50;

            oglSelf.Left = 2;
            oglSelf.Top = 2;

            oglSelf.MakeCurrent();
            GL.MatrixMode(MatrixMode.Projection);
            GL.LoadIdentity();

            //58 degrees view
            GL.Viewport(0, 0, oglSelf.Width, oglSelf.Height);
            Matrix4 mat = Matrix4.CreatePerspectiveFieldOfView(1.01f, 1.0f, 1.0f, 20000);
            GL.LoadMatrix(ref mat);

            GL.MatrixMode(MatrixMode.Modelview);

            tlp1.Width = Width - oglSelf.Width - 4;
            tlp1.Left = oglSelf.Width;

            Screen myScreen = Screen.FromControl(this);
            Rectangle area = myScreen.WorkingArea;

            this.Top = (area.Height - this.Height) / 2;
            this.Left = (area.Width - this.Width) / 2;
        }

        private void FixLabelsCurve()
        {
        }

        private string BuildHeadPathName(string lineType, string timeFormat)
        {
            string hydPrefix = cboxHydLiftLine.Checked ? CHeadPath.HydLiftLinePrefix + " " : "";
            return hydPrefix + mf.hdl.idx.ToString(CultureInfo.InvariantCulture) + " " + lineType + " " + DateTime.Now.ToString(timeFormat, CultureInfo.InvariantCulture);
        }

        private void btnCycleForward_Click(object sender, EventArgs e)
        {
            mf.bnd.bndList[0].hdLine?.Clear();

            if (mf.hdl.tracksArr.Count > 0)
            {
                mf.hdl.idx++;
                if (mf.hdl.idx > (mf.hdl.tracksArr.Count - 1)) mf.hdl.idx = 0;
            }
            else
            {
                mf.hdl.idx = -1;
            }

            FixLabelsCurve();
        }

        private void btnCycleBackward_Click(object sender, EventArgs e)
        {
            mf.bnd.bndList[0].hdLine?.Clear();

            if (mf.hdl.tracksArr.Count > 0)
            {
                mf.hdl.idx--;
                if (mf.hdl.idx < 0) mf.hdl.idx = (mf.hdl.tracksArr.Count - 1);
            }
            else
            {
                mf.hdl.idx = -1;
            }

            FixLabelsCurve();
        }

        private void btnDeleteCurve_Click(object sender, EventArgs e)
        {
            //mf.bnd.bndList[0].hdLine?.Clear();

            if (mf.hdl.tracksArr.Count > 0 && mf.hdl.idx > -1)
            {
                mf.hdl.tracksArr.RemoveAt(mf.hdl.idx);
                mf.hdl.idx--;
            }

            if (mf.hdl.tracksArr.Count > 0)
            {
                if (mf.hdl.idx == -1)
                {
                    mf.hdl.idx++;
                }
            }
            else mf.hdl.idx = -1;

            FixLabelsCurve();
        }

        private void oglSelf_MouseDown(object sender, MouseEventArgs e)
        {
            Point pt = oglSelf.PointToClient(Cursor.Position);

            int wid = oglSelf.Width;
            int halfWid = oglSelf.Width / 2;
            double scale = (double)wid * 0.903;

            if (cboxIsZoom.Checked && !zoomToggle)
            {
                sX = ((halfWid - (double)pt.X) / wid) * 1.1;
                sY = ((halfWid - (double)pt.Y) / -wid) * 1.1;
                zoom = 0.1;
                zoomToggle = true;
                return;
            }

            zoomToggle = false;

            //Convert to Origin in the center of window, 800 pixels
            fixPt.X = pt.X - halfWid;
            fixPt.Y = (wid - pt.Y - halfWid);

            vec3 plotPt = new vec3
            {
                //convert screen coordinates to field coordinates
                easting = fixPt.X * mf.maxFieldDistance / scale * zoom,
                northing = fixPt.Y * mf.maxFieldDistance / scale * zoom,
                heading = 0
            };

            plotPt.easting += mf.fieldCenterX + mf.maxFieldDistance * -sX;
            plotPt.northing += mf.fieldCenterY + mf.maxFieldDistance * -sY;

            pint.easting = plotPt.easting;
            pint.northing = plotPt.northing;

            zoom = 1;
            sX = 0;
            sY = 0;

            if (TryHandleHydLiftLineClick(new vec3(pint)))
            {
                return;
            }

            mf.bnd.bndList[0].hdLine?.Clear();
            mf.hdl.idx = -1;

            if (isA)
            {
                double minDistA = double.MaxValue;
                start = 99999; end = 99999;

                for (int j = 0; j < mf.bnd.bndList.Count; j++)
                {
                    int snapIndex = FindNearestBoundarySnapIndex(mf.bnd.bndList[j].fenceLine, pint);
                    if (snapIndex > -1)
                    {
                        double dist = GetDistanceSquared(pint, mf.bnd.bndList[j].fenceLine[snapIndex]);
                        if (dist < minDistA)
                        {
                            minDistA = dist;
                            bndSelect = j;
                            start = snapIndex;
                        }
                    }
                }

                if (start != 99999)
                {
                    pint = new vec3(mf.bnd.bndList[bndSelect].fenceLine[start]);
                }

                isA = false;
            }
            else
            {
                double minDistA = double.MaxValue;
                int j = bndSelect;

                int snapIndex = FindNearestBoundarySnapIndex(mf.bnd.bndList[j].fenceLine, pint);
                if (snapIndex > -1)
                {
                    double dist = GetDistanceSquared(pint, mf.bnd.bndList[j].fenceLine[snapIndex]);
                    if (dist < minDistA)
                    {
                        minDistA = dist;
                        end = snapIndex;
                    }
                }

                if (end != 99999)
                {
                    pint = new vec3(mf.bnd.bndList[j].fenceLine[end]);
                }

                isA = true;

                if (start == end)
                {
                    start = 99999; end = 99999;
                    FormDialog.Show("Line Error", "Start Point = End Point ", DialogSeverity.Error);
                    return;
                }

                //build the lines
                if (rbtnCurve.Checked)
                {
                    mf.hdl.tracksArr.Add(new CHeadPath());
                    mf.hdl.idx = mf.hdl.tracksArr.Count - 1;

                    bool isLoop = false;
                    int limit = end;

                    if ((Math.Abs(start - end)) > (mf.bnd.bndList[bndSelect].fenceLine.Count * 0.5))
                    {
                        if (start < end)
                        {
                            (start, end) = (end, start);
                        }

                        isLoop = true;
                        if (start < end)
                        {
                            limit = end;
                            end = 0;
                        }
                        else
                        {
                            limit = end;
                            end = mf.bnd.bndList[bndSelect].fenceLine.Count;
                        }
                    }
                    else
                    {
                        if (start > end)
                        {
                            (start, end) = (end, start);
                        }
                    }

                    mf.hdl.tracksArr[mf.hdl.idx].a_point = NormalizeBoundaryIndex(start, mf.bnd.bndList[bndSelect].fenceLine.Count);
                    mf.hdl.tracksArr[mf.hdl.idx].b_point = NormalizeBoundaryIndex(isLoop ? limit : end, mf.bnd.bndList[bndSelect].fenceLine.Count);
                    mf.hdl.tracksArr[mf.hdl.idx].lineStartIndex = 0;
                    mf.hdl.tracksArr[mf.hdl.idx].trackPts?.Clear();

                    if (start < end)
                    {
                        for (int i = start; i <= end; i++)
                        {
                            //calculate the point inside the boundary
                            mf.hdl.tracksArr[mf.hdl.idx].trackPts.Add(new vec3(mf.bnd.bndList[bndSelect].fenceLine[i]));

                            if (isLoop && i == mf.bnd.bndList[bndSelect].fenceLine.Count - 1)
                            {
                                i = -1;
                                isLoop = false;
                                end = limit;
                            }
                        }
                    }
                    else
                    {
                        for (int i = start; i >= end; i--)
                        {
                            //calculate the point inside the boundary                            
                            mf.hdl.tracksArr[mf.hdl.idx].trackPts.Add(new vec3(mf.bnd.bndList[bndSelect].fenceLine[i]));

                            if (isLoop && i == 0)
                            {
                                i = mf.bnd.bndList[bndSelect].fenceLine.Count - 1;
                                isLoop = false;
                                end = limit;
                            }
                        }
                    }

                    //who knows which way it actually goes
                    CABCurve.CalculateHeadings(ref mf.hdl.tracksArr[mf.hdl.idx].trackPts);

                    int ptCnt = mf.hdl.tracksArr[mf.hdl.idx].trackPts.Count - 1;
                    mf.hdl.tracksArr[mf.hdl.idx].lineEndIndex = ptCnt;

                    for (int i = 1; i < 30; i++)
                    {
                        vec3 pnt = new vec3(mf.hdl.tracksArr[mf.hdl.idx].trackPts[ptCnt]);
                        pnt.easting += (Math.Sin(pnt.heading) * i);
                        pnt.northing += (Math.Cos(pnt.heading) * i);
                        mf.hdl.tracksArr[mf.hdl.idx].trackPts.Add(pnt);
                    }

                    vec3 stat = new vec3(mf.hdl.tracksArr[mf.hdl.idx].trackPts[0]);

                    for (int i = 1; i < 30; i++)
                    {
                        vec3 pnt = new vec3(stat);
                        pnt.easting -= (Math.Sin(pnt.heading) * i);
                        pnt.northing -= (Math.Cos(pnt.heading) * i);
                        mf.hdl.tracksArr[mf.hdl.idx].trackPts.Insert(0, pnt);
                    }
                    mf.hdl.tracksArr[mf.hdl.idx].lineStartIndex += 29;
                    mf.hdl.tracksArr[mf.hdl.idx].lineEndIndex += 29;

                    //create a name
                    mf.hdl.tracksArr[mf.hdl.idx].name = BuildHeadPathName("Cu", "mm:ss");

                    mf.hdl.tracksArr[mf.hdl.idx].moveDistance = 0;

                    mf.hdl.tracksArr[mf.hdl.idx].mode = (int)TrackMode.Curve;

                    mf.FileSaveHeadLines();

                    //update the arrays
                    start = 99999; end = 99999;

                    FixLabelsCurve();
                    btnExit.Focus();
                }
                else if (rbtnLine.Checked)
                {
                    if ((Math.Abs(start - end)) > (mf.bnd.bndList[bndSelect].fenceLine.Count * 0.5))
                    {
                        if (start < end)
                        {
                            (start, end) = (end, start);
                        }
                    }
                    else
                    {
                        if (start > end)
                        {
                            (start, end) = (end, start);
                        }
                    }

                    vec3 ptA = new vec3(mf.bnd.bndList[bndSelect].fenceLine[start]);
                    vec3 ptB = new vec3(mf.bnd.bndList[bndSelect].fenceLine[end]);

                    //calculate the AB Heading
                    double abHead = Math.Atan2(
                        mf.bnd.bndList[bndSelect].fenceLine[end].easting - mf.bnd.bndList[bndSelect].fenceLine[start].easting,
                        mf.bnd.bndList[bndSelect].fenceLine[end].northing - mf.bnd.bndList[bndSelect].fenceLine[start].northing);
                    if (abHead < 0) abHead += glm.twoPI;

                    if (mf.hdl.idx < mf.hdl.tracksArr.Count - 1)
                    {
                        mf.hdl.idx++;
                        mf.hdl.tracksArr.Insert(mf.hdl.idx, new CHeadPath());
                    }
                    else
                    {
                        mf.hdl.tracksArr.Add(new CHeadPath());
                        mf.hdl.idx = mf.hdl.tracksArr.Count - 1;
                    }

                    mf.hdl.tracksArr[mf.hdl.idx].a_point = NormalizeBoundaryIndex(start, mf.bnd.bndList[bndSelect].fenceLine.Count);
                    mf.hdl.tracksArr[mf.hdl.idx].b_point = NormalizeBoundaryIndex(end, mf.bnd.bndList[bndSelect].fenceLine.Count);
                    mf.hdl.tracksArr[mf.hdl.idx].lineStartIndex = 0;
                    mf.hdl.tracksArr[mf.hdl.idx].trackPts?.Clear();

                    ptA.heading = abHead;
                    ptB.heading = abHead;

                    for (int i = 0; i <= (int)(glm.Distance(ptA, ptB)); i++)
                    {
                        vec3 ptC = new vec3(ptA)
                        {
                            easting = (Math.Sin(abHead) * i) + ptA.easting,
                            northing = (Math.Cos(abHead) * i) + ptA.northing,
                            heading = abHead
                        };
                        mf.hdl.tracksArr[mf.hdl.idx].trackPts.Add(ptC);
                    }

                    int ptCnt = mf.hdl.tracksArr[mf.hdl.idx].trackPts.Count - 1;
                    mf.hdl.tracksArr[mf.hdl.idx].lineEndIndex = ptCnt;

                    for (int i = 1; i < 30; i++)
                    {
                        vec3 pnt = new vec3(mf.hdl.tracksArr[mf.hdl.idx].trackPts[ptCnt]);
                        pnt.easting += (Math.Sin(pnt.heading) * i);
                        pnt.northing += (Math.Cos(pnt.heading) * i);
                        mf.hdl.tracksArr[mf.hdl.idx].trackPts.Add(pnt);
                    }

                    vec3 stat = new vec3(mf.hdl.tracksArr[mf.hdl.idx].trackPts[0]);

                    for (int i = 1; i < 30; i++)
                    {
                        vec3 pnt = new vec3(stat);
                        pnt.easting -= (Math.Sin(pnt.heading) * i);
                        pnt.northing -= (Math.Cos(pnt.heading) * i);
                        mf.hdl.tracksArr[mf.hdl.idx].trackPts.Insert(0, pnt);
                    }
                    mf.hdl.tracksArr[mf.hdl.idx].lineStartIndex += 29;
                    mf.hdl.tracksArr[mf.hdl.idx].lineEndIndex += 29;

                    //create a name
                    mf.hdl.tracksArr[mf.hdl.idx].name = BuildHeadPathName("AB", "hh:mm:ss");

                    mf.hdl.tracksArr[mf.hdl.idx].moveDistance = 0;

                    mf.hdl.tracksArr[mf.hdl.idx].mode = (int)TrackMode.AB;

                    mf.FileSaveHeadLines();

                    FixLabelsCurve();
                    start = 99999; end = 99999;
                }

                //mf.bnd.bndList[0].hdLine?.Clear();
                mf.hdl.desList?.Clear();

                if (mf.hdl.tracksArr.Count < 1 || mf.hdl.idx == -1) return;

                double distAway = (double)nudSetDistance.Value * mf.ftOrMtoM;
                mf.hdl.tracksArr[mf.hdl.idx].moveDistance += distAway;

                double distSqAway = (distAway * distAway) - 0.01;
                vec3 point;

                int refCount = mf.hdl.tracksArr[mf.hdl.idx].trackPts.Count;
                int shiftedLineStartIndex = -1;
                int shiftedLineEndIndex = -1;
                for (int i = 0; i < refCount; i++)
                {
                    point = new vec3(
                    mf.hdl.tracksArr[mf.hdl.idx].trackPts[i].easting - (Math.Sin(glm.PIBy2 + mf.hdl.tracksArr[mf.hdl.idx].trackPts[i].heading) * distAway),
                    mf.hdl.tracksArr[mf.hdl.idx].trackPts[i].northing - (Math.Cos(glm.PIBy2 + mf.hdl.tracksArr[mf.hdl.idx].trackPts[i].heading) * distAway),
                    mf.hdl.tracksArr[mf.hdl.idx].trackPts[i].heading);
                    bool Add = true;

                    for (int t = 0; t < refCount; t++)
                    {
                        double dist = ((point.easting - mf.hdl.tracksArr[mf.hdl.idx].trackPts[t].easting) * (point.easting - mf.hdl.tracksArr[mf.hdl.idx].trackPts[t].easting))
                            + ((point.northing - mf.hdl.tracksArr[mf.hdl.idx].trackPts[t].northing) * (point.northing - mf.hdl.tracksArr[mf.hdl.idx].trackPts[t].northing));
                        if (dist < distSqAway)
                        {
                            Add = false;
                            break;
                        }
                    }

                    if (Add)
                    {
                        if (mf.hdl.desList.Count > 0)
                        {
                            double dist = ((point.easting - mf.hdl.desList[mf.hdl.desList.Count - 1].easting) * (point.easting - mf.hdl.desList[mf.hdl.desList.Count - 1].easting))
                                + ((point.northing - mf.hdl.desList[mf.hdl.desList.Count - 1].northing) * (point.northing - mf.hdl.desList[mf.hdl.desList.Count - 1].northing));
                            if (dist > 1)
                            {
                                if (i >= mf.hdl.tracksArr[mf.hdl.idx].lineStartIndex && shiftedLineStartIndex < 0) shiftedLineStartIndex = mf.hdl.desList.Count;
                                if (i <= mf.hdl.tracksArr[mf.hdl.idx].lineEndIndex) shiftedLineEndIndex = mf.hdl.desList.Count;
                                mf.hdl.desList.Add(point);
                            }
                        }
                        else
                        {
                            if (i >= mf.hdl.tracksArr[mf.hdl.idx].lineStartIndex && shiftedLineStartIndex < 0) shiftedLineStartIndex = mf.hdl.desList.Count;
                            if (i <= mf.hdl.tracksArr[mf.hdl.idx].lineEndIndex) shiftedLineEndIndex = mf.hdl.desList.Count;
                            mf.hdl.desList.Add(point);
                        }
                    }
                }

                mf.hdl.tracksArr[mf.hdl.idx].trackPts.Clear();

                for (int i = 0; i < mf.hdl.desList.Count; i++)
                {
                    mf.hdl.tracksArr[mf.hdl.idx].trackPts.Add(new vec3(mf.hdl.desList[i]));
                }

                if (shiftedLineStartIndex >= 0) mf.hdl.tracksArr[mf.hdl.idx].lineStartIndex = shiftedLineStartIndex;
                if (shiftedLineEndIndex >= 0) mf.hdl.tracksArr[mf.hdl.idx].lineEndIndex = shiftedLineEndIndex;

                mf.hdl.desList?.Clear();
            }
        }

        private bool TryHandleHydLiftLineClick(vec3 clickPoint)
        {
            if (!cboxHydLiftLine.Checked)
            {
                isHydLiftLineStartSet = false;
                return false;
            }

            start = 99999;
            end = 99999;
            isA = true;

            if (!isHydLiftLineStartSet)
            {
                hydLiftLineStartPoint = new vec3(clickPoint);
                isHydLiftLineStartSet = true;
                oglSelf.Refresh();
                btnExit.Focus();
                return true;
            }

            isHydLiftLineStartSet = false;

            if (glm.Distance(hydLiftLineStartPoint, clickPoint) < 0.5)
            {
                FormDialog.Show("Line Error", "Start Point = End Point ", DialogSeverity.Error);
                return true;
            }

            AddHydLiftLine(hydLiftLineStartPoint, clickPoint);
            mf.FileSaveHeadLines();
            FixLabelsCurve();
            oglSelf.Refresh();
            btnExit.Focus();
            return true;
        }

        private void AddHydLiftLine(vec3 ptA, vec3 ptB)
        {
            double lineLength = glm.Distance(ptA, ptB);
            double abHead = Math.Atan2(ptB.easting - ptA.easting, ptB.northing - ptA.northing);
            if (abHead < 0) abHead += glm.twoPI;

            mf.hdl.tracksArr.Add(new CHeadPath());
            mf.hdl.idx = mf.hdl.tracksArr.Count - 1;

            CHeadPath headPath = mf.hdl.tracksArr[mf.hdl.idx];
            headPath.a_point = -1;
            headPath.b_point = -1;
            headPath.lineStartIndex = 0;
            headPath.trackPts?.Clear();

            ptA.heading = abHead;
            ptB.heading = abHead;

            int pointCount = Math.Max(1, (int)Math.Ceiling(lineLength));
            for (int i = 0; i <= pointCount; i++)
            {
                double distance = Math.Min(i, lineLength);
                vec3 ptC = new vec3(ptA)
                {
                    easting = (Math.Sin(abHead) * distance) + ptA.easting,
                    northing = (Math.Cos(abHead) * distance) + ptA.northing,
                    heading = abHead
                };
                headPath.trackPts.Add(ptC);
            }

            int ptCnt = headPath.trackPts.Count - 1;
            headPath.lineEndIndex = ptCnt;

            for (int i = 1; i < 30; i++)
            {
                vec3 pnt = new vec3(headPath.trackPts[ptCnt]);
                pnt.easting += (Math.Sin(pnt.heading) * i);
                pnt.northing += (Math.Cos(pnt.heading) * i);
                headPath.trackPts.Add(pnt);
            }

            vec3 startPoint = new vec3(headPath.trackPts[0]);

            for (int i = 1; i < 30; i++)
            {
                vec3 pnt = new vec3(startPoint);
                pnt.easting -= (Math.Sin(pnt.heading) * i);
                pnt.northing -= (Math.Cos(pnt.heading) * i);
                headPath.trackPts.Insert(0, pnt);
            }

            headPath.lineStartIndex += 29;
            headPath.lineEndIndex += 29;
            headPath.name = BuildHeadPathName("AB", "hh:mm:ss");
            headPath.moveDistance = 0;
            headPath.mode = (int)TrackMode.AB;
        }

        private void oglSelf_Paint(object sender, PaintEventArgs e)
        {
            oglSelf.MakeCurrent();

            GL.Clear(ClearBufferMask.DepthBufferBit | ClearBufferMask.ColorBufferBit);
            GL.LoadIdentity();                  // Reset The View

            //back the camera up
            GL.Translate(0, 0, -mf.maxFieldDistance * zoom);

            //translate to that spot in the world
            GL.Translate(-mf.fieldCenterX + sX * mf.maxFieldDistance, -mf.fieldCenterY + sY * mf.maxFieldDistance, 0);

            DrawFenceLinesAsBoundary();
            DrawExistingFieldReferenceLines();
            DrawHydLiftLinesAsBoundary();
            DrawHydLiftPreviewLine();

            //draw the actual built lines
            //if (start == 99999 && end == 99999)
            {
                DrawBuiltLines();
            }

            DrawABTouchLine();

            GL.Disable(EnableCap.Blend);

            GL.Flush();
            oglSelf.SwapBuffers();
        }

        private void oglSelf_Resize(object sender, EventArgs e)
        {
            oglSelf.MakeCurrent();
            GL.MatrixMode(MatrixMode.Projection);
            GL.LoadIdentity();

            //58 degrees view
            GL.Viewport(0, 0, oglSelf.Width, oglSelf.Height);

            Matrix4 mat = Matrix4.CreatePerspectiveFieldOfView(1.01f, 1.0f, 1.0f, 20000);
            GL.LoadMatrix(ref mat);

            GL.MatrixMode(MatrixMode.Modelview);
        }

        private void DrawFenceLinesAsBoundary()
        {
            for (int j = 0; j < mf.bnd.bndList.Count; j++)
            {
                if (mf.bnd.bndList[j].fenceLine.Count < 2) continue;

                GL.LineWidth(7);
                GL.Color3(0.0f, 0.0f, 0.0f);
                GL.Begin(PrimitiveType.LineLoop);
                for (int i = 0; i < mf.bnd.bndList[j].fenceLine.Count; i++)
                {
                    GL.Vertex3(mf.bnd.bndList[j].fenceLine[i].easting, mf.bnd.bndList[j].fenceLine[i].northing, 0);
                }
                GL.End();

                GL.LineWidth(3);
                if (j == bndSelect)
                    GL.Color3(0.86f, 0.86f, 0.86f);
                else
                    GL.Color3(0.65f, 0.38f, 0.14f);

                GL.Begin(PrimitiveType.LineLoop);
                for (int i = 0; i < mf.bnd.bndList[j].fenceLine.Count; i++)
                {
                    GL.Vertex3(mf.bnd.bndList[j].fenceLine[i].easting, mf.bnd.bndList[j].fenceLine[i].northing, 0);
                }
                GL.End();
            }
        }

        private void DrawHydLiftLinesAsBoundary()
        {
            if (mf.hdl?.tracksArr == null || mf.hdl.tracksArr.Count == 0) return;

            foreach (CHeadPath headPath in mf.hdl.tracksArr)
            {
                if (headPath.trackPts == null || headPath.trackPts.Count < 2) continue;

                GL.LineWidth(10);
                GL.Color3(0.0f, 0.0f, 0.0f);
                DrawHeadPathWorkingLine(headPath);

                GL.LineWidth(6);
                GL.Color3(0.05f, 0.85f, 1.0f);
                DrawHeadPathWorkingLine(headPath);

                int startIndex = GetHeadPathWorkingStartIndex(headPath);
                int endIndex = GetHeadPathWorkingEndIndex(headPath);
                GL.PointSize(18);
                GL.Begin(PrimitiveType.Points);
                GL.Color3(1.0f, 0.75f, 0.35f);
                GL.Vertex3(headPath.trackPts[startIndex].easting, headPath.trackPts[startIndex].northing, 0);
                GL.Color3(0.5f, 0.75f, 1.0f);
                GL.Vertex3(headPath.trackPts[endIndex].easting, headPath.trackPts[endIndex].northing, 0);
                GL.End();
            }
        }

        private void DrawExistingFieldReferenceLines()
        {
            DrawExistingGuidanceLines();
            DrawExistingTramLines();
            DrawExistingRecordedPath();
            DrawExistingFlags();
        }

        private void DrawExistingGuidanceLines()
        {
            if (mf.trk?.gArr == null || mf.trk.gArr.Count == 0) return;

            GL.LineWidth(8);
            GL.Color3(0.0f, 0.0f, 0.0f);
            DrawGuidanceLinesPrimitive();

            GL.LineWidth(4);
            GL.Color3(0.95f, 0.95f, 0.95f);
            DrawGuidanceLinesPrimitive();
        }

        private void DrawGuidanceLinesPrimitive()
        {
            foreach (CTrk track in mf.trk.gArr)
            {
                if (!track.isVisible) continue;

                if (track.mode == TrackMode.AB)
                {
                    double heading = track.heading;
                    vec2 pointA = track.ptA;
                    vec2 pointB = track.ptB;

                    if (glm.Distance(pointA, pointB) < 0.1)
                    {
                        pointA = new vec2(
                            track.ptA.easting - (Math.Sin(heading) * 2000),
                            track.ptA.northing - (Math.Cos(heading) * 2000));
                        pointB = new vec2(
                            track.ptA.easting + (Math.Sin(heading) * 2000),
                            track.ptA.northing + (Math.Cos(heading) * 2000));
                    }

                    GL.Begin(PrimitiveType.Lines);
                    GL.Vertex3(pointA.easting, pointA.northing, 0);
                    GL.Vertex3(pointB.easting, pointB.northing, 0);
                    GL.End();
                }
                else if (track.curvePts != null && track.curvePts.Count > 1)
                {
                    GL.Begin(PrimitiveType.LineStrip);
                    foreach (vec3 point in track.curvePts)
                    {
                        GL.Vertex3(point.easting, point.northing, 0);
                    }
                    GL.End();
                }
            }
        }

        private void DrawExistingTramLines()
        {
            if (mf.tram == null) return;

            GL.LineWidth(7);
            GL.Color3(0.0f, 0.0f, 0.0f);
            DrawTramLinesPrimitive();

            GL.LineWidth(3);
            GL.Color3(1.0f, 0.55f, 0.8f);
            DrawTramLinesPrimitive();
        }

        private void DrawTramLinesPrimitive()
        {
            if (mf.tram.tramList != null)
            {
                foreach (List<vec2> tramLine in mf.tram.tramList)
                {
                    if (tramLine == null || tramLine.Count < 2) continue;

                    GL.Begin(PrimitiveType.LineStrip);
                    foreach (vec2 point in tramLine)
                    {
                        GL.Vertex3(point.easting, point.northing, 0);
                    }
                    GL.End();
                }
            }

            DrawVec2LineLoop(mf.tram.tramBndOuterArr);
            DrawVec2LineLoop(mf.tram.tramBndInnerArr);
        }

        private void DrawVec2LineLoop(List<vec2> line)
        {
            if (line == null || line.Count < 2) return;

            GL.Begin(PrimitiveType.LineLoop);
            foreach (vec2 point in line)
            {
                GL.Vertex3(point.easting, point.northing, 0);
            }
            GL.End();
        }

        private void DrawExistingRecordedPath()
        {
            if (mf.recPath?.recList == null || mf.recPath.recList.Count < 2) return;

            GL.LineWidth(7);
            GL.Color3(0.0f, 0.0f, 0.0f);
            DrawRecordedPathPrimitive();

            GL.LineWidth(3);
            GL.Color3(1.0f, 0.92f, 0.2f);
            DrawRecordedPathPrimitive();
        }

        private void DrawRecordedPathPrimitive()
        {
            GL.Begin(PrimitiveType.LineStrip);
            foreach (CRecPathPt point in mf.recPath.recList)
            {
                GL.Vertex3(point.easting, point.northing, 0);
            }
            GL.End();
        }

        private void DrawExistingFlags()
        {
            if (mf.flagPts == null || mf.flagPts.Count == 0) return;

            GL.PointSize(16);
            GL.Begin(PrimitiveType.Points);

            foreach (CFlag flag in mf.flagPts)
            {
                GL.Color3(1.0f, 0.2f, 0.2f);
                GL.Vertex3(flag.easting, flag.northing, 0);
            }

            GL.End();
        }

        private void DrawHydLiftPreviewLine()
        {
            if (!isHydLiftLineStartSet || !cboxHydLiftLine.Checked) return;

            Point clientPoint = oglSelf.PointToClient(Cursor.Position);
            if (clientPoint.X < 0 || clientPoint.Y < 0 || clientPoint.X > oglSelf.Width || clientPoint.Y > oglSelf.Height) return;

            vec3 previewPoint = ScreenToFieldPoint(clientPoint);

            GL.LineWidth(12);
            GL.Color3(0.0f, 0.0f, 0.0f);
            DrawTwoPointLine(hydLiftLineStartPoint, previewPoint);

            GL.LineWidth(7);
            GL.Color3(0.05f, 0.85f, 1.0f);
            DrawTwoPointLine(hydLiftLineStartPoint, previewPoint);
        }

        private void DrawTwoPointLine(vec3 startPoint, vec3 endPoint)
        {
            GL.Begin(PrimitiveType.Lines);
            GL.Vertex3(startPoint.easting, startPoint.northing, 0);
            GL.Vertex3(endPoint.easting, endPoint.northing, 0);
            GL.End();
        }

        private void DrawHeadPathWorkingLine(CHeadPath headPath)
        {
            int startIndex = GetHeadPathWorkingStartIndex(headPath);
            int endIndex = GetHeadPathWorkingEndIndex(headPath);

            GL.Begin(PrimitiveType.LineStrip);
            for (int i = startIndex; i <= endIndex; i++)
            {
                vec3 item = headPath.trackPts[i];
                GL.Vertex3(item.easting, item.northing, 0);
            }
            GL.End();
        }

        private int GetHeadPathWorkingStartIndex(CHeadPath headPath)
        {
            if (headPath.lineStartIndex >= 0 && headPath.lineStartIndex < headPath.trackPts.Count) return headPath.lineStartIndex;
            return 0;
        }

        private int GetHeadPathWorkingEndIndex(CHeadPath headPath)
        {
            if (headPath.lineEndIndex >= 0 && headPath.lineEndIndex < headPath.trackPts.Count) return headPath.lineEndIndex;
            return headPath.trackPts.Count - 1;
        }

        private vec3 ScreenToFieldPoint(Point clientPoint)
        {
            int wid = oglSelf.Width;
            int halfWid = oglSelf.Width / 2;
            double scale = (double)wid * 0.903;

            Point point = new Point
            {
                X = clientPoint.X - halfWid,
                Y = wid - clientPoint.Y - halfWid
            };

            vec3 plotPt = new vec3
            {
                easting = point.X * mf.maxFieldDistance / scale * zoom,
                northing = point.Y * mf.maxFieldDistance / scale * zoom,
                heading = 0
            };

            plotPt.easting += mf.fieldCenterX + mf.maxFieldDistance * -sX;
            plotPt.northing += mf.fieldCenterY + mf.maxFieldDistance * -sY;

            return plotPt;
        }

        private void DrawBuiltLines()
        {
            if (isLinesVisible && mf.hdl.tracksArr.Count > 0)
            {
                //GL.Enable(EnableCap.LineStipple);
                GL.LineStipple(1, 0x7070);
                GL.PointSize(3);

                for (int i = 0; i < mf.hdl.tracksArr.Count; i++)
                {
                    if (mf.hdl.tracksArr[i].IsHydLiftLine) continue;

                    if (mf.hdl.tracksArr[i].mode == (int)TrackMode.AB)
                    {
                        GL.Color3(0.973f, 0.9f, 0.10f);
                    }
                    else
                    {
                        GL.Color3(0.3f, 0.99f, 0.20f);
                    }

                    GL.LineWidth(mf.hdl.tracksArr[i].IsHydLiftLine ? 5 : 3);
                    GL.Begin(PrimitiveType.LineStrip);
                    foreach (vec3 item in mf.hdl.tracksArr[i].trackPts)
                    {
                        GL.Vertex3(item.easting, item.northing, 0);
                    }
                    GL.End();
                }

                //GL.Disable(EnableCap.LineStipple);

                if (mf.hdl.idx > -1 && !mf.hdl.tracksArr[mf.hdl.idx].IsHydLiftLine)
                {
                    GL.LineWidth(6);
                    GL.Color3(1.0f, 0.0f, 1.0f);

                    GL.Begin(PrimitiveType.LineStrip);
                    foreach (vec3 item in mf.hdl.tracksArr[mf.hdl.idx].trackPts)
                    {
                        GL.Vertex3(item.easting, item.northing, 0);
                    }
                    GL.End();

                    int cnt = mf.hdl.tracksArr[mf.hdl.idx].trackPts.Count - 1;
                    GL.PointSize(28);
                    GL.Color3(0, 0, 0);
                    GL.Begin(PrimitiveType.Points);
                    GL.Vertex3(mf.hdl.tracksArr[mf.hdl.idx].trackPts[0].easting, mf.hdl.tracksArr[mf.hdl.idx].trackPts[0].northing, 0);
                    GL.Color3(0, 0, 0);
                    GL.Vertex3(mf.hdl.tracksArr[mf.hdl.idx].trackPts[cnt].easting, mf.hdl.tracksArr[mf.hdl.idx].trackPts[cnt].northing, 0);
                    GL.End();

                    GL.PointSize(20);
                    GL.Color3(1.0f, 0.7f, 0.35f);
                    GL.Begin(PrimitiveType.Points);
                    GL.Vertex3(mf.hdl.tracksArr[mf.hdl.idx].trackPts[0].easting, mf.hdl.tracksArr[mf.hdl.idx].trackPts[0].northing, 0);
                    GL.Color3(0.6f, 0.75f, 0.99f);
                    GL.Vertex3(mf.hdl.tracksArr[mf.hdl.idx].trackPts[cnt].easting, mf.hdl.tracksArr[mf.hdl.idx].trackPts[cnt].northing, 0);
                    GL.End();
                }
            }

            GL.LineWidth(8);
            GL.Color3(0.93f, 0.899f, 0.50f);
            GL.Begin(PrimitiveType.LineStrip);

            for (int i = 0; i < mf.bnd.bndList[0].hdLine.Count; i++)
            {
                GL.Vertex3(mf.bnd.bndList[0].hdLine[i].easting, mf.bnd.bndList[0].hdLine[i].northing, 0);
            }
            GL.End();
        }

        private void DrawABTouchLine()
        {
            GL.Color3(0.65, 0.650, 0.0);
            GL.PointSize(24);
            GL.Begin(PrimitiveType.Points);

            GL.Color3(0, 0, 0);
            if (isHydLiftLineStartSet) GL.Vertex3(hydLiftLineStartPoint.easting, hydLiftLineStartPoint.northing, 0);
            if (start != 99999) GL.Vertex3(mf.bnd.bndList[bndSelect].fenceLine[start].easting, mf.bnd.bndList[bndSelect].fenceLine[start].northing, 0);
            if (end != 99999) GL.Vertex3(mf.bnd.bndList[bndSelect].fenceLine[end].easting, mf.bnd.bndList[bndSelect].fenceLine[end].northing, 0);
            GL.End();

            GL.PointSize(16);
            GL.Begin(PrimitiveType.Points);

            GL.Color3(1.0f, 0.75f, 0.350f);
            if (isHydLiftLineStartSet) GL.Vertex3(hydLiftLineStartPoint.easting, hydLiftLineStartPoint.northing, 0);
            if (start != 99999) GL.Vertex3(mf.bnd.bndList[bndSelect].fenceLine[start].easting, mf.bnd.bndList[bndSelect].fenceLine[start].northing, 0);

            GL.Color3(0.5f, 0.75f, 1.0f);
            if (end != 99999) GL.Vertex3(mf.bnd.bndList[bndSelect].fenceLine[end].easting, mf.bnd.bndList[bndSelect].fenceLine[end].northing, 0);
            GL.End();
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            oglSelf.Refresh();
        }

        private void btnExit_Click(object sender, EventArgs e)
        {
            mf.FileSaveHeadLines();
            //does headland control sections
            mf.bnd.isSectionControlledByHeadland = cboxIsSectionControlled.Checked;
            Properties.ToolSettings.Default.setHeadland_isSectionControlled = cboxIsSectionControlled.Checked;
            Properties.ToolSettings.Default.Save();

            Close();
        }

        private void nudSetDistance_Click(object sender, EventArgs e)
        {
            ((NudlessNumericUpDown)sender).ShowKeypad(this);
            btnExit.Focus();
        }

        // Returns 1 if the lines intersect, otherwis
        public double iE = 0, iN = 0;

        public List<int> crossings = new List<int>(1);

        private void btnDeleteHeadland_Click(object sender, EventArgs e)
        {
            start = 99999; end = 99999;
            isA = true;
            mf.hdl.desList?.Clear();
            mf.bnd.bndList[0].hdLine?.Clear();

            //int ptCount = mf.bnd.bndList[0].fenceLine.Count;

            //for (int i = 0; i < ptCount; i++)
            //{
            //    mf.bnd.bndList[0].hdLine.Add(new vec3(mf.bnd.bndList[0].fenceLine[i]));
            //}
        }

        private void btnBndLoop_Click(object sender, EventArgs e)
        {
            //sort the lines
            mf.hdl.tracksArr.Sort((p, q) =>
            {
                if (p.IsHydLiftLine != q.IsHydLiftLine) return p.IsHydLiftLine ? 1 : -1;
                return p.a_point.CompareTo(q.a_point);
            });
            mf.FileSaveHeadLines();

            mf.hdl.idx = -1;

            //build the headland
            mf.bnd.bndList[0].hdLine?.Clear();

            List<CHeadPath> headlandTracks = mf.hdl.tracksArr.FindAll(headPath => !headPath.IsHydLiftLine);

            if (headlandTracks.Count == 2 && TryBuildTwoEndHeadland(headlandTracks))
            {
                FinishBuiltHeadland();
                return;
            }

            int numOfLines = headlandTracks.Count;
            int nextLine = 0;
            crossings.Clear();

            int isStart = 0;

            for (int lineNum = 0; lineNum < headlandTracks.Count; lineNum++)
            {
                nextLine = lineNum - 1;
                if (nextLine < 0) nextLine = headlandTracks.Count - 1;

                if (nextLine == lineNum)
                {
                    FormDialog.Show("Create Error", "Is there maybe only 1 line?", DialogSeverity.Error);
                    Log.EventWriter("Headache, Only 1 Line");

                    return;
                }

                for (int i = 0; i < headlandTracks[lineNum].trackPts.Count - 2; i++)
                {
                    GeoLineSegment headPathSegment = headlandTracks[lineNum].GetHeadPathSegment(i);
                    for (int k = 0; k < headlandTracks[nextLine].trackPts.Count - 2; k++)
                    {
                        GeoLineSegment otherSegment = headlandTracks[nextLine].GetHeadPathSegment(k);
                        GeoCoord? intersectionPoint = headPathSegment.IntersectionPoint(otherSegment);
                        if (intersectionPoint.HasValue)
                        {
                            if (isStart == 0) i++;
                            crossings.Add(i);
                            isStart++;
                            if (isStart == 2) goto again;
                            nextLine = lineNum + 1;

                            if (nextLine > headlandTracks.Count - 1) nextLine = 0;
                        }
                    }
                }

            again:
                isStart = 0;
            }

            if (crossings.Count != headlandTracks.Count * 2)
            {
                FormDialog.Show("Crossings Error", "Make sure all ends cross and only once", DialogSeverity.Error);
                Log.EventWriter("Headache, All ends cross and only once");
                mf.bnd.bndList[0].hdLine?.Clear();
                return;
            }

            for (int i = 0; i < headlandTracks.Count; i++)
            {
                int low = crossings[i * 2];
                int high = crossings[i * 2 + 1];
                for (int k = low; k < high; k++)
                {
                    mf.bnd.bndList[0].hdLine.Add(headlandTracks[i].trackPts[k]);
                }
            }

            //for (int i = 0; i < mf.hdl.tracksArr.Count; i++)
            //{
            //    mf.hdl.desList?.Clear();

            //    int low = crossings[i * 2];
            //    int high = crossings[i * 2 + 1];
            //    for (int k = low; k < high; k++)
            //    {
            //        mf.hdl.desList.Add(mf.hdl.tracksArr[i].trackPts[k]);
            //    }

            //    mf.hdl.tracksArr[i].trackPts?.Clear();

            //    foreach (var item in mf.hdl.desList)
            //    {
            //        mf.hdl.tracksArr[i].trackPts.Add(new vec3(item));
            //    }
            //}

            FinishBuiltHeadland();
        }

        private bool TryBuildTwoEndHeadland(List<CHeadPath> headlandTracks)
        {
            if (mf.bnd.bndList.Count == 0 || mf.bnd.bndList[0].fenceLine.Count < 4) return false;

            List<vec3> fenceLine = mf.bnd.bndList[0].fenceLine;
            CHeadPath firstLine = headlandTracks[0];
            CHeadPath secondLine = headlandTracks[1];

            if (!IsValidBoundaryIndex(firstLine.a_point, fenceLine.Count) ||
                !IsValidBoundaryIndex(firstLine.b_point, fenceLine.Count) ||
                !IsValidBoundaryIndex(secondLine.a_point, fenceLine.Count) ||
                !IsValidBoundaryIndex(secondLine.b_point, fenceLine.Count) ||
                firstLine.a_point == firstLine.b_point ||
                secondLine.a_point == secondLine.b_point ||
                firstLine.trackPts.Count < 2 ||
                secondLine.trackPts.Count < 2)
            {
                return false;
            }

            vec3 firstStart = GetHeadPathPoint(firstLine, true, fenceLine[firstLine.a_point]);
            vec3 firstEnd = GetHeadPathPoint(firstLine, false, fenceLine[firstLine.b_point]);
            vec3 secondStart = GetHeadPathPoint(secondLine, true, fenceLine[secondLine.a_point]);
            vec3 secondEnd = GetHeadPathPoint(secondLine, false, fenceLine[secondLine.b_point]);

            int firstSideStart = FindClosestBoundaryIndexOnSegment(firstLine.b_point, secondLine.a_point, firstEnd, fenceLine);
            int firstSideEnd = FindClosestBoundaryIndexOnSegment(firstLine.b_point, secondLine.a_point, secondStart, fenceLine);
            int secondSideStart = FindClosestBoundaryIndexOnSegment(secondLine.b_point, firstLine.a_point, secondEnd, fenceLine);
            int secondSideEnd = FindClosestBoundaryIndexOnSegment(secondLine.b_point, firstLine.a_point, firstStart, fenceLine);

            AddHeadlandPoint(firstStart);
            AddHeadlandPoint(firstEnd);
            AddBoundarySegment(firstSideStart, firstSideEnd, fenceLine);
            AddHeadlandPoint(secondStart);
            AddHeadlandPoint(secondEnd);
            AddBoundarySegment(secondSideStart, secondSideEnd, fenceLine);

            return mf.bnd.bndList[0].hdLine.Count > 3;
        }

        private int FindNearestBoundarySnapIndex(List<vec3> fenceLine, vec3 clickPoint)
        {
            if (fenceLine == null || fenceLine.Count == 0) return -1;

            int nearestIndex = 0;
            double nearestDistance = double.MaxValue;

            for (int i = 0; i < fenceLine.Count; i++)
            {
                double distance = GetDistanceSquared(clickPoint, fenceLine[i]);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestIndex = i;
                }
            }

            int nearestCornerIndex = -1;
            double nearestCornerDistance = double.MaxValue;
            const double cornerAngle = 0.45;

            for (int i = 0; i < fenceLine.Count; i++)
            {
                int previousIndex = NormalizeBoundaryIndex(i - 4, fenceLine.Count);
                int nextIndex = NormalizeBoundaryIndex(i + 4, fenceLine.Count);

                double firstEasting = fenceLine[i].easting - fenceLine[previousIndex].easting;
                double firstNorthing = fenceLine[i].northing - fenceLine[previousIndex].northing;
                double secondEasting = fenceLine[nextIndex].easting - fenceLine[i].easting;
                double secondNorthing = fenceLine[nextIndex].northing - fenceLine[i].northing;

                double firstLength = Math.Sqrt((firstEasting * firstEasting) + (firstNorthing * firstNorthing));
                double secondLength = Math.Sqrt((secondEasting * secondEasting) + (secondNorthing * secondNorthing));
                if (firstLength < 0.1 || secondLength < 0.1) continue;

                double cross = (firstEasting * secondNorthing) - (firstNorthing * secondEasting);
                double dot = (firstEasting * secondEasting) + (firstNorthing * secondNorthing);
                double angle = Math.Abs(Math.Atan2(cross, dot));
                if (angle < cornerAngle) continue;

                double distance = GetDistanceSquared(clickPoint, fenceLine[i]);
                if (distance < nearestCornerDistance)
                {
                    nearestCornerDistance = distance;
                    nearestCornerIndex = i;
                }
            }

            const double cornerSnapDistance = 20;
            if (nearestCornerIndex > -1 && nearestCornerDistance <= cornerSnapDistance * cornerSnapDistance)
            {
                return nearestCornerIndex;
            }

            return nearestIndex;
        }

        private double GetDistanceSquared(vec3 firstPoint, vec3 secondPoint)
        {
            return ((firstPoint.easting - secondPoint.easting) * (firstPoint.easting - secondPoint.easting)) +
                ((firstPoint.northing - secondPoint.northing) * (firstPoint.northing - secondPoint.northing));
        }

        private vec3 GetHeadPathPoint(CHeadPath headPath, bool isStart, vec3 boundaryPoint)
        {
            int index = isStart
                ? (IsValidTrackIndex(headPath.lineStartIndex, headPath.trackPts.Count) ? headPath.lineStartIndex : FindClosestTrackPoint(headPath.trackPts, boundaryPoint))
                : (IsValidTrackIndex(headPath.lineEndIndex, headPath.trackPts.Count) ? headPath.lineEndIndex : FindClosestTrackPoint(headPath.trackPts, boundaryPoint));

            return headPath.trackPts[index];
        }

        private int FindClosestBoundaryIndexOnSegment(int startIndex, int endIndex, vec3 point, List<vec3> fenceLine)
        {
            int fenceCount = fenceLine.Count;
            int index = NormalizeBoundaryIndex(startIndex, fenceCount);
            int endFenceIndex = NormalizeBoundaryIndex(endIndex, fenceCount);
            int closestIndex = index;
            double closestDistance = double.MaxValue;

            while (true)
            {
                double distance = GetDistanceSquared(point, fenceLine[index]);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestIndex = index;
                }

                if (index == endFenceIndex) break;

                index++;
                if (index >= fenceCount) index = 0;
            }

            return closestIndex;
        }

        private void AddHeadPathBetweenBoundaryEnds(CHeadPath headPath, vec3 startBoundary, vec3 endBoundary)
        {
            int startIndex = IsValidTrackIndex(headPath.lineStartIndex, headPath.trackPts.Count)
                ? headPath.lineStartIndex
                : FindClosestTrackPoint(headPath.trackPts, startBoundary);

            int endIndex = IsValidTrackIndex(headPath.lineEndIndex, headPath.trackPts.Count)
                ? headPath.lineEndIndex
                : FindClosestTrackPoint(headPath.trackPts, endBoundary);

            AddHeadlandPoint(headPath.trackPts[startIndex]);
            AddHeadlandPoint(headPath.trackPts[endIndex]);
        }

        private void AddBoundarySegment(int startIndex, int endIndex, List<vec3> fenceLine)
        {
            int fenceCount = fenceLine.Count;
            int index = NormalizeBoundaryIndex(startIndex, fenceCount);
            int endFenceIndex = NormalizeBoundaryIndex(endIndex, fenceCount);

            while (true)
            {
                AddHeadlandPoint(fenceLine[index]);
                if (index == endFenceIndex) break;

                index++;
                if (index >= fenceCount) index = 0;
            }
        }

        private int FindClosestTrackPoint(List<vec3> trackPts, vec3 boundaryPoint)
        {
            int closestIndex = 0;
            double closestDistance = double.MaxValue;

            for (int i = 0; i < trackPts.Count; i++)
            {
                double distance =
                    ((trackPts[i].easting - boundaryPoint.easting) * (trackPts[i].easting - boundaryPoint.easting)) +
                    ((trackPts[i].northing - boundaryPoint.northing) * (trackPts[i].northing - boundaryPoint.northing));

                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestIndex = i;
                }
            }

            return closestIndex;
        }

        private void AddHeadlandPoint(vec3 point)
        {
            List<vec3> hdLine = mf.bnd.bndList[0].hdLine;
            if (hdLine.Count > 0)
            {
                vec3 lastPoint = hdLine[hdLine.Count - 1];
                double distance =
                    ((lastPoint.easting - point.easting) * (lastPoint.easting - point.easting)) +
                    ((lastPoint.northing - point.northing) * (lastPoint.northing - point.northing));

                if (distance < 0.01) return;
            }

            hdLine.Add(new vec3(point));
        }

        private bool IsValidBoundaryIndex(int index, int count)
        {
            return index >= 0 && index < count;
        }

        private bool IsValidTrackIndex(int index, int count)
        {
            return index >= 0 && index < count;
        }

        private int NormalizeBoundaryIndex(int index, int count)
        {
            if (count == 0) return 0;
            index %= count;
            if (index < 0) index += count;
            return index;
        }

        private void FinishBuiltHeadland()
        {
            vec3[] hdArr;

            if (mf.bnd.bndList[0].hdLine.Count > 0)
            {
                hdArr = new vec3[mf.bnd.bndList[0].hdLine.Count];
                mf.bnd.bndList[0].hdLine.CopyTo(hdArr);
                mf.bnd.bndList[0].hdLine?.Clear();
            }
            else
            {
                mf.bnd.bndList[0].hdLine?.Clear();
                return;
            }

            //middle points
            for (int i = 1; i < hdArr.Length; i++)
            {
                hdArr[i - 1].heading = Math.Atan2(hdArr[i - 1].easting - hdArr[i].easting, hdArr[i - 1].northing - hdArr[i].northing);
                if (hdArr[i].heading < 0) hdArr[i].heading += glm.twoPI;
            }

            double delta = 0;
            for (int i = 0; i < hdArr.Length; i++)
            {
                if (i == 0)
                {
                    mf.bnd.bndList[0].hdLine.Add(new vec3(hdArr[i].easting, hdArr[i].northing, hdArr[i].heading));
                    continue;
                }
                delta += (hdArr[i - 1].heading - hdArr[i].heading);

                if (Math.Abs(delta) > 0.005)
                {
                    vec3 pt = new vec3(hdArr[i].easting, hdArr[i].northing, hdArr[i].heading);

                    mf.bnd.bndList[0].hdLine.Add(pt);
                    delta = 0;
                }
            }

            mf.FileSaveHeadland();
        }

        private void cboxToolWidths_SelectedIndexChanged(object sender, EventArgs e)
        {
            nudSetDistance.Value = (decimal)((Math.Round((mf.tool.width - mf.tool.overlap) * cboxToolWidths.SelectedIndex, 1)) * mf.m2FtOrM);
        }

        private void btnHeadlandOff_Click(object sender, EventArgs e)
        {
            mf.bnd.bndList[0].hdLine?.Clear();
            mf.FileSaveHeadland();
            mf.bnd.isHeadlandOn = false;
            mf.vehicle.isHydLiftOn = false;
            Close();
        }

        private void btnBLength_Click(object sender, EventArgs e)
        {
            if (mf.hdl.idx > -1)
            {
                int ptCnt = mf.hdl.tracksArr[mf.hdl.idx].trackPts.Count - 1;

                for (int i = 1; i < 10; i++)
                {
                    vec3 pt = new vec3(mf.hdl.tracksArr[mf.hdl.idx].trackPts[ptCnt]);
                    pt.easting += (Math.Sin(pt.heading) * i);
                    pt.northing += (Math.Cos(pt.heading) * i);
                    mf.hdl.tracksArr[mf.hdl.idx].trackPts.Add(pt);
                }
            }
        }

        private void btnBShrink_Click(object sender, EventArgs e)
        {
            if (mf.hdl.idx > -1)
            {
                if (mf.hdl.tracksArr[mf.hdl.idx].trackPts.Count > 8)
                    mf.hdl.tracksArr[mf.hdl.idx].trackPts.RemoveRange(mf.hdl.tracksArr[mf.hdl.idx].trackPts.Count - 5, 5);
            }
        }

        private void btnALength_Click(object sender, EventArgs e)
        {
            if (mf.hdl.idx > -1)
            {
                //and the beginning
                vec3 start = new vec3(mf.hdl.tracksArr[mf.hdl.idx].trackPts[0]);

                for (int i = 1; i < 10; i++)
                {
                    vec3 pt = new vec3(start);
                    pt.easting -= (Math.Sin(pt.heading) * i);
                    pt.northing -= (Math.Cos(pt.heading) * i);
                    mf.hdl.tracksArr[mf.hdl.idx].trackPts.Insert(0, pt);
                }
            }
        }

        private void btnAShrink_Click(object sender, EventArgs e)
        {
            if (mf.hdl.idx > -1)
            {
                if (mf.hdl.tracksArr[mf.hdl.idx].trackPts.Count > 8)
                    mf.hdl.tracksArr[mf.hdl.idx].trackPts.RemoveRange(0, 5);
            }
        }

        private void btnCancelTouch_Click(object sender, EventArgs e)
        {
            //update the arrays
            start = 99999; end = 99999;
            isA = true;
            isHydLiftLineStartSet = false;
            FixLabelsCurve();
            mf.curve.desList?.Clear();
            zoom = 1;
            sX = 0;
            sY = 0;
            zoomToggle = false;
            btnExit.Focus();
        }

        private void cboxIsSectionControlled_Click(object sender, EventArgs e)
        {
            if (cboxIsSectionControlled.Checked) cboxIsSectionControlled.Image = Properties.Resources.HeadlandSectionOn;
            else cboxIsSectionControlled.Image = Properties.Resources.HeadlandSectionOff;
        }

        private void cboxIsZoom_CheckedChanged(object sender, EventArgs e)
        {
            zoomToggle = false;
        }

        private void oglSelf_Load(object sender, EventArgs e)
        {
            oglSelf.MakeCurrent();
            GL.Enable(EnableCap.CullFace);
            GL.CullFace(CullFaceMode.Back);
            GL.ClearColor(0.22f, 0.22f, 0.22f, 1.0f);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        }
    }
}
