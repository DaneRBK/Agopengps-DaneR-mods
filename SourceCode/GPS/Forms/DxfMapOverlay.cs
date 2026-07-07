using AgLibrary.Logging;
using AgOpenGPS.Core.Models;
using AgOpenGPS.Core.Translations;
using AgOpenGPS.Forms;
using AgOpenGPS.IO;
using OpenTK.Graphics.OpenGL;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace AgOpenGPS
{
    public partial class FormGPS
    {
        private const int DxfDefaultUtmZone = 34;
        private const double DxfMinimumParcelAreaSqm = 100.0;
        private readonly List<DxfMapPolygon> dxfMapPolygons = new List<DxfMapPolygon>();
        private readonly List<DxfMapLine> dxfMapLines = new List<DxfMapLine>();
        private readonly List<DxfUtmPoint> dxfManualFieldPoints = new List<DxfUtmPoint>();
        private bool isDxfMapOn;
        private string dxfMapFilePath = string.Empty;
        private int dxfMapUtmZone = DxfDefaultUtmZone;
        private Wgs84 dxfMapLocalPlaneOrigin = new Wgs84(999, 999);
        private bool hasDxfSavedCamera;
        private double dxfSavedPanX;
        private double dxfSavedPanY;
        private double dxfSavedDistanceToLookAt;

        private void ToggleDxfMapTool()
        {
            if (isDxfMapOn)
            {
                ClearDxfMap();
                Form openForm = Application.OpenForms["FormDxfMapTool"];
                openForm?.Close();
                TimedMessageBox(1500, "DXF Map", "DXF map OFF");
                return;
            }

            ShowDxfMapTool();
        }

        private void ShowDxfMapTool()
        {
            Form openForm = Application.OpenForms["FormDxfMapTool"];
            if (openForm != null)
            {
                openForm.Focus();
                return;
            }

            FormDxfMapTool form = new FormDxfMapTool(this);
            form.Show(this);
        }

        public void ShowDxfMapPreviewWindow(bool startFourPointMode = false)
        {
            Form openForm = Application.OpenForms["FormDxfMapPreview"];
            if (openForm != null)
            {
                if (startFourPointMode && openForm is FormDxfMapPreview preview)
                {
                    preview.StartFourPointMode();
                }

                openForm.Focus();
                return;
            }

            FormDxfMapPreview form = new FormDxfMapPreview(this);
            if (startFourPointMode)
            {
                form.StartFourPointMode();
            }

            form.Show(this);
        }

        public string GetLatestDownloadsDxfFile()
        {
            string downloads = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads");

            if (!Directory.Exists(downloads)) return string.Empty;

            FileInfo file = new DirectoryInfo(downloads)
                .GetFiles("*.dxf")
                .OrderByDescending(f => f.LastWriteTime)
                .FirstOrDefault();

            return file?.FullName ?? string.Empty;
        }

        public DxfMapLoadResult LoadDxfMap(string filePath, int utmZone)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return new DxfMapLoadResult(false, "DXF file not found.");
            }

            try
            {
                dxfMapUtmZone = utmZone > 0 ? utmZone : GetCurrentUtmZone();
                dxfMapFilePath = filePath;
                dxfMapPolygons.Clear();
                dxfMapLines.Clear();

                DxfSourceGeometry sourceGeometry = DxfMapReader.LoadGeometry(filePath);
                foreach (DxfSourcePolygon source in sourceGeometry.Polygons)
                {
                    double area = Math.Abs(SignedArea(source.Points));
                    if (area < DxfMinimumParcelAreaSqm) continue;
                    if (!LooksLikeUtm(source.Points)) continue;

                    DxfMapPolygon polygon = BuildDxfMapPolygon(source);
                    if (polygon.UtmPoints.Count < 3) continue;

                    dxfMapPolygons.Add(polygon);
                }

                foreach (DxfSourceLine sourceLine in sourceGeometry.Lines)
                {
                    if (!LooksLikeUtm(sourceLine.Start) || !LooksLikeUtm(sourceLine.End)) continue;
                    dxfMapLines.Add(new DxfMapLine(sourceLine.Start, sourceLine.End, sourceLine.Layer));
                }

                RefreshDxfMapLocalPoints(force: true);

                isDxfMapOn = dxfMapPolygons.Count > 0 || dxfMapLines.Count > 0;
                UpdateDxfMapButtonAppearance();
                oglMain?.Refresh();
                oglZoom?.Refresh();

                string message = isDxfMapOn
                    ? "Loaded " + dxfMapPolygons.Count.ToString(CultureInfo.CurrentCulture) + " closed boundaries and "
                        + dxfMapLines.Count.ToString(CultureInfo.CurrentCulture) + " DXF lines. " + GetDxfMapDistanceStatus()
                    : "No UTM field lines found.";

                TimedMessageBox(2000, "DXF Map", message);
                return new DxfMapLoadResult(isDxfMapOn, message);
            }
            catch (Exception ex)
            {
                Log.EventWriter("DXF map load failed: " + ex);
                return new DxfMapLoadResult(false, ex.Message);
            }
        }

        public void ClearDxfMap()
        {
            dxfMapPolygons.Clear();
            dxfMapLines.Clear();
            dxfManualFieldPoints.Clear();
            isDxfMapOn = false;
            dxfMapFilePath = string.Empty;
            dxfMapLocalPlaneOrigin = new Wgs84(999, 999);
            RestoreDxfCameraIfNeeded();
            UpdateDxfMapButtonAppearance();
            oglMain?.Refresh();
            oglZoom?.Refresh();
        }

        public DxfMapCreateFieldResult CreateFieldFromDxfAtTractor()
        {
            if (isJobStarted)
            {
                return new DxfMapCreateFieldResult(false, "Close current field first.");
            }

            if (dxfMapPolygons.Count == 0)
            {
                return new DxfMapCreateFieldResult(false, "No closed DXF boundary at tractor. Lines are visible, but field creation needs a closed boundary.");
            }

            DxfUtmPoint tractor = Wgs84ToUtm(AppModel.CurrentLatLon, dxfMapUtmZone);
            DxfMapPolygon selected = dxfMapPolygons
                .Where(p => IsPointInsideDxfPolygon(tractor, p.UtmPoints))
                .OrderBy(p => Math.Abs(SignedArea(p.UtmPoints)))
                .FirstOrDefault();

            if (selected == null)
            {
                return new DxfMapCreateFieldResult(false, "Tractor is not inside a DXF field boundary.");
            }

            return CreateFieldFromDxfBoundary(selected.UtmPoints, BuildDxfFieldName());
        }

        public int DxfManualPointCount => dxfManualFieldPoints.Count;

        public void ClearDxfManualFieldPoints()
        {
            dxfManualFieldPoints.Clear();
            Form preview = Application.OpenForms["FormDxfMapPreview"];
            preview?.Invalidate();
        }

        public void UndoDxfManualFieldPoint()
        {
            if (dxfManualFieldPoints.Count == 0) return;
            dxfManualFieldPoints.RemoveAt(dxfManualFieldPoints.Count - 1);
            Form preview = Application.OpenForms["FormDxfMapPreview"];
            preview?.Invalidate();
        }

        public bool TryAddDxfManualPointFromPreview(Point previewPoint, Rectangle previewBounds, double previewZoomMultiplier, double previewPanX, double previewPanY, out string message)
        {
            message = string.Empty;

            if (!isDxfMapOn || (dxfMapPolygons.Count == 0 && dxfMapLines.Count == 0))
            {
                message = "Load DXF map first.";
                return false;
            }

            if (dxfManualFieldPoints.Count >= 4)
            {
                message = "4 points already selected. Save or clear first.";
                return false;
            }

            if (!TryGetDxfPreviewTransform(previewBounds, previewZoomMultiplier, previewPanX, previewPanY, out Rectangle mapBounds, out DxfUtmPoint tractor, out double centerX, out double centerY, out double scale, out _, out _, out _, out _))
            {
                message = "DXF map cannot be projected.";
                return false;
            }

            if (!mapBounds.Contains(previewPoint))
            {
                message = "Click inside the map.";
                return false;
            }

            DxfUtmPoint clickUtm = new DxfUtmPoint(
                tractor.Easting + ((previewPoint.X - centerX) / scale),
                tractor.Northing - ((previewPoint.Y - centerY) / scale));

            double pickToleranceMeters = Math.Max(1.0, 28.0 / scale);
            if (!TryFindNearestDxfCorner(clickUtm, pickToleranceMeters, out DxfUtmPoint corner))
            {
                message = "No DXF line near click. Click closer to field line.";
                return false;
            }

            if (dxfManualFieldPoints.Any(p => Math.Sqrt(DxfDistanceSquared(p, corner)) < 0.25))
            {
                message = "That corner is already selected.";
                return false;
            }

            dxfManualFieldPoints.Add(corner);
            message = "Point " + dxfManualFieldPoints.Count.ToString(CultureInfo.CurrentCulture) + "/4 selected.";
            Form preview = Application.OpenForms["FormDxfMapPreview"];
            preview?.Invalidate();
            return true;
        }

        public DxfMapCreateFieldResult CreateFieldFromDxfManualPoints(string fieldName)
        {
            if (dxfManualFieldPoints.Count != 4)
            {
                return new DxfMapCreateFieldResult(false, "Select 4 points first.");
            }

            return CreateFieldFromDxfBoundary(SortDxfPointsAroundCenter(dxfManualFieldPoints), fieldName, fixFenceLine: true);
        }

        private DxfMapCreateFieldResult CreateFieldFromDxfBoundary(List<DxfUtmPoint> boundaryPoints, string requestedFieldName, bool fixFenceLine = false)
        {
            if (isJobStarted)
            {
                return new DxfMapCreateFieldResult(false, "Close current field first.");
            }

            if (boundaryPoints == null || boundaryPoints.Count < 3)
            {
                return new DxfMapCreateFieldResult(false, "Boundary needs at least 3 points.");
            }

            string fieldName = SanitizeDxfFieldName(requestedFieldName);
            if (string.IsNullOrWhiteSpace(fieldName))
            {
                return new DxfMapCreateFieldResult(false, "Field name is empty.");
            }

            string fieldDirectory = Path.Combine(RegistrySettings.fieldsDirectory, fieldName);
            if (Directory.Exists(fieldDirectory))
            {
                return new DxfMapCreateFieldResult(false, "Field already exists: " + fieldName);
            }

            try
            {
                currentFieldDirectory = fieldName;
                pn.DefineLocalPlane(AppModel.CurrentLatLon, false);
                Directory.CreateDirectory(fieldDirectory);

                JobNew();
                FileCreateField();
                FileCreateSections();
                FileCreateRecPath();
                FileCreateContour();
                FileCreateElevation();
                FileSaveFlags();

                CBoundaryList boundary = new CBoundaryList();
                foreach (DxfUtmPoint point in boundaryPoints)
                {
                    Wgs84 wgs = UtmToWgs84(point, dxfMapUtmZone);
                    GeoCoord local = AppModel.LocalPlane.ConvertWgs84ToGeoCoord(wgs);
                    boundary.fenceLine.Add(new vec3(local.Easting, local.Northing, 0));
                }

                if (boundary.fenceLine.Count > 1)
                {
                    vec3 first = boundary.fenceLine[0];
                    vec3 last = boundary.fenceLine[boundary.fenceLine.Count - 1];
                    if (DxfDistanceSquared(first, last) < 0.01)
                    {
                        boundary.fenceLine.RemoveAt(boundary.fenceLine.Count - 1);
                    }
                }

                boundary.CalculateFenceArea(0, cleanIntersections: false);
                if (fixFenceLine)
                {
                    boundary.FixFenceLine(0);
                }
                bnd.bndList.Clear();
                bnd.bndList.Add(boundary);
                CalculateMinMax();
                bnd.BuildTurnLines();
                fd.UpdateFieldBoundaryGUIAreas();
                FileSaveBoundary();

                Properties.Settings.Default.setF_CurrentDir = currentFieldDirectory;
                Properties.Settings.Default.Save();
                isobus.SendFieldName(currentFieldDirectory);

                ClearDxfMap();

                PanelsAndOGLSize();
                SetButtons();
                SetZoom();
                oglMain?.Refresh();

                Log.EventWriter("DXF field created: " + currentFieldDirectory);
                return new DxfMapCreateFieldResult(true, "Created field: " + currentFieldDirectory);
            }
            catch (Exception ex)
            {
                Log.EventWriter("DXF field create failed: " + ex);
                return new DxfMapCreateFieldResult(false, ex.Message);
            }
        }

        public string GetDxfMapStatusText()
        {
            if (!isDxfMapOn)
            {
                return "No DXF map loaded.";
            }

            return Path.GetFileName(dxfMapFilePath) + " | zone " + dxfMapUtmZone.ToString(CultureInfo.CurrentCulture)
                + " | boundaries " + dxfMapPolygons.Count.ToString(CultureInfo.CurrentCulture)
                + " | lines " + dxfMapLines.Count.ToString(CultureInfo.CurrentCulture)
                + " | " + GetDxfMapDistanceStatus();
        }

        public void DrawDxfMapPreview(Graphics graphics, Rectangle bounds, double previewZoomMultiplier, double previewPanX = 0.0, double previewPanY = 0.0)
        {
            graphics.Clear(Color.FromArgb(248, 249, 250));
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            if (!isDxfMapOn || (dxfMapPolygons.Count == 0 && dxfMapLines.Count == 0))
            {
                using (Font font = new Font("Tahoma", 14F, FontStyle.Bold))
                using (Brush brush = new SolidBrush(Color.Black))
                {
                    graphics.DrawString("Load DXF map first.", font, brush, bounds.Left + 20, bounds.Top + 20);
                }
                return;
            }

            if (!TryGetDxfPreviewTransform(bounds, previewZoomMultiplier, previewPanX, previewPanY, out Rectangle mapBounds, out DxfUtmPoint tractor, out double centerX, out double centerY, out double scale, out double minEast, out double maxEast, out double minNorth, out double maxNorth))
            {
                return;
            }

            bool tractorInsideBounds = tractor.Easting >= minEast && tractor.Easting <= maxEast
                && tractor.Northing >= minNorth && tractor.Northing <= maxNorth;

            Func<DxfUtmPoint, PointF> mapPoint = point =>
            {
                float x = (float)(centerX + ((point.Easting - tractor.Easting) * scale));
                float y = (float)(centerY - ((point.Northing - tractor.Northing) * scale));
                return new PointF(x, y);
            };

            using (Pen linePen = new Pen(Color.FromArgb(40, 80, 120), 1.0f))
            using (Pen polygonPen = new Pen(Color.FromArgb(0, 160, 220), 1.2f))
            using (Pen manualPen = new Pen(Color.FromArgb(255, 132, 0), 4.0f))
            using (Pen tractorHaloPen = new Pen(Color.Yellow, 7f))
            using (Pen tractorPen = new Pen(Color.Red, 4f))
            using (Pen tractorCrossPen = new Pen(Color.Black, 2f))
            using (Brush tractorBrush = new SolidBrush(Color.Red))
            using (Brush manualBrush = new SolidBrush(Color.FromArgb(255, 132, 0)))
            using (Brush manualTextBrush = new SolidBrush(Color.White))
            using (Font labelFont = new Font("Tahoma", 10F, FontStyle.Bold))
            using (Brush labelBrush = new SolidBrush(Color.Black))
            {
                graphics.DrawRectangle(Pens.LightGray, mapBounds);

                foreach (DxfMapLine line in dxfMapLines)
                {
                    graphics.DrawLine(linePen, mapPoint(line.Start), mapPoint(line.End));
                }

                foreach (DxfMapPolygon polygon in dxfMapPolygons)
                {
                    if (polygon.UtmPoints.Count < 2) continue;
                    PointF[] points = polygon.UtmPoints.Select(mapPoint).ToArray();
                    graphics.DrawPolygon(polygonPen, points);
                }

                DrawDxfManualPreviewPoints(graphics, mapPoint, manualPen, manualBrush, manualTextBrush, labelFont);

                float r = 14f;
                PointF tractorPoint = new PointF((float)centerX, (float)centerY);

                graphics.DrawEllipse(tractorHaloPen, tractorPoint.X - r, tractorPoint.Y - r, r * 2, r * 2);
                graphics.FillEllipse(tractorBrush, tractorPoint.X - r, tractorPoint.Y - r, r * 2, r * 2);
                graphics.DrawEllipse(tractorPen, tractorPoint.X - r, tractorPoint.Y - r, r * 2, r * 2);
                graphics.DrawLine(tractorCrossPen, tractorPoint.X - r - 8, tractorPoint.Y, tractorPoint.X + r + 8, tractorPoint.Y);
                graphics.DrawLine(tractorCrossPen, tractorPoint.X, tractorPoint.Y - r - 8, tractorPoint.X, tractorPoint.Y + r + 8);

                string tractorLabel = "TRACTOR";
                SizeF labelSize = graphics.MeasureString(tractorLabel, labelFont);
                RectangleF labelRect = new RectangleF(tractorPoint.X + 18, tractorPoint.Y - 16, labelSize.Width + 8, labelSize.Height + 4);
                if (labelRect.Right > mapBounds.Right) labelRect.X = tractorPoint.X - labelRect.Width - 18;
                if (labelRect.Bottom > mapBounds.Bottom) labelRect.Y = mapBounds.Bottom - labelRect.Height - 2;
                if (labelRect.Top < mapBounds.Top) labelRect.Y = mapBounds.Top + 2;
                graphics.FillRectangle(Brushes.White, labelRect);
                graphics.DrawRectangle(Pens.Gray, Rectangle.Round(labelRect));
                graphics.DrawString(tractorLabel, labelFont, tractorBrush, labelRect.X + 4, labelRect.Y + 2);

                string status = GetDxfMapStatusText();
                if (!tractorInsideBounds)
                {
                    status += " | tractor outside drawing extents";
                }
                graphics.FillRectangle(Brushes.White, bounds.Left + 8, bounds.Top + 8, bounds.Width - 16, 28);
                graphics.DrawRectangle(Pens.Gray, bounds.Left + 8, bounds.Top + 8, bounds.Width - 16, 28);
                graphics.DrawString(status, labelFont, labelBrush, bounds.Left + 14, bounds.Top + 13);
            }
        }

        private bool TryGetDxfUtmBounds(out double minEast, out double maxEast, out double minNorth, out double maxNorth)
        {
            minEast = double.MaxValue;
            maxEast = double.MinValue;
            minNorth = double.MaxValue;
            maxNorth = double.MinValue;

            foreach (DxfMapPolygon polygon in dxfMapPolygons)
            {
                foreach (DxfUtmPoint point in polygon.UtmPoints)
                {
                    minEast = Math.Min(minEast, point.Easting);
                    maxEast = Math.Max(maxEast, point.Easting);
                    minNorth = Math.Min(minNorth, point.Northing);
                    maxNorth = Math.Max(maxNorth, point.Northing);
                }
            }

            foreach (DxfMapLine line in dxfMapLines)
            {
                minEast = Math.Min(minEast, Math.Min(line.Start.Easting, line.End.Easting));
                maxEast = Math.Max(maxEast, Math.Max(line.Start.Easting, line.End.Easting));
                minNorth = Math.Min(minNorth, Math.Min(line.Start.Northing, line.End.Northing));
                maxNorth = Math.Max(maxNorth, Math.Max(line.Start.Northing, line.End.Northing));
            }

            return minEast != double.MaxValue && maxEast > minEast && maxNorth > minNorth;
        }

        private bool TryGetDxfPreviewTransform(
            Rectangle bounds,
            double zoomMultiplier,
            double previewPanX,
            double previewPanY,
            out Rectangle mapBounds,
            out DxfUtmPoint tractor,
            out double centerX,
            out double centerY,
            out double scale,
            out double minEast,
            out double maxEast,
            out double minNorth,
            out double maxNorth)
        {
            mapBounds = new Rectangle(bounds.Left + 10, bounds.Top + 46, bounds.Width - 20, bounds.Height - 56);
            tractor = new DxfUtmPoint(0, 0);
            centerX = 0;
            centerY = 0;
            scale = 0;
            minEast = 0;
            maxEast = 0;
            minNorth = 0;
            maxNorth = 0;

            if (mapBounds.Width < 20 || mapBounds.Height < 20) return false;
            if (!TryGetDxfUtmBounds(out minEast, out maxEast, out minNorth, out maxNorth)) return false;

            tractor = Wgs84ToUtm(AppModel.CurrentLatLon, dxfMapUtmZone);
            centerX = mapBounds.Left + (mapBounds.Width * 0.5) + previewPanX;
            centerY = mapBounds.Top + (mapBounds.Height * 0.5) + previewPanY;

            const int margin = 18;
            double maxEastFromTractor = Math.Max(Math.Abs(minEast - tractor.Easting), Math.Abs(maxEast - tractor.Easting));
            double maxNorthFromTractor = Math.Max(Math.Abs(minNorth - tractor.Northing), Math.Abs(maxNorth - tractor.Northing));
            maxEastFromTractor = Math.Max(1.0, maxEastFromTractor);
            maxNorthFromTractor = Math.Max(1.0, maxNorthFromTractor);
            scale = Math.Min(
                ((mapBounds.Width * 0.5) - margin) / maxEastFromTractor,
                ((mapBounds.Height * 0.5) - margin) / maxNorthFromTractor);
            scale *= Math.Max(0.1, zoomMultiplier);

            return !double.IsNaN(scale) && !double.IsInfinity(scale) && scale > 0;
        }

        private void DrawDxfManualPreviewPoints(
            Graphics graphics,
            Func<DxfUtmPoint, PointF> mapPoint,
            Pen manualPen,
            Brush manualBrush,
            Brush manualTextBrush,
            Font labelFont)
        {
            if (dxfManualFieldPoints.Count == 0) return;

            PointF[] points = dxfManualFieldPoints.Select(mapPoint).ToArray();
            if (points.Length > 1)
            {
                graphics.DrawLines(manualPen, points);
                if (points.Length == 4)
                {
                    graphics.DrawLine(manualPen, points[3], points[0]);
                }
            }

            for (int i = 0; i < points.Length; i++)
            {
                const float r = 12f;
                graphics.FillEllipse(manualBrush, points[i].X - r, points[i].Y - r, r * 2, r * 2);
                graphics.DrawEllipse(Pens.Black, points[i].X - r, points[i].Y - r, r * 2, r * 2);

                string label = (i + 1).ToString(CultureInfo.CurrentCulture);
                SizeF size = graphics.MeasureString(label, labelFont);
                graphics.DrawString(label, labelFont, manualTextBrush, points[i].X - (size.Width * 0.5f), points[i].Y - (size.Height * 0.5f));
            }
        }

        private bool TryFindNearestDxfCorner(DxfUtmPoint clickPoint, double toleranceMeters, out DxfUtmPoint corner)
        {
            corner = new DxfUtmPoint(0, 0);
            double bestDistanceSquared = double.MaxValue;
            DxfMapLine bestLine = null;

            foreach (DxfMapLine line in dxfMapLines)
            {
                double distanceSquared = DxfDistanceToSegmentSquared(clickPoint, line.Start, line.End);
                if (distanceSquared < bestDistanceSquared)
                {
                    bestDistanceSquared = distanceSquared;
                    bestLine = line;
                }
            }

            if (bestLine == null || Math.Sqrt(bestDistanceSquared) > toleranceMeters)
            {
                return false;
            }

            corner = DxfDistanceSquared(clickPoint, bestLine.Start) <= DxfDistanceSquared(clickPoint, bestLine.End)
                ? bestLine.Start
                : bestLine.End;

            return true;
        }

        public void FitDxfMapToScreen()
        {
            if (!isDxfMapOn || (dxfMapPolygons.Count == 0 && dxfMapLines.Count == 0)) return;

            RefreshDxfMapLocalPoints(force: false);

            if (!TryGetDxfLocalBounds(out double minEast, out double maxEast, out double minNorth, out double maxNorth))
            {
                return;
            }

            double centerEast = (minEast + maxEast) * 0.5;
            double centerNorth = (minNorth + maxNorth) * 0.5;
            double deltaEast = centerEast - pivotAxlePos.easting;
            double deltaNorth = centerNorth - pivotAxlePos.northing;

            double heading = camera.FollowDirectionHint ? DegreesToRadians(camHeading) : 0.0;
            double cos = Math.Cos(heading);
            double sin = Math.Sin(heading);

            SaveDxfCameraIfNeeded();
            camera.PanX = -((cos * deltaEast) - (sin * deltaNorth));
            camera.PanY = -((sin * deltaEast) + (cos * deltaNorth));
            camera.DistanceToLookAt = Math.Max(500.0, Math.Min(60000.0, Math.Max(maxEast - minEast, maxNorth - minNorth) * 0.85));
            SetZoom();
            oglMain?.Refresh();
            oglZoom?.Refresh();
        }

        private void SaveDxfCameraIfNeeded()
        {
            if (hasDxfSavedCamera) return;

            dxfSavedPanX = camera.PanX;
            dxfSavedPanY = camera.PanY;
            dxfSavedDistanceToLookAt = camera.DistanceToLookAt;
            hasDxfSavedCamera = true;
        }

        private void RestoreDxfCameraIfNeeded()
        {
            if (!hasDxfSavedCamera) return;

            camera.PanX = dxfSavedPanX;
            camera.PanY = dxfSavedPanY;
            camera.DistanceToLookAt = dxfSavedDistanceToLookAt;
            hasDxfSavedCamera = false;
            SetZoom();
        }

        private string GetDxfMapDistanceStatus()
        {
            if (dxfMapPolygons.Count == 0 && dxfMapLines.Count == 0) return "no DXF geometry";

            DxfUtmPoint tractor = Wgs84ToUtm(AppModel.CurrentLatLon, dxfMapUtmZone);
            if (dxfMapPolygons.Any(p => IsPointInsideDxfPolygon(tractor, p.UtmPoints)))
            {
                return "tractor inside DXF boundary";
            }

            double distance = Math.Sqrt(GetNearestDxfDistanceSquared(tractor));
            return "nearest " + distance.ToString("N0", CultureInfo.CurrentCulture) + " m";
        }

        private static double GetNearestDxfDistanceSquared(DxfUtmPoint point, IEnumerable<DxfMapPolygon> polygons)
        {
            double best = double.MaxValue;
            foreach (DxfMapPolygon polygon in polygons)
            {
                best = Math.Min(best, GetNearestDxfDistanceSquared(point, polygon.UtmPoints));
            }

            return best;
        }

        private double GetNearestDxfDistanceSquared(DxfUtmPoint point)
        {
            double best = GetNearestDxfDistanceSquared(point, dxfMapPolygons);
            foreach (DxfMapLine line in dxfMapLines)
            {
                best = Math.Min(best, DxfDistanceToSegmentSquared(point, line.Start, line.End));
            }

            return best;
        }

        private static double GetNearestDxfDistanceSquared(DxfUtmPoint point, List<DxfUtmPoint> polygon)
        {
            if (polygon == null || polygon.Count == 0) return double.MaxValue;
            if (polygon.Count == 1) return DxfDistanceSquared(point, polygon[0]);

            double best = double.MaxValue;
            for (int i = 1; i < polygon.Count; i++)
            {
                best = Math.Min(best, DxfDistanceToSegmentSquared(point, polygon[i - 1], polygon[i]));
            }

            if (polygon.Count > 2)
            {
                best = Math.Min(best, DxfDistanceToSegmentSquared(point, polygon[polygon.Count - 1], polygon[0]));
            }

            return best;
        }

        private static double DxfDistanceToSegmentSquared(DxfUtmPoint point, DxfUtmPoint a, DxfUtmPoint b)
        {
            double abEast = b.Easting - a.Easting;
            double abNorth = b.Northing - a.Northing;
            double abLenSquared = (abEast * abEast) + (abNorth * abNorth);

            if (abLenSquared < 0.000001) return DxfDistanceSquared(point, a);

            double apEast = point.Easting - a.Easting;
            double apNorth = point.Northing - a.Northing;
            double t = ((apEast * abEast) + (apNorth * abNorth)) / abLenSquared;
            t = Math.Max(0.0, Math.Min(1.0, t));

            double closestEast = a.Easting + (abEast * t);
            double closestNorth = a.Northing + (abNorth * t);
            double dEast = point.Easting - closestEast;
            double dNorth = point.Northing - closestNorth;
            return (dEast * dEast) + (dNorth * dNorth);
        }

        private bool TryGetDxfLocalBounds(out double minEast, out double maxEast, out double minNorth, out double maxNorth)
        {
            minEast = double.MaxValue;
            maxEast = double.MinValue;
            minNorth = double.MaxValue;
            maxNorth = double.MinValue;

            foreach (DxfMapPolygon polygon in dxfMapPolygons)
            {
                foreach (vec3 point in polygon.LocalPoints)
                {
                    minEast = Math.Min(minEast, point.easting);
                    maxEast = Math.Max(maxEast, point.easting);
                    minNorth = Math.Min(minNorth, point.northing);
                    maxNorth = Math.Max(maxNorth, point.northing);
                }
            }

            foreach (DxfMapLine line in dxfMapLines)
            {
                minEast = Math.Min(minEast, Math.Min(line.LocalStart.easting, line.LocalEnd.easting));
                maxEast = Math.Max(maxEast, Math.Max(line.LocalStart.easting, line.LocalEnd.easting));
                minNorth = Math.Min(minNorth, Math.Min(line.LocalStart.northing, line.LocalEnd.northing));
                maxNorth = Math.Max(maxNorth, Math.Max(line.LocalStart.northing, line.LocalEnd.northing));
            }

            return minEast != double.MaxValue && maxEast > minEast && maxNorth > minNorth;
        }

        private void UpdateDxfMapButtonAppearance()
        {
            if (btnDxfMap == null) return;

            btnDxfMap.BackColor = isDxfMapOn
                ? Color.FromArgb(116, 190, 92)
                : Color.FromArgb(185, 185, 185);
        }

        private void DrawDxfMapOverlay()
        {
            if (!isDxfMapOn || (dxfMapPolygons.Count == 0 && dxfMapLines.Count == 0)) return;

            RefreshDxfMapLocalPoints(force: false);

            bool depthWasEnabled = GL.IsEnabled(EnableCap.DepthTest);
            bool blendWasEnabled = GL.IsEnabled(EnableCap.Blend);
            bool textureWasEnabled = GL.IsEnabled(EnableCap.Texture2D);
            GL.Disable(EnableCap.DepthTest);
            GL.Enable(EnableCap.Blend);
            GL.Disable(EnableCap.Texture2D);

            DxfUtmPoint tractor = Wgs84ToUtm(AppModel.CurrentLatLon, dxfMapUtmZone);

            GL.LineWidth(3.0f);
            GL.Color4(1.0f, 1.0f, 1.0f, 1.0f);
            GL.Begin(PrimitiveType.Lines);
            foreach (DxfMapLine line in dxfMapLines)
            {
                GL.Vertex3(line.LocalStart.easting, line.LocalStart.northing, 1.0);
                GL.Vertex3(line.LocalEnd.easting, line.LocalEnd.northing, 1.0);
            }
            GL.End();

            foreach (DxfMapPolygon polygon in dxfMapPolygons)
            {
                bool containsTractor = IsPointInsideDxfPolygon(tractor, polygon.UtmPoints);
                if (containsTractor)
                {
                    GL.LineWidth(5.0f);
                    GL.Color4(1.0f, 0.90f, 0.0f, 1.0f);
                }
                else
                {
                    GL.LineWidth(2.4f);
                    GL.Color4(0.0f, 0.85f, 1.0f, 1.0f);
                }

                GL.Begin(PrimitiveType.LineLoop);
                foreach (vec3 point in polygon.LocalPoints)
                {
                    GL.Vertex3(point.easting, point.northing, 1.1);
                }
                GL.End();
            }

            GL.LineWidth(1.0f);
            if (depthWasEnabled) GL.Enable(EnableCap.DepthTest);
            else GL.Disable(EnableCap.DepthTest);

            if (blendWasEnabled) GL.Enable(EnableCap.Blend);
            else GL.Disable(EnableCap.Blend);

            if (textureWasEnabled) GL.Enable(EnableCap.Texture2D);
            else GL.Disable(EnableCap.Texture2D);
        }

        private DxfMapPolygon BuildDxfMapPolygon(DxfSourcePolygon source)
        {
            return new DxfMapPolygon(source.Points, source.Layer);
        }

        private void RefreshDxfMapLocalPoints(bool force)
        {
            Wgs84 currentOrigin = AppModel.LocalPlane.Origin;
            if (!force
                && Math.Abs(currentOrigin.Latitude - dxfMapLocalPlaneOrigin.Latitude) < 0.000000001
                && Math.Abs(currentOrigin.Longitude - dxfMapLocalPlaneOrigin.Longitude) < 0.000000001)
            {
                return;
            }

            foreach (DxfMapPolygon polygon in dxfMapPolygons)
            {
                polygon.LocalPoints.Clear();
                foreach (DxfUtmPoint point in polygon.UtmPoints)
                {
                    Wgs84 wgs = UtmToWgs84(point, dxfMapUtmZone);
                    GeoCoord local = AppModel.LocalPlane.ConvertWgs84ToGeoCoord(wgs);
                    polygon.LocalPoints.Add(new vec3(local.Easting, local.Northing, 0));
                }
            }

            for (int i = 0; i < dxfMapLines.Count; i++)
            {
                DxfMapLine line = dxfMapLines[i];
                line.LocalStart = ConvertDxfUtmToLocal(line.Start);
                line.LocalEnd = ConvertDxfUtmToLocal(line.End);
            }

            dxfMapLocalPlaneOrigin = currentOrigin;
        }

        private vec3 ConvertDxfUtmToLocal(DxfUtmPoint point)
        {
            Wgs84 wgs = UtmToWgs84(point, dxfMapUtmZone);
            GeoCoord local = AppModel.LocalPlane.ConvertWgs84ToGeoCoord(wgs);
            return new vec3(local.Easting, local.Northing, 0);
        }

        private int GetCurrentUtmZone()
        {
            double lon = AppModel.CurrentLatLon.Longitude;
            if (Math.Abs(lon) < 0.000001) return DxfDefaultUtmZone;
            return Math.Max(1, Math.Min(60, (int)Math.Floor((lon + 180.0) / 6.0) + 1));
        }

        private static bool LooksLikeUtm(List<DxfUtmPoint> points)
        {
            return points != null
                && points.Count >= 3
                && points.All(p => p.Easting > 100000.0 && p.Easting < 900000.0 && p.Northing > 1000000.0 && p.Northing < 10000000.0);
        }

        private static bool LooksLikeUtm(DxfUtmPoint point)
        {
            return point.Easting > 100000.0 && point.Easting < 900000.0
                && point.Northing > 1000000.0 && point.Northing < 10000000.0;
        }

        private string BuildDxfFieldName()
        {
            string baseName = Path.GetFileNameWithoutExtension(dxfMapFilePath);
            if (string.IsNullOrWhiteSpace(baseName)) baseName = "DXF Field";

            baseName = Regex.Replace(baseName, glm.fileRegex, "").Trim();
            if (baseName.Length > 28) baseName = baseName.Substring(0, 28).Trim();

            string name = baseName + " " + DateTime.Now.ToString("yyyy-MM-dd HH-mm", CultureInfo.InvariantCulture);
            string original = name;
            int suffix = 2;
            while (Directory.Exists(Path.Combine(RegistrySettings.fieldsDirectory, name)))
            {
                name = original + " " + suffix.ToString(CultureInfo.InvariantCulture);
                suffix++;
            }

            return name;
        }

        private static string SanitizeDxfFieldName(string fieldName)
        {
            if (string.IsNullOrWhiteSpace(fieldName)) return string.Empty;

            string cleanName = Regex.Replace(fieldName, glm.fileRegex, "").Trim();
            if (cleanName.Length > 48) cleanName = cleanName.Substring(0, 48).Trim();
            return cleanName;
        }

        private static List<DxfUtmPoint> SortDxfPointsAroundCenter(IEnumerable<DxfUtmPoint> points)
        {
            List<DxfUtmPoint> list = points.ToList();
            if (list.Count < 3) return list;

            double centerEast = list.Average(p => p.Easting);
            double centerNorth = list.Average(p => p.Northing);
            return list
                .OrderBy(p => Math.Atan2(p.Northing - centerNorth, p.Easting - centerEast))
                .ToList();
        }

        private static double SignedArea(List<DxfUtmPoint> points)
        {
            if (points == null || points.Count < 3) return 0.0;

            double area = 0.0;
            for (int i = 0; i < points.Count; i++)
            {
                DxfUtmPoint a = points[i];
                DxfUtmPoint b = points[(i + 1) % points.Count];
                area += (a.Easting * b.Northing) - (b.Easting * a.Northing);
            }

            return area * 0.5;
        }

        private static bool IsPointInsideDxfPolygon(DxfUtmPoint point, List<DxfUtmPoint> polygon)
        {
            if (polygon == null || polygon.Count < 3) return false;

            bool inside = false;
            int j = polygon.Count - 1;

            for (int i = 0; i < polygon.Count; i++)
            {
                bool crosses = ((polygon[i].Northing > point.Northing) != (polygon[j].Northing > point.Northing))
                    && (point.Easting < (polygon[j].Easting - polygon[i].Easting)
                    * (point.Northing - polygon[i].Northing)
                    / (polygon[j].Northing - polygon[i].Northing)
                    + polygon[i].Easting);

                if (crosses) inside = !inside;
                j = i;
            }

            return inside;
        }

        private static double DxfDistanceSquared(vec3 a, vec3 b)
        {
            double dEast = a.easting - b.easting;
            double dNorth = a.northing - b.northing;
            return (dEast * dEast) + (dNorth * dNorth);
        }

        private static double DxfDistanceSquared(DxfUtmPoint a, DxfUtmPoint b)
        {
            double dEast = a.Easting - b.Easting;
            double dNorth = a.Northing - b.Northing;
            return (dEast * dEast) + (dNorth * dNorth);
        }

        private static DxfUtmPoint Wgs84ToUtm(Wgs84 wgs, int zone)
        {
            const double a = 6378137.0;
            const double eccSquared = 0.0066943799901413165;
            const double k0 = 0.9996;

            double latRad = DegreesToRadians(wgs.Latitude);
            double lonRad = DegreesToRadians(wgs.Longitude);
            double lonOrigin = (zone - 1) * 6 - 180 + 3;
            double lonOriginRad = DegreesToRadians(lonOrigin);

            double eccPrimeSquared = eccSquared / (1 - eccSquared);
            double n = a / Math.Sqrt(1 - eccSquared * Math.Sin(latRad) * Math.Sin(latRad));
            double t = Math.Tan(latRad) * Math.Tan(latRad);
            double c = eccPrimeSquared * Math.Cos(latRad) * Math.Cos(latRad);
            double aa = Math.Cos(latRad) * (lonRad - lonOriginRad);

            double m = a * ((1 - eccSquared / 4 - 3 * eccSquared * eccSquared / 64 - 5 * eccSquared * eccSquared * eccSquared / 256) * latRad
                - (3 * eccSquared / 8 + 3 * eccSquared * eccSquared / 32 + 45 * eccSquared * eccSquared * eccSquared / 1024) * Math.Sin(2 * latRad)
                + (15 * eccSquared * eccSquared / 256 + 45 * eccSquared * eccSquared * eccSquared / 1024) * Math.Sin(4 * latRad)
                - (35 * eccSquared * eccSquared * eccSquared / 3072) * Math.Sin(6 * latRad));

            double easting = k0 * n * (aa + (1 - t + c) * aa * aa * aa / 6
                + (5 - 18 * t + t * t + 72 * c - 58 * eccPrimeSquared) * aa * aa * aa * aa * aa / 120) + 500000.0;

            double northing = k0 * (m + n * Math.Tan(latRad) * (aa * aa / 2
                + (5 - t + 9 * c + 4 * c * c) * aa * aa * aa * aa / 24
                + (61 - 58 * t + t * t + 600 * c - 330 * eccPrimeSquared) * aa * aa * aa * aa * aa * aa / 720));

            if (wgs.Latitude < 0) northing += 10000000.0;

            return new DxfUtmPoint(easting, northing);
        }

        private static Wgs84 UtmToWgs84(DxfUtmPoint utm, int zone)
        {
            const double a = 6378137.0;
            const double eccSquared = 0.0066943799901413165;
            const double k0 = 0.9996;

            double eccPrimeSquared = eccSquared / (1 - eccSquared);
            double e1 = (1 - Math.Sqrt(1 - eccSquared)) / (1 + Math.Sqrt(1 - eccSquared));
            double x = utm.Easting - 500000.0;
            double y = utm.Northing;
            double lonOrigin = (zone - 1) * 6 - 180 + 3;

            double m = y / k0;
            double mu = m / (a * (1 - eccSquared / 4 - 3 * eccSquared * eccSquared / 64 - 5 * eccSquared * eccSquared * eccSquared / 256));

            double phi1Rad = mu
                + (3 * e1 / 2 - 27 * e1 * e1 * e1 / 32) * Math.Sin(2 * mu)
                + (21 * e1 * e1 / 16 - 55 * e1 * e1 * e1 * e1 / 32) * Math.Sin(4 * mu)
                + (151 * e1 * e1 * e1 / 96) * Math.Sin(6 * mu);

            double n1 = a / Math.Sqrt(1 - eccSquared * Math.Sin(phi1Rad) * Math.Sin(phi1Rad));
            double t1 = Math.Tan(phi1Rad) * Math.Tan(phi1Rad);
            double c1 = eccPrimeSquared * Math.Cos(phi1Rad) * Math.Cos(phi1Rad);
            double r1 = a * (1 - eccSquared) / Math.Pow(1 - eccSquared * Math.Sin(phi1Rad) * Math.Sin(phi1Rad), 1.5);
            double d = x / (n1 * k0);

            double lat = phi1Rad - (n1 * Math.Tan(phi1Rad) / r1)
                * (d * d / 2
                - (5 + 3 * t1 + 10 * c1 - 4 * c1 * c1 - 9 * eccPrimeSquared) * d * d * d * d / 24
                + (61 + 90 * t1 + 298 * c1 + 45 * t1 * t1 - 252 * eccPrimeSquared - 3 * c1 * c1) * d * d * d * d * d * d / 720);

            double lon = DegreesToRadians(lonOrigin)
                + (d - (1 + 2 * t1 + c1) * d * d * d / 6
                + (5 - 2 * c1 + 28 * t1 - 3 * c1 * c1 + 8 * eccPrimeSquared + 24 * t1 * t1) * d * d * d * d * d / 120)
                / Math.Cos(phi1Rad);

            return new Wgs84(RadiansToDegrees(lat), RadiansToDegrees(lon));
        }

        private static double DegreesToRadians(double degrees)
        {
            return degrees * Math.PI / 180.0;
        }

        private static double RadiansToDegrees(double radians)
        {
            return radians * 180.0 / Math.PI;
        }

        public sealed class DxfMapLoadResult
        {
            public DxfMapLoadResult(bool success, string message)
            {
                Success = success;
                Message = message;
            }

            public bool Success { get; }
            public string Message { get; }
        }

        public sealed class DxfMapCreateFieldResult
        {
            public DxfMapCreateFieldResult(bool success, string message)
            {
                Success = success;
                Message = message;
            }

            public bool Success { get; }
            public string Message { get; }
        }

        private sealed class DxfMapPolygon
        {
            public DxfMapPolygon(List<DxfUtmPoint> utmPoints, string layer)
            {
                UtmPoints = utmPoints;
                Layer = layer;
            }

            public List<DxfUtmPoint> UtmPoints { get; }
            public List<vec3> LocalPoints { get; } = new List<vec3>();
            public string Layer { get; }
        }

        private sealed class DxfMapLine
        {
            public DxfMapLine(DxfUtmPoint start, DxfUtmPoint end, string layer)
            {
                Start = start;
                End = end;
                Layer = layer;
            }

            public DxfUtmPoint Start { get; }
            public DxfUtmPoint End { get; }
            public vec3 LocalStart { get; set; }
            public vec3 LocalEnd { get; set; }
            public string Layer { get; }
        }

        private sealed class DxfSourceGeometry
        {
            public List<DxfSourcePolygon> Polygons { get; } = new List<DxfSourcePolygon>();
            public List<DxfSourceLine> Lines { get; } = new List<DxfSourceLine>();
        }

        private sealed class DxfSourcePolygon
        {
            public DxfSourcePolygon(List<DxfUtmPoint> points, string layer)
            {
                Points = points;
                Layer = layer;
            }

            public List<DxfUtmPoint> Points { get; }
            public string Layer { get; }
        }

        private sealed class DxfSourceLine
        {
            public DxfSourceLine(DxfUtmPoint start, DxfUtmPoint end, string layer)
            {
                Start = start;
                End = end;
                Layer = layer;
            }

            public DxfUtmPoint Start { get; }
            public DxfUtmPoint End { get; }
            public string Layer { get; }
        }

        private struct DxfUtmPoint
        {
            public DxfUtmPoint(double easting, double northing)
            {
                Easting = easting;
                Northing = northing;
            }

            public double Easting { get; }
            public double Northing { get; }
        }

        private static class DxfMapReader
        {
            public static DxfSourceGeometry LoadGeometry(string filePath)
            {
                List<DxfPair> pairs = ReadPairs(filePath);
                DxfSourceGeometry geometry = new DxfSourceGeometry();
                bool inEntities = false;

                for (int i = 0; i < pairs.Count; i++)
                {
                    if (!inEntities && pairs[i].Code == "2" && pairs[i].Value.Equals("ENTITIES", StringComparison.OrdinalIgnoreCase))
                    {
                        inEntities = true;
                        continue;
                    }

                    if (!inEntities) continue;
                    if (pairs[i].Code == "0" && pairs[i].Value.Equals("ENDSEC", StringComparison.OrdinalIgnoreCase)) break;

                    if (pairs[i].Code == "0" && pairs[i].Value.Equals("LWPOLYLINE", StringComparison.OrdinalIgnoreCase))
                    {
                        ReadLwPolyline(pairs, ref i, geometry);
                    }
                    else if (pairs[i].Code == "0" && pairs[i].Value.Equals("POLYLINE", StringComparison.OrdinalIgnoreCase))
                    {
                        ReadPolyline(pairs, ref i, geometry);
                    }
                    else if (pairs[i].Code == "0" && pairs[i].Value.Equals("LINE", StringComparison.OrdinalIgnoreCase))
                    {
                        DxfSourceLine line = ReadLine(pairs, ref i);
                        if (line != null) geometry.Lines.Add(line);
                    }
                }

                return geometry;
            }

            private static void ReadLwPolyline(List<DxfPair> pairs, ref int index, DxfSourceGeometry geometry)
            {
                List<DxfUtmPoint> points = new List<DxfUtmPoint>();
                string layer = string.Empty;
                int flags = 0;
                double? currentX = null;

                index++;
                while (index < pairs.Count)
                {
                    DxfPair pair = pairs[index];
                    if (pair.Code == "0")
                    {
                        index--;
                        break;
                    }

                    if (pair.Code == "8") layer = pair.Value;
                    else if (pair.Code == "70") int.TryParse(pair.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out flags);
                    else if (pair.Code == "10") currentX = ParseDouble(pair.Value);
                    else if (pair.Code == "20" && currentX.HasValue)
                    {
                        points.Add(new DxfUtmPoint(currentX.Value, ParseDouble(pair.Value)));
                        currentX = null;
                    }

                    index++;
                }

                bool isClosed = (flags & 1) == 1 || AreSame(points.FirstOrDefault(), points.LastOrDefault());
                if (isClosed && points.Count >= 3)
                {
                    RemoveDuplicateLastPoint(points);
                    geometry.Polygons.Add(new DxfSourcePolygon(points, layer));
                    AddPolylineSegments(geometry, points, layer, closed: true);
                }
                else if (points.Count >= 2)
                {
                    AddPolylineSegments(geometry, points, layer, closed: false);
                }
            }

            private static void ReadPolyline(List<DxfPair> pairs, ref int index, DxfSourceGeometry geometry)
            {
                List<DxfUtmPoint> points = new List<DxfUtmPoint>();
                string layer = string.Empty;
                int flags = 0;

                index++;
                while (index < pairs.Count)
                {
                    DxfPair pair = pairs[index];
                    if (pair.Code == "0" && pair.Value.Equals("SEQEND", StringComparison.OrdinalIgnoreCase)) break;
                    if (pair.Code == "0" && pair.Value.Equals("VERTEX", StringComparison.OrdinalIgnoreCase))
                    {
                        DxfUtmPoint? point = ReadVertex(pairs, ref index);
                        if (point.HasValue) points.Add(point.Value);
                    }
                    else
                    {
                        if (pair.Code == "8") layer = pair.Value;
                        else if (pair.Code == "70") int.TryParse(pair.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out flags);
                    }

                    index++;
                }

                bool isClosed = (flags & 1) == 1 || AreSame(points.FirstOrDefault(), points.LastOrDefault());
                if (isClosed && points.Count >= 3)
                {
                    RemoveDuplicateLastPoint(points);
                    geometry.Polygons.Add(new DxfSourcePolygon(points, layer));
                    AddPolylineSegments(geometry, points, layer, closed: true);
                }
                else if (points.Count >= 2)
                {
                    AddPolylineSegments(geometry, points, layer, closed: false);
                }
            }

            private static DxfSourceLine ReadLine(List<DxfPair> pairs, ref int index)
            {
                string layer = string.Empty;
                double? x1 = null;
                double? y1 = null;
                double? x2 = null;
                double? y2 = null;

                index++;
                while (index < pairs.Count)
                {
                    DxfPair pair = pairs[index];
                    if (pair.Code == "0")
                    {
                        index--;
                        break;
                    }

                    if (pair.Code == "8") layer = pair.Value;
                    else if (pair.Code == "10") x1 = ParseDouble(pair.Value);
                    else if (pair.Code == "20") y1 = ParseDouble(pair.Value);
                    else if (pair.Code == "11") x2 = ParseDouble(pair.Value);
                    else if (pair.Code == "21") y2 = ParseDouble(pair.Value);

                    index++;
                }

                if (!x1.HasValue || !y1.HasValue || !x2.HasValue || !y2.HasValue) return null;

                return new DxfSourceLine(
                    new DxfUtmPoint(x1.Value, y1.Value),
                    new DxfUtmPoint(x2.Value, y2.Value),
                    layer);
            }

            private static void AddPolylineSegments(DxfSourceGeometry geometry, List<DxfUtmPoint> points, string layer, bool closed)
            {
                for (int i = 1; i < points.Count; i++)
                {
                    geometry.Lines.Add(new DxfSourceLine(points[i - 1], points[i], layer));
                }

                if (closed && points.Count > 2)
                {
                    geometry.Lines.Add(new DxfSourceLine(points[points.Count - 1], points[0], layer));
                }
            }

            private static DxfUtmPoint? ReadVertex(List<DxfPair> pairs, ref int index)
            {
                double? x = null;
                double? y = null;

                index++;
                while (index < pairs.Count)
                {
                    DxfPair pair = pairs[index];
                    if (pair.Code == "0")
                    {
                        index--;
                        break;
                    }

                    if (pair.Code == "10") x = ParseDouble(pair.Value);
                    else if (pair.Code == "20") y = ParseDouble(pair.Value);

                    index++;
                }

                if (!x.HasValue || !y.HasValue) return null;
                return new DxfUtmPoint(x.Value, y.Value);
            }

            private static List<DxfPair> ReadPairs(string filePath)
            {
                List<DxfPair> pairs = new List<DxfPair>();
                using (StreamReader reader = new StreamReader(filePath))
                {
                    while (!reader.EndOfStream)
                    {
                        string code = reader.ReadLine();
                        string value = reader.ReadLine();
                        if (code == null || value == null) break;
                        pairs.Add(new DxfPair(code.Trim(), value.Trim()));
                    }
                }

                return pairs;
            }

            private static double ParseDouble(string value)
            {
                return double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
            }

            private static bool AreSame(DxfUtmPoint a, DxfUtmPoint b)
            {
                return Math.Abs(a.Easting - b.Easting) < 0.001 && Math.Abs(a.Northing - b.Northing) < 0.001;
            }

            private static void RemoveDuplicateLastPoint(List<DxfUtmPoint> points)
            {
                if (points.Count < 2) return;
                if (AreSame(points[0], points[points.Count - 1]))
                {
                    points.RemoveAt(points.Count - 1);
                }
            }

            private struct DxfPair
            {
                public DxfPair(string code, string value)
                {
                    Code = code;
                    Value = value;
                }

                public string Code { get; }
                public string Value { get; }
            }
        }
    }
}
