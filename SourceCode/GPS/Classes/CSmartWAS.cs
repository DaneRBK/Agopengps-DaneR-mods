//Please, if you use this, share the improvements

using AgLibrary.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AgOpenGPS
{
    /// <summary>
    /// Smart WAS Calibration - Collects and analyzes steering angle data during guidance
    /// to determine the optimal WAS zero point using statistical analysis
    /// </summary>
    public class CSmartWAS
    {
        private readonly FormGPS mf;

        // Data collection settings
        private const int MAX_SAMPLES = 2000;
        private const int MIN_SAMPLES = 200;
        private const double MIN_SPEED_KMH = 2.0;
        private const double MAX_ANGLE_DEG = 15.0;
        private const double MAX_LEARNING_TURN_ANGLE_DEG = 5.0;
        private const double MAX_DIST_OFF_MM = 500.0;
        private const double AUTOSTEER_START_DELAY_SECONDS = 10.0;
        private const double AUTO_APPLY_MIN_CONFIDENCE = 75.0;
        private const int AUTO_APPLY_MIN_SAMPLES = 1000;

        // Collected data
        private readonly List<double> steerAngleHistory = new List<double>();
        private readonly object dataLock = new object();

        // Analysis results
        private double meanAngle;
        private double medianAngle;
        private double stdDeviation;
        private double recommendedOffset;
        private double confidenceLevel;
        private bool hasValidCalibration;
        private bool wasAutoSteerOn;
        private DateTime autoSteerActivatedUtc = DateTime.MinValue;
        private bool hasPendingAutoCorrection;
        private int pendingAutoCorrectionCounts;
        private int lastAutoCorrectionCounts;
        private string autoZeroStatus = "Waiting.";

        #region Properties

        public bool IsCollecting { get; private set; }
        public int SampleCount => steerAngleHistory.Count;
        public double RecommendedOffset => recommendedOffset;
        public double Confidence => confidenceLevel;
        public bool HasValidCalibration => hasValidCalibration;
        public double Mean => meanAngle;
        public double Median => medianAngle;
        public double StdDev => stdDeviation;
        public int LastAutoCorrectionCounts => lastAutoCorrectionCounts;
        public string AutoZeroStatus => autoZeroStatus;

        #endregion

        public CSmartWAS(FormGPS callingForm)
        {
            mf = callingForm;
            Reset();
        }

        /// <summary>
        /// Start collecting steering angle samples during autosteer operation
        /// </summary>
        public void Start()
        {
            lock (dataLock)
            {
                IsCollecting = true;
                Log.EventWriter("Smart WAS: Data collection started");
            }
        }

        /// <summary>
        /// Stop collecting samples and analyze the data
        /// </summary>
        public void Stop()
        {
            lock (dataLock)
            {
                IsCollecting = false;
                Log.EventWriter($"Smart WAS: Collection stopped, {SampleCount} samples collected");
            }
        }

        /// <summary>
        /// Clear all collected data and reset analysis
        /// </summary>
        public void Reset()
        {
            lock (dataLock)
            {
                steerAngleHistory.Clear();
                meanAngle = 0;
                medianAngle = 0;
                stdDeviation = 0;
                recommendedOffset = 0;
                confidenceLevel = 0;
                hasValidCalibration = false;
                wasAutoSteerOn = false;
                autoSteerActivatedUtc = DateTime.MinValue;
                hasPendingAutoCorrection = false;
                pendingAutoCorrectionCounts = 0;
                lastAutoCorrectionCounts = 0;
                autoZeroStatus = "Waiting.";
            }
        }

        /// <summary>
        /// Adjust collected data after applying WAS offset to prevent double-correction
        /// </summary>
        public void ApplyOffsetCorrection(double offsetDegrees)
        {
            lock (dataLock)
            {
                if (steerAngleHistory.Count == 0) return;

                for (int i = 0; i < steerAngleHistory.Count; i++)
                {
                    steerAngleHistory[i] += offsetDegrees;
                }

                AnalyzeData();
                Log.EventWriter($"Smart WAS: Applied {offsetDegrees:F2}° correction to {steerAngleHistory.Count} samples");
            }
        }

        /// <summary>
        /// Add a steering angle sample during autosteer operation
        /// Called from main GPS loop
        /// </summary>
        public void AddSample(double steerAngleDegrees)
        {
            // Check collection conditions
            if (!IsCollecting) return;
            UpdateAutosteerState();
            if (!mf.isBtnAutoSteerOn)
            {
                return;
            }

            DateTime now = DateTime.UtcNow;
            if (autoSteerActivatedUtc == DateTime.MinValue)
            {
                autoSteerActivatedUtc = now;
            }

            double secondsSinceAutoSteerOn = (now - autoSteerActivatedUtc).TotalSeconds;
            if (secondsSinceAutoSteerOn < AUTOSTEER_START_DELAY_SECONDS)
            {
                autoZeroStatus = $"Waiting {AUTOSTEER_START_DELAY_SECONDS - secondsSinceAutoSteerOn:F0}s after autosteer on.";
                return;
            }

            if (IsTractorTurning(steerAngleDegrees))
            {
                autoZeroStatus = "Paused while tractor is turning.";
                return;
            }

            if (mf.avgSpeed < MIN_SPEED_KMH) return;
            if (Math.Abs(mf.guidanceLineDistanceOff) > MAX_DIST_OFF_MM) return;
            if (Math.Abs(steerAngleDegrees) > MAX_ANGLE_DEG) return;

            // Normalize for WAS inversion: inverted WAS flips the required correction direction
            bool wasInverted = (Properties.VehicleSettings.Default.setArdSteer_setting0 & 1) != 0;
            if (wasInverted) steerAngleDegrees = -steerAngleDegrees;

            lock (dataLock)
            {
                steerAngleHistory.Add(steerAngleDegrees);

                // Keep buffer size limited
                if (steerAngleHistory.Count > MAX_SAMPLES)
                {
                    steerAngleHistory.RemoveAt(0);
                }

                // Run analysis periodically
                if (steerAngleHistory.Count >= MIN_SAMPLES)
                {
                    AnalyzeData();
                    PrepareAutomaticWasZero();
                }
            }
        }

        public void UpdateAutosteerState()
        {
            if (!IsCollecting) return;

            if (mf.isBtnAutoSteerOn)
            {
                if (!wasAutoSteerOn)
                {
                    wasAutoSteerOn = true;
                    autoSteerActivatedUtc = DateTime.UtcNow;
                }

                return;
            }

            if (wasAutoSteerOn)
            {
                TryApplyPendingAutomaticWasZero();
            }

            wasAutoSteerOn = false;
            autoSteerActivatedUtc = DateTime.MinValue;
            if (!hasPendingAutoCorrection && lastAutoCorrectionCounts == 0)
            {
                autoZeroStatus = "Waiting for autosteer.";
            }
        }

        private bool IsTractorTurning(double commandSteerAngleDegrees)
        {
            if (mf.yt != null && mf.yt.isYouTurnTriggered) return true;
            if (Math.Abs(commandSteerAngleDegrees) > MAX_LEARNING_TURN_ANGLE_DEG) return true;
            if (Math.Abs(mf.mc.actualSteerAngleDegrees) > MAX_LEARNING_TURN_ANGLE_DEG) return true;

            return false;
        }

        public int GetNextAutomaticStepCounts(int countsPerDegree)
        {
            if (!hasValidCalibration) return 0;
            if (SampleCount < AUTO_APPLY_MIN_SAMPLES) return 0;
            if (confidenceLevel <= GetRequiredAutoConfidence()) return 0;

            int recommendedCounts = GetOffsetCounts(countsPerDegree);
            int absCounts = Math.Abs(recommendedCounts);
            if (absCounts <= 1) return 0;

            int maxStep = GetAdaptiveAutoStepLimit(absCounts);
            return Math.Sign(recommendedCounts) * Math.Min(absCounts, maxStep);
        }

        /// <summary>
        /// Get recommended WAS offset in counts based on current CPD setting
        /// </summary>
        public int GetOffsetCounts(int countsPerDegree)
        {
            if (!hasValidCalibration) return 0;
            return (int)Math.Round(recommendedOffset * countsPerDegree);
        }

        /// <summary>
        /// Perform statistical analysis on collected data
        /// </summary>
        private void AnalyzeData()
        {
            if (steerAngleHistory.Count < MIN_SAMPLES)
            {
                hasValidCalibration = false;
                return;
            }

            try
            {
                // Calculate statistics
                CalculateStatistics();

                // Recommended offset is negative of median
                // (if median is -2.3°, we need +2.3° correction)
                recommendedOffset = -medianAngle;

                // Calculate confidence score
                confidenceLevel = CalculateConfidence();

                // Valid if confidence is reasonable and offset is within safe range
                hasValidCalibration = confidenceLevel > 40 &&
                                    Math.Abs(recommendedOffset) < 10.0;
            }
            catch (Exception ex)
            {
                Log.EventWriter($"Smart WAS Analysis Error: {ex.Message}");
                hasValidCalibration = false;
            }
        }

        private void PrepareAutomaticWasZero()
        {
            var settings = Properties.VehicleSettings.Default;
            if (!settings.setAS_autoWasZeroEnabled)
            {
                autoZeroStatus = "Auto WAS off.";
                hasPendingAutoCorrection = false;
                pendingAutoCorrectionCounts = 0;
                return;
            }

            if (System.Windows.Forms.Application.OpenForms["FormSteer"] != null
                || System.Windows.Forms.Application.OpenForms["FormSteerWiz"] != null)
            {
                autoZeroStatus = "Paused while steer settings are open.";
                return;
            }

            if (!hasValidCalibration || SampleCount < AUTO_APPLY_MIN_SAMPLES)
            {
                autoZeroStatus = $"Collecting stable WAS samples {SampleCount}/{AUTO_APPLY_MIN_SAMPLES}.";
                hasPendingAutoCorrection = false;
                pendingAutoCorrectionCounts = 0;
                return;
            }

            double requiredConfidence = GetRequiredAutoConfidence();
            if (confidenceLevel <= requiredConfidence)
            {
                autoZeroStatus = $"Waiting for confidence {confidenceLevel:F0}% / {requiredConfidence:F0}%.";
                hasPendingAutoCorrection = false;
                pendingAutoCorrectionCounts = 0;
                return;
            }

            int countsPerDegree = Math.Max(1, (int)settings.setAS_countsPerDegree);
            int stepCounts = GetNextAutomaticStepCounts(countsPerDegree);
            if (stepCounts == 0)
            {
                autoZeroStatus = "WAS zero is centered.";
                hasPendingAutoCorrection = false;
                pendingAutoCorrectionCounts = 0;
                return;
            }

            int proposedOffset = settings.setAS_wasOffset + stepCounts;
            if (Math.Abs(proposedOffset) > settings.setAS_autoWasZeroMaxTotalCounts)
            {
                autoZeroStatus = "Auto WAS limit reached.";
                Log.EventWriter($"Smart WAS Auto: proposed offset {proposedOffset} rejected by total limit");
                hasPendingAutoCorrection = false;
                pendingAutoCorrectionCounts = 0;
                return;
            }

            hasPendingAutoCorrection = true;
            pendingAutoCorrectionCounts = stepCounts;
            autoZeroStatus = $"Ready {stepCounts:+0;-0;0} counts. Will apply when autosteer turns off.";
        }

        private void TryApplyPendingAutomaticWasZero()
        {
            var settings = Properties.VehicleSettings.Default;
            if (!hasPendingAutoCorrection)
            {
                PrepareAutomaticWasZero();
            }
            if (!hasPendingAutoCorrection) return;

            int stepCounts = pendingAutoCorrectionCounts;
            int countsPerDegree = Math.Max(1, (int)settings.setAS_countsPerDegree);
            int proposedOffset = settings.setAS_wasOffset + stepCounts;
            double appliedConfidence = confidenceLevel;

            settings.setAS_wasOffset = proposedOffset;
            settings.Save();
            mf.SendSettings();

            lastAutoCorrectionCounts = stepCounts;
            hasPendingAutoCorrection = false;
            pendingAutoCorrectionCounts = 0;
            ClearCollectedSamples();
            autoZeroStatus = $"Auto corrected {stepCounts:+0;-0;0} counts, WAS zero {proposedOffset}.";
            Log.EventWriter($"Smart WAS Auto: corrected {stepCounts} counts, new WAS zero {proposedOffset}, confidence {appliedConfidence:F0}%");
        }

        private double GetRequiredAutoConfidence()
        {
            return Math.Max(AUTO_APPLY_MIN_CONFIDENCE, Properties.VehicleSettings.Default.setAS_autoWasZeroMinConfidence);
        }

        private int GetAdaptiveAutoStepLimit(int absRecommendedCounts)
        {
            int configuredMax = Math.Max(1, Properties.VehicleSettings.Default.setAS_autoWasZeroMaxStepCounts);

            int dynamicMax;
            if (absRecommendedCounts >= 24 || stdDeviation > 4.0)
            {
                dynamicMax = 6;
            }
            else if (absRecommendedCounts >= 12 || stdDeviation > 2.0)
            {
                dynamicMax = 3;
            }
            else
            {
                dynamicMax = 1;
            }

            return Math.Min(configuredMax, dynamicMax);
        }

        private void ClearCollectedSamples()
        {
            steerAngleHistory.Clear();
            meanAngle = 0;
            medianAngle = 0;
            stdDeviation = 0;
            recommendedOffset = 0;
            confidenceLevel = 0;
            hasValidCalibration = false;
        }

        /// <summary>
        /// Calculate mean, median, and standard deviation
        /// </summary>
        private void CalculateStatistics()
        {
            int count = steerAngleHistory.Count;

            // Mean
            meanAngle = steerAngleHistory.Average();

            // Median
            var sorted = steerAngleHistory.OrderBy(x => x).ToList();
            if (count % 2 == 0)
            {
                medianAngle = (sorted[count / 2 - 1] + sorted[count / 2]) * 0.5;
            }
            else
            {
                medianAngle = sorted[count / 2];
            }

            // Standard Deviation (sample)
            if (count > 1)
            {
                double sumSquares = steerAngleHistory.Sum(x => (x - meanAngle) * (x - meanAngle));
                stdDeviation = Math.Sqrt(sumSquares / (count - 1));
            }
            else
            {
                stdDeviation = 0;
            }
        }

        /// <summary>
        /// Calculate confidence level based on data quality
        /// </summary>
        private double CalculateConfidence()
        {
            if (steerAngleHistory.Count < MIN_SAMPLES) return 0;

            // Count samples within standard deviations
            int within1Std = 0;
            int within2Std = 0;

            foreach (double angle in steerAngleHistory)
            {
                double deviation = Math.Abs(angle - medianAngle);
                if (deviation <= stdDeviation) within1Std++;
                if (deviation <= 2 * stdDeviation) within2Std++;
            }

            double pct1Std = (double)within1Std / steerAngleHistory.Count;
            double pct2Std = (double)within2Std / steerAngleHistory.Count;

            // Normal distribution expectations
            double expected1Std = 0.68;
            double expected2Std = 0.95;

            // Score based on normal distribution fit
            double score1 = 1 - Math.Abs(pct1Std - expected1Std) / expected1Std;
            double score2 = 1 - Math.Abs(pct2Std - expected2Std) / expected2Std;

            // Penalize large recommended offsets
            double magnitudeScore = Math.Max(0, 1 - Math.Abs(recommendedOffset) / 10.0);

            // Sample size factor
            double sizeFactor = Math.Min(1.0, (double)steerAngleHistory.Count / (MIN_SAMPLES * 3));

            // Combine scores
            double confidence = ((score1 * 0.3 + score2 * 0.3 + magnitudeScore * 0.2 + sizeFactor * 0.2) * 100);
            return Math.Max(0, Math.Min(100, confidence));
        }

        /// <summary>
        /// Get statistics string for UI display
        /// </summary>
        public string GetStatsString()
        {
            if (SampleCount == 0) return "No data";

            return $"Samples: {SampleCount}\n" +
                   $"Mean: {meanAngle:F2}°\n" +
                   $"Median: {medianAngle:F2}°\n" +
                   $"StdDev: {stdDeviation:F2}°\n" +
                   $"Offset: {recommendedOffset:F2}°\n" +
                   $"Confidence: {confidenceLevel:F0}%";
        }
    }
}
