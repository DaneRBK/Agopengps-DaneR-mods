using System;
using System.Collections.Generic;

namespace AgOpenGPS
{
    public partial class FormGPS
    {
        private const double WorkedTrackCompletionThreshold = 0.70;
        private const double WorkedTrackMinimumLengthMeters = 20.0;

        private int workedTrackLastTrackIndex = -1;
        private int workedTrackLastLane = int.MinValue;
        private TrackMode workedTrackLastMode = TrackMode.None;

        private void ResetWorkedTrackCoverageStep()
        {
            workedTrackLastTrackIndex = -1;
            workedTrackLastLane = int.MinValue;
            workedTrackLastMode = TrackMode.None;
        }

        private void UpdateWorkedTrackCoverage()
        {
            if (!TryGetCurrentWorkedTrack(out CTrk track, out int lane, out TrackMode mode))
            {
                ResetWorkedTrackCoverageStep();
                return;
            }

            if (yt.isYouTurnTriggered || patchCounter <= 0 || Math.Abs(avgSpeed) < 0.2)
            {
                ResetWorkedTrackCoverageStep();
                return;
            }

            if (workedTrackLastTrackIndex != trk.idx || workedTrackLastLane != lane || workedTrackLastMode != mode)
            {
                workedTrackLastTrackIndex = trk.idx;
                workedTrackLastLane = lane;
                workedTrackLastMode = mode;
                return;
            }

            double distance = Math.Abs(sectionTriggerDistance);
            if (distance < 0.05 || distance > 25.0) return;

            double widthFraction = GetWorkedTrackActiveWidthFraction();
            if (widthFraction <= 0.0) return;

            if (!track.workedTrackDistances.ContainsKey(lane))
            {
                track.workedTrackDistances[lane] = 0.0;
            }

            track.workedTrackDistances[lane] += distance * widthFraction;
            MarkCurrentTrackWorkedIfComplete();
        }

        public void MarkCurrentTrackWorkedIfComplete()
        {
            if (!TryGetCurrentWorkedTrack(out CTrk track, out int lane, out TrackMode mode)) return;
            if (IsWorkedTrackComplete(lane, mode == TrackMode.AB)) return;
        }

        public bool IsWorkedTrackComplete(int lane, bool isAB)
        {
            if (trk.idx < 0 || trk.idx >= trk.gArr.Count) return false;

            CTrk track = trk.gArr[trk.idx];
            if (track.workedTracks.Contains(lane)) return true;

            TrackMode mode = isAB ? TrackMode.AB : track.mode;

            double trackLength = EstimateWorkedTrackLength(track, lane, mode);
            if (trackLength < WorkedTrackMinimumLengthMeters) return false;

            UpdateWorkedTrackDistanceFromCoverage(track, lane, isAB, trackLength);

            track.workedTrackDistances.TryGetValue(lane, out double workedDistance);
            if ((workedDistance / trackLength) >= WorkedTrackCompletionThreshold)
            {
                track.workedTracks.Add(lane);
                return true;
            }

            return false;
        }

        public bool IsWorkedTrackSelectable(int lane, bool isAB)
        {
            if (IsWorkedTrackComplete(lane, isAB)) return false;
            if (!isAB) return true;
            if (trk.idx < 0 || trk.idx >= trk.gArr.Count) return false;

            CTrk track = trk.gArr[trk.idx];
            if (track.mode != TrackMode.AB) return true;
            if (bnd.bndList.Count == 0 || bnd.bndList[0].fenceLine.Count < 3) return true;

            double boundaryLength = EstimateABLaneBoundaryLength(track, lane, ABLine.isHeadingSameWay);
            boundaryLength = Math.Max(boundaryLength, EstimateABLaneBoundaryLength(track, lane, !ABLine.isHeadingSameWay));
            return boundaryLength >= WorkedTrackMinimumLengthMeters;
        }

        private bool TryGetCurrentWorkedTrack(out CTrk track, out int lane, out TrackMode mode)
        {
            track = null;
            lane = 0;
            mode = TrackMode.None;

            if (!isJobStarted || trk.idx < 0 || trk.idx >= trk.gArr.Count) return false;

            track = trk.gArr[trk.idx];
            mode = track.mode;

            if (mode == TrackMode.AB)
            {
                lane = ABLine.howManyPathsAway;
                return true;
            }

            if (mode == TrackMode.Curve || mode == TrackMode.bndCurve || mode == TrackMode.waterPivot)
            {
                lane = curve.howManyPathsAway;
                return true;
            }

            return false;
        }

        private double GetWorkedTrackActiveWidthFraction()
        {
            double activeWidth = 0.0;

            for (int i = 0; i < tool.numOfSections; i++)
            {
                if (!section[i].isMappingOn) continue;

                double sectionWidth = section[i].sectionWidth;
                if (sectionWidth <= 0.0)
                {
                    sectionWidth = Math.Abs(section[i].positionRight - section[i].positionLeft);
                }

                activeWidth += sectionWidth;
            }

            if (activeWidth <= 0.0 || tool.width <= 0.0) return 0.0;

            double fraction = activeWidth / tool.width;
            if (fraction > 1.0) return 1.0;
            return fraction;
        }

