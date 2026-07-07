using System;
using System.Collections.Generic;
using System.Windows.Forms;
using AgOpenGPS.Core.Translations;

namespace AgOpenGPS
{
    public partial class FormGPS
    {
        private const double MarkedEdgesBoundaryBinMeters = 1.0;
        private const double MarkedEdgesBoundaryMinimumLengthMeters = 12.0;
        private const double MarkedEdgesBoundaryOuterSamplePercent = 0.18;
        private const double MarkedEdgesBoundaryOuterPercentile = 0.04;
        private const int MarkedEdgesBoundaryGapToleranceBins = 3;

        private void btnMarkedEdgesBoundary_Click(object sender, EventArgs e)
        {
            BuildBoundaryFromMarkedOuterEdges();
        }

        private void UpdateMarkedEdgesBoundaryButtonVisibility()
        {
            if (btnMarkedEdgesBoundary == null) return;

            btnMarkedEdgesBoundary.Visible = isJobStarted
                && bnd?.bndList != null
                && bnd.bndList.Count == 0;
        }

        private void BuildBoundaryFromMarkedOuterEdges()
        {
            if (!isJobStarted)
            {
                TimedMessageBox(2000, gStr.gsFieldNotOpen, gStr.gsStartNewField);
                return;
            }

            if (trk.idx < 0 || trk.idx >= trk.gArr.Count || trk.gArr[trk.idx].mode != TrackMode.AB)
            {
                YesMessageBox("Make or select an AB line first.");
                return;
            }

            if (bnd.bndList.Count > 0)
            {
                YesMessageBox("Boundary already exists. Delete it first to build one from marked outside edges.");
                return;
            }

            List<vec3> mappingPoints = GetMarkedEdgePoints();
            if (mappingPoints.Count < 20)
            {
                YesMessageBox("Not enough marked area. Mark the outside field edges first.");
                return;
            }

            CTrk track = trk.gArr[trk.idx];
            double abHeading = GetMarkedEdgesTrackHeading(track);
            double sinH = Math.Sin(abHeading);
            double cosH = Math.Cos(abHeading);

            double minAlong = double.MaxValue;
            double maxAlong = double.MinValue;
            double minCross = double.MaxValue;
            double maxCross = double.MinValue;

            for (int i = 0; i < mappingPoints.Count; i++)
            {
                double along = ProjectMarkedEdgesAlong(mappingPoints[i], sinH, cosH);
                double cross = ProjectMarkedEdgesCross(mappingPoints[i], sinH, cosH);

                if (along < minAlong) minAlong = along;
                if (along > maxAlong) maxAlong = along;
                if (cross < minCross) minCross = cross;
                if (cross > maxCross) maxCross = cross;
            }

            if (!GetLongestMarkedAlongInterval(mappingPoints, sinH, cosH, out double lengthMinAlong, out double lengthMaxAlong))
            {
                YesMessageBox("Could not find the longest marked pass.");
                return;
            }

            minAlong = lengthMinAlong;
            maxAlong = lengthMaxAlong;

            double mappedLength = maxAlong - minAlong;
            if (mappedLength < MarkedEdgesBoundaryMinimumLengthMeters)
            {
                YesMessageBox("Longest marked pass is too short to make a boundary.");
                return;
            }

            double mappedWidth = maxCross - minCross;
            double minimumFieldWidth = Math.Max((tool.width - tool.overlap) * 1.15, 2.0);
            if (mappedWidth < minimumFieldWidth)
            {
                YesMessageBox("Marked edges are too close together. Mark the outside edges of the field.");
                return;
            }

            GetStableOuterCrossLimits(mappingPoints, sinH, cosH, out double stableLowCross, out double stableHighCross);

            double edgeBandWidth = Math.Max((tool.width - tool.overlap) * 0.45, 1.0);
            edgeBandWidth = Math.Min(edgeBandWidth, Math.Max(1.0, (stableHighCross - stableLowCross) * 0.2));

            int binCount = Math.Max(1, (int)Math.Ceiling(mappedLength / MarkedEdgesBoundaryBinMeters) + 1);
            MarkedEdgesBin[] bins = new MarkedEdgesBin[binCount];
            for (int i = 0; i < binCount; i++) bins[i] = new MarkedEdgesBin();

            for (int i = 0; i < mappingPoints.Count; i++)
            {
                vec3 point = mappingPoints[i];
                double along = ProjectMarkedEdgesAlong(point, sinH, cosH);
                double cross = ProjectMarkedEdgesCross(point, sinH, cosH);
                int binIndex = (int)((along - minAlong) / MarkedEdgesBoundaryBinMeters);

                if (binIndex < 0 || binIndex >= bins.Length) continue;
                bins[binIndex].Add(point, cross);
            }

            List<vec3> lowSide = new List<vec3>();
            List<vec3> highSide = new List<vec3>();

            for (int i = 0; i < bins.Length; i++)
            {
                double binAlong = minAlong + ((i + 0.5) * MarkedEdgesBoundaryBinMeters);
                bins[i].GetOuterAverageOrProjected(
                    stableLowCross,
                    stableHighCross,
                    edgeBandWidth,
                    MarkedEdgesBoundaryOuterSamplePercent,
                    binAlong,
                    sinH,
                    cosH,
                    out vec3 lowPoint,
                    out vec3 highPoint);

                lowSide.Add(lowPoint);
                highSide.Add(highPoint);
            }

            if (lowSide.Count < 6 || highSide.Count < 6)
            {
                YesMessageBox("Could not find clean outside edges. Mark the outer field edges along the AB direction.");
                return;
            }

            SmoothMarkedOpenEdge(lowSide);
            SmoothMarkedOpenEdge(highSide);

            List<vec3> boundaryPoints = new List<vec3>(lowSide.Count + highSide.Count);
            for (int i = 0; i < lowSide.Count; i++) boundaryPoints.Add(lowSide[i]);
            for (int i = highSide.Count - 1; i >= 0; i--) boundaryPoints.Add(highSide[i]);

            CreateBoundaryFromMarkedPoints(boundaryPoints);
            TimedMessageBox(2500, "Outer Edges", "Boundary created from marked outside edges.");
        }

