using AgOpenGPS.Core.Models;
using AgOpenGPS.Core.DrawLib;
using AgOpenGPS.IO;
using OpenTK.Graphics.OpenGL;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Media;
using System.Windows.Forms;

namespace AgOpenGPS
{
    public partial class FormGPS
    {
        private const string ObstacleTagStart = "[AOG_OBST:";
        private const string ObstacleTypePole = "POLE";
        private const string ObstacleTypeHole = "HOLE";
        private const string ObstacleTypeHose = "HOSE";

        private bool isObstacleTouchMode;
        private bool hasPendingObstacle;
        private bool isPendingObstacleDragging;
        private double pendingObstacleEasting;
        private double pendingObstacleNorthing;
        private string obstacleTouchNotes = "Obstacle";
        private string obstacleTouchType = ObstacleTypePole;
        private double obstacleTouchWidth = 0.3;
        private double obstacleTouchLength = 0.3;
        private string pendingObstacleNotes = "Obstacle";
        private string pendingObstacleType = ObstacleTypePole;
        private double pendingObstacleWidth = 0.3;
        private double pendingObstacleLength = 0.3;
        private Timer obstacleDeleteHoldTimer;
        private bool isObstacleDeleteHoldActive;
        private int obstacleDeleteHoldFlagIndex = -1;
        private Point obstacleDeleteHoldStartPoint;
        private Timer obstacleAlarmTimer;
        private bool isObstacleAlarmEnabled;
        private bool isObstacleAlarmActive;
        private double obstacleAlarmDistanceMeters = 10.0;
        private DateTime obstacleAlarmLastBeepUtc = DateTime.MinValue;

        public bool IsObstacleAlarmEnabled => isObstacleAlarmEnabled;
        public double ObstacleAlarmDistanceMeters => obstacleAlarmDistanceMeters;

        public bool TryGetNearestObstacleDistance(out string obstacleName, out double distanceMeters)
        {
            obstacleName = string.Empty;
            distanceMeters = double.MaxValue;

            if (!isJobStarted || flagPts.Count == 0)
            {
                return false;
            }

            double tractorEasting = pivotAxlePos.easting;
            double tractorNorthing = pivotAxlePos.northing;
            if (double.IsNaN(tractorEasting) || double.IsNaN(tractorNorthing))
            {
                return false;
            }

            for (int i = 0; i < flagPts.Count; i++)
            {
                double distance = GetObstacleDistance(flagPts[i], tractorEasting, tractorNorthing);
                if (distance < distanceMeters)
                {
                    distanceMeters = distance;
                    obstacleName = StripObstacleTag(flagPts[i].notes);
                }
            }

            if (string.IsNullOrWhiteSpace(obstacleName))
            {
                obstacleName = "Obstacle";
            }

            return distanceMeters < double.MaxValue;
        }

        public void AddObstacleFlag(double forwardMeters, double rightMeters, string notes)
        {
            AddObstacleFlag(forwardMeters, rightMeters, notes, ObstacleTypePole, 0.3, 0.3);
        }

        public void AddObstacleFlag(double forwardMeters, double rightMeters, string notes, string obstacleType, double widthMeters, double lengthMeters)
        {
            double heading = fixHeading;
            double easting = pn.fix.easting
                + (Math.Sin(heading) * forwardMeters)
                + (Math.Sin(heading + glm.PIBy2) * rightMeters);
            double northing = pn.fix.northing
                + (Math.Cos(heading) * forwardMeters)
                + (Math.Cos(heading + glm.PIBy2) * rightMeters);

            SaveObstacleFlagAtWorld(easting, northing, notes, obstacleType, widthMeters, lengthMeters);
        }

        public void SetPendingObstacleByOffset(double forwardMeters, double rightMeters, string notes)
        {
            SetPendingObstacleByOffset(forwardMeters, rightMeters, notes, ObstacleTypePole, 0.3, 0.3);
        }

        public void SetPendingObstacleByOffset(double forwardMeters, double rightMeters, string notes, string obstacleType, double widthMeters, double lengthMeters)
        {
            double heading = fixHeading;
            double easting = pn.fix.easting
                + (Math.Sin(heading) * forwardMeters)
                + (Math.Sin(heading + glm.PIBy2) * rightMeters);
            double northing = pn.fix.northing
                + (Math.Cos(heading) * forwardMeters)
                + (Math.Cos(heading + glm.PIBy2) * rightMeters);

            SetPendingObstacleAtWorld(easting, northing, notes, obstacleType, widthMeters, lengthMeters, true);
        }

        public void UpdatePendingObstacleDetails(string notes, string obstacleType, double widthMeters, double lengthMeters)
        {
            obstacleType = NormalizeObstacleType(obstacleType);
            obstacleTouchNotes = string.IsNullOrWhiteSpace(notes) ? "Obstacle" : notes.Trim();
            obstacleTouchType = obstacleType;
            obstacleTouchWidth = obstacleType == ObstacleTypePole ? 0.3 : Math.Max(0.05, widthMeters);
            obstacleTouchLength = obstacleType == ObstacleTypePole ? 0.3 : Math.Max(0.05, lengthMeters);

            if (!hasPendingObstacle)
            {
                return;
            }

            pendingObstacleNotes = obstacleTouchNotes;
            pendingObstacleType = obstacleType;
            pendingObstacleWidth = obstacleTouchWidth;
            pendingObstacleLength = obstacleTouchLength;
            oglMain.Refresh();
        }