        private double EstimateWorkedTrackLength(CTrk track, int lane, TrackMode mode)
        {
            if (mode == TrackMode.AB)
            {
                double boundaryLength = EstimateABLaneBoundaryLength(track, lane, ABLine.isHeadingSameWay);
                boundaryLength = Math.Max(boundaryLength, EstimateABLaneBoundaryLength(track, lane, !ABLine.isHeadingSameWay));
                if (boundaryLength >= WorkedTrackMinimumLengthMeters) return boundaryLength;

                double abLength = glm.Distance(track.ptA, track.ptB);
                if (abLength >= WorkedTrackMinimumLengthMeters) return abLength;

                return WorkedTrackMinimumLengthMeters;
            }

            if (track.curvePts != null && track.curvePts.Count > 1)
            {
                double curveLength = 0.0;
                for (int i = 1; i < track.curvePts.Count; i++)
                {
                    curveLength += glm.Distance(track.curvePts[i - 1], track.curvePts[i]);
                }

                if (curveLength >= WorkedTrackMinimumLengthMeters) return curveLength;
            }

            return WorkedTrackMinimumLengthMeters;
        }

        private void UpdateWorkedTrackDistanceFromCoverage(CTrk track, int lane, bool isAB, double trackLength)
        {
            if (!isAB) return;
            if (track.workedTrackDistances.TryGetValue(lane, out double existing) && existing >= trackLength * WorkedTrackCompletionThreshold) return;

            double covered = EstimateABLaneCoveredDistanceFromPatches(track, lane);
            if (covered <= 0.0) return;

            if (!track.workedTrackDistances.ContainsKey(lane) || covered > track.workedTrackDistances[lane])
            {
                track.workedTrackDistances[lane] = covered;
            }
        }

        private double EstimateABLaneBoundaryLength(CTrk track, int lane, bool isHeadingSameWay)
        {
            if (bnd.bndList.Count == 0 || bnd.bndList[0].fenceLine.Count < 3) return 0.0;
            if (!TryBuildABLaneLine(track, lane, isHeadingSameWay, out vec3 lineA, out vec3 lineB)) return 0.0;

            double dx = lineB.easting - lineA.easting;
            double dy = lineB.northing - lineA.northing;
            double lineLength = Math.Sqrt((dx * dx) + (dy * dy));
            if (lineLength < 0.001) return 0.0;

            double dirEast = dx / lineLength;
            double dirNorth = dy / lineLength;
            List<double> intersections = new List<double>();
            List<vec3> fence = bnd.bndList[0].fenceLine;

            for (int i = 0; i < fence.Count; i++)
            {
                vec3 segA = fence[i];
                vec3 segB = fence[(i + 1) % fence.Count];

                if (TrySegmentIntersection(lineA, lineB, segA, segB, out double east, out double north))
                {
                    double projection = ((east - lineA.easting) * dirEast) + ((north - lineA.northing) * dirNorth);
                    bool duplicate = false;

                    for (int j = 0; j < intersections.Count; j++)
                    {
                        if (Math.Abs(intersections[j] - projection) < 0.5)
                        {
                            duplicate = true;
                            break;
                        }
                    }

                    if (!duplicate) intersections.Add(projection);
                }
            }

            if (intersections.Count < 2) return 0.0;

            intersections.Sort();
            return intersections[intersections.Count - 1] - intersections[0];
        }

        private double EstimateABLaneCoveredDistanceFromPatches(CTrk track, int lane)
        {
            double coveredSameWay = EstimateABLaneCoveredDistanceFromPatches(track, lane, ABLine.isHeadingSameWay);
            double coveredOppositeWay = EstimateABLaneCoveredDistanceFromPatches(track, lane, !ABLine.isHeadingSameWay);
            return Math.Max(coveredSameWay, coveredOppositeWay);
        }