        private List<vec3> GetMarkedEdgePoints()
        {
            List<vec3> points = new List<vec3>();

            for (int sectionIndex = 0; sectionIndex < triStrip.Count; sectionIndex++)
            {
                int patchCount = triStrip[sectionIndex].patchList.Count;
                if (patchCount == 0) continue;

                foreach (var triList in triStrip[sectionIndex].patchList)
                {
                    for (int i = 1; i < triList.Count; i++)
                    {
                        if (double.IsNaN(triList[i].easting) || double.IsNaN(triList[i].northing)) continue;
                        if (double.IsInfinity(triList[i].easting) || double.IsInfinity(triList[i].northing)) continue;

                        points.Add(new vec3(triList[i].easting, triList[i].northing, 0));
                    }
                }
            }

            return points;
        }

        private double GetMarkedEdgesTrackHeading(CTrk track)
        {
            double dx = track.ptB.easting - track.ptA.easting;
            double dy = track.ptB.northing - track.ptA.northing;

            if ((dx * dx) + (dy * dy) > 0.01)
            {
                double heading = Math.Atan2(dx, dy);
                if (heading < 0) heading += glm.twoPI;
                return heading;
            }

            return track.heading;
        }

        private static double ProjectMarkedEdgesAlong(vec3 point, double sinH, double cosH)
        {
            return (point.easting * sinH) + (point.northing * cosH);
        }

        private static double ProjectMarkedEdgesCross(vec3 point, double sinH, double cosH)
        {
            return (point.easting * cosH) - (point.northing * sinH);
        }