        public void StartObstacleTouchMode(string notes)
        {
            StartObstacleTouchMode(notes, ObstacleTypePole, 0.3, 0.3);
        }

        public void StartObstacleTouchMode(string notes, string obstacleType, double widthMeters, double lengthMeters)
        {
            if (!isJobStarted)
            {
                TimedMessageBox(2500, "Obstacle", "Open field first");
                return;
            }

            obstacleTouchNotes = string.IsNullOrWhiteSpace(notes) ? "Obstacle" : notes.Trim();
            obstacleTouchType = NormalizeObstacleType(obstacleType);
            if (obstacleTouchType == ObstacleTypeHose)
            {
                if (!HasActiveABLine())
                {
                    TimedMessageBox(2500, "Obstacle", "Make AB line first");
                    return;
                }

                if (bnd?.bndList == null || bnd.bndList.Count == 0 || bnd.bndList[0].fenceLine == null || bnd.bndList[0].fenceLine.Count < 3)
                {
                    TimedMessageBox(2500, "Obstacle", "Create boundary first");
                    return;
                }
            }

            obstacleTouchWidth = Math.Max(0.05, widthMeters);
            obstacleTouchLength = Math.Max(0.05, lengthMeters);
            isObstacleTouchMode = true;
            TimedMessageBox(3500, "Obstacle", "Touch map, drag, then Save");
        }

        public void SetPendingHoseAtFieldCenter(string notes)
        {
            if (!isJobStarted)
            {
                TimedMessageBox(2500, "Obstacle", "Open field first");
                return;
            }

            if (!HasActiveABLine())
            {
                TimedMessageBox(2500, "Obstacle", "Make AB line first");
                return;
            }

            if (!TryGetOuterBoundaryCenter(out double easting, out double northing))
            {
                TimedMessageBox(2500, "Obstacle", "Create boundary first");
                return;
            }

            SetPendingObstacleAtWorld(easting, northing, notes, ObstacleTypeHose, 0.0, 0.0, true);
        }

        public bool SavePendingObstacleFlag()
        {
            if (!hasPendingObstacle)
            {
                TimedMessageBox(2500, "Obstacle", "Touch map first");
                return false;
            }

            SaveObstacleFlagAtWorld(
                pendingObstacleEasting,
                pendingObstacleNorthing,
                pendingObstacleNotes,
                pendingObstacleType,
                pendingObstacleWidth,
                pendingObstacleLength);
            hasPendingObstacle = false;
            isPendingObstacleDragging = false;
            isObstacleTouchMode = false;
            oglMain.Refresh();
            return true;
        }

        public void CancelPendingObstacleFlag()
        {
            if (!hasPendingObstacle && !isObstacleTouchMode)
            {
                return;
            }

            hasPendingObstacle = false;
            isPendingObstacleDragging = false;
            isObstacleTouchMode = false;
            oglMain.Refresh();
        }

        public void SetObstacleAlarmSettings(bool enabled, double distanceMeters)
        {
            isObstacleAlarmEnabled = enabled;
            obstacleAlarmDistanceMeters = Math.Max(0.5, distanceMeters);

            if (enabled)
            {
                EnsureObstacleAlarmTimer();
                obstacleAlarmTimer.Start();
                TimedMessageBox(1800, "Obstacle alarm", "ON at " + obstacleAlarmDistanceMeters.ToString("N1", CultureInfo.CurrentCulture) + " m");
            }
            else
            {
                isObstacleAlarmActive = false;
                obstacleAlarmTimer?.Stop();
                TimedMessageBox(1500, "Obstacle alarm", "OFF");
            }
        }

        private void EnsureObstacleAlarmTimer()
        {
            if (obstacleAlarmTimer != null)
            {
                return;
            }

            obstacleAlarmTimer = new Timer
            {
                Interval = 500
            };
            obstacleAlarmTimer.Tick += (sender, e) => UpdateObstacleAlarm();
        }

        private void UpdateObstacleAlarm()
        {
            if (!isObstacleAlarmEnabled || !isJobStarted || flagPts.Count == 0)
            {
                isObstacleAlarmActive = false;
                return;
            }

            if (!TryGetNearestObstacleDistance(out string nearestName, out double nearestDistance))
                return;

            if (nearestDistance <= obstacleAlarmDistanceMeters)
            {
                if (!isObstacleAlarmActive)
                {
                    string name = string.IsNullOrWhiteSpace(nearestName) ? "Obstacle" : nearestName;
                    TimedMessageBox(1500, "Obstacle alarm", name + " " + nearestDistance.ToString("N1", CultureInfo.CurrentCulture) + " m");
                }

                isObstacleAlarmActive = true;
                DateTime now = DateTime.UtcNow;
                if ((now - obstacleAlarmLastBeepUtc).TotalMilliseconds >= 900)
                {
                    SystemSounds.Beep.Play();
                    obstacleAlarmLastBeepUtc = now;
                }
            }
            else
            {
                isObstacleAlarmActive = false;
            }
        }

