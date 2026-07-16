using AgLibrary.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AgOpenGPS
{
    public partial class FormGPS
    {
        private readonly List<AutoRollLearnPass> autoRollLearnPasses = new List<AutoRollLearnPass>();
        private AutoRollLearnPass autoRollLearnActivePass;
        private DateTime autoRollLearnLastApplyUtc = DateTime.MinValue;
        private double autoRollLearnMeasuredErrorMeters;
        private double autoRollLearnSuggestedCorrectionDeg;
        private double autoRollLearnConfidence;
        private string autoRollLearnStatus = "Waiting.";
        private string autoRollLearnLastAction = "None.";

        public double AutoRollLearnMeasuredErrorCm => autoRollLearnMeasuredErrorMeters * 100.0;
        public double AutoRollLearnSuggestedCorrectionDeg => autoRollLearnSuggestedCorrectionDeg;
        public double AutoRollLearnConfidence => autoRollLearnConfidence;
        public double AutoRollLearnRequiredConfidence => Properties.VehicleSettings.Default.setIMU_autoRollLearnMinConfidence;
        public string AutoRollLearnStatus => autoRollLearnStatus;
        public string AutoRollLearnLastAction => autoRollLearnLastAction;
        public bool AutoRollLearnHasSuggestion => Math.Abs(autoRollLearnSuggestedCorrectionDeg) >= 0.005 && autoRollLearnConfidence > 0.0;
        public int AutoRollLearnPassCount => autoRollLearnPasses.Count;

        private sealed class AutoRollLearnPass
        {
            public int TrackIndex;
            public int Lane;
            public int Direction;
            public int Samples;
            public double MinAlong = double.MaxValue;
            public double MaxAlong = double.MinValue;
            public double MinEdgeSum;
            public double MaxEdgeSum;
            public double XteAbsSum;
            public DateTime LastSampleUtc;

            public double Length => MaxAlong > MinAlong ? MaxAlong - MinAlong : 0.0;
            public double AverageMinEdge => Samples > 0 ? MinEdgeSum / Samples : 0.0;
            public double AverageMaxEdge => Samples > 0 ? MaxEdgeSum / Samples : 0.0;
            public double AverageXteCm => Samples > 0 ? (XteAbsSum / Samples) * 100.0 : 0.0;
        }

        public void ResetAutoRollLearn()
        {
            autoRollLearnPasses.Clear();
            autoRollLearnActivePass = null;
            autoRollLearnMeasuredErrorMeters = 0.0;
            autoRollLearnSuggestedCorrectionDeg = 0.0;
            autoRollLearnConfidence = 0.0;
            autoRollLearnStatus = "Reset. Waiting for AB pass.";
            autoRollLearnLastAction = "Learning reset.";
        }

        public bool ApplyAutoRollLearnSuggestion(bool autoApply = false)
        {
            if (!AutoRollLearnHasSuggestion) return false;

            var settings = Properties.VehicleSettings.Default;
            double step = ClampAutoRollLearn(autoRollLearnSuggestedCorrectionDeg,
                -settings.setIMU_autoRollLearnMaxStepDeg,
                settings.setIMU_autoRollLearnMaxStepDeg);

            if (Math.Abs(step) < 0.001) return false;

            settings.setIMU_rollZero += step;
            settings.Save();
            ahrs.rollZero = settings.setIMU_rollZero;
            autoRollLearnLastApplyUtc = DateTime.UtcNow;
            autoRollLearnLastAction = (autoApply ? "Auto applied " : "Applied ")
                + step.ToString("N3", CultureInfo.CurrentCulture)
                + " deg. New roll zero "
                + settings.setIMU_rollZero.ToString("N3", CultureInfo.CurrentCulture)
                + ".";
            Log.EventWriter("Auto Roll Learn " + autoRollLearnLastAction);
            ResetAutoRollLearnAfterApply();
            return true;
        }

        private void ResetAutoRollLearnAfterApply()
        {
            string lastAction = autoRollLearnLastAction;
            autoRollLearnPasses.Clear();
            autoRollLearnActivePass = null;
            autoRollLearnMeasuredErrorMeters = 0.0;
            autoRollLearnSuggestedCorrectionDeg = 0.0;
            autoRollLearnConfidence = 0.0;
            autoRollLearnStatus = "Correction applied. Starting new learning cycle.";
            autoRollLearnLastAction = lastAction + " New learning cycle started.";
        }

        private void UpdateAutoRollLearn()
        {
            var settings = Properties.VehicleSettings.Default;
            if (!settings.setIMU_autoRollLearnEnabled)
            {
                EndAutoRollLearnActivePass();
                autoRollLearnStatus = "Off.";
                return;
            }

            if (!IsAutoRollLearnAllowed(out string reason))
            {
                EndAutoRollLearnActivePass();
                autoRollLearnStatus = reason;
                return;
            }

            AutoRollLearnPass pass = GetOrCreateAutoRollLearnPass();
            AddAutoRollLearnSample(pass);
            if (TryEvaluateAutoRollLearnPass(pass, true))
            {
                return;
            }

            autoRollLearnStatus = "Learning lane " + pass.Lane.ToString(CultureInfo.CurrentCulture)
                + " " + (pass.Direction > 0 ? "forward" : "reverse")
                + ", " + pass.Length.ToString("N1", CultureInfo.CurrentCulture)
                + " m.";
        }

        private bool IsAutoRollLearnAllowed(out string reason)
        {
            var settings = Properties.VehicleSettings.Default;
            reason = "Waiting.";

            if (!isJobStarted)
            {
                reason = "Waiting for open field.";
                return false;
            }

            if (!isBtnAutoSteerOn)
            {
                reason = "Waiting for autosteer.";
                return false;
            }

            if (trk.idx < 0 || trk.idx >= trk.gArr.Count || trk.gArr[trk.idx].mode != TrackMode.AB)
            {
                reason = "Waiting for AB line.";
                return false;
            }

            if (patchCounter <= 0)
            {
                reason = "Waiting for marking.";
                return false;
            }

            if (Math.Abs(avgSpeed) < 0.5)
            {
                reason = "Waiting for movement.";
                return false;
            }

            if (Math.Abs(ABLine.distanceFromCurrentLinePivot) * 100.0 > settings.setIMU_autoRollLearnMaxXteCm)
            {
                reason = "XTE too high: " + (Math.Abs(ABLine.distanceFromCurrentLinePivot) * 100.0).ToString("N1", CultureInfo.CurrentCulture) + " cm.";
                return false;
            }

            return true;
        }

        private AutoRollLearnPass GetOrCreateAutoRollLearnPass()
        {
            int direction = ABLine.isHeadingSameWay ? 1 : -1;
            int lane = ABLine.howManyPathsAway;
            int trackIndex = trk.idx;

            if (autoRollLearnActivePass != null
                && autoRollLearnActivePass.TrackIndex == trackIndex
                && autoRollLearnActivePass.Lane == lane
                && autoRollLearnActivePass.Direction == direction)
            {
                return autoRollLearnActivePass;
            }

            EndAutoRollLearnActivePass();
            autoRollLearnActivePass = new AutoRollLearnPass
            {
                TrackIndex = trackIndex,
                Lane = lane,
                Direction = direction
            };
            return autoRollLearnActivePass;
        }

        private void AddAutoRollLearnSample(AutoRollLearnPass pass)
        {
            double heading = trk.gArr[trk.idx].heading;
            double alongEast = Math.Sin(heading);
            double alongNorth = Math.Cos(heading);
            double normalEast = Math.Cos(heading);
            double normalNorth = -Math.Sin(heading);

            double toolEast = GetGpsOnlyMappingToolEasting();
            double toolNorth = GetGpsOnlyMappingToolNorthing();
            double along = ((toolEast - trk.gArr[trk.idx].ptA.easting) * alongEast)
                + ((toolNorth - trk.gArr[trk.idx].ptA.northing) * alongNorth);

            GetAutoRollLearnActiveSectionRange(out int startSection, out int endSection);
            double leftEdge = (section[startSection].leftPoint.easting * normalEast) + (section[startSection].leftPoint.northing * normalNorth);
            double rightEdge = (section[endSection].rightPoint.easting * normalEast) + (section[endSection].rightPoint.northing * normalNorth);
            double minEdge = Math.Min(leftEdge, rightEdge);
            double maxEdge = Math.Max(leftEdge, rightEdge);

            pass.MinAlong = Math.Min(pass.MinAlong, along);
            pass.MaxAlong = Math.Max(pass.MaxAlong, along);
            pass.MinEdgeSum += minEdge;
            pass.MaxEdgeSum += maxEdge;
            pass.XteAbsSum += Math.Abs(ABLine.distanceFromCurrentLinePivot);
            pass.Samples++;
            pass.LastSampleUtc = DateTime.UtcNow;
        }

        private void GetAutoRollLearnActiveSectionRange(out int startSection, out int endSection)
        {
            startSection = 0;
            endSection = Math.Max(0, tool.numOfSections - 1);

            for (int j = 0; j < tool.numOfSections; j++)
            {
                if (section[j].isMappingOn || section[j].isSectionOn)
                {
                    startSection = j;
                    break;
                }
            }

            for (int j = tool.numOfSections - 1; j >= 0; j--)
            {
                if (section[j].isMappingOn || section[j].isSectionOn)
                {
                    endSection = j;
                    break;
                }
            }
        }

        private void EndAutoRollLearnActivePass()
        {
            if (autoRollLearnActivePass == null) return;

            AutoRollLearnPass pass = autoRollLearnActivePass;
            autoRollLearnActivePass = null;

            if (!IsAutoRollLearnPassUsable(pass))
            {
                return;
            }

            autoRollLearnPasses.Add(pass);
            while (autoRollLearnPasses.Count > 60)
            {
                autoRollLearnPasses.RemoveAt(0);
            }

            EvaluateAutoRollLearnPass(pass);
        }

        private bool IsAutoRollLearnPassUsable(AutoRollLearnPass pass)
        {
            var settings = Properties.VehicleSettings.Default;
            return pass.Samples >= 8
                && pass.Length >= settings.setIMU_autoRollLearnMinPassLength
                && pass.AverageXteCm <= settings.setIMU_autoRollLearnMaxXteCm;
        }

        private void EvaluateAutoRollLearnPass(AutoRollLearnPass pass)
        {
            TryEvaluateAutoRollLearnPass(pass, false);
        }

        private bool TryEvaluateAutoRollLearnPass(AutoRollLearnPass pass, bool isLive)
        {
            AutoRollLearnPass neighbor = autoRollLearnPasses
                .Where(p => p != pass
                    && p.TrackIndex == pass.TrackIndex
                    && Math.Abs(p.Lane - pass.Lane) == 1
                    && p.Direction != pass.Direction)
                .OrderByDescending(p => GetAutoRollLearnAlongOverlap(pass, p))
                .FirstOrDefault();

            if (neighbor == null)
            {
                if (!isLive)
                {
                    autoRollLearnStatus = "Pass saved. Waiting for adjacent opposite pass.";
                }

                return false;
            }

            double alongOverlap = GetAutoRollLearnAlongOverlap(pass, neighbor);
            double minNeeded = Math.Max(10.0, Properties.VehicleSettings.Default.setIMU_autoRollLearnMinPassLength * 0.5);
            if (alongOverlap < minNeeded)
            {
                if (!isLive)
                {
                    autoRollLearnStatus = "Adjacent pass overlap too short.";
                }

                return false;
            }

            double firstMin = pass.AverageMinEdge;
            double firstMax = pass.AverageMaxEdge;
            double secondMin = neighbor.AverageMinEdge;
            double secondMax = neighbor.AverageMaxEdge;
            if (secondMin < firstMin)
            {
                double tMin = firstMin;
                double tMax = firstMax;
                firstMin = secondMin;
                firstMax = secondMax;
                secondMin = tMin;
                secondMax = tMax;
            }

            double gap = Math.Max(0.0, secondMin - firstMax);
            double observedOverlap = Math.Max(0.0, Math.Min(firstMax, secondMax) - Math.Max(firstMin, secondMin));
            double desiredOverlap = Math.Max(0.0, tool.overlap);
            double measuredError = observedOverlap > 0.0 ? observedOverlap - desiredOverlap : -gap - desiredOverlap;
            double antennaHeight = Math.Max(0.10, Properties.VehicleSettings.Default.setVehicle_antennaHeight);
            double correction = -0.5 * glm.toDegrees(Math.Atan(measuredError / antennaHeight));

            autoRollLearnMeasuredErrorMeters = measuredError;
            autoRollLearnSuggestedCorrectionDeg = correction;
            autoRollLearnConfidence = ClampAutoRollLearn(55.0 + Math.Min(35.0, alongOverlap) + Math.Min(10.0, pass.Samples + neighbor.Samples) * 0.5, 0.0, 95.0);
            autoRollLearnStatus = (isLive ? "Live " : string.Empty)
                + (measuredError >= 0.0 ? "overlap " : "gap ")
                + Math.Abs(measuredError * 100.0).ToString("N1", CultureInfo.CurrentCulture)
                + " cm, roll suggestion "
                + correction.ToString("N3", CultureInfo.CurrentCulture)
                + " deg.";

            TryAutoApplyAutoRollLearn();
            return true;
        }

        private void TryAutoApplyAutoRollLearn()
        {
            var settings = Properties.VehicleSettings.Default;
            if (!settings.setIMU_autoRollLearnAutoApply) return;
            if (autoRollLearnConfidence < settings.setIMU_autoRollLearnMinConfidence) return;
            if ((DateTime.UtcNow - autoRollLearnLastApplyUtc).TotalSeconds < 60.0) return;

            ApplyAutoRollLearnSuggestion(autoApply: true);
        }

        private static double GetAutoRollLearnAlongOverlap(AutoRollLearnPass a, AutoRollLearnPass b)
        {
            return Math.Max(0.0, Math.Min(a.MaxAlong, b.MaxAlong) - Math.Max(a.MinAlong, b.MinAlong));
        }

        private static double ClampAutoRollLearn(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