        private static void GetStableOuterCrossLimits(List<vec3> mappingPoints, double sinH, double cosH, out double stableLowCross, out double stableHighCross)
        {
            List<double> crosses = new List<double>(mappingPoints.Count);
            for (int i = 0; i < mappingPoints.Count; i++)
            {
                crosses.Add(ProjectMarkedEdgesCross(mappingPoints[i], sinH, cosH));
            }

            crosses.Sort();

            int lowIndex = Math.Max(0, (int)Math.Floor(crosses.Count * MarkedEdgesBoundaryOuterPercentile));
            int highIndex = Math.Min(crosses.Count - 1, (int)Math.Ceiling(crosses.Count * (1.0 - MarkedEdgesBoundaryOuterPercentile)) - 1);

            stableLowCross = crosses[lowIndex];
            stableHighCross = crosses[highIndex];
        }

        private static bool GetLongestMarkedAlongInterval(List<vec3> mappingPoints, double sinH, double cosH, out double minAlong, out double maxAlong)
        {
            minAlong = 0;
            maxAlong = 0;

            if (mappingPoints.Count == 0) return false;

            double globalMinAlong = double.MaxValue;
            double globalMaxAlong = double.MinValue;

            for (int i = 0; i < mappingPoints.Count; i++)
            {
                double along = ProjectMarkedEdgesAlong(mappingPoints[i], sinH, cosH);
                if (along < globalMinAlong) globalMinAlong = along;
                if (along > globalMaxAlong) globalMaxAlong = along;
            }

            if (globalMaxAlong <= globalMinAlong) return false;

            int binCount = Math.Max(1, (int)Math.Ceiling((globalMaxAlong - globalMinAlong) / MarkedEdgesBoundaryBinMeters) + 1);
            bool[] occupied = new bool[binCount];

            for (int i = 0; i < mappingPoints.Count; i++)
            {
                double along = ProjectMarkedEdgesAlong(mappingPoints[i], sinH, cosH);
                int binIndex = (int)((along - globalMinAlong) / MarkedEdgesBoundaryBinMeters);
                if (binIndex >= 0 && binIndex < occupied.Length) occupied[binIndex] = true;
            }

            int currentStart = -1;
            int currentLastOccupied = -1;
            int currentGap = 0;
            int bestStart = -1;
            int bestEnd = -1;

            for (int i = 0; i < occupied.Length; i++)
            {
                if (occupied[i])
                {
                    if (currentStart < 0) currentStart = i;
                    currentLastOccupied = i;
                    currentGap = 0;
                    continue;
                }

                if (currentStart < 0) continue;

                currentGap++;
                if (currentGap <= MarkedEdgesBoundaryGapToleranceBins) continue;

                UpdateBestMarkedInterval(currentStart, currentLastOccupied, ref bestStart, ref bestEnd);
                currentStart = -1;
                currentLastOccupied = -1;
                currentGap = 0;
            }

            if (currentStart >= 0)
            {
                UpdateBestMarkedInterval(currentStart, currentLastOccupied, ref bestStart, ref bestEnd);
            }

            if (bestStart < 0 || bestEnd < bestStart) return false;

            minAlong = globalMinAlong + (bestStart * MarkedEdgesBoundaryBinMeters);
            maxAlong = globalMinAlong + ((bestEnd + 1) * MarkedEdgesBoundaryBinMeters);
            return true;
        }

        private static void UpdateBestMarkedInterval(int currentStart, int currentEnd, ref int bestStart, ref int bestEnd)
        {
            if (currentEnd < currentStart) return;

            if (bestStart < 0 || (currentEnd - currentStart) > (bestEnd - bestStart))
            {
                bestStart = currentStart;
                bestEnd = currentEnd;
            }
        }

        private void CreateBoundaryFromMarkedPoints(List<vec3> boundaryPoints)
        {
            CBoundaryList newBoundary = new CBoundaryList();
            for (int i = 0; i < boundaryPoints.Count; i++)
            {
                newBoundary.fenceLine.Add(new vec3(boundaryPoints[i]));
            }

            newBoundary.CalculateFenceArea(0);
            newBoundary.FixFenceLine(0);

            bnd.bndList.Clear();
            bnd.bndList.Add(newBoundary);
            CalculateMinMax();
            bnd.BuildTurnLines();
            fd.UpdateFieldBoundaryGUIAreas();
            FileSaveBoundary();
            UpdateMarkedEdgesBoundaryButtonVisibility();
            PanelSizeRightAndBottom();
        }