        private bool TryStartObstacleDeleteHold(Point point)
        {
            if (Application.OpenForms["FormObstacleMarker"] == null || hasPendingObstacle || isObstacleTouchMode)
            {
                return false;
            }

            if (point.X < 80 || point.Y > oglMain.Height - 60 || (point.X < 300 && point.Y > oglMain.Height - 100))
            {
                return false;
            }

            if (!TryScreenPointToGround(point, out double easting, out double northing))
            {
                return false;
            }

            if (!TryFindObstacleAt(easting, northing, out int flagIndex))
            {
                return false;
            }

            EnsureObstacleDeleteTimer();
            obstacleDeleteHoldFlagIndex = flagIndex;
            obstacleDeleteHoldStartPoint = point;
            isObstacleDeleteHoldActive = true;
            obstacleDeleteHoldTimer.Stop();
            obstacleDeleteHoldTimer.Start();
            TimedMessageBox(1000, "Obstacle", "Hold 3 sec to delete");
            return true;
        }

        private void CancelObstacleDeleteHold()
        {
            if (!isObstacleDeleteHoldActive)
            {
                return;
            }

            isObstacleDeleteHoldActive = false;
            obstacleDeleteHoldFlagIndex = -1;
            obstacleDeleteHoldTimer?.Stop();
        }

        private void CancelObstacleDeleteHoldIfMoved(Point point)
        {
            if (!isObstacleDeleteHoldActive)
            {
                return;
            }

            int dx = point.X - obstacleDeleteHoldStartPoint.X;
            int dy = point.Y - obstacleDeleteHoldStartPoint.Y;
            if ((dx * dx) + (dy * dy) > 100)
            {
                CancelObstacleDeleteHold();
            }
        }

        private void EnsureObstacleDeleteTimer()
        {
            if (obstacleDeleteHoldTimer != null)
            {
                return;
            }

            obstacleDeleteHoldTimer = new Timer
            {
                Interval = 3000
            };
            obstacleDeleteHoldTimer.Tick += (sender, e) => DeleteObstacleAfterHold();
        }

        private void DeleteObstacleAfterHold()
        {
            obstacleDeleteHoldTimer?.Stop();
            if (!isObstacleDeleteHoldActive)
            {
                return;
            }

            int index = obstacleDeleteHoldFlagIndex;
            isObstacleDeleteHoldActive = false;
            obstacleDeleteHoldFlagIndex = -1;

            if (index < 0 || index >= flagPts.Count)
            {
                return;
            }

            string obstacleName = StripObstacleTag(flagPts[index].notes);
            flagPts.RemoveAt(index);
            for (int i = 0; i < flagPts.Count; i++)
            {
                flagPts[i].ID = i + 1;
            }

            flagNumberPicked = 0;
            FileSaveFlags();
            oglMain.Refresh();
            TimedMessageBox(2500, "Obstacle", "Deleted " + obstacleName);
        }

        private bool TryHandleObstacleTouchMap(Point point)
        {
            if (!isObstacleTouchMode && !hasPendingObstacle)
            {
                return false;
            }

            if (point.X < 80 || point.Y > oglMain.Height - 60 || (point.X < 300 && point.Y > oglMain.Height - 100))
            {
                return true;
            }

            if (!TryScreenPointToGround(point, out double easting, out double northing))
            {
                TimedMessageBox(2500, "Obstacle", "Map point not found");
                return true;
            }

            string notes = hasPendingObstacle ? pendingObstacleNotes : obstacleTouchNotes;
            string obstacleType = hasPendingObstacle ? pendingObstacleType : obstacleTouchType;
            double width = hasPendingObstacle ? pendingObstacleWidth : obstacleTouchWidth;
            double length = hasPendingObstacle ? pendingObstacleLength : obstacleTouchLength;
            SetPendingObstacleAtWorld(easting, northing, notes, obstacleType, width, length, !hasPendingObstacle);
            isObstacleTouchMode = false;
            isPendingObstacleDragging = true;
            return true;
        }

        private void TryDragPendingObstacle(Point point)
        {
            if (!isPendingObstacleDragging || !hasPendingObstacle)
            {
                return;
            }

            if (point.X < 80 || point.Y > oglMain.Height - 60 || (point.X < 300 && point.Y > oglMain.Height - 100))
            {
                return;
            }

            if (TryScreenPointToGround(point, out double easting, out double northing))
            {
                SetPendingObstacleAtWorld(
                    easting,
                    northing,
                    pendingObstacleNotes,
                    pendingObstacleType,
                    pendingObstacleWidth,
                    pendingObstacleLength,
                    false);
            }
        }

        private void StopPendingObstacleDrag()
        {
            isPendingObstacleDragging = false;
        }

