using AgLibrary.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AgOpenGPS
{
    public partial class FormGPS
    {
        public enum AdaptiveAiMode
        {
            Off = 0,
            Suggest = 1,
            Auto = 2
        }

        private DateTime adaptiveAiLastSampleUtc = DateTime.MinValue;
        private DateTime adaptiveAiWindowStartUtc = DateTime.MinValue;
        private DateTime adaptiveAiEligibleSinceUtc = DateTime.MinValue;
        private DateTime adaptiveAiLastChangeUtc = DateTime.MinValue;
        private double adaptiveAiLastXteMeters = double.NaN;
        private double adaptiveAiAbsXteSumCm;
        private double adaptiveAiAbsRateSumCmSec;
        private double adaptiveAiAbsSteerErrorSumDeg;
        private double adaptiveAiSignedXteSumCm;
        private double adaptiveAiSignedRollSumDeg;
        private double adaptiveAiSignedHeadingErrorSumDeg;
        private double adaptiveAiSignedSteerErrorSumDeg;
        private double adaptiveAiNearCenterActualSteerSumDeg;
        private double adaptiveAiNearCenterSampleCount;
        private double adaptiveAiLeftSteerErrorSumDeg;
        private double adaptiveAiRightSteerErrorSumDeg;
        private double adaptiveAiLeftSteerSampleCount;
        private double adaptiveAiRightSteerSampleCount;
        private double adaptiveAiMovingAwayCount;
        private double adaptiveAiOscillationCount;
        private double adaptiveAiSampleCount;
        private int adaptiveAiLastXteSign;
        private string adaptiveAiRecommendation = "Waiting for steady autosteer.";
        private string adaptiveAiLastAction = "None";
        private string adaptiveAiDiagnosticText = "Waiting for steady autosteer.";
        private double adaptiveAiConfidence;
        private int adaptiveAiSuggestedKpDelta;
        private int adaptiveAiSuggestedMinDelta;
        private double adaptiveAiSuggestedResponseDelta;
        private byte adaptiveAiRollbackKp;
        private byte adaptiveAiRollbackMin;
        private double adaptiveAiRollbackCalmResponse;
        private bool adaptiveAiHasRollback;
        private double adaptiveAiCurrentXteCm;
        private double adaptiveAiCurrentRateCmSec;
        private double adaptiveAiCurrentHeadingErrorDeg;
        private double adaptiveAiCurrentTargetSteerDeg;
        private double adaptiveAiCurrentActualSteerDeg;
        private double adaptiveAiCurrentRollDeg;
        private string adaptiveAiBehaviorText = "Waiting.";
        private readonly List<AdaptiveAiLiveSample> adaptiveAiLiveSamples = new List<AdaptiveAiLiveSample>();
        private const double ADAPTIVE_AI_START_DELAY_SECONDS = 10.0;
        private const double ADAPTIVE_AI_MIN_LEARNING_SPEED_KMH = 2.0;

        public string AdaptiveAiRecommendation => adaptiveAiRecommendation;
        public string AdaptiveAiLastAction => adaptiveAiLastAction;
        public double AdaptiveAiConfidence => adaptiveAiConfidence;
        public double AdaptiveAiAverageXteCm => adaptiveAiSampleCount > 0 ? adaptiveAiAbsXteSumCm / adaptiveAiSampleCount : 0;
        public double AdaptiveAiAverageSignedXteCm => adaptiveAiSampleCount > 0 ? adaptiveAiSignedXteSumCm / adaptiveAiSampleCount : 0;
        public double AdaptiveAiAverageRollDeg => adaptiveAiSampleCount > 0 ? adaptiveAiSignedRollSumDeg / adaptiveAiSampleCount : 0;
        public double AdaptiveAiAverageHeadingErrorDeg => adaptiveAiSampleCount > 0 ? adaptiveAiSignedHeadingErrorSumDeg / adaptiveAiSampleCount : 0;
        public double AdaptiveAiAverageSignedSteerErrorDeg => adaptiveAiSampleCount > 0 ? adaptiveAiSignedSteerErrorSumDeg / adaptiveAiSampleCount : 0;
        public double AdaptiveAiAverageRateCmSec => adaptiveAiSampleCount > 0 ? adaptiveAiAbsRateSumCmSec / adaptiveAiSampleCount : 0;
        public double AdaptiveAiAverageSteerErrorDeg => adaptiveAiSampleCount > 0 ? adaptiveAiAbsSteerErrorSumDeg / adaptiveAiSampleCount : 0;
        public double AdaptiveAiCurrentXteCm => adaptiveAiCurrentXteCm;
        public double AdaptiveAiCurrentRateCmSec => adaptiveAiCurrentRateCmSec;
        public double AdaptiveAiCurrentHeadingErrorDeg => adaptiveAiCurrentHeadingErrorDeg;
        public double AdaptiveAiCurrentTargetSteerDeg => adaptiveAiCurrentTargetSteerDeg;
        public double AdaptiveAiCurrentActualSteerDeg => adaptiveAiCurrentActualSteerDeg;
        public double AdaptiveAiCurrentRollDeg => adaptiveAiCurrentRollDeg;
        public string AdaptiveAiBehaviorText => adaptiveAiBehaviorText;
        public string AdaptiveAiDiagnosticText => adaptiveAiDiagnosticText;
        public int AdaptiveAiSuggestedKpDelta => adaptiveAiSuggestedKpDelta;
        public int AdaptiveAiSuggestedMinDelta => adaptiveAiSuggestedMinDelta;
        public double AdaptiveAiSuggestedResponseDelta => adaptiveAiSuggestedResponseDelta;
        public bool AdaptiveAiHasRollback => adaptiveAiHasRollback;
        public sealed class AdaptiveAiLiveSample
        {
            public AdaptiveAiLiveSample(double xteCm, double rateCmSec, double headingErrorDeg, double targetSteerDeg, double actualSteerDeg, double rollDeg, bool rollCut)
            {
                XteCm = xteCm;
                RateCmSec = rateCmSec;
                HeadingErrorDeg = headingErrorDeg;
                TargetSteerDeg = targetSteerDeg;
                ActualSteerDeg = actualSteerDeg;
                RollDeg = rollDeg;
                RollCut = rollCut;
            }

            public double XteCm { get; }
            public double RateCmSec { get; }
            public double HeadingErrorDeg { get; }
            public double TargetSteerDeg { get; }
            public double ActualSteerDeg { get; }
            public double RollDeg { get; }
            public bool RollCut { get; }
        }

        public List<AdaptiveAiLiveSample> GetAdaptiveAiLiveSamples()
        {
            return adaptiveAiLiveSamples.ToList();
        }

        public void UpdateAdaptiveSteeringAi(short steerCommand)
        {
            var settings = Properties.VehicleSettings.Default;
            double targetSteerDeg = steerCommand * 0.01;
            double actualSteerDeg = mc.actualSteerAngleDegrees;

            if ((AdaptiveAiMode)settings.setAS_adaptiveAiMode == AdaptiveAiMode.Off)
            {
                adaptiveAiEligibleSinceUtc = DateTime.MinValue;
                ResetAdaptiveAiWindow("Off.");
                return;
            }

            if (!IsAdaptiveAiAllowed())
            {
                adaptiveAiEligibleSinceUtc = DateTime.MinValue;
                ResetAdaptiveAiWindow("Waiting for steady autosteer.");
                return;
            }

            DateTime now = DateTime.UtcNow;
            if (!IsAdaptiveAiStartDelayComplete(now))
            {
                return;
            }

            double xteMeters = guidanceLineDistanceOff * 0.001;
            double headingErrorDeg = vehicle.modeActualHeadingError;
            double rollDeg = ahrs.imuRoll == 88888 ? 0 : ahrs.imuRoll;
            double steerErrorDeg = targetSteerDeg - actualSteerDeg;

            if (double.IsNaN(adaptiveAiLastXteMeters) || adaptiveAiLastSampleUtc == DateTime.MinValue)
            {
                adaptiveAiLastXteMeters = xteMeters;
                adaptiveAiLastSampleUtc = now;
                adaptiveAiWindowStartUtc = now;
                return;
            }

            double dt = Math.Max(0.02, (now - adaptiveAiLastSampleUtc).TotalSeconds);
            double rateCmSec = ((xteMeters - adaptiveAiLastXteMeters) / dt) * 100.0;
            int xteSign = Math.Sign(xteMeters);
            adaptiveAiCurrentXteCm = xteMeters * 100.0;
            adaptiveAiCurrentRateCmSec = rateCmSec;
            adaptiveAiCurrentHeadingErrorDeg = headingErrorDeg;
            adaptiveAiCurrentTargetSteerDeg = targetSteerDeg;
            adaptiveAiCurrentActualSteerDeg = actualSteerDeg;
            adaptiveAiCurrentRollDeg = rollDeg;

            adaptiveAiAbsXteSumCm += Math.Abs(xteMeters * 100.0);
            adaptiveAiAbsRateSumCmSec += Math.Abs(rateCmSec);
            adaptiveAiAbsSteerErrorSumDeg += Math.Abs(steerErrorDeg);
            adaptiveAiSignedXteSumCm += xteMeters * 100.0;
            adaptiveAiSignedRollSumDeg += rollDeg;
            adaptiveAiSignedHeadingErrorSumDeg += headingErrorDeg;
            adaptiveAiSignedSteerErrorSumDeg += steerErrorDeg;
            if (Math.Abs(targetSteerDeg) < 1.0)
            {
                adaptiveAiNearCenterActualSteerSumDeg += actualSteerDeg;
                adaptiveAiNearCenterSampleCount++;
            }
            else if (targetSteerDeg < 0)
            {
                adaptiveAiLeftSteerErrorSumDeg += Math.Abs(steerErrorDeg);
                adaptiveAiLeftSteerSampleCount++;
            }
            else
            {
                adaptiveAiRightSteerErrorSumDeg += Math.Abs(steerErrorDeg);
                adaptiveAiRightSteerSampleCount++;
            }
            adaptiveAiSampleCount++;
            AddAdaptiveAiLiveSample(adaptiveAiCurrentXteCm, rateCmSec, headingErrorDeg, targetSteerDeg, actualSteerDeg, rollDeg);

            if (Math.Abs(xteMeters * 100.0) > Math.Max(0.5, settings.setAS_adaptiveAiTargetXteCm * 0.5)
                && Math.Sign(xteMeters) == Math.Sign(rateCmSec))
            {
                adaptiveAiMovingAwayCount++;
            }

            if (adaptiveAiLastXteSign != 0 && xteSign != 0 && xteSign != adaptiveAiLastXteSign)
            {
                adaptiveAiOscillationCount++;
            }

            adaptiveAiLastXteMeters = xteMeters;
            adaptiveAiLastSampleUtc = now;
            if (xteSign != 0) adaptiveAiLastXteSign = xteSign;

            if ((now - adaptiveAiWindowStartUtc).TotalSeconds < Math.Max(5.0, settings.setAS_adaptiveAiEvaluateSec))
            {
                UpdateAdaptiveAiBehaviorText();
                UpdateAdaptiveAiDiagnosticText();
                adaptiveAiRecommendation = "Collecting samples "
                    + (now - adaptiveAiWindowStartUtc).TotalSeconds.ToString("N0", CultureInfo.CurrentCulture)
                    + " / " + settings.setAS_adaptiveAiEvaluateSec.ToString("N0", CultureInfo.CurrentCulture) + " s.";
                return;
            }

            EvaluateAdaptiveAiWindow();
            if ((AdaptiveAiMode)settings.setAS_adaptiveAiMode == AdaptiveAiMode.Auto
                && adaptiveAiConfidence >= settings.setAS_adaptiveAiMinConfidence
                && (now - adaptiveAiLastChangeUtc).TotalSeconds >= Math.Max(30.0, settings.setAS_adaptiveAiEvaluateSec))
            {
                ApplyAdaptiveAiSuggestion(autoApply: true);
            }

            ResetAdaptiveAiSamplesOnly();
            adaptiveAiWindowStartUtc = now;
        }

        public bool ApplyAdaptiveAiSuggestion(bool autoApply = false)
        {
            if (adaptiveAiSuggestedKpDelta == 0
                && adaptiveAiSuggestedMinDelta == 0
                && Math.Abs(adaptiveAiSuggestedResponseDelta) < 0.1)
            {
                adaptiveAiLastAction = "No change.";
                return false;
            }

            var settings = Properties.VehicleSettings.Default;
            SaveAdaptiveAiRollback();

            int kp = ClampInt(settings.setAS_Kp + adaptiveAiSuggestedKpDelta, settings.setAS_adaptiveAiMinKp, settings.setAS_adaptiveAiMaxKp);
            int min = ClampInt(settings.setAS_minSteerPWM + adaptiveAiSuggestedMinDelta, settings.setAS_adaptiveAiMinPwm, settings.setAS_adaptiveAiMaxPwm);
            double response = Clamp(settings.setAS_adaptiveSteerCalmResponsePercent + adaptiveAiSuggestedResponseDelta, 40.0, 120.0);

            settings.setAS_Kp = (byte)kp;
            settings.setAS_KpLeft = (byte)kp;
            settings.setAS_KpRight = (byte)kp;
            settings.setAS_minSteerPWM = (byte)min;
            settings.setAS_minSteerPWMLeft = (byte)min;
            settings.setAS_minSteerPWMRight = (byte)min;
            settings.setAS_adaptiveSteerCalmResponsePercent = response;
            settings.Save();
            SendSettings();

            adaptiveAiLastChangeUtc = DateTime.UtcNow;
            adaptiveAiLastAction = (autoApply ? "Auto applied: " : "Applied: ")
                + "Kp " + FormatSigned(adaptiveAiSuggestedKpDelta)
                + ", Min " + FormatSigned(adaptiveAiSuggestedMinDelta)
                + ", Response " + FormatSigned(adaptiveAiSuggestedResponseDelta) + "%.";
            Log.EventWriter("Adaptive steering AI " + adaptiveAiLastAction);
            return true;
        }

        public bool RollbackAdaptiveAi()
        {
            if (!adaptiveAiHasRollback) return false;

            var settings = Properties.VehicleSettings.Default;
            settings.setAS_Kp = adaptiveAiRollbackKp;
            settings.setAS_KpLeft = adaptiveAiRollbackKp;
            settings.setAS_KpRight = adaptiveAiRollbackKp;
            settings.setAS_minSteerPWM = adaptiveAiRollbackMin;
            settings.setAS_minSteerPWMLeft = adaptiveAiRollbackMin;
            settings.setAS_minSteerPWMRight = adaptiveAiRollbackMin;
            settings.setAS_adaptiveSteerCalmResponsePercent = adaptiveAiRollbackCalmResponse;
            settings.Save();
            SendSettings();

            adaptiveAiHasRollback = false;
            adaptiveAiLastAction = "Rollback restored Kp " + adaptiveAiRollbackKp.ToString(CultureInfo.CurrentCulture)
                + ", Min " + adaptiveAiRollbackMin.ToString(CultureInfo.CurrentCulture)
                + ", Response " + adaptiveAiRollbackCalmResponse.ToString("N0", CultureInfo.CurrentCulture) + "%.";
            Log.EventWriter("Adaptive steering AI rollback");
            return true;
        }

        public void ResetAdaptiveAiLearning()
        {
            adaptiveAiHasRollback = false;
            adaptiveAiSuggestedKpDelta = 0;
            adaptiveAiSuggestedMinDelta = 0;
            adaptiveAiSuggestedResponseDelta = 0;
            adaptiveAiConfidence = 0;
            adaptiveAiLastAction = "Learning reset.";
            ResetAdaptiveAiWindow("Waiting for steady autosteer.");
        }

        private bool IsAdaptiveAiAllowed()
        {
            var settings = Properties.VehicleSettings.Default;
            return isBtnAutoSteerOn
                && !isReverse
                && avgSpeed >= Math.Max(ADAPTIVE_AI_MIN_LEARNING_SPEED_KMH, settings.setAS_adaptiveAiMinSpeed)
                && guidanceLineDistanceOff != 32000
                && guidanceLineDistanceOff != 32020
                && Math.Abs(guidanceLineDistanceOff) < 29000
                && !IsRollCutActing;
        }

        private bool IsAdaptiveAiStartDelayComplete(DateTime now)
        {
            if (adaptiveAiEligibleSinceUtc == DateTime.MinValue)
            {
                adaptiveAiEligibleSinceUtc = now;
            }

            double elapsedSeconds = (now - adaptiveAiEligibleSinceUtc).TotalSeconds;
            if (elapsedSeconds >= ADAPTIVE_AI_START_DELAY_SECONDS) return true;

            ResetAdaptiveAiSamplesOnly();
            adaptiveAiLastXteMeters = double.NaN;
            adaptiveAiLastSampleUtc = DateTime.MinValue;
            adaptiveAiWindowStartUtc = DateTime.MinValue;
            adaptiveAiConfidence = 0;
            adaptiveAiRecommendation = "Waiting "
                + (ADAPTIVE_AI_START_DELAY_SECONDS - elapsedSeconds).ToString("N0", CultureInfo.CurrentCulture)
                + " s after autosteer on and moving.";
            adaptiveAiDiagnosticText = "Diagnosis: waiting for 10 s stable movement above 2 kmh.";
            adaptiveAiBehaviorText = "Waiting for stable movement.";
            return false;
        }


        private void EvaluateAdaptiveAiWindow()
        {
            var settings = Properties.VehicleSettings.Default;
            double samples = Math.Max(1.0, adaptiveAiSampleCount);
            double avgXte = adaptiveAiAbsXteSumCm / samples;
            double avgRate = adaptiveAiAbsRateSumCmSec / samples;
            double avgSteerError = adaptiveAiAbsSteerErrorSumDeg / samples;
            double awayRatio = adaptiveAiMovingAwayCount / samples;
            double oscillationRatio = adaptiveAiOscillationCount / samples;
            double target = Math.Max(1.0, settings.setAS_adaptiveAiTargetXteCm);

            adaptiveAiSuggestedKpDelta = 0;
            adaptiveAiSuggestedMinDelta = 0;
            adaptiveAiSuggestedResponseDelta = 0;

            if (avgXte > target * 1.6 && awayRatio > 0.45 && avgSteerError > 1.5)
            {
                adaptiveAiConfidence = Clamp(55.0 + ((avgXte - target) * 4.0) + (awayRatio * 25.0), 0, 95);
                adaptiveAiSuggestedKpDelta = GetAdaptiveAiKpStep(positive: true);
                adaptiveAiSuggestedResponseDelta = Math.Max(1.0, settings.setAS_adaptiveAiMaxStepPercent);
                if (avgSteerError > 4.0) adaptiveAiSuggestedMinDelta = 1;
                adaptiveAiRecommendation = "Too soft / slow to catch line. Increase Kp and response.";
                adaptiveAiBehaviorText = "Slow steering response. Tractor is leaving line before wheel catches up.";
            }
            else if ((oscillationRatio > 0.05 && avgXte > target * 0.8) || (avgRate > 8.0 && avgSteerError < 1.2))
            {
                adaptiveAiConfidence = Clamp(55.0 + (oscillationRatio * 350.0) + Math.Max(0, avgRate - 5.0) * 3.0, 0, 95);
                adaptiveAiSuggestedKpDelta = GetAdaptiveAiKpStep(positive: false);
                adaptiveAiSuggestedResponseDelta = -Math.Max(1.0, settings.setAS_adaptiveAiMaxStepPercent);
                adaptiveAiRecommendation = "Oscillation detected. Reduce Kp and soften response.";
                adaptiveAiBehaviorText = "Oscillation / hunting around line.";
            }
            else if (avgXte <= target && avgRate < 3.0)
            {
                adaptiveAiConfidence = 80;
                adaptiveAiRecommendation = "Steering looks stable. No change.";
                adaptiveAiBehaviorText = "Stable on line.";
            }
            else
            {
                adaptiveAiConfidence = 45;
                adaptiveAiRecommendation = "Not enough clear pattern. Keep current settings.";
                UpdateAdaptiveAiBehaviorText();
            }

            UpdateAdaptiveAiDiagnosticText();
        }

        private void AddAdaptiveAiLiveSample(double xteCm, double rateCmSec, double headingErrorDeg, double targetSteerDeg, double actualSteerDeg, double rollDeg)
        {
            adaptiveAiLiveSamples.Add(new AdaptiveAiLiveSample(xteCm, rateCmSec, headingErrorDeg, targetSteerDeg, actualSteerDeg, rollDeg, IsRollCutActing));
            while (adaptiveAiLiveSamples.Count > 180)
            {
                adaptiveAiLiveSamples.RemoveAt(0);
            }
        }

        private void UpdateAdaptiveAiBehaviorText()
        {
            double avgSignedXte = AdaptiveAiAverageSignedXteCm;
            double avgAbsXte = AdaptiveAiAverageXteCm;
            double avgRate = AdaptiveAiAverageRateCmSec;
            double avgSteerError = AdaptiveAiAverageSteerErrorDeg;
            double avgRoll = AdaptiveAiAverageRollDeg;

            if (IsRollCutActing)
            {
                adaptiveAiBehaviorText = "Roll spike / bump detected. Tuning paused.";
            }
            else if (Math.Abs(avgRoll) > 2.0 && Math.Abs(adaptiveAiCurrentRateCmSec) > 3.0)
            {
                adaptiveAiBehaviorText = "Tractor is rocking with roll change. Watch roll/sidehill correction.";
            }
            else if (Math.Abs(avgSignedXte) > Math.Max(1.5, Properties.VehicleSettings.Default.setAS_adaptiveAiTargetXteCm * 0.7))
            {
                adaptiveAiBehaviorText = avgSignedXte > 0
                    ? "Bias to right side of line."
                    : "Bias to left side of line.";
            }
            else if (avgRate > 8.0 || adaptiveAiOscillationCount > 2)
            {
                adaptiveAiBehaviorText = "Oscillation / hunting around line.";
            }
            else if (avgAbsXte > Properties.VehicleSettings.Default.setAS_adaptiveAiTargetXteCm * 1.5 && avgSteerError > 1.5)
            {
                adaptiveAiBehaviorText = "Slow steering response. Tractor is leaving line before wheel catches up.";
            }
            else
            {
                adaptiveAiBehaviorText = "No clear problem pattern yet.";
            }
        }

        private void UpdateAdaptiveAiDiagnosticText()
        {
            var settings = Properties.VehicleSettings.Default;
            double avgSignedXte = AdaptiveAiAverageSignedXteCm;
            double avgAbsXte = AdaptiveAiAverageXteCm;
            double avgRate = AdaptiveAiAverageRateCmSec;
            double avgSteerError = AdaptiveAiAverageSteerErrorDeg;
            double avgHeadingError = AdaptiveAiAverageHeadingErrorDeg;
            double avgRoll = AdaptiveAiAverageRollDeg;
            double target = Math.Max(1.0, settings.setAS_adaptiveAiTargetXteCm);
            double centerActualSteer = adaptiveAiNearCenterSampleCount > 5
                ? adaptiveAiNearCenterActualSteerSumDeg / adaptiveAiNearCenterSampleCount
                : 0.0;
            double leftError = adaptiveAiLeftSteerSampleCount > 5
                ? adaptiveAiLeftSteerErrorSumDeg / adaptiveAiLeftSteerSampleCount
                : 0.0;
            double rightError = adaptiveAiRightSteerSampleCount > 5
                ? adaptiveAiRightSteerErrorSumDeg / adaptiveAiRightSteerSampleCount
                : 0.0;

            if (adaptiveAiSampleCount < 10)
            {
                adaptiveAiDiagnosticText = "Diagnosis: collecting more samples.";
            }
            else if (Math.Abs(centerActualSteer) > 0.7 && avgAbsXte > target * 0.8)
            {
                adaptiveAiDiagnosticText = "Diagnosis: check WAS zero first. Wheel angle is not centered when command is near zero.";
            }
            else if (Math.Abs(avgHeadingError) > 0.6 && Math.Abs(avgSignedXte) > target * 0.7 && avgSteerError < 2.0)
            {
                adaptiveAiDiagnosticText = "Diagnosis: likely heading offset / dual heading offset, not Kp. Tractor holds angle but line is biased.";
            }
            else if (Math.Abs(avgRoll) > 1.5 && Math.Abs(avgSignedXte) > target * 0.7)
            {
                adaptiveAiDiagnosticText = "Diagnosis: likely roll offset or sidehill compensation. Bias follows roll/sidehill.";
            }
            else if (leftError > 0.1 && rightError > 0.1 && Math.Abs(leftError - rightError) > Math.Max(1.0, Math.Min(leftError, rightError) * 0.5))
            {
                adaptiveAiDiagnosticText = leftError > rightError
                    ? "Diagnosis: left steering direction is lagging more. Check left Min/Kp/counts or hydraulic balance."
                    : "Diagnosis: right steering direction is lagging more. Check right Min/Kp/counts or hydraulic balance.";
            }
            else if (avgRate > 8.0 || adaptiveAiOscillationCount > 2)
            {
                adaptiveAiDiagnosticText = "Diagnosis: gain/response too aggressive or lookahead too short. Reduce Kp/response before changing calibration.";
            }
            else if (avgAbsXte > target * 1.5 && avgSteerError > 1.5)
            {
                adaptiveAiDiagnosticText = "Diagnosis: steering is too slow. Kp, Min to move or response can be increased safely in small steps.";
            }
            else if (Math.Abs(avgSignedXte) > target * 0.7)
            {
                adaptiveAiDiagnosticText = avgSignedXte > 0
                    ? "Diagnosis: constant right bias. Check heading/roll/WAS zero before increasing gain."
                    : "Diagnosis: constant left bias. Check heading/roll/WAS zero before increasing gain.";
            }
            else
            {
                adaptiveAiDiagnosticText = "Diagnosis: no strong calibration fault detected.";
            }
        }

        private int GetAdaptiveAiKpStep(bool positive)
        {
            var settings = Properties.VehicleSettings.Default;
            int step = Math.Max(1, (int)Math.Round(settings.setAS_Kp * (settings.setAS_adaptiveAiMaxStepPercent * 0.01)));
            return positive ? step : -step;
        }

        private void SaveAdaptiveAiRollback()
        {
            var settings = Properties.VehicleSettings.Default;
            adaptiveAiRollbackKp = settings.setAS_Kp;
            adaptiveAiRollbackMin = settings.setAS_minSteerPWM;
            adaptiveAiRollbackCalmResponse = settings.setAS_adaptiveSteerCalmResponsePercent;
            adaptiveAiHasRollback = true;
        }

        private void ResetAdaptiveAiWindow(string recommendation)
        {
            ResetAdaptiveAiSamplesOnly();
            adaptiveAiLastXteMeters = double.NaN;
            adaptiveAiLastSampleUtc = DateTime.MinValue;
            adaptiveAiWindowStartUtc = DateTime.MinValue;
            adaptiveAiRecommendation = recommendation;
            adaptiveAiDiagnosticText = "Diagnosis: " + recommendation;
            adaptiveAiConfidence = 0;
        }

        private void ResetAdaptiveAiSamplesOnly()
        {
            adaptiveAiAbsXteSumCm = 0;
            adaptiveAiAbsRateSumCmSec = 0;
            adaptiveAiAbsSteerErrorSumDeg = 0;
            adaptiveAiSignedXteSumCm = 0;
            adaptiveAiSignedRollSumDeg = 0;
            adaptiveAiSignedHeadingErrorSumDeg = 0;
            adaptiveAiSignedSteerErrorSumDeg = 0;
            adaptiveAiNearCenterActualSteerSumDeg = 0;
            adaptiveAiNearCenterSampleCount = 0;
            adaptiveAiLeftSteerErrorSumDeg = 0;
            adaptiveAiRightSteerErrorSumDeg = 0;
            adaptiveAiLeftSteerSampleCount = 0;
            adaptiveAiRightSteerSampleCount = 0;
            adaptiveAiMovingAwayCount = 0;
            adaptiveAiOscillationCount = 0;
            adaptiveAiSampleCount = 0;
            adaptiveAiLastXteSign = 0;
        }

        private static int ClampInt(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static string FormatSigned(double value)
        {
            return value >= 0
                ? "+" + value.ToString("N0", CultureInfo.CurrentCulture)
                : value.ToString("N0", CultureInfo.CurrentCulture);
        }
    }
}
