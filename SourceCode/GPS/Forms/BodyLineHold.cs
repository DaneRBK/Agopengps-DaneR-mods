using System;
using System.Drawing;
using AgOpenGPS.Core.Models;

namespace AgOpenGPS
{
    public partial class FormGPS
    {
        private double bodyLineHoldFilteredCorrectionDeg;
        private double bodyLineHoldBodyErrorDeg;
        private double bodyLineHoldAbHeadingDeg;
        private double bodyLineHoldDualHeadingDeg;
        private bool bodyLineHoldNotificationVisible;

        public double BodyLineHoldCorrectionDeg => bodyLineHoldFilteredCorrectionDeg;
        public double BodyLineHoldBodyErrorDeg => bodyLineHoldBodyErrorDeg;
        public double BodyLineHoldAbHeadingDeg => bodyLineHoldAbHeadingDeg;
        public double BodyLineHoldDualHeadingDeg => bodyLineHoldDualHeadingDeg;
        public bool IsBodyLineHoldActing => Math.Abs(bodyLineHoldFilteredCorrectionDeg) >= 0.05;

        public short ApplyBodyLineHold(short steerAngle)
        {
            var settings = Properties.VehicleSettings.Default;

            if (!settings.setAS_bodyLineHoldEnabled
                || !isBtnAutoSteerOn
                || isReverse
                || avgSpeed < settings.setAS_bodyLineHoldMinSpeed
                || trk.idx < 0
                || trk.idx >= trk.gArr.Count
                || trk.gArr[trk.idx].mode != TrackMode.AB)
            {
                ResetBodyLineHold();
                return steerAngle;
            }

            bodyLineHoldAbHeadingDeg = glm.toDegrees(trk.gArr[trk.idx].heading);
            bodyLineHoldDualHeadingDeg = pn.headingTrueDual;

            double abHeading = trk.gArr[trk.idx].heading;
            double dualHeading = glm.toRadians(pn.headingTrueDual);
            double bodyErrorRad = NormalizeAxialError(abHeading - dualHeading);
            bodyLineHoldBodyErrorDeg = glm.toDegrees(bodyErrorRad);

            double direction = settings.setAS_bodyLineHoldInvertDirection ? -1.0 : 1.0;
            double rawCorrectionDeg = bodyLineHoldBodyErrorDeg * settings.setAS_bodyLineHoldGain * direction;
            rawCorrectionDeg = Clamp(rawCorrectionDeg,
                -settings.setAS_bodyLineHoldMaxCorrection,
                settings.setAS_bodyLineHoldMaxCorrection);

            double filter = Clamp(settings.setAS_bodyLineHoldFilterPercent * 0.01, 0.01, 1.0);
            bodyLineHoldFilteredCorrectionDeg += (rawCorrectionDeg - bodyLineHoldFilteredCorrectionDeg) * filter;

            double correctedAngle = (steerAngle * 0.01) + bodyLineHoldFilteredCorrectionDeg;
            correctedAngle = Clamp(correctedAngle, -vehicle.maxSteerAngle, vehicle.maxSteerAngle);
            UpdateBodyLineHoldNotification();
            return (short)Math.Round(correctedAngle * 100.0);
        }

        private void ResetBodyLineHold()
        {
            bodyLineHoldFilteredCorrectionDeg = 0;
            bodyLineHoldBodyErrorDeg = 0;
            bodyLineHoldAbHeadingDeg = 0;
            bodyLineHoldDualHeadingDeg = pn?.headingTrueDual ?? 0;
            UpdateBodyLineHoldNotification();
        }

        private void UpdateBodyLineHoldNotification()
        {
            if (lblHardwareMessage == null) return;

            if (IsBodyLineHoldActing && !IsXteGuardActing)
            {
                lblHardwareMessage.Text = "BODY LINE HOLD ACTIVE   "
                    + bodyLineHoldFilteredCorrectionDeg.ToString("N2")
                    + " deg";
                lblHardwareMessage.BackColor = Color.FromArgb(90, 190, 255);
                lblHardwareMessage.ForeColor = Color.Black;
                lblHardwareMessage.Visible = true;
                lblHardwareMessage.BringToFront();
                bodyLineHoldNotificationVisible = true;
                return;
            }

            if (bodyLineHoldNotificationVisible && !IsXteGuardActing)
            {
                lblHardwareMessage.Visible = false;
                bodyLineHoldNotificationVisible = false;
            }
        }

        private static double NormalizeAxialError(double angle)
        {
            angle = NormalizePi(angle);

            if (angle > Math.PI * 0.5)
            {
                angle -= Math.PI;
            }
            else if (angle < -Math.PI * 0.5)
            {
                angle += Math.PI;
            }

            return angle;
        }

        private static double NormalizePi(double angle)
        {
            while (angle > Math.PI) angle -= glm.twoPI;
            while (angle < -Math.PI) angle += glm.twoPI;
            return angle;
        }
    }
}