        private void SetPendingObstacleAtWorld(
            double easting,
            double northing,
            string notes,
            string obstacleType,
            double widthMeters,
            double lengthMeters,
            bool showMessage)
        {
            pendingObstacleEasting = easting;
            pendingObstacleNorthing = northing;
            pendingObstacleNotes = string.IsNullOrWhiteSpace(notes) ? "Obstacle" : notes.Trim();
            pendingObstacleType = NormalizeObstacleType(obstacleType);
            pendingObstacleWidth = pendingObstacleType == ObstacleTypePole ? 0.3 : Math.Max(0.05, widthMeters);
            pendingObstacleLength = pendingObstacleType == ObstacleTypePole ? 0.3 : Math.Max(0.05, lengthMeters);
            hasPendingObstacle = true;

            if (showMessage)
            {
                TimedMessageBox(3500, "Obstacle", "Move by finger, then Save");
            }

            oglMain.Refresh();
        }

        private void SaveObstacleFlagAtWorld(double easting, double northing, string notes, string obstacleType, double widthMeters, double lengthMeters)
        {
            obstacleType = NormalizeObstacleType(obstacleType);
            double shapeHeading = GetObstacleShapeHeading(obstacleType);

            Wgs84 latLon = AppModel.LocalPlane.ConvertGeoCoordToWgs84(new GeoCoord(northing, easting));
            int nextFlag = flagPts.Count + 1;
            string flagNotes = "Obstacle";
            if (!string.IsNullOrWhiteSpace(notes))
            {
                flagNotes = StripObstacleTag(notes.Trim());
            }

            widthMeters = obstacleType == ObstacleTypePole ? 0.3 : Math.Max(0.05, widthMeters);
            lengthMeters = obstacleType == ObstacleTypePole ? 0.3 : Math.Max(0.05, lengthMeters);
            string flagNotesWithTag = flagNotes + " " + BuildObstacleTag(obstacleType, widthMeters, lengthMeters);

            CFlag flagPt = new CFlag(
                latLon.Latitude,
                latLon.Longitude,
                easting,
                northing,
                shapeHeading,
                0,
                nextFlag,
                flagNotesWithTag);

            flagPts.Add(flagPt);
            flagPts = FlagsFiles.DeduplicateFlags(flagPts);
            FileSaveFlags();
            flagNumberPicked = nextFlag;
            oglMain.Refresh();

            TimedMessageBox(3500, "Obstacle", flagNotes + " saved");
        }

        private string NormalizeObstacleType(string obstacleType)
        {
            if (string.Equals(obstacleType, ObstacleTypeHole, StringComparison.OrdinalIgnoreCase)) return ObstacleTypeHole;
            if (string.Equals(obstacleType, ObstacleTypeHose, StringComparison.OrdinalIgnoreCase)) return ObstacleTypeHose;
            return ObstacleTypePole;
        }

        private string BuildObstacleTag(string obstacleType, double widthMeters, double lengthMeters)
        {
            return ObstacleTagStart
                + NormalizeObstacleType(obstacleType)
                + ";W=" + widthMeters.ToString("0.###", CultureInfo.InvariantCulture)
                + ";L=" + lengthMeters.ToString("0.###", CultureInfo.InvariantCulture)
                + "]";
        }

        private string StripObstacleTag(string notes)
        {
            if (string.IsNullOrWhiteSpace(notes)) return string.Empty;

            int start = notes.IndexOf(ObstacleTagStart, StringComparison.Ordinal);
            if (start < 0) return notes.Trim();

            int end = notes.IndexOf(']', start);
            if (end < 0) return notes.Substring(0, start).Trim();

            return (notes.Substring(0, start) + notes.Substring(end + 1)).Trim();
        }

