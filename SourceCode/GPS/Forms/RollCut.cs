using System;

namespace AgOpenGPS
{
    public partial class FormGPS
    {
        private double rollCutLastRawRoll = double.NaN;
        private DateTime rollCutLastTimeUtc = DateTime.MinValue;
        private DateTime rollCutHoldUntilUtc = DateTime.MinValue;
        private double rollCutEffectiveRoll = double.NaN;
        private double rollCutRateDegSec;
        private bool rollCutIsHolding;

        public double RollCutEffectiveRoll => rollCutEffectiveRoll;
        public double RollCutRateDegSec => rollCutRateDegSec;
        public bool IsRollCutActing => rollCutIsHolding
            || (!double.IsNaN(rollCutEffectiveRoll)
                && ahrs.imuRoll != 88888
                && Math.Abs(rollCutEffectiveRoll - ahrs.imuRoll) > 0.05);

        public double GetRollForSteering()
        {
            double rawRoll = ahrs.imuRoll;
            if (rawRoll == 88888)
            {
                ResetRollCut();
                return 88888;
            }

            var settings = Properties.VehicleSettings.Default;
            if (!settings.setIMU_rollCutEnabled)
            {
                ResetRollCut();
                return rawRoll;
            }

            DateTime now = DateTime.UtcNow;
            if (double.IsNaN(rollCutLastRawRoll) || rollCutLastTimeUtc == DateTime.MinValue || double.IsNaN(rollCutEffectiveRoll))
            {
                rollCutLastRawRoll = rawRoll;
                rollCutEffectiveRoll = rawRoll;
                rollCutLastTimeUtc = now;
                rollCutRateDegSec = 0;
                rollCutIsHolding = false;
                return rollCutEffectiveRoll;
            }

            double dt = Math.Max(0.02, (now - rollCutLastTimeUtc).TotalSeconds);
            if (dt < 0.03 && Math.Abs(rawRoll - rollCutLastRawRoll) < 0.001)
            {
                return rollCutEffectiveRoll;
            }

            rollCutRateDegSec = (rawRoll - rollCutLastRawRoll) / dt;
            rollCutLastRawRoll = rawRoll;
            rollCutLastTimeUtc = now;

            double angleThreshold = Math.Max(0.1, settings.setIMU_rollCutAngleDeg);
            double windowSec = Math.Max(0.05, settings.setIMU_rollCutWindowSec);
            double rateThreshold = Math.Max(settings.setIMU_rollCutRateDegSec, angleThreshold / windowSec);
            double rawToEffectiveDelta = Math.Abs(rawRoll - rollCutEffectiveRoll);
            bool suddenRollChange = rawToEffectiveDelta >= angleThreshold
                && Math.Abs(rollCutRateDegSec) >= rateThreshold;

            if (suddenRollChange)
            {
                rollCutHoldUntilUtc = now.AddSeconds(Math.Max(0.05, settings.setIMU_rollCutHoldSec));
                rollCutIsHolding = true;
                return rollCutEffectiveRoll;
            }

            if (now < rollCutHoldUntilUtc)
            {
                rollCutIsHolding = true;
                return rollCutEffectiveRoll;
            }

            rollCutIsHolding = false;
            double recovery = Clamp(settings.setIMU_rollCutRecoveryPercent * 0.01, 0.01, 1.0);
            rollCutEffectiveRoll += (rawRoll - rollCutEffectiveRoll) * recovery;

            if (Math.Abs(rollCutEffectiveRoll - rawRoll) < 0.03)
            {
                rollCutEffectiveRoll = rawRoll;
            }

            return rollCutEffectiveRoll;
        }

        private void ResetRollCut()
        {
            rollCutLastRawRoll = double.NaN;
            rollCutLastTimeUtc = DateTime.MinValue;
            rollCutHoldUntilUtc = DateTime.MinValue;
            rollCutEffectiveRoll = double.NaN;
            rollCutRateDegSec = 0;
            rollCutIsHolding = false;
        }

    }
}
