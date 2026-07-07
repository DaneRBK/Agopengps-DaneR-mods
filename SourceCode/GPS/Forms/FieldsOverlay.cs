using AgLibrary.Logging;
using AgOpenGPS.Core.Models;
using AgOpenGPS.IO;
using OpenTK.Graphics.OpenGL;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;

namespace AgOpenGPS
{
    public partial class FormGPS
    {
        private const double FieldsOverlayNameDistanceMeters = 500.0;
        private const double FieldsOverlayAutoOpenDistanceMeters = 80.0;
        private const double FieldsOverlayAutoOpenMaxSpeedKmh = 1.0;
        private const double FieldsOverlayAutoOpenDelaySeconds = 10.0;
        private readonly List<FieldsOverlayShape> fieldsOverlayShapes = new List<FieldsOverlayShape>();
        private bool isFieldsOverlayOn;
        private bool isFieldsOverlayAutoOpening;
        private string nearestFieldsOverlayName = string.Empty;
        private string fieldsOverlayAutoOpenCandidateName = string.Empty;
        private DateTime lastFieldsOverlayNameTime = DateTime.MinValue;
        private DateTime fieldsOverlayAutoOpenCandidateSince = DateTime.MinValue;

        private void ToggleFieldsOverlay()
        {
            isFieldsOverlayOn = !isFieldsOverlayOn;

            if (isFieldsOverlayOn)
            {
                LoadFieldsOverlayShapes();
                TimedMessageBox(2000, "Fields", fieldsOverlayShapes.Count.ToString() + " fields drawn on main map");
            }
            else
            {
                fieldsOverlayShapes.Clear();
                nearestFieldsOverlayName = string.Empty;
                ResetFieldsOverlayAutoOpenCandidate();
                TimedMessageBox(1500, "Fields", "Fields overlay OFF");
            }

            UpdateFieldsOverlayButtonAppearance();
            oglMain?.Refresh();
        }

        private void UpdateFieldsOverlayButtonAppearance()
        {
            if (btnFieldsMap == null) return;

            btnFieldsMap.BackColor = isFieldsOverlayOn
                ? Color.FromArgb(116, 190, 92)
                : Color.FromArgb(185, 185, 185);
        }

        private void LoadFieldsOverlayShapes()
        {
            fieldsOverlayShapes.Clear();
            nearestFieldsOverlayName = string.Empty;

            if (AppModel.FieldsDirectory == null || !AppModel.FieldsDirectory.Exists)
            {
                return;
            }

            foreach (DirectoryInfo fieldDirectory in AppModel.FieldsDirectory.GetDirectories())
            {
                FieldsOverlayShape shape = TryLoadFieldsOverlayShape(fieldDirectory);
                if (shape != null)
                {
                    fieldsOverlayShapes.Add(shape);
                }
            }
        }

        private FieldsOverlayShape TryLoadFieldsOverlayShape(DirectoryInfo fieldDirectory)
        {
            try
            {
                Wgs84 origin = FieldPlaneFiles.LoadOrigin(fieldDirectory.FullName);
                LocalPlane sourcePlane = new LocalPlane(origin, new SharedFieldProperties());
                FieldsOverlayShape shape = new FieldsOverlayShape(
                    fieldDirectory.Name,
                    Path.Combine(fieldDirectory.FullName, "Field.txt"));

                AddFieldsOverlayBoundaries(fieldDirectory.FullName, sourcePlane, shape);
                AddFieldsOverlaySections(fieldDirectory.FullName, sourcePlane, shape);
                AddFieldsOverlayTracks(fieldDirectory.FullName, sourcePlane, shape);

                if (!shape.HasGeometry)
                {
                    GeoCoord originCoord = AppModel.LocalPlane.ConvertWgs84ToGeoCoord(origin);
                    shape.LabelPosition = new vec3(originCoord.Easting, originCoord.Northing, 0);
                    shape.BoundsPoints.Add(shape.LabelPosition.Value);
                }

                shape.UpdateLabelPosition();
                return shape.BoundsPoints.Count > 0 ? shape : null;
            }
            catch (Exception ex)
            {
                Log.EventWriter("Fields overlay skipped " + fieldDirectory.Name + ": " + ex.Message);
                return null;
            }
        }

        private void AddFieldsOverlayBoundaries(string fieldDirectory, LocalPlane sourcePlane, FieldsOverlayShape shape)
        {
            List<CBoundaryList> boundaries = BoundaryFiles.Load(fieldDirectory);
            foreach (CBoundaryList boundary in boundaries)
            {
                List<vec3> line = ConvertFieldsOverlayPoints(boundary.fenceLine, sourcePlane);
                if (line.Count < 3) continue;

                shape.Boundaries.Add(line);
                shape.BoundsPoints.AddRange(line);
            }
        }

