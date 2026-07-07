using AgLibrary.Logging;
using AgOpenGPS.Core;
using AgOpenGPS.Core.Interfaces;
using AgOpenGPS.Core.Models;
using AgOpenGPS.Core.Streamers;
using AgOpenGPS.Core.Translations;
using AgOpenGPS.Forms;
using AgOpenGPS.Helpers;
using AgOpenGPS.IO;
using GMap.NET;
using GMap.NET.MapProviders;
using GMap.NET.WindowsForms;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AgOpenGPS
{
    public partial class FormMap : Form
    {
        //access to the main GPS form and all its variables
        private readonly FormGPS mf = null;

        private bool isClosing;
        private GMapPolygon polygon;
        private GMapOverlay overlay = new GMapOverlay();
        private Point lastMouseLocation;
        private bool isColorMap = true;
        private ComboBox cboxMapProvider;
        private Button btnLargeMap;
        private bool isLoadingProvider;
        private bool isShowingAllFields;
        private bool isAutomationCapture;
        private const int HighResolutionNavigationMapPixels = 5120;
        private const int FallbackNavigationMapPixels = 4096;
        private const int HighResolutionNavigationMapZoom = 16;
        private const double MaxNavigationMapMeters = 18000.0;
        private const double NavigationMapHalfSizeMeters = 5000.0;

        public FormMap(Form callingForm)
        {
            //get copy of the calling main form
            mf = callingForm as FormGPS;

            InitializeComponent();
            //translate all the controls
            this.Text = gStr.gsMapForBackground;
            labelNewBoundary.Text = gStr.gsNewFromDefault + " " + gStr.gsBoundary;
            labelBoundary.Text = gStr.gsBoundary;
            lblPoints.Text = gStr.gsPoints + ":";
            labelBackground.Text = gStr.gsBackground;

            AddMapProviderSelector();
            ApplyMapProvider(Properties.Settings.Default.setWindow_BingMapProvider);
            gMapControl.ShowCenter = false;
            gMapControl.DragButton = MouseButtons.Left;

            polygon = new GMapPolygon(new List<PointLatLng>(), "bingLine")
            {
                Fill = Brushes.Transparent,
                Stroke = new Pen(Color.White, 4f) { LineJoin = LineJoin.Round }
            };
            overlay.Polygons.Add(polygon);
            gMapControl.Overlays.Add(overlay);
        }

        private void FormMap_Load(object sender, EventArgs e)
        {
            Size = Properties.Settings.Default.setWindow_BingMapSize;

            gMapControl.Zoom = Properties.Settings.Default.setWindow_BingZoom;
            gMapControl.Position = new PointLatLng(
                mf.AppModel.CurrentLatLon.Latitude,
                mf.AppModel.CurrentLatLon.Longitude);

            cboxDrawMap.Checked = mf.worldGrid.HasBingMap;

            if (mf.worldGrid.HasBingMap) cboxDrawMap.Image = Properties.Resources.MappingOn;
            else cboxDrawMap.Image = Properties.Resources.MappingOff;

            if (!mf.isJobStarted && gMapControl.Zoom > 13)
            {
                gMapControl.Zoom = 13;
            }

            if (!ScreenHelper.IsOnScreen(Bounds))
            {
                Top = 0;
                Left = 0;
            }

            btnDeleteAll.Enabled = true;
        }

        private void FormMap_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!isClosing)
            {
                e.Cancel = true;
                return;
            }
            Properties.Settings.Default.setWindow_BingMapSize = Size;
            Properties.Settings.Default.setWindow_BingZoom = (int)gMapControl.Zoom;
            Properties.Settings.Default.setWindow_BingMapProvider = GetSelectedMapProviderKey();
            Properties.Settings.Default.Save();
        }

        private void AddMapProviderSelector()
        {
            cboxMapProvider = new ComboBox
            {
                Anchor = AnchorStyles.None,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Tahoma", 13.5F, FontStyle.Bold),
                Size = new Size(150, 30)
            };

            cboxMapProvider.Items.Add("Bing Hybrid");
            cboxMapProvider.Items.Add("Bing Satellite");
            cboxMapProvider.SelectedIndexChanged += cboxMapProvider_SelectedIndexChanged;

            btnLargeMap = new Button
            {
                Anchor = AnchorStyles.None,
                BackColor = Color.Gainsboro,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Tahoma", 12F, FontStyle.Bold),
                Size = new Size(78, 34),
                Text = "10 km",
                UseVisualStyleBackColor = false
            };
            btnLargeMap.FlatAppearance.BorderSize = 1;
            btnLargeMap.Click += btnLargeMap_Click;

            tableLayoutPanel1.SetColumnSpan(cboxMapProvider, 2);
            tableLayoutPanel1.Controls.Add(cboxMapProvider, 0, 5);
            tableLayoutPanel1.Controls.Add(btnLargeMap, 2, 5);
        }

        private void ApplyMapProvider(string providerKey)
        {
            isLoadingProvider = true;

            if (string.Equals(providerKey, "Satellite", StringComparison.OrdinalIgnoreCase))
            {
                gMapControl.MapProvider = GMapProviders.BingSatelliteMap;
                cboxMapProvider.SelectedItem = "Bing Satellite";
            }
            else
            {
                gMapControl.MapProvider = GMapProviders.BingHybridMap;
                cboxMapProvider.SelectedItem = "Bing Hybrid";
            }

            isLoadingProvider = false;
        }

        private string GetSelectedMapProviderKey()
        {
            return string.Equals(cboxMapProvider.SelectedItem?.ToString(), "Bing Satellite", StringComparison.OrdinalIgnoreCase)
                ? "Satellite"
                : "Hybrid";
        }

        private void cboxMapProvider_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (isLoadingProvider) return;

            ApplyMapProvider(GetSelectedMapProviderKey());
            Properties.Settings.Default.setWindow_BingMapProvider = GetSelectedMapProviderKey();
            Properties.Settings.Default.Save();

            if (cboxDrawMap.Checked && !isAutomationCapture && !isShowingAllFields)
            {
                BingMap bingMap = CreateBingMap();
                if (bingMap == null)
                {
                    FormDialog.Show("BingMap Error", "Map Too Large", DialogSeverity.Error);
                    Log.EventWriter("BingMap, Map Too Large");
                    return;
                }

                SetAndSaveBingMap(bingMap);
            }
        }

        private void btnLargeMap_Click(object sender, EventArgs e)
        {
            SetLargeNavigationMapView(15);
            UpdateWindowTitle();
        }

        public void ShowAllFieldsOnMap()
        {
            isShowingAllFields = true;
            Text = "Fields map";
            ApplyMapProvider("Satellite");
            cboxEnableLineDraw.Checked = false;
            btnAddFence.Enabled = false;
            btnDeletePoint.Enabled = false;
            ResetPolygonAndMarkers();

            List<FieldMapShape> fields = LoadFieldMapShapes();
            if (fields.Count == 0)
            {
                FormDialog.Show("Fields map", "No fields with GPS position found.", DialogSeverity.Info);
                return;
            }

            double minLat = double.MaxValue;
            double maxLat = double.MinValue;
            double minLng = double.MaxValue;
            double maxLng = double.MinValue;

            foreach (FieldMapShape field in fields)
            {
                foreach (PointLatLng point in field.BoundsPoints)
                {
                    minLat = Math.Min(minLat, point.Lat);
                    maxLat = Math.Max(maxLat, point.Lat);
                    minLng = Math.Min(minLng, point.Lng);
                    maxLng = Math.Max(maxLng, point.Lng);
                }

                foreach (GMapPolygon fieldPolygon in field.Polygons)
                {
                    overlay.Polygons.Add(fieldPolygon);
                }

                foreach (GMapRoute route in field.Routes)
                {
                    overlay.Routes.Add(route);
                }

                overlay.Markers.Add(new GMapMarkerCircle(
                    field.LabelPosition,
                    6f,
                    field.Name));
            }

            FitMapToBounds(minLat, maxLat, minLng, maxLng);
            UpdateWindowTitle();
        }

        private List<FieldMapShape> LoadFieldMapShapes()
        {
            List<FieldMapShape> shapes = new List<FieldMapShape>();

            if (mf.AppModel.FieldsDirectory == null || !mf.AppModel.FieldsDirectory.Exists)
            {
                return shapes;
            }

            foreach (DirectoryInfo fieldDirectory in mf.AppModel.FieldsDirectory.GetDirectories())
            {
                FieldMapShape shape = TryLoadFieldMapShape(fieldDirectory);
                if (shape != null)
                {
                    shapes.Add(shape);
                }
            }

            return shapes;
        }

        private FieldMapShape TryLoadFieldMapShape(DirectoryInfo fieldDirectory)
        {
            try
            {
                Wgs84 origin = FieldPlaneFiles.LoadOrigin(fieldDirectory.FullName);
                LocalPlane plane = new LocalPlane(origin, new SharedFieldProperties());
                FieldMapShape shape = new FieldMapShape(fieldDirectory.Name, new PointLatLng(origin.Latitude, origin.Longitude));

                AddBoundaryGeometry(fieldDirectory.FullName, plane, shape);
                AddSectionGeometry(fieldDirectory.FullName, plane, shape);
                AddTrackGeometry(fieldDirectory.FullName, plane, shape);

                if (!shape.HasGeometry)
                {
                    shape.BoundsPoints.Add(shape.LabelPosition);
                }

                shape.UpdateLabelFromBounds();
                return shape.BoundsPoints.Count > 0 ? shape : null;
            }
            catch (Exception ex)
            {
                Log.EventWriter("Fields map skipped " + fieldDirectory.Name + ": " + ex.Message);
                return null;
            }
        }

        private void AddBoundaryGeometry(string fieldDirectory, LocalPlane plane, FieldMapShape shape)
        {
            List<CBoundaryList> boundaries = BoundaryFiles.Load(fieldDirectory);
            for (int i = 0; i < boundaries.Count; i++)
            {
                List<PointLatLng> points = ConvertVec3ListToMapPoints(boundaries[i].fenceLine, plane);
                if (points.Count < 3) continue;

                GMapPolygon fieldPolygon = new GMapPolygon(points, shape.Name + " boundary")
                {
                    Fill = Brushes.Transparent,
                    Stroke = new Pen(Color.FromArgb(40, 180, 80), 3f) { LineJoin = LineJoin.Round }
                };

                shape.Polygons.Add(fieldPolygon);
                shape.BoundsPoints.AddRange(points);
            }
        }

        private void AddSectionGeometry(string fieldDirectory, LocalPlane plane, FieldMapShape shape)
        {
            List<List<vec3>> patches = SectionsFiles.Load(fieldDirectory);
            if (patches.Count == 0) return;

            double minEast = double.MaxValue;
            double maxEast = double.MinValue;
            double minNorth = double.MaxValue;
            double maxNorth = double.MinValue;

            foreach (List<vec3> patch in patches)
            {
                for (int i = 1; i < patch.Count; i++)
                {
                    vec3 point = patch[i];
                    minEast = Math.Min(minEast, point.easting);
                    maxEast = Math.Max(maxEast, point.easting);
                    minNorth = Math.Min(minNorth, point.northing);
                    maxNorth = Math.Max(maxNorth, point.northing);
                }
            }

            if (minEast == double.MaxValue || maxEast <= minEast || maxNorth <= minNorth) return;

            List<vec3> box = new List<vec3>
            {
                new vec3(minEast, minNorth, 0),
                new vec3(maxEast, minNorth, 0),
                new vec3(maxEast, maxNorth, 0),
                new vec3(minEast, maxNorth, 0)
            };

            List<PointLatLng> points = ConvertVec3ListToMapPoints(box, plane);
            GMapPolygon sectionPolygon = new GMapPolygon(points, shape.Name + " marked")
            {
                Fill = new SolidBrush(Color.FromArgb(25, 80, 200, 80)),
                Stroke = new Pen(Color.FromArgb(90, 210, 90), 2f) { DashStyle = DashStyle.Dash }
            };

            shape.Polygons.Add(sectionPolygon);
            shape.BoundsPoints.AddRange(points);
        }

        private void AddTrackGeometry(string fieldDirectory, LocalPlane plane, FieldMapShape shape)
        {
            List<CTrk> tracks;
            try
            {
                tracks = TrackFiles.Load(fieldDirectory);
            }
            catch
            {
                return;
            }

            foreach (CTrk track in tracks)
            {
                if (!track.isVisible) continue;

                List<PointLatLng> points = new List<PointLatLng>();
                if (track.mode == TrackMode.AB)
                {
                    points.Add(ToMapPoint(new vec3(track.ptA.easting, track.ptA.northing, 0), plane));
                    points.Add(ToMapPoint(new vec3(track.ptB.easting, track.ptB.northing, 0), plane));
                }
                else if (track.curvePts != null && track.curvePts.Count > 1)
                {
                    points = ConvertVec3ListToMapPoints(track.curvePts, plane);
                }

                if (points.Count < 2) continue;

                GMapRoute route = new GMapRoute(points, shape.Name + " track")
                {
                    Stroke = new Pen(Color.FromArgb(40, 120, 255), 2f)
                };

                shape.Routes.Add(route);
                shape.BoundsPoints.AddRange(points);
            }
        }

        private List<PointLatLng> ConvertVec3ListToMapPoints(IEnumerable<vec3> points, LocalPlane plane)
        {
            List<PointLatLng> result = new List<PointLatLng>();
            foreach (vec3 point in points)
            {
                result.Add(ToMapPoint(point, plane));
            }

            return result;
        }

        private PointLatLng ToMapPoint(vec3 point, LocalPlane plane)
        {
            Wgs84 wgs = plane.ConvertGeoCoordToWgs84(point.ToGeoCoord());
            return new PointLatLng(wgs.Latitude, wgs.Longitude);
        }

        private void FitMapToBounds(double minLat, double maxLat, double minLng, double maxLng)
        {
            double latPadding = Math.Max(0.0005, (maxLat - minLat) * 0.15);
            double lngPadding = Math.Max(0.0005, (maxLng - minLng) * 0.15);

            RectLatLng rect = new RectLatLng(
                maxLat + latPadding,
                minLng - lngPadding,
                (maxLng - minLng) + (lngPadding * 2.0),
                (maxLat - minLat) + (latPadding * 2.0));

            if (!gMapControl.SetZoomToFitRect(rect))
            {
                gMapControl.Position = new PointLatLng(
                    (minLat + maxLat) * 0.5,
                    (minLng + maxLng) * 0.5);
                gMapControl.Zoom = 12;
            }
        }

        public async Task ActivateLargeNavigationBackgroundAsync()
        {
            await ActivateLargeNavigationBackgroundAsync(HighResolutionNavigationMapPixels);
        }

        private async Task ActivateLargeNavigationBackgroundAsync(int capturePixels)
        {
            try
            {
                isAutomationCapture = true;
                PrepareHighResolutionNavigationCapture(capturePixels);
                ApplyMapProvider("Satellite");
                cboxDrawMap.Checked = true;
                cboxDrawMap.Image = Properties.Resources.MappingOn;

                BingMap bingMap = await CreateLargeNavigationMapWithRetryAsync();

                SetAndSaveBingMap(bingMap);
            }
            catch (ObjectDisposedException)
            {
                if (capturePixels <= FallbackNavigationMapPixels) throw;

                Log.EventWriter("BingMap high resolution capture disposed, retrying smaller capture");
                await ActivateLargeNavigationBackgroundAsync(FallbackNavigationMapPixels);
            }
            finally
            {
                isAutomationCapture = false;
            }
        }

        private async Task<BingMap> CreateLargeNavigationMapWithRetryAsync()
        {
            for (int zoom = HighResolutionNavigationMapZoom; zoom <= 18; zoom++)
            {
                SetLargeNavigationMapView(zoom);
                await WaitForMapTilesAsync();

                BingMap bingMap = CreateNavigationBingMap();
                if (bingMap != null)
                {
                    return bingMap;
                }

                Log.EventWriter("BingMap too large at zoom " + zoom.ToString(CultureInfo.InvariantCulture) + ", retrying closer zoom");
            }

            throw new InvalidOperationException("Map is still too large. Try again after GPS/map position is stable.");
        }

        private BingMap CreateNavigationBingMap()
        {
            if (gMapControl == null || gMapControl.IsDisposed) return null;

            Bitmap bitmap = new Bitmap(gMapControl.Width, gMapControl.Height);
            gMapControl.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));

            if (!isColorMap)
            {
                bitmap = glm.MakeGrayscale3(bitmap);
            }

            GeoCoord center = mf.AppModel.LocalPlane.ConvertWgs84ToGeoCoord(mf.AppModel.CurrentLatLon);
            GeoCoord minCoord = new GeoCoord(
                center.Northing - NavigationMapHalfSizeMeters,
                center.Easting - NavigationMapHalfSizeMeters);
            GeoCoord maxCoord = new GeoCoord(
                center.Northing + NavigationMapHalfSizeMeters,
                center.Easting + NavigationMapHalfSizeMeters);

            return new BingMap(new GeoBoundingBox(minCoord, maxCoord), bitmap);
        }

        private void SetLargeNavigationMapView(int zoom)
        {
            gMapControl.Position = new PointLatLng(
                mf.AppModel.CurrentLatLon.Latitude,
                mf.AppModel.CurrentLatLon.Longitude);
            gMapControl.Zoom = zoom;
        }

        private void PrepareHighResolutionNavigationCapture(int capturePixels)
        {
            if (gMapControl == null || gMapControl.IsDisposed) return;

            WindowState = FormWindowState.Normal;
            int sidePanelWidth = tableLayoutPanel1.Width + 30;
            Size = new Size(
                capturePixels + sidePanelWidth,
                capturePixels + 45);
            Location = new Point(0, 0);
            PerformLayout();
        }

        private async Task WaitForMapTilesAsync()
        {
            if (gMapControl == null || gMapControl.IsDisposed) return;

            gMapControl.Refresh();
            Application.DoEvents();
            await Task.Delay(8000);
            if (gMapControl == null || gMapControl.IsDisposed) return;

            gMapControl.Refresh();
            Application.DoEvents();
        }

        private void btnExit_Click(object sender, EventArgs e)
        {
            isClosing = true;
            Close();
        }

        public void CloseFromAutomation()
        {
            isClosing = true;
            Close();
        }

        private void UpdateWindowTitle()
        {
            PointLatLng pos = gMapControl.FromLocalToLatLng(lastMouseLocation.X, lastMouseLocation.Y);
            Text = $"Mouse = {PointLatLngToString(pos)} / Zoom = {gMapControl.Zoom} ";
        }

        private void gMapControl_MouseMove(object sender, MouseEventArgs e)
        {
            lastMouseLocation = e.Location;
            UpdateWindowTitle();
        }

        private void gMapControl_MouseWheel(object sender, MouseEventArgs e)
        {
            UpdateWindowTitle();
        }

        private void btnGo_Click(object sender, EventArgs e)
        {
            isShowingAllFields = false;
            gMapControl.Position = new PointLatLng(
                mf.AppModel.CurrentLatLon.Latitude,
                mf.AppModel.CurrentLatLon.Longitude);
            if (polygon.Points.Count == 0)
            {
                overlay.Markers.Clear();

                // Create marker's location point
                var point = new PointLatLng(
                    mf.AppModel.CurrentLatLon.Latitude,
                    mf.AppModel.CurrentLatLon.Longitude);

                // Create marker instance: specify location on the map and radius
                var marker = new GMapMarkerCircle(point, 5f);

                // Add marker to the map
                overlay.Markers.Add(marker);
            }
            UpdateWindowTitle();
        }

        private void gMapControl_OnMapClick(PointLatLng pointClick, MouseEventArgs e)
        {
            if (isShowingAllFields) return;

            if (!cboxEnableLineDraw.Checked)
                return;

            if (polygon.Points.Count == 0)
                overlay.Markers.Clear();

            polygon.Points.Add(pointClick);
            gMapControl.UpdatePolygonLocalPosition(polygon);

            // Create marker instance: specify location on the map, radius and label
            var marker = new GMapMarkerCircle(pointClick, 4f, polygon.Points.Count.ToString());

            // Add marker to the map
            overlay.Markers.Add(marker);
        }

        private void btnDeletePoint_Click(object sender, EventArgs e)
        {
            if (polygon.Points.Count == 0)
                return;

            string sNum = polygon.Points.Count.ToString();

            polygon.Points.RemoveAt(polygon.Points.Count - 1);
            gMapControl.UpdatePolygonLocalPosition(polygon);

            foreach (var marker in overlay.Markers.OfType<GMapMarkerCircle>())
            {
                if (marker.Label == sNum)
                {
                    overlay.Markers.Remove(marker);
                    break;
                }
            }
        }

        private void btnAddFence_Click(object sender, EventArgs e)
        {
            if (polygon.Points.Count > 2)
            {
                CBoundaryList New = new CBoundaryList();
                foreach (var point in polygon.Points)
                {
                    GeoCoord geoCoord = mf.AppModel.LocalPlane.ConvertWgs84ToGeoCoord(new Wgs84(point.Lat, point.Lng));
                    New.fenceLine.Add(new vec3(geoCoord));
                }

                New.CalculateFenceArea(mf.bnd.bndList.Count);
                New.FixFenceLine(mf.bnd.bndList.Count);

                mf.bnd.bndList.Add(New);
                mf.fd.UpdateFieldBoundaryGUIAreas();

                //turn lines made from boundaries
                mf.CalculateMinMax();
                mf.FileSaveBoundary();
                mf.bnd.BuildTurnLines();
                mf.btnABDraw.Visible = true;
            }

            cboxEnableLineDraw.Checked = false;

            //clean up line
            ResetPolygonAndMarkers();

            btnAddFence.Enabled = false;
            btnDeletePoint.Enabled = false;
        }

        private void btnDeleteAll_Click(object sender, EventArgs e)
        {
            if (polygon.Points.Count > 0)
            {
                ResetPolygonAndMarkers();
                return;
            }

            if (mf.bnd.bndList == null || mf.bnd.bndList.Count == 0)
            {
                FormDialog.Show(gStr.gsBoundary, gStr.gsNoBoundary, DialogSeverity.Error);
                return;
            }

            DialogResult result = FormDialog.ShowQuestion(
                gStr.gsDeleteForSure,
                "Delete Last Field Boundary Made?");


            if (result == DialogResult.OK)
            {
                int cnt = mf.bnd.bndList.Count;
                mf.bnd.bndList[cnt - 1].hdLine?.Clear();
                mf.bnd.bndList.RemoveAt(cnt - 1);

                mf.FileSaveBoundary();
                mf.bnd.BuildTurnLines();
                mf.fd.UpdateFieldBoundaryGUIAreas();
                mf.btnABDraw.Visible = false;
            }

            cboxEnableLineDraw.Checked = false;

            //clean up line
            ResetPolygonAndMarkers();

            btnAddFence.Enabled = false;
            btnDeletePoint.Enabled = false;
        }

        private void cboxEnableLineDraw_Click(object sender, EventArgs e)
        {
            isShowingAllFields = false;

            if (cboxEnableLineDraw.Checked)
            {
                FormDialog.Show("Boundary Create Mode", "Touch Map to Create The Boundary", DialogSeverity.Info);
                btnAddFence.Enabled = true;
                btnDeletePoint.Enabled = true;
                Log.EventWriter("Bing Touch Boundary started");
            }
            else
            {
                btnAddFence.Enabled = false;
                btnDeletePoint.Enabled = false;
            }

            ResetPolygonAndMarkers();
        }

        private GeoCoord GeoCoordFormMapPoint(PointLatLng mapPoint)
        {
            return mf.AppModel.LocalPlane.ConvertWgs84ToGeoCoord(new Wgs84(mapPoint.Lat, mapPoint.Lng));
        }

        // Returns null if bingMap is too big
        private BingMap CreateBingMap()
        {
            BingMap bingMap = null;
            PointLatLng topLeft = gMapControl.ViewArea.LocationTopLeft;
            PointLatLng bottomRight = gMapControl.ViewArea.LocationRightBottom;
            GeoBoundingBox geoBoundingBox = GeoBoundingBox.CreateEmpty();

            geoBoundingBox.Include(GeoCoordFormMapPoint(topLeft));
            geoBoundingBox.Include(GeoCoordFormMapPoint(bottomRight));

            double widthMeters = Math.Abs(geoBoundingBox.MaxEasting - geoBoundingBox.MinEasting);
            double heightMeters = Math.Abs(geoBoundingBox.MaxNorthing - geoBoundingBox.MinNorthing);
            bool tooBig = widthMeters > MaxNavigationMapMeters || heightMeters > MaxNavigationMapMeters;
            if (!tooBig)
            {
                Bitmap bitmap = new Bitmap(gMapControl.Width, gMapControl.Height);
                gMapControl.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));

                if (!isColorMap)
                {
                    bitmap = glm.MakeGrayscale3(bitmap);
                }
                bingMap = new BingMap(geoBoundingBox, bitmap);
            }
            return bingMap;
        }

        private void SetAndSaveBingMap(BingMap bingMap)
        {
            mf.worldGrid.BingMap = bingMap;

            if (mf.AppCore.ActiveField != null)
            {
                BingMapStreamer streamer = new BingMapStreamer();
                streamer.TryWrite(bingMap, mf.AppCore.ActiveField.FieldDirectory);
            }
        }

        private void cboxDrawMap_Click(object sender, EventArgs e)
        {
            if (polygon.Points.Count > 0)
            {
                FormDialog.Show(gStr.gsBoundary, "Finish Making Boundary", DialogSeverity.Info);
                cboxDrawMap.Checked = !cboxDrawMap.Checked;
                return;
            }

            if (cboxDrawMap.Checked)
            {
                cboxDrawMap.Image = Properties.Resources.MappingOn;
                BingMap bingMap = CreateBingMap();
                if (bingMap == null)
                {
                    FormDialog.Show("BingMap Error", "Map Too Large", DialogSeverity.Error);
                    Log.EventWriter("BingMap, Map Too Large");
                }
                SetAndSaveBingMap(bingMap);
            }
            else
            {
                cboxDrawMap.Image = Properties.Resources.MappingOff;
                ResetMapGrid();
            }
        }

        private void ResetMapGrid()
        {
            SetAndSaveBingMap(null);
            ResetPolygonAndMarkers();
        }

        private void ResetPolygonAndMarkers()
        {
            polygon.Points.Clear();
            gMapControl.UpdatePolygonLocalPosition(polygon);
            overlay.Markers.Clear();
        }

        private void btnZoomOut_Click(object sender, EventArgs e)
        {
            int zoom = (int)gMapControl.Zoom;
            zoom--;
            if (zoom < 12) zoom = 12;
            gMapControl.Zoom = zoom;//mapControl
            UpdateWindowTitle();
        }

        private void btnZoomIn_Click(object sender, EventArgs e)
        {
            int zoom = (int)gMapControl.Zoom;
            zoom++;
            if (zoom > 19) zoom = 19;
            gMapControl.Zoom = zoom;//mapControl
            UpdateWindowTitle();
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            lblPoints.Text = (polygon.Points.Count > 0) ?
                (gStr.gsPoints + ": " + polygon.Points.Count.ToString()) :
                "";

            if (mf.bnd.bndList.Count == 0)
            {
                lblBnds.Text = gStr.gsNone;
            }
            else
            {
                lblBnds.Text = "1 " + gStr.gsOuter + "\r\n";
                if (1 < mf.bnd.bndList.Count)
                {
                    lblBnds.Text += (mf.bnd.bndList.Count - 1).ToString() + " " + gStr.gsInner;
                }
            }
        }

        private static string PointLatLngToString(PointLatLng point)
        {
            return point.Lat.ToString("N7") + ", " + point.Lng.ToString("N7");
        }

        private class FieldMapShape
        {
            public FieldMapShape(string name, PointLatLng fallbackPosition)
            {
                Name = name;
                LabelPosition = fallbackPosition;
            }

            public string Name { get; }
            public List<PointLatLng> BoundsPoints { get; } = new List<PointLatLng>();
            public List<GMapPolygon> Polygons { get; } = new List<GMapPolygon>();
            public List<GMapRoute> Routes { get; } = new List<GMapRoute>();
            public PointLatLng LabelPosition { get; private set; }
            public bool HasGeometry => Polygons.Count > 0 || Routes.Count > 0;

            public void UpdateLabelFromBounds()
            {
                if (BoundsPoints.Count == 0) return;

                double lat = 0;
                double lng = 0;
                for (int i = 0; i < BoundsPoints.Count; i++)
                {
                    lat += BoundsPoints[i].Lat;
                    lng += BoundsPoints[i].Lng;
                }

                LabelPosition = new PointLatLng(lat / BoundsPoints.Count, lng / BoundsPoints.Count);
            }
        }

        private class GMapMarkerCircle : GMapMarker
        {
            private readonly float _radius;
            private readonly Brush _brush = Brushes.Red;
            private readonly Font _font = new Font("Tahoma", 10F, FontStyle.Bold);
            private readonly Brush _labelBrush = Brushes.Black;
            private readonly Brush _labelBackBrush = new SolidBrush(Color.FromArgb(220, Color.White));

            public GMapMarkerCircle(PointLatLng pos, float radius, string label = null)
                : base(pos)
            {
                _radius = radius;
                Label = label;
            }

            public string Label { get; }

            public override void OnRender(Graphics g)
            {
                g.FillEllipse(_brush, LocalPosition.X - _radius, LocalPosition.Y - _radius, 2 * _radius, 2 * _radius);

                if (Label != null)
                {
                    SizeF labelSize = g.MeasureString(Label, _font);
                    RectangleF backRect = new RectangleF(
                        LocalPosition.X + _radius + 4,
                        LocalPosition.Y - labelSize.Height * 0.5f,
                        labelSize.Width + 6,
                        labelSize.Height + 2);

                    g.FillRectangle(_labelBackBrush, backRect);
                    g.DrawString(Label, _font, _labelBrush, backRect.Left + 3, backRect.Top + 1);
                }
            }
        }

        private class SilentFieldStreamerPresenter : IFieldStreamerPresenter
        {
            public void PresentBoundaryFileMissing() { }
            public void PresentBoundaryFileCorrupt() { }
            public void PresentContourFileMissing() { }
            public void PresentContourFileCorrupt() { }
            public void PresentCurveLineFileCorrupt() { }
            public void PresentFlagsFileMissing() { }
            public void PresentFlagsFileCorrupt() { }
            public void PresentRecordedPathFileCorrupt() { }
            public void PresentSectionFileMissing() { }
            public void PresentSectionFileCorrupt() { }
            public void PresentTramLinesFileCorrupt() { }
        }
    }
}
