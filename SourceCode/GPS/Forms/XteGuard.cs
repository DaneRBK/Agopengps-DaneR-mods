using System;
using System.Drawing;

namespace AgOpenGPS
{
    public partial class FormGPS
    {
        private double xteGuardLastXteMeters = double.NaN;
        private DateTime xteGuardLastTimeUtc = DateTime.MinValue;
        private double xteGuardCorrectionDeg;
        private double xteGuardRateCmSec;
        private bool xteGuardNotificationVisible;

        public double XteGuardCorrectionDeg => xteGuardCorrectionDeg;
        public double XteGuardRateCmSec => xteGuardRateCmSec;
        public bool IsXteGuardActing => Math.Abs(xteGuardCorrectionDeg) >= 0.05;

        public short ApplyXteGuard(short steerAngle)
        {
            var settings = Properties.VehicleSettings.Default;

            if (!settings.setAS_xteGuardEnabled
                || !isBtnAutoSteerOn
                || isReverse
                || avgSpeed < settings.setAS_xteGuardMinSpeed
                || guidanceLineDistanceOff == 32000
                || guidanceLineDistanceOff == 32020
                || Math.Abs(guidanceLineDistanceOff) > 29000)
            {
                ResetXteGuard();
                return steerAngle;
            }

            double xteMeters = guidanceLineDistanceOff * 0.001;
            DateTime now = DateTime.UtcNow;

            if (double.IsNaN(xteGuardLastXteMeters) || xteGuardLastTimeUtc == DateTime.MinValue)
            {
                xteGuardLastXteMeters = xteMeters;
                xteGuardLastTimeUtc = now;
                return steerAngle;
            }

            double dt = Math.Max(0.02, (now - xteGuardLastTimeUtc).TotalSeconds);
            xteGuardRateCmSec = ((xteMeters - xteGuardLastXteMeters) / dt) * 100.0;
            xteGuardLastXteMeters = xteMeters;
            xteGuardLastTimeUtc = now;

            double absRate = Math.Abs(xteGuardRateCmSec);
            bool movingAwayFromLine = Math.Abs(xteMeters) > 0.005 && Math.Sign(xteMeters) == Math.Sign(xteGuardRateCmSec);

            if (movingAwayFromLine && absRate > settings.setAS_xteGuardTriggerCmSec)
            {
                double excessRate = absRate - settings.setAS_xteGuardTriggerCmSec;
                double direction = -Math.Sign(xteGuardRateCmSec);
                if (settings.setAS_xteGuardInvertDirection) direction *= -1.0;

                xteGuardCorrectionDeg = direction * excessRate * settings.setAS_xteGuardGain;
                xteGuardCorrectionDeg = Clamp(xteGuardCorrectionDeg,
                    -settings.setAS_xteGuardMaxCorrection,
                    settings.setAS_xteGuardMaxCorrection);
            }
            else
            {
                double decay = Clamp(settings.setAS_xteGuardDecay, 0.01, 0.95);
                xteGuardCorrectionDeg *= 1.0 - decay;
                if (Math.Abs(xteGuardCorrectionDeg) < 0.02) xteGuardCorrectionDeg = 0;
            }

            double guardedAngle = (steerAngle * 0.01) + xteGuardCorrectionDeg;
            guardedAngle = Clamp(guardedAngle, -vehicle.maxSteerAngle, vehicle.maxSteerAngle);
            UpdateXteGuardNotification();
            return (short)Math.Round(guardedAngle * 100.0);
        }

        private void ResetXteGuard()
        {
            xteGuardLastXteMeters = double.NaN;
            xteGuardLastTimeUtc = DateTime.MinValue;
            xteGuardRateCmSec = 0;
            xteGuardCorrectionDeg = 0;
            UpdateXteGuardNotification();
        }

        private void UpdateXteGuardNotification()
        {
            if (lblHardwareMessage == null) return;

            if (IsXteGuardActing)
            {
                lblHardwareMessage.Text = "XTE GUARD ACTIVE   "
                    + xteGuardCorrectionDeg.ToString("N2")
                    + " deg";
                lblHardwareMessage.BackColor = Color.FromArgb(255, 220, 95);
                lblHardwareMessage.ForeColor = Color.Black;
                lblHardwareMessage.Visible = true;
                lblHardwareMessage.BringToFront();
                xteGuardNotificationVisible = true;
                return;
            }

            if (xteGuardNotificationVisible)
            {
                lblHardwareMessage.Visible = false;
                xteGuardNotificationVisible = false;
            }
        }

        private static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
