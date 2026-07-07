using System;

namespace AgOpenGPS
{
    public partial class FormGPS
    {
        private double adaptiveSteerLastXteMeters = double.NaN;
        private DateTime adaptiveSteerLastTimeUtc = DateTime.MinValue;
        private double adaptiveSteerRateCmSec;
        private double adaptiveSteerScale = 1.0;
        private double adaptiveSteerCommandDeg;
        private DateTime adaptiveSteerAwaySinceUtc = DateTime.MinValue;
        private int adaptiveSteerAwaySampleCount;
        private int adaptiveSteerAwayDirection;
        private bool adaptiveSteerBoostBlocked;

        public double AdaptiveSteerRateCmSec => adaptiveSteerRateCmSec;
        public double AdaptiveSteerScalePercent => adaptiveSteerScale * 100.0;
        public double AdaptiveSteerCommandDeg => adaptiveSteerCommandDeg;
        public int AdaptiveSteerAwaySampleCount => adaptiveSteerAwaySampleCount;
        public bool IsAdaptiveSteerBoostBlocked => adaptiveSteerBoostBlocked;

        public short ApplyAdaptiveSteeringResponse(short steerAngle)
        {
            var settings = Properties.VehicleSettings.Default;

            if (!settings.setAS_adaptiveSteerEnabled
                || !isBtnAutoSteerOn
                || isReverse
                || avgSpeed < settings.setAS_adaptiveSteerMinSpeed
                || guidanceLineDistanceOff == 32000
                || guidanceLineDistanceOff == 32020
                || Math.Abs(guidanceLineDistanceOff) > 29000)
            {
                ResetAdaptiveSteeringResponse();
                return steerAngle;
            }

            double xteMeters = guidanceLineDistanceOff * 0.001;
            DateTime now = DateTime.UtcNow;

            if (double.IsNaN(adaptiveSteerLastXteMeters) || adaptiveSteerLastTimeUtc == DateTime.MinValue)
            {
                adaptiveSteerLastXteMeters = xteMeters;
                adaptiveSteerLastTimeUtc = now;
                adaptiveSteerScale = 1.0;
                adaptiveSteerCommandDeg = steerAngle * 0.01;
                return steerAngle;
            }

            double dt = Math.Max(0.02, (now - adaptiveSteerLastTimeUtc).TotalSeconds);
            adaptiveSteerRateCmSec = ((xteMeters - adaptiveSteerLastXteMeters) / dt) * 100.0;
            adaptiveSteerLastXteMeters = xteMeters;
            adaptiveSteerLastTimeUtc = now;

            double absXteCm = Math.Abs(xteMeters * 100.0);
            double absRateCmSec = Math.Abs(adaptiveSteerRateCmSec);
            bool movingAwayFromLine = absXteCm > 0.5 && Math.Sign(xteMeters) == Math.Sign(adaptiveSteerRateCmSec);
            int awayDirection = movingAwayFromLine ? Math.Sign(xteMeters) : 0;

            if (movingAwayFromLine && awayDirection == adaptiveSteerAwayDirection)
            {
                adaptiveSteerAwaySampleCount++;
            }
            else if (movingAwayFromLine)
            {
                adaptiveSteerAwayDirection = awayDirection;
                adaptiveSteerAwaySampleCount = 1;
                adaptiveSteerAwaySinceUtc = now;
            }
            else
            {
                adaptiveSteerAwayDirection = 0;
                adaptiveSteerAwaySampleCount = 0;
                adaptiveSteerAwaySinceUtc = DateTime.MinValue;
            }

            bool boostConfirmed = movingAwayFromLine
                && adaptiveSteerAwaySampleCount >= Math.Max(1, settings.setAS_adaptiveSteerRequiredSamples)
                && adaptiveSteerAwaySinceUtc != DateTime.MinValue
                && (now - adaptiveSteerAwaySinceUtc).TotalSeconds >= Math.Max(0, settings.setAS_adaptiveSteerBoostDelaySec);

            adaptiveSteerBoostBlocked = IsRollCutActing;

            double desiredScale = 1.0;

            if (absXteCm <= settings.setAS_adaptiveSteerCalmBandCm && !movingAwayFromLine)
            {
                desiredScale = Clamp(settings.setAS_adaptiveSteerCalmResponsePercent * 0.01, 0.30, 1.20);
            }
            else if (!adaptiveSteerBoostBlocked && boostConfirmed && absRateCmSec > settings.setAS_adaptiveSteerTriggerCmSec)
            {
                double excessRate = absRateCmSec - settings.setAS_adaptiveSteerTriggerCmSec;
                double boostPercent = Clamp(
                    excessRate * settings.setAS_adaptiveSteerBoostGain,
                    0,
                    settings.setAS_adaptiveSteerMaxBoostPercent);

                desiredScale = 1.0 + boostPercent * 0.01;
            }

            double smoothing = Clamp(settings.setAS_adaptiveSteerSmoothingPercent * 0.01, 0.01, 1.0);
            adaptiveSteerScale += (desiredScale - adaptiveSteerScale) * smoothing;

            double targetAngle = steerAngle * 0.01;
            double actualAngle = mc.actualSteerAngleDegrees;
            adaptiveSteerCommandDeg = actualAngle + (targetAngle - actualAngle) * adaptiveSteerScale;
            adaptiveSteerCommandDeg = Clamp(adaptiveSteerCommandDeg, -vehicle.maxSteerAngle, vehicle.maxSteerAngle);

            return (short)Math.Round(adaptiveSteerCommandDeg * 100.0);
        }

        private void ResetAdaptiveSteeringResponse()
        {
            adaptiveSteerLastXteMeters = double.NaN;
            adaptiveSteerLastTimeUtc = DateTime.MinValue;
            adaptiveSteerRateCmSec = 0;
            adaptiveSteerScale = 1.0;
            adaptiveSteerCommandDeg = 0;
            adaptiveSteerAwaySinceUtc = DateTime.MinValue;
            adaptiveSteerAwaySampleCount = 0;
            adaptiveSteerAwayDirection = 0;
            adaptiveSteerBoostBlocked = false;
        }
    }
}