        private double EstimateABLaneCoveredDistanceFromPatches(CTrk track, int lane, bool isHeadingSameWay)
        {
            if (!TryBuildABLaneLine(track, lane, isHeadingSameWay, out vec3 lineA, out vec3 lineB)) return 0.0;

            double dx = lineB.easting - lineA.easting;
            double dy = lineB.northing - lineA.northing;
            double lineLength = Math.Sqrt((dx * dx) + (dy * dy));
            if (lineLength < 0.001) return 0.0;

            double dirEast = dx / lineLength;
            double dirNorth = dy / lineLength;
            double centerTolerance = Math.Max(0.35, Math.Min(0.8, (tool.width - tool.overlap) * 0.08));
            List<Tuple<double, double>> intervals = new List<Tuple<double, double>>();

            foreach (CPatches patches in triStrip)
            {
                if (patches == null || patches.patchList == null) continue;

                foreach (List<vec3> strip in patches.patchList)
                {
                    if (strip == null || strip.Count < 5) continue;

                    for (int i = 3; i + 1 < strip.Count; i += 2)
                    {
                        vec3 prevLeft = strip[i - 2];
                        vec3 prevRight = strip[i - 1];
                        vec3 left = strip[i];
                        vec3 right = strip[i + 1];

                        double minLat = Math.Min(Math.Min(GetLineLateral(prevLeft, lineA, dirEast, dirNorth), GetLineLateral(prevRight, lineA, dirEast, dirNorth)),
                            Math.Min(GetLineLateral(left, lineA, dirEast, dirNorth), GetLineLateral(right, lineA, dirEast, dirNorth)));
                        double maxLat = Math.Max(Math.Max(GetLineLateral(prevLeft, lineA, dirEast, dirNorth), GetLineLateral(prevRight, lineA, dirEast, dirNorth)),
                            Math.Max(GetLineLateral(left, lineA, dirEast, dirNorth), GetLineLateral(right, lineA, dirEast, dirNorth)));

                        if (minLat > centerTolerance || maxLat < -centerTolerance) continue;

                        double start = (GetLineProjection(prevLeft, lineA, dirEast, dirNorth) + GetLineProjection(prevRight, lineA, dirEast, dirNorth)) * 0.5;
                        double end = (GetLineProjection(left, lineA, dirEast, dirNorth) + GetLineProjection(right, lineA, dirEast, dirNorth)) * 0.5;
                        if (Math.Abs(end - start) < 0.05) continue;

                        if (end < start)
                        {
                            double temp = start;
                            start = end;
                            end = temp;
                        }

                        intervals.Add(Tuple.Create(start, end));
                    }
                }
            }

            return SumMergedIntervals(intervals);
        }

        private bool TryBuildABLaneLine(CTrk track, int lane, bool isHeadingSameWay, out vec3 lineA, out vec3 lineB)
        {
            lineA = new vec3();
            lineB = new vec3();

            if (track == null || track.mode != TrackMode.AB) return false;

            double heading = track.heading;
            double widthMinusOverlap = tool.width - tool.overlap;
            if (widthMinusOverlap <= 0.01) widthMinusOverlap = tool.width;
            if (widthMinusOverlap <= 0.01) return false;

            double distAway = (widthMinusOverlap * lane) + (isHeadingSameWay ? -tool.offset : tool.offset) + track.nudgeDistance;
            distAway += 0.5 * widthMinusOverlap;

            double offsetEast = Math.Cos(-heading) * distAway;
            double offsetNorth = Math.Sin(-heading) * distAway;

            lineA.easting = track.ptA.easting + offsetEast - (Math.Sin(heading) * ABLine.abLength);
            lineA.northing = track.ptA.northing + offsetNorth - (Math.Cos(heading) * ABLine.abLength);
            lineA.heading = heading;

            lineB.easting = track.ptB.easting + offsetEast + (Math.Sin(heading) * ABLine.abLength);
            lineB.northing = track.ptB.northing + offsetNorth + (Math.Cos(heading) * ABLine.abLength);
            lineB.heading = heading;
            return true;
        }

        private static double GetLineProjection(vec3 point, vec3 lineA, double dirEast, double dirNorth)
        {
            return ((point.easting - lineA.easting) * dirEast) + ((point.northing - lineA.northing) * dirNorth);
        }

        private static double GetLineLateral(vec3 point, vec3 lineA, double dirEast, double dirNorth)
        {
            double relEast = point.easting - lineA.easting;
            double relNorth = point.northing - lineA.northing;
            return (relEast * -dirNorth) + (relNorth * dirEast);
        }

        private static double SumMergedIntervals(List<Tuple<double, double>> intervals)
        {
            if (intervals.Count == 0) return 0.0;

            intervals.Sort((a, b) => a.Item1.CompareTo(b.Item1));

            double total = 0.0;
            double start = intervals[0].Item1;
            double end = intervals[0].Item2;

            for (int i = 1; i < intervals.Count; i++)
            {
                if (intervals[i].Item1 <= end + 0.5)
                {
                    if (intervals[i].Item2 > end) end = intervals[i].Item2;
                }
                else
                {
                    total += end - start;
                    start = intervals[i].Item1;
                    end = intervals[i].Item2;
                }
            }

            total += end - start;
            return total;
        }

        private static bool TrySegmentIntersection(vec3 a0, vec3 a1, vec3 b0, vec3 b1, out double east, out double north)
        {
            east = 0.0;
            north = 0.0;

            double s1x = a1.easting - a0.easting;
            double s1y = a1.northing - a0.northing;
            double s2x = b1.easting - b0.easting;
            double s2y = b1.northing - b0.northing;
            double denom = (-s2x * s1y) + (s1x * s2y);

            if (Math.Abs(denom) < 0.0000001) return false;

            double s = ((-s1y * (a0.easting - b0.easting)) + (s1x * (a0.northing - b0.northing))) / denom;
            double t = ((s2x * (a0.northing - b0.northing)) - (s2y * (a0.easting - b0.easting))) / denom;

            if (s < 0.0 || s > 1.0 || t < 0.0 || t > 1.0) return false;

            east = a0.easting + (t * s1x);
            north = a0.northing + (t * s1y);
            return true;
        }
    }
}