        private static void SmoothMarkedOpenEdge(List<vec3> edge)
        {
            if (edge.Count < 5) return;

            vec3[] source = edge.ToArray();

            for (int i = 1; i < edge.Count - 1; i++)
            {
                edge[i] = new vec3(
                    (source[i - 1].easting + (source[i].easting * 2.0) + source[i + 1].easting) * 0.25,
                    (source[i - 1].northing + (source[i].northing * 2.0) + source[i + 1].northing) * 0.25,
                    0);
            }
        }

        private sealed class MarkedEdgesBin
        {
            private readonly List<MarkedEdgesPoint> points = new List<MarkedEdgesPoint>();

            public bool HasPoint { get; private set; }
            public double MinCross { get; private set; }
            public double MaxCross { get; private set; }

            public void Add(vec3 point, double cross)
            {
                points.Add(new MarkedEdgesPoint(point, cross));

                if (!HasPoint)
                {
                    HasPoint = true;
                    MinCross = cross;
                    MaxCross = cross;
                    return;
                }

                if (cross < MinCross) MinCross = cross;
                if (cross > MaxCross) MaxCross = cross;
            }

            public void GetOuterAverageOrProjected(
                double stableLowCross,
                double stableHighCross,
                double edgeBandWidth,
                double samplePercent,
                double along,
                double sinH,
                double cosH,
                out vec3 lowPoint,
                out vec3 highPoint)
            {
                List<MarkedEdgesPoint> lowPoints = new List<MarkedEdgesPoint>();
                List<MarkedEdgesPoint> highPoints = new List<MarkedEdgesPoint>();

                for (int i = 0; i < points.Count; i++)
                {
                    if (points[i].Cross <= stableLowCross + edgeBandWidth)
                    {
                        lowPoints.Add(points[i]);
                    }

                    if (points[i].Cross >= stableHighCross - edgeBandWidth)
                    {
                        highPoints.Add(points[i]);
                    }
                }

                lowPoints.Sort((a, b) => a.Cross.CompareTo(b.Cross));
                highPoints.Sort((a, b) => a.Cross.CompareTo(b.Cross));

                if (lowPoints.Count == 0)
                {
                    lowPoint = FromAlongCross(along, stableLowCross, sinH, cosH);
                }
                else
                {
                    int lowSampleCount = Math.Max(1, (int)Math.Ceiling(lowPoints.Count * samplePercent));
                    lowPoint = AveragePoints(lowPoints, 0, lowSampleCount);
                }

                if (highPoints.Count == 0)
                {
                    highPoint = FromAlongCross(along, stableHighCross, sinH, cosH);
                }
                else
                {
                    int highSampleCount = Math.Max(1, (int)Math.Ceiling(highPoints.Count * samplePercent));
                    highPoint = AveragePoints(highPoints, highPoints.Count - highSampleCount, highSampleCount);
                }
            }

            private static vec3 FromAlongCross(double along, double cross, double sinH, double cosH)
            {
                return new vec3(
                    (along * sinH) + (cross * cosH),
                    (along * cosH) - (cross * sinH),
                    0);
            }

            private static vec3 AveragePoints(List<MarkedEdgesPoint> source, int start, int count)
            {
                double east = 0;
                double north = 0;

                for (int i = start; i < start + count; i++)
                {
                    east += source[i].Point.easting;
                    north += source[i].Point.northing;
                }

                return new vec3(east / count, north / count, 0);
            }
        }

        private readonly struct MarkedEdgesPoint
        {
            public readonly vec3 Point;
            public readonly double Cross;

            public MarkedEdgesPoint(vec3 point, double cross)
            {
                Point = point;
                Cross = cross;
            }
        }
    }
}