        private void AddFieldsOverlaySections(string fieldDirectory, LocalPlane sourcePlane, FieldsOverlayShape shape)
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

            List<vec3> overlayBox = ConvertFieldsOverlayPoints(box, sourcePlane);
            if (overlayBox.Count < 3) return;

            shape.MarkedAreas.Add(overlayBox);
            shape.BoundsPoints.AddRange(overlayBox);
        }

        private void AddFieldsOverlayTracks(string fieldDirectory, LocalPlane sourcePlane, FieldsOverlayShape shape)
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

                List<vec3> points = new List<vec3>();
                if (track.mode == TrackMode.AB)
                {
                    points.Add(new vec3(track.ptA.easting, track.ptA.northing, 0));
                    points.Add(new vec3(track.ptB.easting, track.ptB.northing, 0));
                }
                else if (track.curvePts != null && track.curvePts.Count > 1)
                {
                    points.AddRange(track.curvePts);
                }

                List<vec3> overlayTrack = ConvertFieldsOverlayPoints(points, sourcePlane);
                if (overlayTrack.Count < 2) continue;

                shape.Tracks.Add(overlayTrack);
                shape.BoundsPoints.AddRange(overlayTrack);
            }
        }

        private List<vec3> ConvertFieldsOverlayPoints(IEnumerable<vec3> points, LocalPlane sourcePlane)
        {
            List<vec3> result = new List<vec3>();

            foreach (vec3 point in points)
            {
                Wgs84 wgs = sourcePlane.ConvertGeoCoordToWgs84(point.ToGeoCoord());
                GeoCoord local = AppModel.LocalPlane.ConvertWgs84ToGeoCoord(wgs);
                result.Add(new vec3(local.Easting, local.Northing, point.heading));
            }

            return result;
        }

        private void DrawFieldsOverlay()
        {
            if (!isFieldsOverlayOn || fieldsOverlayShapes.Count == 0) return;

            GL.Disable(EnableCap.Texture2D);
            GL.LineWidth(2.5f);

            foreach (FieldsOverlayShape shape in fieldsOverlayShapes)
            {
                GL.Color4(0.05f, 0.85f, 0.20f, 0.95f);
                foreach (List<vec3> boundary in shape.Boundaries)
                {
                    DrawFieldsOverlayLine(boundary, PrimitiveType.LineLoop, 0.10);
                }

                GL.LineWidth(2.0f);
                GL.Enable(EnableCap.LineStipple);
                GL.LineStipple(1, 0x0F0F);
                GL.Color4(0.45f, 1.0f, 0.35f, 0.85f);
                foreach (List<vec3> markedArea in shape.MarkedAreas)
                {
                    DrawFieldsOverlayLine(markedArea, PrimitiveType.LineLoop, 0.11);
                }
                GL.Disable(EnableCap.LineStipple);

                GL.LineWidth(2.0f);
                GL.Color4(0.15f, 0.50f, 1.0f, 0.95f);
                foreach (List<vec3> track in shape.Tracks)
                {
                    DrawFieldsOverlayLine(track, PrimitiveType.LineStrip, 0.12);
                }

                if (shape.LabelPosition.HasValue && ShouldDrawFieldsOverlayName(shape.LabelPosition.Value))
                {
                    GL.Color3(1.0f, 1.0f, 0.2f);
                    font.DrawText3D(
                        shape.LabelPosition.Value.easting,
                        shape.LabelPosition.Value.northing,
                        shape.Name,
                        camHeading,
                        0.85);
                }
            }

            UpdateNearestFieldsOverlayName();
            CheckFieldsOverlayAutoOpen();
        }

        private static void DrawFieldsOverlayLine(List<vec3> points, PrimitiveType primitiveType, double z)
        {
            if (points == null || points.Count < 2) return;

            GL.Begin(primitiveType);
            foreach (vec3 point in points)
            {
                GL.Vertex3(point.easting, point.northing, z);
            }
            GL.End();
        }

        private bool ShouldDrawFieldsOverlayName(vec3 labelPosition)
        {
            double dEast = labelPosition.easting - pivotAxlePos.easting;
            double dNorth = labelPosition.northing - pivotAxlePos.northing;
            return (dEast * dEast) + (dNorth * dNorth) <= FieldsOverlayNameDistanceMeters * FieldsOverlayNameDistanceMeters;
        }

        private void UpdateNearestFieldsOverlayName()
        {
            string bestName = string.Empty;
            double bestDistanceSquared = double.MaxValue;

            foreach (FieldsOverlayShape shape in fieldsOverlayShapes)
            {
                if (!shape.LabelPosition.HasValue) continue;

                vec3 label = shape.LabelPosition.Value;
                double dEast = label.easting - pivotAxlePos.easting;
                double dNorth = label.northing - pivotAxlePos.northing;
                double distanceSquared = (dEast * dEast) + (dNorth * dNorth);

                if (distanceSquared < bestDistanceSquared)
                {
                    bestDistanceSquared = distanceSquared;
                    bestName = shape.Name;
                }
            }

            if (bestDistanceSquared > FieldsOverlayNameDistanceMeters * FieldsOverlayNameDistanceMeters)
            {
                nearestFieldsOverlayName = string.Empty;
                return;
            }

            DateTime now = DateTime.UtcNow;
            if (bestName == nearestFieldsOverlayName && (now - lastFieldsOverlayNameTime).TotalSeconds < 20)
            {
                return;
            }

            nearestFieldsOverlayName = bestName;
            lastFieldsOverlayNameTime = now;
            TimedMessageBox(1500, "Field", bestName);
        }

        private void CheckFieldsOverlayAutoOpen()
        {
            if (!isFieldsOverlayOn || isJobStarted || isFieldsOverlayAutoOpening || fieldsOverlayShapes.Count == 0)
            {
                ResetFieldsOverlayAutoOpenCandidate();
                return;
            }

            if (Math.Abs(avgSpeed) >= FieldsOverlayAutoOpenMaxSpeedKmh)
            {
                ResetFieldsOverlayAutoOpenCandidate();
                return;
            }

            FieldsOverlayShape nearestShape = null;
            double nearestDistanceSquared = FieldsOverlayAutoOpenDistanceMeters * FieldsOverlayAutoOpenDistanceMeters;

            foreach (FieldsOverlayShape shape in fieldsOverlayShapes)
            {
                double distanceSquared = GetDistanceToFieldsOverlayShapeSquared(pivotAxlePos, shape);
                if (distanceSquared <= nearestDistanceSquared)
                {
                    nearestDistanceSquared = distanceSquared;
                    nearestShape = shape;
                }
            }

            if (nearestShape == null)
            {
                ResetFieldsOverlayAutoOpenCandidate();
                return;
            }

            DateTime now = DateTime.UtcNow;
            if (!string.Equals(fieldsOverlayAutoOpenCandidateName, nearestShape.Name, StringComparison.OrdinalIgnoreCase))
            {
                fieldsOverlayAutoOpenCandidateName = nearestShape.Name;
                fieldsOverlayAutoOpenCandidateSince = now;
                TimedMessageBox(1500, "Field nearby", nearestShape.Name + " - hold under 1 km/h");
                return;
            }

            if ((now - fieldsOverlayAutoOpenCandidateSince).TotalSeconds < FieldsOverlayAutoOpenDelaySeconds)
            {
                return;
            }

            AutoOpenFieldsOverlayField(nearestShape);
        }

        private async void AutoOpenFieldsOverlayField(FieldsOverlayShape shape)
        {
            if (shape == null || isFieldsOverlayAutoOpening || isJobStarted) return;

            isFieldsOverlayAutoOpening = true;
            string fieldFilePath = shape.FieldFilePath;
            string fieldName = shape.Name;

            try
            {
                TimedMessageBox(1500, "Opening field", fieldName);
                await FileOpenField(fieldFilePath);

                if (isJobStarted)
                {
                    isFieldsOverlayOn = false;
                    fieldsOverlayShapes.Clear();
                    UpdateFieldsOverlayButtonAppearance();
                    TimedMessageBox(2000, "Field opened", fieldName);
                }
            }
            catch (Exception ex)
            {
                Log.EventWriter("Fields overlay auto open failed " + fieldName + ": " + ex.Message);
                TimedMessageBox(2000, "Field open failed", fieldName);
            }
            finally
            {
                isFieldsOverlayAutoOpening = false;
                ResetFieldsOverlayAutoOpenCandidate();
            }
        }

        private void ResetFieldsOverlayAutoOpenCandidate()
        {
            fieldsOverlayAutoOpenCandidateName = string.Empty;
            fieldsOverlayAutoOpenCandidateSince = DateTime.MinValue;
        }

        private static double GetDistanceToFieldsOverlayShapeSquared(vec3 position, FieldsOverlayShape shape)
        {
            double best = double.MaxValue;

            foreach (List<vec3> boundary in shape.Boundaries)
            {
                if (IsPointInsideFieldsOverlayPolygon(position, boundary)) return 0.0;
                best = Math.Min(best, DistanceToFieldsOverlayLineSquared(position, boundary, true));
            }

            foreach (List<vec3> markedArea in shape.MarkedAreas)
            {
                if (IsPointInsideFieldsOverlayPolygon(position, markedArea)) return 0.0;
                best = Math.Min(best, DistanceToFieldsOverlayLineSquared(position, markedArea, true));
            }

            foreach (List<vec3> track in shape.Tracks)
            {
                best = Math.Min(best, DistanceToFieldsOverlayLineSquared(position, track, false));
            }

            foreach (vec3 point in shape.BoundsPoints)
            {
                best = Math.Min(best, DistanceSquared(position, point));
            }

            return best;
        }

        private static double DistanceToFieldsOverlayLineSquared(vec3 position, List<vec3> points, bool closed)
        {
            if (points == null || points.Count == 0) return double.MaxValue;
            if (points.Count == 1) return DistanceSquared(position, points[0]);

            double best = double.MaxValue;
            for (int i = 1; i < points.Count; i++)
            {
                best = Math.Min(best, DistanceToSegmentSquared(position, points[i - 1], points[i]));
            }

            if (closed && points.Count > 2)
            {
                best = Math.Min(best, DistanceToSegmentSquared(position, points[points.Count - 1], points[0]));
            }

            return best;
        }

        private static double DistanceToSegmentSquared(vec3 point, vec3 a, vec3 b)
        {
            double abEast = b.easting - a.easting;
            double abNorth = b.northing - a.northing;
            double abLenSquared = (abEast * abEast) + (abNorth * abNorth);

            if (abLenSquared < 0.000001)
            {
                return DistanceSquared(point, a);
            }

            double apEast = point.easting - a.easting;
            double apNorth = point.northing - a.northing;
            double t = ((apEast * abEast) + (apNorth * abNorth)) / abLenSquared;
            t = Math.Max(0.0, Math.Min(1.0, t));

            double closestEast = a.easting + (abEast * t);
            double closestNorth = a.northing + (abNorth * t);
            double dEast = point.easting - closestEast;
            double dNorth = point.northing - closestNorth;
            return (dEast * dEast) + (dNorth * dNorth);
        }

        private static double DistanceSquared(vec3 a, vec3 b)
        {
            double dEast = a.easting - b.easting;
            double dNorth = a.northing - b.northing;
            return (dEast * dEast) + (dNorth * dNorth);
        }

        private static bool IsPointInsideFieldsOverlayPolygon(vec3 point, List<vec3> polygon)
        {
            if (polygon == null || polygon.Count < 3) return false;

            bool inside = false;
            int j = polygon.Count - 1;

            for (int i = 0; i < polygon.Count; i++)
            {
                bool crosses = ((polygon[i].northing > point.northing) != (polygon[j].northing > point.northing))
                    && (point.easting < (polygon[j].easting - polygon[i].easting)
                    * (point.northing - polygon[i].northing)
                    / (polygon[j].northing - polygon[i].northing)
                    + polygon[i].easting);

                if (crosses) inside = !inside;
                j = i;
            }

            return inside;
        }

        private sealed class FieldsOverlayShape
        {
            public FieldsOverlayShape(string name, string fieldFilePath)
            {
                Name = name;
                FieldFilePath = fieldFilePath;
            }

            public string Name { get; }
            public string FieldFilePath { get; }
            public List<List<vec3>> Boundaries { get; } = new List<List<vec3>>();
            public List<List<vec3>> MarkedAreas { get; } = new List<List<vec3>>();
            public List<List<vec3>> Tracks { get; } = new List<List<vec3>>();
            public List<vec3> BoundsPoints { get; } = new List<vec3>();
            public vec3? LabelPosition { get; set; }
            public bool HasGeometry => Boundaries.Count > 0 || MarkedAreas.Count > 0 || Tracks.Count > 0;

            public void UpdateLabelPosition()
            {
                if (BoundsPoints.Count == 0) return;

                double minEast = double.MaxValue;
                double maxEast = double.MinValue;
                double minNorth = double.MaxValue;
                double maxNorth = double.MinValue;

                foreach (vec3 point in BoundsPoints)
                {
                    minEast = Math.Min(minEast, point.easting);
                    maxEast = Math.Max(maxEast, point.easting);
                    minNorth = Math.Min(minNorth, point.northing);
                    maxNorth = Math.Max(maxNorth, point.northing);
                }

                LabelPosition = new vec3(
                    (minEast + maxEast) * 0.5,
                    (minNorth + maxNorth) * 0.5,
                    0);
            }
        }
    }
}
