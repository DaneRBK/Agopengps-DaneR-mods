using AgOpenGPS.Core.Drawing;
using AgOpenGPS.Core.DrawLib;
using AgOpenGPS.Core.Models;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace AgOpenGPS
{
    public class CABLine
    {
        private readonly ColorRgba newAbLineColor = new ColorRgba(0.95f, 0.70f, 0.50f);
        private readonly ColorRgba pointsTextGreen = new ColorRgba(0.2f, 0.950f, 0.20f);
        private readonly ColorRgba pointARed = new ColorRgba(0.95f, 0.0f, 0.0f);
        private readonly ColorRgba pointBCyan = new ColorRgba(0.0f, 0.90f, 0.95f);
        private readonly ColorRgba referenceLineRed = new ColorRgba(0.930f, 0.2f, 0.2f);
        private readonly ColorRgba currentAbLinePurple = new ColorRgba(0.95f, 0.20f, 0.950f);
        private readonly ColorRgba extraGuidelinesBlack = new ColorRgba(0.0f, 0.0f, 0.0f, 0.5f);
        private readonly ColorRgba extraGuidelinesGreen = new ColorRgba(0.19907f, 0.6f, 0.19750f, 0.6f);
        private readonly ColorRgba obstacleAvoidanceColor = new ColorRgba(0.05f, 0.85f, 0.95f);

        public double abHeading, abLength;

        public bool isABValid;

        //the current AB guidance line
        public vec3 currentLinePtA = new vec3(0.0, 0.0, 0.0);
        public vec3 currentLinePtB = new vec3(0.0, 1.0, 0.0);

        public double distanceFromCurrentLinePivot;
        public double distanceFromRefLine;

        //pure pursuit values
        public vec2 goalPointAB = new vec2(0, 0);

        public int howManyPathsAway, lastHowManyPathsAway;
        public bool isMakingABLine;
        public bool isHeadingSameWay = true, lastIsHeadingSameWay;

        //public bool isOnTramLine;
        //public int tramBasedOn;
        public double ppRadiusAB;

        public vec2 radiusPointAB = new vec2(0, 0);
        public double rEastAB, rNorthAB;

        public double snapDistance, lastSecond = 0;
        public double steerAngleAB;
        public int lineWidth, numGuideLines;

        //design
        public vec2 desPtA = new vec2(0.2, 0.15);
        public vec2 desPtB = new vec2(0.3, 0.3);

        public vec2 desLineEndA = new vec2(0.0, 0.0);
        public vec2 desLineEndB = new vec2(999997, 1.0);

        public double desHeading = 0;

        public string desName = "";

        //autosteer errors
        public double pivotDistanceError, pivotDistanceErrorLast, pivotDerivative;

        //derivative counters
        private int counter2;

        private const double ObstacleFlagRadius = 1.0;
        private const double ObstacleDetectionAheadMeters = 30.0;
        private bool isObstacleAvoidanceActive;
        private readonly List<XyCoord> obstacleAvoidancePath = new List<XyCoord>(64);

        public double inty;
        public double pivotErrorTotal;

        //Color tramColor = Color.YellowGreen;

        //pointers to mainform controls
        private readonly FormGPS mf;

        public CABLine(FormGPS _f)
        {
            //constructor
            mf = _f;
            //isOnTramLine = true;
            lineWidth = Properties.Settings.Default.setDisplay_lineWidth;
            abLength = 2000;
            numGuideLines = Properties.Settings.Default.setAS_numGuideLines;
        }

        public vec2 GetTravelRightUnitVector()
        {
            double directionSign = (mf.isReverse ^ isHeadingSameWay) ? 1.0 : -1.0;

            return new vec2(
                Math.Cos(abHeading) * directionSign,
                -Math.Sin(abHeading) * directionSign);
        }

        public double GetObstacleAvoidanceOffsetMeters(vec3 pivot, double goalPointDistance)
        {
            isObstacleAvoidanceActive = false;

            if (mf.flagPts == null || mf.flagPts.Count == 0) return 0;

            vec2 right = GetTravelRightUnitVector();
            double forwardEast = -right.northing;
            double forwardNorth = right.easting;

            double halfToolWidth = Math.Max(0.5, mf.tool.width * 0.5);
            double obstacleOffset = Math.Max(0.0, Properties.ToolSettings.Default.setVehicle_obstacleOffset);
            double defaultClearance = halfToolWidth + ObstacleFlagRadius + obstacleOffset;
            double steeringScanDistance = Math.Max(goalPointDistance * 3.0, defaultClearance + 8.0);
            if (steeringScanDistance > ObstacleDetectionAheadMeters) steeringScanDistance = ObstacleDetectionAheadMeters;
            double displayScanDistance = ObstacleDetectionAheadMeters;

            double strongestOffset = 0;
            double strongestInfluence = 0;
            CFlag strongestFlag = null;
            double strongestAhead = 0;
            CFlag displayFlag = null;
            double displayAhead = 0;

            foreach (CFlag flag in mf.flagPts)
            {
                if (!IsObstacleFlag(flag)) continue;
                GetObstacleExtents(flag, right, forwardEast, forwardNorth, out double halfForward, out double halfLateral);
                double clearance = halfToolWidth + halfLateral + obstacleOffset;

                double east = flag.easting - pivot.easting;
                double north = flag.northing - pivot.northing;
                double ahead = (east * forwardEast) + (north * forwardNorth);
                double nearAhead = ahead - halfForward;
                double farAhead = ahead + halfForward;

                if (farAhead < 0 || nearAhead > displayScanDistance) continue;

                double lateral = (east * right.easting) + (north * right.northing);
                double lateralLimit = clearance + halfToolWidth;

                if (Math.Abs(lateral) > lateralLimit) continue;

                if (displayFlag == null || ahead < displayAhead)
                {
                    displayFlag = flag;
                    displayAhead = ahead;
                }

                if (nearAhead > steeringScanDistance) continue;

                double distanceInfluence = nearAhead <= 0.0 ? 1.0 : 1.0 - (nearAhead / steeringScanDistance);
                double lateralInfluence = 1.0 - (Math.Abs(lateral) / lateralLimit);
                double influence = Math.Max(0.0, distanceInfluence * lateralInfluence);

                if (influence <= strongestInfluence) continue;

                strongestInfluence = influence;
                strongestOffset = (lateral >= 0 ? -1.0 : 1.0) * clearance * Math.Min(1.0, 0.35 + influence);
                strongestFlag = flag;
                strongestAhead = nearAhead;
            }

            if (displayFlag != null)
            {
                BuildPredictedObstacleAvoidancePath(
                    pivot,
                    goalPointDistance,
                    defaultClearance,
                    steeringScanDistance,
                    right,
                    forwardEast,
                    forwardNorth);
            }

            return strongestOffset;
        }

        public static bool IsObstacleFlag(CFlag flag)
        {
            if (flag == null) return false;

            string notes = flag.notes ?? string.Empty;
            if (TryGetObstacleTagType(notes, out string obstacleType))
            {
                return string.Equals(obstacleType, "POLE", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(obstacleType, "HOLE", StringComparison.OrdinalIgnoreCase);
            }

            return notes.IndexOf("tree", StringComparison.OrdinalIgnoreCase) >= 0
                || notes.IndexOf("drvo", StringComparison.OrdinalIgnoreCase) >= 0
                || notes.IndexOf("stablo", StringComparison.OrdinalIgnoreCase) >= 0
                || notes.IndexOf("hole", StringComparison.OrdinalIgnoreCase) >= 0
                || notes.IndexOf("rupa", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool TryGetObstacleTagType(string notes, out string obstacleType)
        {
            obstacleType = string.Empty;
            const string obstacleTagStart = "[AOG_OBST:";

            if (string.IsNullOrWhiteSpace(notes)) return false;

            int start = notes.IndexOf(obstacleTagStart, StringComparison.Ordinal);
            if (start < 0) return false;

            int typeStart = start + obstacleTagStart.Length;
            int typeEnd = notes.IndexOfAny(new[] { ';', ']' }, typeStart);
            if (typeEnd <= typeStart) return false;

            obstacleType = notes.Substring(typeStart, typeEnd - typeStart);
            return true;
        }

        private static bool TryGetObstacleTag(
            string notes,
            out string obstacleType,
            out double widthMeters,
            out double lengthMeters)
        {
            obstacleType = string.Empty;
            widthMeters = 0.3;
            lengthMeters = 0.3;
            const string obstacleTagStart = "[AOG_OBST:";

            if (string.IsNullOrWhiteSpace(notes)) return false;

            int start = notes.IndexOf(obstacleTagStart, StringComparison.Ordinal);
            if (start < 0) return false;

            int end = notes.IndexOf(']', start);
            if (end < 0) return false;

            string tag = notes.Substring(start + obstacleTagStart.Length, end - start - obstacleTagStart.Length);
            string[] parts = tag.Split(';');
            if (parts.Length < 1) return false;

            obstacleType = parts[0];

            for (int i = 1; i < parts.Length; i++)
            {
                string[] pair = parts[i].Split('=');
                if (pair.Length != 2) continue;

                if (string.Equals(pair[0], "W", StringComparison.OrdinalIgnoreCase)
                    && double.TryParse(pair[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedWidth))
                {
                    widthMeters = Math.Max(0.05, parsedWidth);
                }

                if (string.Equals(pair[0], "L", StringComparison.OrdinalIgnoreCase)
                    && double.TryParse(pair[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedLength))
                {
                    lengthMeters = Math.Max(0.05, parsedLength);
                }
            }

            return true;
        }

        private static void GetObstacleExtents(
            CFlag flag,
            vec2 right,
            double forwardEast,
            double forwardNorth,
            out double halfForward,
            out double halfLateral)
        {
            halfForward = ObstacleFlagRadius;
            halfLateral = ObstacleFlagRadius;

            if (!TryGetObstacleTag(flag.notes, out string obstacleType, out double widthMeters, out double lengthMeters)
                || !string.Equals(obstacleType, "HOLE", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            double halfLength = Math.Max(0.05, lengthMeters) * 0.5;
            double halfWidth = Math.Max(0.05, widthMeters) * 0.5;
            double holeForwardEast = Math.Sin(flag.heading);
            double holeForwardNorth = Math.Cos(flag.heading);
            double holeRightEast = Math.Sin(flag.heading + glm.PIBy2);
            double holeRightNorth = Math.Cos(flag.heading + glm.PIBy2);

            halfForward =
                Math.Abs((holeForwardEast * forwardEast) + (holeForwardNorth * forwardNorth)) * halfLength
                + Math.Abs((holeRightEast * forwardEast) + (holeRightNorth * forwardNorth)) * halfWidth;

            halfLateral =
                Math.Abs((holeForwardEast * right.easting) + (holeForwardNorth * right.northing)) * halfLength
                + Math.Abs((holeRightEast * right.easting) + (holeRightNorth * right.northing)) * halfWidth;
        }

        private double CalculateObstacleAvoidanceOffset(
            double pivotEasting,
            double pivotNorthing,
            double clearance,
            double scanDistance,
            vec2 right,
            double forwardEast,
            double forwardNorth)
        {
            double halfToolWidth = Math.Max(0.5, mf.tool.width * 0.5);
            double userOffset = Math.Max(0.0, Properties.ToolSettings.Default.setVehicle_obstacleOffset);
            double strongestOffset = 0.0;
            double strongestInfluence = 0.0;

            foreach (CFlag flag in mf.flagPts)
            {
                if (!IsObstacleFlag(flag)) continue;
                GetObstacleExtents(flag, right, forwardEast, forwardNorth, out double halfForward, out double halfLateral);
                double obstacleClearance = halfToolWidth + halfLateral + userOffset;
                double lateralLimit = obstacleClearance + halfToolWidth;

                double east = flag.easting - pivotEasting;
                double north = flag.northing - pivotNorthing;
                double ahead = (east * forwardEast) + (north * forwardNorth);
                double nearAhead = ahead - halfForward;
                double farAhead = ahead + halfForward;

                if (farAhead < 0 || nearAhead > scanDistance) continue;

                double lateral = (east * right.easting) + (north * right.northing);
                if (Math.Abs(lateral) > lateralLimit) continue;

                double distanceInfluence = nearAhead <= 0.0 ? 1.0 : 1.0 - (nearAhead / scanDistance);
                double lateralInfluence = 1.0 - (Math.Abs(lateral) / lateralLimit);
                double influence = Math.Max(0.0, distanceInfluence * lateralInfluence);

                if (influence <= strongestInfluence) continue;

                strongestInfluence = influence;
                strongestOffset = (lateral >= 0 ? -1.0 : 1.0) * obstacleClearance * Math.Min(1.0, 0.35 + influence);
            }

            return strongestOffset;
        }

        private void BuildPredictedObstacleAvoidancePath(
            vec3 pivot,
            double goalPointDistance,
            double clearance,
            double steeringScanDistance,
            vec2 right,
            double forwardEast,
            double forwardNorth)
        {
            obstacleAvoidancePath.Clear();

            double simEasting = pivot.easting;
            double simNorthing = pivot.northing;
            double simHeading = pivot.heading;
            double step = 1.0;
            double visibleDistance = ObstacleDetectionAheadMeters;
            double wheelbase = Math.Max(0.5, mf.vehicle.VehicleConfig.Wheelbase);
            double lookAhead = Math.Max(2.0, goalPointDistance);

            obstacleAvoidancePath.Add(new XyCoord(simEasting, simNorthing));

            for (double distanceTravelled = 0.0; distanceTravelled < visibleDistance; distanceTravelled += step)
            {
                double obstacleOffset = CalculateObstacleAvoidanceOffset(
                    simEasting,
                    simNorthing,
                    clearance,
                    steeringScanDistance,
                    right,
                    forwardEast,
                    forwardNorth);

                vec2 simPivot = new vec2(simEasting, simNorthing);
                vec2 basePoint = GetClosestPointOnCurrentLine(simPivot);
                vec2 goalPoint = new vec2(
                    basePoint.easting + (forwardEast * lookAhead) + (right.easting * obstacleOffset),
                    basePoint.northing + (forwardNorth * lookAhead) + (right.northing * obstacleOffset));

                double goalPointDistanceSquared = glm.DistanceSquared(goalPoint.northing, goalPoint.easting, simNorthing, simEasting);
                if (goalPointDistanceSquared < 0.0001) break;

                double localHeading = glm.twoPI - simHeading;
                double steerAngle = glm.toDegrees(Math.Atan(2 * (((goalPoint.easting - simEasting) * Math.Cos(localHeading))
                    + ((goalPoint.northing - simNorthing) * Math.Sin(localHeading))) * wheelbase
                    / goalPointDistanceSquared));

                if (steerAngle < -mf.vehicle.maxSteerAngle) steerAngle = -mf.vehicle.maxSteerAngle;
                if (steerAngle > mf.vehicle.maxSteerAngle) steerAngle = mf.vehicle.maxSteerAngle;

                simHeading += (step / wheelbase) * Math.Tan(glm.toRadians(steerAngle));
                if (simHeading < 0.0) simHeading += glm.twoPI;
                if (simHeading > glm.twoPI) simHeading -= glm.twoPI;

                simEasting += Math.Sin(simHeading) * step;
                simNorthing += Math.Cos(simHeading) * step;
                obstacleAvoidancePath.Add(new XyCoord(simEasting, simNorthing));
            }

            isObstacleAvoidanceActive = true;
        }

        private vec2 GetClosestPointOnCurrentLine(vec3 pivot)
        {
            return GetClosestPointOnCurrentLine(new vec2(pivot.easting, pivot.northing));
        }

        private vec2 GetClosestPointOnCurrentLine(vec2 pivot)
        {
            double dx = currentLinePtB.easting - currentLinePtA.easting;
            double dy = currentLinePtB.northing - currentLinePtA.northing;
            double lengthSquared = (dx * dx) + (dy * dy);

            if (lengthSquared < 0.000001)
            {
                return new vec2(pivot.easting, pivot.northing);
            }

            double u = (((pivot.easting - currentLinePtA.easting) * dx)
                + ((pivot.northing - currentLinePtA.northing) * dy))
                / lengthSquared;

            return new vec2(
                currentLinePtA.easting + (u * dx),
                currentLinePtA.northing + (u * dy));
        }

        public void BuildCurrentABLineList(vec3 pivot)
        {
            if (mf.trk.gArr.Count < mf.trk.idx || mf.trk.idx < 0) return;

            CTrk track = mf.trk.gArr[mf.trk.idx];

            if (!isABValid || ((mf.secondsSinceStart - lastSecond) > 0.66 && (!mf.isBtnAutoSteerOn || mf.mc.steerSwitchHigh)))
            {
                lastSecond = mf.secondsSinceStart;

                double dx, dy;

                abHeading = track.heading;

                track.endPtA.easting = track.ptA.easting - (Math.Sin(abHeading) * abLength);
                track.endPtA.northing = track.ptA.northing - (Math.Cos(abHeading) * abLength);

                track.endPtB.easting = track.ptB.easting + (Math.Sin(abHeading) * abLength);
                track.endPtB.northing = track.ptB.northing + (Math.Cos(abHeading) * abLength);

                //move the ABLine over based on the overlap amount set in
                double widthMinusOverlap = mf.tool.width - mf.tool.overlap;

                //x2-x1
                dx = track.endPtB.easting - track.endPtA.easting;
                //z2-z1
                dy = track.endPtB.northing - track.endPtA.northing;

                distanceFromRefLine = ((dy * mf.guidanceLookPos.easting) - (dx * mf.guidanceLookPos.northing) + (track.endPtB.easting
                                        * track.endPtA.northing) - (track.endPtB.northing * track.endPtA.easting))
                                            / Math.Sqrt((dy * dy) + (dx * dx));

                distanceFromRefLine -= (0.5 * widthMinusOverlap);

                isHeadingSameWay = Math.PI - Math.Abs(Math.Abs(pivot.heading - abHeading) - Math.PI) < glm.PIBy2;

                //if (mf.yt.isYouTurnTriggered && !mf.yt.isGoingStraightThrough) isHeadingSameWay = !isHeadingSameWay;

                //Which ABLine is the vehicle on, negative is left and positive is right side

                double RefDist = (distanceFromRefLine + (isHeadingSameWay ? mf.tool.offset : -mf.tool.offset) - track.nudgeDistance) / widthMinusOverlap;

                if (RefDist < 0) howManyPathsAway = (int)(RefDist - 0.5);
                else howManyPathsAway = (int)(RefDist + 0.5);
            }

            if (!isABValid || howManyPathsAway != lastHowManyPathsAway || (isHeadingSameWay != lastIsHeadingSameWay && mf.tool.offset != 0))
            {
                isABValid = true;
                lastHowManyPathsAway = howManyPathsAway;
                lastIsHeadingSameWay = isHeadingSameWay;

                double widthMinusOverlap = mf.tool.width - mf.tool.overlap;

                double distAway = widthMinusOverlap * howManyPathsAway + (isHeadingSameWay ? -mf.tool.offset : mf.tool.offset) + track.nudgeDistance;

                distAway += (0.5 * widthMinusOverlap);

                //move the curline as well. 
                vec2 nudgePtA = new vec2(track.ptA);
                vec2 nudgePtB = new vec2(track.ptB);

                //depending which way you are going, the offset can be either side
                vec2 point1 = new vec2((Math.Cos(-abHeading) * distAway) + nudgePtA.easting, (Math.Sin(-abHeading) * distAway) + nudgePtA.northing);

                vec2 point2 = new vec2((Math.Cos(-abHeading) * distAway) + nudgePtB.easting, (Math.Sin(-abHeading) * distAway) + nudgePtB.northing);

                //create the new line extent points for current ABLine based on original heading of AB line
                currentLinePtA.easting = point1.easting - (Math.Sin(abHeading) * abLength);
                currentLinePtA.northing = point1.northing - (Math.Cos(abHeading) * abLength);

                currentLinePtB.easting = point2.easting + (Math.Sin(abHeading) * abLength);
                currentLinePtB.northing = point2.northing + (Math.Cos(abHeading) * abLength);

                currentLinePtA.heading = abHeading;
                currentLinePtB.heading = abHeading;
            }
        }

        public void GetCurrentABLine(vec3 pivot, vec3 steer)
        {
            double dx, dy;

            //Check uturn first
            if (mf.yt.isYouTurnTriggered && mf.yt.DistanceFromYouTurnLine())//do the pure pursuit from youTurn
            {
                //now substitute what it thinks are AB line values with auto turn values
                steerAngleAB = mf.yt.steerAngleYT;
                distanceFromCurrentLinePivot = mf.yt.distanceFromCurrentLine;

                goalPointAB = mf.yt.goalPointYT;
                radiusPointAB.easting = mf.yt.radiusPointYT.easting;
                radiusPointAB.northing = mf.yt.radiusPointYT.northing;
                ppRadiusAB = mf.yt.ppRadiusYT;

                mf.vehicle.modeTimeCounter = 0;
                mf.vehicle.modeActualXTE = (distanceFromCurrentLinePivot);
            }

            //Stanley
            else if (mf.isStanleyUsed)
                mf.gyd.StanleyGuidanceABLine(currentLinePtA, currentLinePtB, pivot, steer);

            //Pure Pursuit
            else
            {
                //get the distance from currently active AB line
                //x2-x1
                dx = currentLinePtB.easting - currentLinePtA.easting;
                //z2-z1
                dy = currentLinePtB.northing - currentLinePtA.northing;

                //how far from current AB Line is fix
                distanceFromCurrentLinePivot = ((dy * pivot.easting) - (dx * pivot.northing) + (currentLinePtB.easting
                            * currentLinePtA.northing) - (currentLinePtB.northing * currentLinePtA.easting))
                            / Math.Sqrt((dy * dy) + (dx * dx));

                //integral slider is set to 0
                if (mf.vehicle.purePursuitIntegralGain != 0 && !mf.isReverse)
                {
                    pivotDistanceError = distanceFromCurrentLinePivot * 0.2 + pivotDistanceError * 0.8;

                    if (counter2++ > 4)
                    {
                        pivotDerivative = pivotDistanceError - pivotDistanceErrorLast;
                        pivotDistanceErrorLast = pivotDistanceError;
                        counter2 = 0;
                        pivotDerivative *= 2;

                        //limit the derivative
                        //if (pivotDerivative > 0.03) pivotDerivative = 0.03;
                        //if (pivotDerivative < -0.03) pivotDerivative = -0.03;
                        //if (Math.Abs(pivotDerivative) < 0.01) pivotDerivative = 0;
                    }

                    //pivotErrorTotal = pivotDistanceError + pivotDerivative;

                    if (mf.isBtnAutoSteerOn
                        && Math.Abs(pivotDerivative) < (0.1)
                        && mf.avgSpeed > 2.5
                        && !mf.yt.isYouTurnTriggered)
                    //&& Math.Abs(pivotDistanceError) < 0.2)

                    {
                        //if over the line heading wrong way, rapidly decrease integral
                        if ((inty < 0 && distanceFromCurrentLinePivot < 0) || (inty > 0 && distanceFromCurrentLinePivot > 0))
                        {
                            inty += pivotDistanceError * mf.vehicle.purePursuitIntegralGain * -0.04;
                        }
                        else
                        {
                            if (Math.Abs(distanceFromCurrentLinePivot) > 0.02)
                            {
                                inty += pivotDistanceError * mf.vehicle.purePursuitIntegralGain * -0.02;
                                if (inty > 0.2) inty = 0.2;
                                else if (inty < -0.2) inty = -0.2;
                            }
                        }
                    }
                    else inty *= 0.95;
                }
                else inty = 0;

                // ** Pure pursuit ** - calc point on ABLine closest to current position
                double U = (((pivot.easting - currentLinePtA.easting) * dx)
                            + ((pivot.northing - currentLinePtA.northing) * dy))
                            / ((dx * dx) + (dy * dy));

                //point on AB line closest to pivot axle point
                rEastAB = currentLinePtA.easting + (U * dx);
                rNorthAB = currentLinePtA.northing + (U * dy);

                //update base on autosteer settings and distance from line
                double goalPointDistance = mf.vehicle.UpdateGoalPointDistance();

                if (mf.isReverse ^ isHeadingSameWay)
                {
                    goalPointAB.easting = rEastAB + (Math.Sin(abHeading) * goalPointDistance);
                    goalPointAB.northing = rNorthAB + (Math.Cos(abHeading) * goalPointDistance);
                }
                else
                {
                    goalPointAB.easting = rEastAB - (Math.Sin(abHeading) * goalPointDistance);
                    goalPointAB.northing = rNorthAB - (Math.Cos(abHeading) * goalPointDistance);
                }

                double obstacleOffset = GetObstacleAvoidanceOffsetMeters(pivot, goalPointDistance);
                if (Math.Abs(obstacleOffset) > 0.01)
                {
                    vec2 right = GetTravelRightUnitVector();
                    goalPointAB.easting += right.easting * obstacleOffset;
                    goalPointAB.northing += right.northing * obstacleOffset;
                }

                //calc "D" the distance from pivot axle to lookahead point
                double goalPointDistanceDSquared
                    = glm.DistanceSquared(goalPointAB.northing, goalPointAB.easting, pivot.northing, pivot.easting);

                //calculate the the new x in local coordinates and steering angle degrees based on wheelbase
                double localHeading;

                if (isHeadingSameWay) localHeading = glm.twoPI - mf.fixHeading + inty;
                else localHeading = glm.twoPI - mf.fixHeading - inty;

                ppRadiusAB = goalPointDistanceDSquared / (2 * (((goalPointAB.easting - pivot.easting) * Math.Cos(localHeading))
                    + ((goalPointAB.northing - pivot.northing) * Math.Sin(localHeading))));

                steerAngleAB = glm.toDegrees(Math.Atan(2 * (((goalPointAB.easting - pivot.easting) * Math.Cos(localHeading))
                    + ((goalPointAB.northing - pivot.northing) * Math.Sin(localHeading))) * mf.vehicle.VehicleConfig.Wheelbase
                    / goalPointDistanceDSquared));

                double rollForSteering = mf.GetRollForSteering();
                if (rollForSteering != 88888)
                    steerAngleAB += rollForSteering * -mf.gyd.sideHillCompFactor;

                //steerAngleAB *= 1.4;

                if (steerAngleAB < -mf.vehicle.maxSteerAngle) steerAngleAB = -mf.vehicle.maxSteerAngle;
                if (steerAngleAB > mf.vehicle.maxSteerAngle) steerAngleAB = mf.vehicle.maxSteerAngle;

                //limit circle size for display purpose
                if (ppRadiusAB < -500) ppRadiusAB = -500;
                if (ppRadiusAB > 500) ppRadiusAB = 500;

                radiusPointAB.easting = pivot.easting + (ppRadiusAB * Math.Cos(localHeading));
                radiusPointAB.northing = pivot.northing + (ppRadiusAB * Math.Sin(localHeading));

                //if (mf.isConstantContourOn)
                //{
                //    //angular velocity in rads/sec  = 2PI * m/sec * radians/meters

                //    //clamp the steering angle to not exceed safe angular velocity
                //    if (Math.Abs(mf.setAngVel) > 1000)
                //    {
                //        //mf.setAngVel = mf.setAngVel < 0 ? -mf.vehicle.maxAngularVelocity : mf.vehicle.maxAngularVelocity;
                //        mf.setAngVel = mf.setAngVel < 0 ? -1000 : 1000;
                //    }
                //}

                //distance is negative if on left, positive if on right
                if (!isHeadingSameWay)
                    distanceFromCurrentLinePivot *= -1.0;

                //used for acquire/hold mode
                mf.vehicle.modeActualXTE = (distanceFromCurrentLinePivot);

                double steerHeadingError = (pivot.heading - abHeading);
                //Fix the circular error
                if (steerHeadingError > Math.PI)
                    steerHeadingError -= Math.PI;
                else if (steerHeadingError < -Math.PI)
                    steerHeadingError += Math.PI;

                if (steerHeadingError > glm.PIBy2)
                    steerHeadingError -= Math.PI;
                else if (steerHeadingError < -glm.PIBy2)
                    steerHeadingError += Math.PI;

                mf.vehicle.modeActualHeadingError = glm.toDegrees(steerHeadingError);

                //Convert to millimeters
                mf.guidanceLineDistanceOff = (short)Math.Round(distanceFromCurrentLinePivot * 1000.0, MidpointRounding.AwayFromZero);
                mf.guidanceLineSteerAngle = (short)(steerAngleAB * 100);
            }

            //mf.setAngVel = 0.277777 * mf.avgSpeed * (Math.Tan(glm.toRadians(steerAngleAB))) / mf.vehicle.wheelbase;
            //mf.setAngVel = glm.toDegrees(mf.setAngVel);
        }

        public void DrawABLineNew()
        {
            //ABLine currently being designed
            GeoCoord[] desLineEndPoints = { desLineEndA.ToGeoCoord(), desLineEndB.ToGeoCoord() };

            GLW.SetLineWidth(lineWidth);
            GLW.SetColor(newAbLineColor);
            GLW.DrawLinesPrimitive(desLineEndPoints);

            GLW.SetColor(pointsTextGreen);
            mf.font.DrawText3D(desPtA.easting, desPtA.northing, "&A", mf.camHeading);
            mf.font.DrawText3D(desPtB.easting, desPtB.northing, "&B", mf.camHeading);
        }

        public void DrawABLines()
        {
            // Don't draw if AB line is not valid yet (prevents drawing with uninitialized values after track switch)
            if (!isABValid) return;

            // Draw AB Points
            CTrk track = mf.trk.gArr[mf.trk.idx];
            GLW.SetPointSize(8.0f);
            GLW.BeginPointsPrimitive();

            GLW.SetColor(pointBCyan);
            GLW.Vertex2(track.ptB.ToGeoCoord());
            GLW.SetColor(pointARed);
            GLW.Vertex2(track.ptA.ToGeoCoord());
            GLW.EndPrimitive();

            GLW.DrawPoint(track.ptA.ToGeoCoord());

            if (!isMakingABLine)
            {
                mf.font.DrawText3D(track.ptA.easting, track.ptA.northing, "&A", mf.camHeading);
                mf.font.DrawText3D(track.ptB.easting, track.ptB.northing, "&B", mf.camHeading);
            }

            GLW.SetPointSize(1.0f);

            //Draw reference AB line
            GeoCoord[] abEndPoints = { track.endPtA.ToGeoCoord(), track.endPtB.ToGeoCoord() };
            GLW.SetLineWidth(4.0f);
            GLW.EnableLineStipple();
            GLW.SetLineStipple(1, 0x0F00);
            GLW.SetColor(referenceLineRed);
            GLW.DrawLinesPrimitive(abEndPoints);
            GLW.DisableLineStipple();

            //draw current AB Line
            GeoCoord[] currentAbLine = { currentLinePtA.ToGeoCoord(), currentLinePtB.ToGeoCoord() };
            LineStyle blackBackgroundStyle = new LineStyle(lineWidth * 3, Colors.Black);
            LineStyle purpleForgroundStyle = new LineStyle(lineWidth, currentAbLinePurple);
            GLW.DrawLinesPrimitiveLayered(
                currentAbLine,
                blackBackgroundStyle,
                purpleForgroundStyle);

            DrawObstacleAvoidancePath();

            if (mf.isSideGuideLines && mf.camera.camSetDistance > mf.tool.width * -400)
            {
                double toolWidth = mf.tool.width - mf.tool.overlap;
                GeoLineSegment currentLine = new GeoLineSegment(currentLinePtA.ToGeoCoord(), currentLinePtB.ToGeoCoord());
                GeoDir perpendicularRightDir = currentLine.Direction.PerpendicularRight;
                GeoLineSegment[] lines = new GeoLineSegment[2 * numGuideLines];
                int linesIndex = 0;

                double oddOffset = 2 * (isHeadingSameWay ? mf.tool.offset : -mf.tool.offset);
                for (int i = 1; i <= numGuideLines; i += 2)
                {
                    GeoLineSegment rightOddLine = currentLine.Shifted((toolWidth * i + oddOffset) * perpendicularRightDir);
                    GeoLineSegment leftOddLine = currentLine.Shifted((toolWidth * -i + oddOffset) * perpendicularRightDir);
                    lines[linesIndex++] = rightOddLine;
                    lines[linesIndex++] = leftOddLine;
                }
                for (int i = 2; i <= numGuideLines; i += 2)
                {
                    GeoLineSegment rightEvenLine = currentLine.Shifted((toolWidth * i) * perpendicularRightDir);
                    GeoLineSegment leftEvenLine = currentLine.Shifted((toolWidth * -i) * perpendicularRightDir);
                    lines[linesIndex++] = rightEvenLine;
                    lines[linesIndex++] = leftEvenLine;
                }
                LineStyle extraGuidelinesBackgroundStyle = new LineStyle(lineWidth * 3, extraGuidelinesBlack);
                LineStyle extraGuidelinesForegroundStyle = new LineStyle(lineWidth, extraGuidelinesGreen);
                GLW.DrawLinesPrimitiveLayered(
                    lines,
                    extraGuidelinesBackgroundStyle,
                    extraGuidelinesForegroundStyle);
            }
            mf.yt.DrawYouTurn();

            GLW.SetPointSize(1.0f);
            GLW.SetLineWidth(1.0f);
        }

        private void DrawObstacleAvoidancePath()
        {
            if (!isObstacleAvoidanceActive || obstacleAvoidancePath.Count < 2) return;

            XyCoord[] avoidancePath = obstacleAvoidancePath.ToArray();

            GLW.SetLineWidth(7.0f);
            GLW.EnableLineStipple();
            GLW.SetLineStipple(1, 0x0F0F);
            GLW.SetColor(Colors.Black);
            GLW.DrawLineStripPrimitive(avoidancePath);

            GLW.SetLineWidth(4.0f);
            GLW.SetColor(obstacleAvoidanceColor);
            GLW.DrawLineStripPrimitive(avoidancePath);
            GLW.DisableLineStipple();
        }

        public void BuildTram()
        {
            if (mf.tram.generateMode != 1)
            {
                mf.tram.BuildTramBnd();
            }
            else
            {
                mf.tram.tramBndOuterArr?.Clear();
                mf.tram.tramBndInnerArr?.Clear();
            }

            mf.tram.tramList?.Clear();
            mf.tram.tramArr?.Clear();

            if (mf.tram.generateMode == 2) return;

            List<vec2> tramRef = new List<vec2>();

            bool isBndExist = mf.bnd.bndList.Count != 0;

            abHeading = mf.trk.gArr[mf.trk.idx].heading;

            double hsin = Math.Sin(abHeading);
            double hcos = Math.Cos(abHeading);

            double len = glm.Distance(mf.trk.gArr[mf.trk.idx].endPtA, mf.trk.gArr[mf.trk.idx].endPtB);
            //divide up the AB line into segments
            vec2 P1 = new vec2();
            for (int i = 0; i < (int)len; i += 4)
            {
                P1.easting = (hsin * i) + mf.trk.gArr[mf.trk.idx].endPtA.easting;
                P1.northing = (hcos * i) + mf.trk.gArr[mf.trk.idx].endPtA.northing;
                tramRef.Add(P1);
            }

            //create list of list of points of triangle strip of AB Highlight
            double headingCalc = abHeading + glm.PIBy2;

            hsin = Math.Sin(headingCalc);
            hcos = Math.Cos(headingCalc);

            mf.tram.tramList?.Clear();
            mf.tram.tramArr?.Clear();

            //no boundary starts on first pass
            int cntr = 0;
            if (isBndExist)
            {
                if (mf.tram.generateMode == 1)
                    cntr = 0;
                else
                    cntr = 1;
            }

            double widd;
            for (int i = cntr; i < mf.tram.passes; i++)
            {
                mf.tram.tramArr = new List<vec2>
                {
                    Capacity = 128
                };

                mf.tram.tramList.Add(mf.tram.tramArr);

                widd = (mf.tram.tramWidth * 0.5) - mf.tram.halfWheelTrack;
                widd += (mf.tram.tramWidth * i);

                for (int j = 0; j < tramRef.Count; j++)
                {
                    P1.easting = hsin * widd + tramRef[j].easting;
                    P1.northing = (hcos * widd) + tramRef[j].northing;

                    if (!isBndExist || mf.bnd.bndList[0].fenceLineEar.IsPointInPolygon(P1))
                    {
                        mf.tram.tramArr.Add(P1);
                    }
                }
            }

            for (int i = cntr; i < mf.tram.passes; i++)
            {
                mf.tram.tramArr = new List<vec2>
                {
                    Capacity = 128
                };

                mf.tram.tramList.Add(mf.tram.tramArr);

                widd = (mf.tram.tramWidth * 0.5) + mf.tram.halfWheelTrack;
                widd += (mf.tram.tramWidth * i);

                for (int j = 0; j < tramRef.Count; j++)
                {
                    P1.easting = (hsin * widd) + tramRef[j].easting;
                    P1.northing = (hcos * widd) + tramRef[j].northing;

                    if (!isBndExist || mf.bnd.bndList[0].fenceLineEar.IsPointInPolygon(P1))
                    {
                        mf.tram.tramArr.Add(P1);
                    }
                }
            }

            tramRef?.Clear();
            //outside tram

            if (mf.bnd.bndList.Count == 0 || mf.tram.passes != 0)
            {
                //return;
            }
        }
    }
}