        private bool TryParseObstacleTag(string notes, out string obstacleType, out double widthMeters, out double lengthMeters)
        {
            obstacleType = ObstacleTypePole;
            widthMeters = 0.3;
            lengthMeters = 0.3;

            if (string.IsNullOrWhiteSpace(notes)) return false;

            int start = notes.IndexOf(ObstacleTagStart, StringComparison.Ordinal);
            if (start < 0) return false;

            int end = notes.IndexOf(']', start);
            if (end < 0) return false;

            string tag = notes.Substring(start + ObstacleTagStart.Length, end - start - ObstacleTagStart.Length);
            string[] parts = tag.Split(';');
            if (parts.Length > 0)
            {
                obstacleType = NormalizeObstacleType(parts[0]);
            }

            for (int i = 1; i < parts.Length; i++)
            {
                string[] pair = parts[i].Split('=');
                if (pair.Length != 2) continue;

                if (string.Equals(pair[0], "W", StringComparison.OrdinalIgnoreCase)
                    && double.TryParse(pair[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedWidth))
                {
                    widthMeters = parsedWidth;
                }

                if (string.Equals(pair[0], "L", StringComparison.OrdinalIgnoreCase)
                    && double.TryParse(pair[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedLength))
                {
                    lengthMeters = parsedLength;
                }
            }

            if (obstacleType == ObstacleTypePole)
            {
                widthMeters = 0.3;
                lengthMeters = 0.3;
            }

            return true;
        }

        private bool TryFindObstacleAt(double easting, double northing, out int flagIndex)
        {
            flagIndex = -1;
            double bestScore = double.MaxValue;

            for (int i = 0; i < flagPts.Count; i++)
            {
                CFlag flag = flagPts[i];
                double score = GetObstacleDistance(flag, easting, northing);
                if (score <= 2.0 && score < bestScore)
                {
                    bestScore = score;
                    flagIndex = i;
                }
            }

            return flagIndex >= 0;
        }

        private double GetObstacleDistance(CFlag flag, double easting, double northing)
        {
            if (!TryParseObstacleTag(flag.notes, out string obstacleType, out double widthMeters, out double lengthMeters))
            {
                return glm.Distance(easting, northing, flag.easting, flag.northing);
            }

            if (NormalizeObstacleType(obstacleType) == ObstacleTypeHose)
            {
                if (!TryGetHoseSegment(flag.easting, flag.northing, flag.heading, out XyCoord start, out XyCoord end))
                {
                    double length = 100.0;
                    start = new XyCoord(flag.easting - Math.Sin(flag.heading) * length, flag.northing - Math.Cos(flag.heading) * length);
                    end = new XyCoord(flag.easting + Math.Sin(flag.heading) * length, flag.northing + Math.Cos(flag.heading) * length);
                }

                return DistancePointToSegment(easting, northing, start.X, start.Y, end.X, end.Y);
            }

            if (NormalizeObstacleType(obstacleType) == ObstacleTypePole)
            {
                widthMeters = 0.3;
                lengthMeters = 0.3;
            }

            double deltaEasting = easting - flag.easting;
            double deltaNorthing = northing - flag.northing;
            double localForward = (deltaEasting * Math.Sin(flag.heading)) + (deltaNorthing * Math.Cos(flag.heading));
            double localRight = (deltaEasting * Math.Sin(flag.heading + glm.PIBy2)) + (deltaNorthing * Math.Cos(flag.heading + glm.PIBy2));
            double outsideForward = Math.Max(0.0, Math.Abs(localForward) - (Math.Max(0.05, lengthMeters) * 0.5));
            double outsideRight = Math.Max(0.0, Math.Abs(localRight) - (Math.Max(0.05, widthMeters) * 0.5));
            return Math.Sqrt((outsideForward * outsideForward) + (outsideRight * outsideRight));
        }

        private static double DistancePointToSegment(
            double pointX,
            double pointY,
            double startX,
            double startY,
            double endX,
            double endY)
        {
            double dx = endX - startX;
            double dy = endY - startY;
            double lengthSquared = (dx * dx) + (dy * dy);

            if (lengthSquared < 0.000001)
            {
                double singleDx = pointX - startX;
                double singleDy = pointY - startY;
                return Math.Sqrt((singleDx * singleDx) + (singleDy * singleDy));
            }

            double t = (((pointX - startX) * dx) + ((pointY - startY) * dy)) / lengthSquared;
            t = Math.Max(0.0, Math.Min(1.0, t));
            double closestX = startX + (t * dx);
            double closestY = startY + (t * dy);
            double distX = pointX - closestX;
            double distY = pointY - closestY;
            return Math.Sqrt((distX * distX) + (distY * distY));
        }

        private bool HasActiveABLine()
        {
            return trk != null && trk.idx > -1 && trk.idx < trk.gArr.Count && trk.gArr[trk.idx].mode == TrackMode.AB;
        }

        private double GetObstacleShapeHeading(string obstacleType)
        {
            if (NormalizeObstacleType(obstacleType) == ObstacleTypeHose && HasActiveABLine())
            {
                return NormalizeHeading(trk.gArr[trk.idx].heading + glm.PIBy2);
            }

            if (HasActiveABLine())
            {
                return trk.gArr[trk.idx].heading;
            }

            return fixHeading;
        }

        private double NormalizeHeading(double heading)
        {
            while (heading < 0.0) heading += glm.twoPI;
            while (heading > glm.twoPI) heading -= glm.twoPI;
            return heading;
        }

        private bool TryGetOuterBoundaryCenter(out double easting, out double northing)
        {
            easting = 0.0;
            northing = 0.0;

            if (bnd?.bndList == null || bnd.bndList.Count == 0 || bnd.bndList[0].fenceLine == null || bnd.bndList[0].fenceLine.Count < 3)
            {
                return false;
            }

            double signedArea = 0.0;
            double centroidEasting = 0.0;
            double centroidNorthing = 0.0;
            List<vec3> fence = bnd.bndList[0].fenceLine;

            for (int i = 0; i < fence.Count; i++)
            {
                vec3 a = fence[i];
                vec3 b = fence[(i + 1) % fence.Count];
                double cross = (a.easting * b.northing) - (b.easting * a.northing);
                signedArea += cross;
                centroidEasting += (a.easting + b.easting) * cross;
                centroidNorthing += (a.northing + b.northing) * cross;
            }

            signedArea *= 0.5;
            if (Math.Abs(signedArea) < 0.001)
            {
                for (int i = 0; i < fence.Count; i++)
                {
                    easting += fence[i].easting;
                    northing += fence[i].northing;
                }

                easting /= fence.Count;
                northing /= fence.Count;
                return true;
            }

            easting = centroidEasting / (6.0 * signedArea);
            northing = centroidNorthing / (6.0 * signedArea);
            return true;
        }

        private void DrawPendingObstacleMarker()
        {
            if (!hasPendingObstacle)
            {
                return;
            }

            DrawObstacleShape(
                pendingObstacleEasting,
                pendingObstacleNorthing,
                GetObstacleShapeHeading(pendingObstacleType),
                pendingObstacleType,
                pendingObstacleWidth,
                pendingObstacleLength,
                true);

            GL.PointSize(14.0f);
            GL.Begin(PrimitiveType.Points);
            GL.Color3(1.0f, 0.45f, 0.0f);
            GL.Vertex3(pendingObstacleEasting, pendingObstacleNorthing, 0);
            GL.End();

            double offSet = Math.Max(0.75, camera.ZoomValue * camera.ZoomValue * 0.01);
            GLW.SetLineWidth(4.0f);
            GL.Color3(1.0f, 0.65f, 0.0f);
            XyCoord[] squareCorners = {
                new XyCoord(pendingObstacleEasting         , pendingObstacleNorthing + offSet),
                new XyCoord(pendingObstacleEasting - offSet, pendingObstacleNorthing),
                new XyCoord(pendingObstacleEasting         , pendingObstacleNorthing - offSet),
                new XyCoord(pendingObstacleEasting + offSet, pendingObstacleNorthing),
            };
            GLW.DrawLineLoopPrimitive(squareCorners);

            font.DrawText3D(
                pendingObstacleEasting,
                pendingObstacleNorthing,
                "! " + StripObstacleTag(pendingObstacleNotes),
                camHeading);
        }

        private void DrawSavedObstacleShape(CFlag flag)
        {
            if (!TryParseObstacleTag(flag.notes, out string obstacleType, out double widthMeters, out double lengthMeters))
            {
                return;
            }

            DrawObstacleShape(flag.easting, flag.northing, flag.heading, obstacleType, widthMeters, lengthMeters, false);
        }

        private void DrawObstacleShape(
            double easting,
            double northing,
            double heading,
            string obstacleType,
            double widthMeters,
            double lengthMeters,
            bool isPending)
        {
            obstacleType = NormalizeObstacleType(obstacleType);

            if (obstacleType == ObstacleTypeHose)
            {
                DrawObstacleHose(easting, northing, heading, isPending);
                return;
            }

            if (obstacleType == ObstacleTypePole)
            {
                widthMeters = 0.3;
                lengthMeters = 0.3;
            }

            widthMeters = Math.Max(0.05, widthMeters);
            lengthMeters = Math.Max(0.05, lengthMeters);
            DrawObstacleRectangle(easting, northing, heading, widthMeters, lengthMeters, obstacleType == ObstacleTypeHole, isPending);
        }

        private void DrawObstacleRectangle(
            double easting,
            double northing,
            double heading,
            double widthMeters,
            double lengthMeters,
            bool isHole,
            bool isPending)
        {
            double halfLength = lengthMeters * 0.5;
            double halfWidth = widthMeters * 0.5;
            double fE = Math.Sin(heading);
            double fN = Math.Cos(heading);
            double rE = Math.Sin(heading + glm.PIBy2);
            double rN = Math.Cos(heading + glm.PIBy2);

            XyCoord[] corners = {
                new XyCoord(easting + fE * halfLength + rE * halfWidth, northing + fN * halfLength + rN * halfWidth),
                new XyCoord(easting + fE * halfLength - rE * halfWidth, northing + fN * halfLength - rN * halfWidth),
                new XyCoord(easting - fE * halfLength - rE * halfWidth, northing - fN * halfLength - rN * halfWidth),
                new XyCoord(easting - fE * halfLength + rE * halfWidth, northing - fN * halfLength + rN * halfWidth),
            };

            GLW.SetLineWidth(isPending ? 5.0f : 3.0f);
            if (isHole)
            {
                GL.Color3(0.95f, 0.25f, 0.05f);
            }
            else
            {
                GL.Color3(0.1f, 0.1f, 0.1f);
            }
            GLW.DrawLineLoopPrimitive(corners);
        }

        private void DrawObstacleHose(double easting, double northing, double heading, bool isPending)
        {
            if (!TryGetHoseSegment(easting, northing, heading, out XyCoord start, out XyCoord end))
            {
                double length = 100.0;
                start = new XyCoord(easting - Math.Sin(heading) * length, northing - Math.Cos(heading) * length);
                end = new XyCoord(easting + Math.Sin(heading) * length, northing + Math.Cos(heading) * length);
            }

            GLW.SetLineWidth(isPending ? 7.0f : 5.0f);
            GL.Color3(0.05f, 0.25f, 1.0f);
            GL.Begin(PrimitiveType.Lines);
            GL.Vertex2(start.X, start.Y);
            GL.Vertex2(end.X, end.Y);
            GL.End();
        }

        private bool TryGetHoseSegment(double easting, double northing, double heading, out XyCoord start, out XyCoord end)
        {
            start = new XyCoord();
            end = new XyCoord();

            if (bnd?.bndList == null || bnd.bndList.Count == 0 || bnd.bndList[0].fenceLine == null || bnd.bndList[0].fenceLine.Count < 3)
            {
                return false;
            }

            double dE = Math.Sin(heading);
            double dN = Math.Cos(heading);
            List<double> lineParams = new List<double>();
            List<vec3> fence = bnd.bndList[0].fenceLine;

            for (int i = 0; i < fence.Count; i++)
            {
                vec3 a = fence[i];
                vec3 b = fence[(i + 1) % fence.Count];
                double sE = b.easting - a.easting;
                double sN = b.northing - a.northing;
                double denominator = Cross(dE, dN, sE, sN);

                if (Math.Abs(denominator) < 0.000001)
                {
                    continue;
                }

                double qE = a.easting - easting;
                double qN = a.northing - northing;
                double t = Cross(qE, qN, sE, sN) / denominator;
                double u = Cross(qE, qN, dE, dN) / denominator;

                if (u >= -0.0001 && u <= 1.0001)
                {
                    bool duplicate = false;
                    for (int j = 0; j < lineParams.Count; j++)
                    {
                        if (Math.Abs(lineParams[j] - t) < 0.05)
                        {
                            duplicate = true;
                            break;
                        }
                    }

                    if (!duplicate)
                    {
                        lineParams.Add(t);
                    }
                }
            }

            if (lineParams.Count < 2)
            {
                return false;
            }

            lineParams.Sort();
            double startT = lineParams[0];
            double endT = lineParams[lineParams.Count - 1];

            start = new XyCoord(easting + dE * startT, northing + dN * startT);
            end = new XyCoord(easting + dE * endT, northing + dN * endT);
            return true;
        }

        private static double Cross(double ax, double ay, double bx, double by)
        {
            return (ax * by) - (ay * bx);
        }

        private void DrawObstacleCountdownText()
        {
            if (!TryGetNearestObstacleDistance(out _, out double distanceMeters))
            {
                return;
            }

            string text = distanceMeters.ToString("N1", CultureInfo.CurrentCulture) + " m";

            GL.Color3(0.0f, 1.0f, 0.0f);
            font.DrawText(
                52.0,
                (oglMain.Height / 2.0) - 8.0,
                text);
        }

        private bool TryScreenPointToGround(Point point, out double easting, out double northing)
        {
            easting = 0.0;
            northing = 0.0;

            try
            {
                oglMain.MakeCurrent();

                int[] viewport = new int[4];
                double[] modelView = new double[16];
                double[] projection = new double[16];

                GL.GetInteger(GetPName.Viewport, viewport);
                GL.GetDouble(GetPName.ModelviewMatrix, modelView);
                GL.GetDouble(GetPName.ProjectionMatrix, projection);

                double[] transform = MultiplyMatrix(projection, modelView);
                if (!InvertMatrix(transform, out double[] inverse))
                {
                    return false;
                }

                double x = ((point.X - viewport[0]) / (double)viewport[2]) * 2.0 - 1.0;
                double y = ((viewport[3] - point.Y - viewport[1]) / (double)viewport[3]) * 2.0 - 1.0;

                if (!UnProject(inverse, x, y, -1.0, out double nearX, out double nearY, out double nearZ)
                    || !UnProject(inverse, x, y, 1.0, out double farX, out double farY, out double farZ))
                {
                    return false;
                }

                double dz = farZ - nearZ;
                if (Math.Abs(dz) < 0.000001)
                {
                    return false;
                }

                double t = -nearZ / dz;
                if (t < 0.0 || t > 1.0)
                {
                    return false;
                }

                easting = nearX + ((farX - nearX) * t);
                northing = nearY + ((farY - nearY) * t);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static double[] MultiplyMatrix(double[] left, double[] right)
        {
            double[] result = new double[16];
            for (int col = 0; col < 4; col++)
            {
                for (int row = 0; row < 4; row++)
                {
                    result[(col * 4) + row] =
                        (left[row] * right[col * 4])
                        + (left[4 + row] * right[(col * 4) + 1])
                        + (left[8 + row] * right[(col * 4) + 2])
                        + (left[12 + row] * right[(col * 4) + 3]);
                }
            }

            return result;
        }

        private static bool UnProject(double[] inverse, double x, double y, double z, out double outX, out double outY, out double outZ)
        {
            double[] input = { x, y, z, 1.0 };
            double[] output = new double[4];

            for (int row = 0; row < 4; row++)
            {
                output[row] =
                    (inverse[row] * input[0])
                    + (inverse[4 + row] * input[1])
                    + (inverse[8 + row] * input[2])
                    + (inverse[12 + row] * input[3]);
            }

            if (Math.Abs(output[3]) < 0.000001)
            {
                outX = 0.0;
                outY = 0.0;
                outZ = 0.0;
                return false;
            }

            outX = output[0] / output[3];
            outY = output[1] / output[3];
            outZ = output[2] / output[3];
            return true;
        }

        private static bool InvertMatrix(double[] m, out double[] invOut)
        {
            double[] inv = new double[16];

            inv[0] = m[5] * m[10] * m[15] - m[5] * m[11] * m[14] - m[9] * m[6] * m[15]
                + m[9] * m[7] * m[14] + m[13] * m[6] * m[11] - m[13] * m[7] * m[10];
            inv[4] = -m[4] * m[10] * m[15] + m[4] * m[11] * m[14] + m[8] * m[6] * m[15]
                - m[8] * m[7] * m[14] - m[12] * m[6] * m[11] + m[12] * m[7] * m[10];
            inv[8] = m[4] * m[9] * m[15] - m[4] * m[11] * m[13] - m[8] * m[5] * m[15]
                + m[8] * m[7] * m[13] + m[12] * m[5] * m[11] - m[12] * m[7] * m[9];
            inv[12] = -m[4] * m[9] * m[14] + m[4] * m[10] * m[13] + m[8] * m[5] * m[14]
                - m[8] * m[6] * m[13] - m[12] * m[5] * m[10] + m[12] * m[6] * m[9];
            inv[1] = -m[1] * m[10] * m[15] + m[1] * m[11] * m[14] + m[9] * m[2] * m[15]
                - m[9] * m[3] * m[14] - m[13] * m[2] * m[11] + m[13] * m[3] * m[10];
            inv[5] = m[0] * m[10] * m[15] - m[0] * m[11] * m[14] - m[8] * m[2] * m[15]
                + m[8] * m[3] * m[14] + m[12] * m[2] * m[11] - m[12] * m[3] * m[10];
            inv[9] = -m[0] * m[9] * m[15] + m[0] * m[11] * m[13] + m[8] * m[1] * m[15]
                - m[8] * m[3] * m[13] - m[12] * m[1] * m[11] + m[12] * m[3] * m[9];
            inv[13] = m[0] * m[9] * m[14] - m[0] * m[10] * m[13] - m[8] * m[1] * m[14]
                + m[8] * m[2] * m[13] + m[12] * m[1] * m[10] - m[12] * m[2] * m[9];
            inv[2] = m[1] * m[6] * m[15] - m[1] * m[7] * m[14] - m[5] * m[2] * m[15]
                + m[5] * m[3] * m[14] + m[13] * m[2] * m[7] - m[13] * m[3] * m[6];
            inv[6] = -m[0] * m[6] * m[15] + m[0] * m[7] * m[14] + m[4] * m[2] * m[15]
                - m[4] * m[3] * m[14] - m[12] * m[2] * m[7] + m[12] * m[3] * m[6];
            inv[10] = m[0] * m[5] * m[15] - m[0] * m[7] * m[13] - m[4] * m[1] * m[15]
                + m[4] * m[3] * m[13] + m[12] * m[1] * m[7] - m[12] * m[3] * m[5];
            inv[14] = -m[0] * m[5] * m[14] + m[0] * m[6] * m[13] + m[4] * m[1] * m[14]
                - m[4] * m[2] * m[13] - m[12] * m[1] * m[6] + m[12] * m[2] * m[5];
            inv[3] = -m[1] * m[6] * m[11] + m[1] * m[7] * m[10] + m[5] * m[2] * m[11]
                - m[5] * m[3] * m[10] - m[9] * m[2] * m[7] + m[9] * m[3] * m[6];
            inv[7] = m[0] * m[6] * m[11] - m[0] * m[7] * m[10] - m[4] * m[2] * m[11]
                + m[4] * m[3] * m[10] + m[8] * m[2] * m[7] - m[8] * m[3] * m[6];
            inv[11] = -m[0] * m[5] * m[11] + m[0] * m[7] * m[9] + m[4] * m[1] * m[11]
                - m[4] * m[3] * m[9] - m[8] * m[1] * m[7] + m[8] * m[3] * m[5];
            inv[15] = m[0] * m[5] * m[10] - m[0] * m[6] * m[9] - m[4] * m[1] * m[10]
                + m[4] * m[2] * m[9] + m[8] * m[1] * m[6] - m[8] * m[2] * m[5];

            double det = m[0] * inv[0] + m[1] * inv[4] + m[2] * inv[8] + m[3] * inv[12];
            if (Math.Abs(det) < 0.0000001)
            {
                invOut = null;
                return false;
            }

            det = 1.0 / det;
            for (int i = 0; i < 16; i++)
            {
                inv[i] *= det;
            }

            invOut = inv;
            return true;
        }

        private void ShowObstacleMarker()
        {
            Form fc = Application.OpenForms["FormObstacleMarker"];

            if (fc != null)
            {
                fc.Focus();
                return;
            }

            Form form = new FormObstacleMarker(this);
            form.Show(this);
        }
    }
}
