using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace AgOpenGPS
{
    public sealed class FormAdaptiveSteeringAiView : Form
    {
        private readonly FormGPS mf;
        private readonly Panel drawingPanel;
        private readonly Timer refreshTimer = new Timer();

        public FormAdaptiveSteeringAiView(Form callingForm)
        {
            mf = callingForm as FormGPS;

            Name = "FormAdaptiveSteeringAiView";
            Text = "AI live view";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(980, 680);
            MinimumSize = new Size(760, 520);
            BackColor = Color.FromArgb(242, 243, 245);

            drawingPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White
            };
            drawingPanel.Paint += DrawingPanel_Paint;
            drawingPanel.Resize += (_, __) => drawingPanel.Invalidate();

            Controls.Add(drawingPanel);

            refreshTimer.Interval = 150;
            refreshTimer.Tick += (_, __) => drawingPanel.Invalidate();
            refreshTimer.Start();

            FormClosing += (_, __) => refreshTimer.Stop();
        }

        private void DrawingPanel_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.FromArgb(248, 249, 250));

            if (mf == null)
            {
                DrawText(g, "No data.", new Rectangle(20, 20, 300, 30), 14, FontStyle.Bold, Brushes.Black);
                return;
            }

            Rectangle bounds = drawingPanel.ClientRectangle;
            Rectangle road = new Rectangle(bounds.Left + 30, bounds.Top + 70, bounds.Width - 60, bounds.Height - 180);
            if (road.Width < 100 || road.Height < 100) return;

            List<FormGPS.AdaptiveAiLiveSample> samples = mf.GetAdaptiveAiLiveSamples();

            DrawHeader(g, bounds);
            DrawRoad(g, road);
            DrawXteTrail(g, road, samples);
            DrawTractor(g, road);
            DrawRollIndicator(g, new Rectangle(bounds.Left + 30, bounds.Bottom - 92, 270, 70));
            DrawSteerIndicator(g, new Rectangle(bounds.Left + 330, bounds.Bottom - 92, 290, 70));
            DrawDiagnosis(g, new Rectangle(bounds.Left + 650, bounds.Bottom - 98, bounds.Width - 680, 78));
        }

        private void DrawHeader(Graphics g, Rectangle bounds)
        {
            string title = "Adaptive Steering AI live view";
            DrawText(g, title, new Rectangle(bounds.Left + 30, bounds.Top + 18, 380, 28), 14, FontStyle.Bold, Brushes.Black);

            string metrics = "XTE " + mf.AdaptiveAiCurrentXteCm.ToString("N1", CultureInfo.CurrentCulture)
                + " cm | Rate " + mf.AdaptiveAiCurrentRateCmSec.ToString("N1", CultureInfo.CurrentCulture)
                + " cm/s | Heading " + mf.AdaptiveAiCurrentHeadingErrorDeg.ToString("N1", CultureInfo.CurrentCulture)
                + " deg | Speed " + mf.avgSpeed.ToString("N1", CultureInfo.CurrentCulture) + " km/h";
            DrawText(g, metrics, new Rectangle(bounds.Left + 30, bounds.Top + 43, bounds.Width - 60, 24), 10, FontStyle.Regular, Brushes.DimGray);
        }

        private static void DrawRoad(Graphics g, Rectangle road)
        {
            using (Brush fill = new SolidBrush(Color.FromArgb(235, 238, 242)))
            using (Pen border = new Pen(Color.FromArgb(190, 195, 204), 1.5f))
            using (Pen center = new Pen(Color.FromArgb(190, 0, 210), 2.2f))
            using (Pen grid = new Pen(Color.FromArgb(220, 224, 230), 1f))
            {
                g.FillRectangle(fill, road);
                g.DrawRectangle(border, road);

                int centerX = road.Left + road.Width / 2;
                for (int offset = -120; offset <= 120; offset += 40)
                {
                    int x = centerX + offset;
                    if (x <= road.Left || x >= road.Right) continue;
                    g.DrawLine(grid, x, road.Top, x, road.Bottom);
                }

                g.DrawLine(center, centerX, road.Top, centerX, road.Bottom);
            }
        }

        private static void DrawXteTrail(Graphics g, Rectangle road, List<FormGPS.AdaptiveAiLiveSample> samples)
        {
            if (samples == null || samples.Count < 2) return;

            int centerX = road.Left + road.Width / 2;
            double scalePxPerCm = Math.Min(8.0, road.Width / 90.0);
            int usableCount = Math.Min(samples.Count, road.Height - 10);
            List<FormGPS.AdaptiveAiLiveSample> viewSamples = samples.Skip(Math.Max(0, samples.Count - usableCount)).ToList();

            PointF[] points = new PointF[viewSamples.Count];
            for (int i = 0; i < viewSamples.Count; i++)
            {
                double xte = Math.Max(-45.0, Math.Min(45.0, viewSamples[i].XteCm));
                float x = (float)(centerX + (xte * scalePxPerCm));
                float y = road.Bottom - 8 - ((viewSamples.Count - 1 - i) * (road.Height - 16f) / Math.Max(1, viewSamples.Count - 1));
                points[i] = new PointF(x, y);
            }

            using (Pen trail = new Pen(Color.FromArgb(0, 122, 255), 3.0f))
            using (Pen warningTrail = new Pen(Color.FromArgb(255, 59, 48), 3.0f))
            {
                bool warning = viewSamples.Any(s => Math.Abs(s.XteCm) > 8.0 || s.RollCut);
                g.DrawLines(warning ? warningTrail : trail, points);
            }
        }

        private void DrawTractor(Graphics g, Rectangle road)
        {
            int centerX = road.Left + road.Width / 2;
            double scalePxPerCm = Math.Min(8.0, road.Width / 90.0);
            float tractorX = (float)(centerX + Math.Max(-45.0, Math.Min(45.0, mf.AdaptiveAiCurrentXteCm)) * scalePxPerCm);
            float tractorY = road.Top + road.Height * 0.70f;
            float heading = (float)Math.Max(-18.0, Math.Min(18.0, mf.AdaptiveAiCurrentHeadingErrorDeg));

            g.TranslateTransform(tractorX, tractorY);
            g.RotateTransform(heading);

            using (Brush body = new SolidBrush(Color.FromArgb(255, 204, 0)))
            using (Brush cab = new SolidBrush(Color.FromArgb(72, 72, 74)))
            using (Pen outline = new Pen(Color.Black, 2f))
            {
                RectangleF rect = new RectangleF(-28, -46, 56, 92);
                g.FillRectangle(body, rect);
                g.DrawRectangle(outline, rect.X, rect.Y, rect.Width, rect.Height);
                g.FillRectangle(cab, -18, -20, 36, 32);
                g.FillRectangle(Brushes.Black, -36, -36, 10, 24);
                g.FillRectangle(Brushes.Black, 26, -36, 10, 24);
                g.FillRectangle(Brushes.Black, -36, 14, 10, 26);
                g.FillRectangle(Brushes.Black, 26, 14, 10, 26);
            }

            g.ResetTransform();
        }

        private void DrawRollIndicator(Graphics g, Rectangle rect)
        {
            DrawPanel(g, rect, "Roll / rocking");
            double roll = Math.Max(-10.0, Math.Min(10.0, mf.AdaptiveAiCurrentRollDeg));
            int cx = rect.Left + rect.Width / 2;
            int cy = rect.Top + 45;
            float angle = (float)(roll * 4.0);

            g.TranslateTransform(cx, cy);
            g.RotateTransform(angle);
            using (Pen p = new Pen(mf.IsRollCutActing ? Color.Red : Color.FromArgb(52, 199, 89), 5f))
            {
                g.DrawLine(p, -80, 0, 80, 0);
            }
            g.ResetTransform();

            DrawText(g, roll.ToString("N1", CultureInfo.CurrentCulture) + " deg", new Rectangle(rect.Left + 12, rect.Top + 38, 90, 22), 10, FontStyle.Bold, Brushes.Black);
        }

        private void DrawSteerIndicator(Graphics g, Rectangle rect)
        {
            DrawPanel(g, rect, "Steer command vs actual");
            double target = mf.AdaptiveAiCurrentTargetSteerDeg;
            double actual = mf.AdaptiveAiCurrentActualSteerDeg;
            int mid = rect.Left + rect.Width / 2;
            int y1 = rect.Top + 42;
            int y2 = rect.Top + 58;
            double scale = rect.Width / 70.0;

            using (Pen targetPen = new Pen(Color.FromArgb(0, 122, 255), 5f))
            using (Pen actualPen = new Pen(Color.FromArgb(255, 149, 0), 5f))
            {
                g.DrawLine(targetPen, mid, y1, (float)(mid + Math.Max(-35, Math.Min(35, target)) * scale), y1);
                g.DrawLine(actualPen, mid, y2, (float)(mid + Math.Max(-35, Math.Min(35, actual)) * scale), y2);
            }
        }

        private void DrawDiagnosis(Graphics g, Rectangle rect)
        {
            DrawPanel(g, rect, "Diagnosis");
            DrawText(g, mf.AdaptiveAiBehaviorText, new Rectangle(rect.Left + 12, rect.Top + 26, rect.Width - 24, 20), 9, FontStyle.Bold, Brushes.Black);
            DrawText(g, mf.AdaptiveAiDiagnosticText, new Rectangle(rect.Left + 12, rect.Top + 48, rect.Width - 24, 28), 8, FontStyle.Regular, Brushes.DimGray);
        }

        private static void DrawPanel(Graphics g, Rectangle rect, string title)
        {
            using (Brush fill = new SolidBrush(Color.White))
            using (Pen border = new Pen(Color.FromArgb(200, 204, 210), 1f))
            {
                g.FillRectangle(fill, rect);
                g.DrawRectangle(border, rect);
            }

            DrawText(g, title, new Rectangle(rect.Left + 10, rect.Top + 5, rect.Width - 20, 22), 9, FontStyle.Bold, Brushes.DimGray);
        }

        private static void DrawText(Graphics g, string text, Rectangle rect, float size, FontStyle style, Brush brush)
        {
            using (Font font = new Font("Tahoma", size, style))
            {
                g.DrawString(text, font, brush, rect);
            }
        }
    }
}
