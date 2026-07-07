using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using AgOpenGPS.Controls;

namespace AgOpenGPS
{
    internal static class ModernUi
    {
        public static readonly Color Background = Color.FromArgb(244, 247, 251);
        public static readonly Color Surface = Color.White;
        public static readonly Color SurfaceAlt = Color.FromArgb(236, 241, 247);
        public static readonly Color Border = Color.FromArgb(206, 216, 228);
        public static readonly Color Text = Color.FromArgb(26, 32, 44);
        public static readonly Color MutedText = Color.FromArgb(94, 108, 126);
        public static readonly Color Accent = Color.FromArgb(22, 119, 255);
        public static readonly Color Success = Color.FromArgb(38, 166, 91);
        public static readonly Color Warning = Color.FromArgb(238, 126, 54);
        public static readonly Color Danger = Color.FromArgb(210, 64, 56);
        public static readonly Color Disabled = Color.FromArgb(221, 228, 236);

        public static readonly Font BaseFont = new Font("Segoe UI", 10.5F, FontStyle.Regular);
        public static readonly Font TitleFont = new Font("Segoe UI Semibold", 14F, FontStyle.Bold);
        public static readonly Font ButtonFont = new Font("Segoe UI Semibold", 11F, FontStyle.Bold);
        public static readonly Font InputFont = new Font("Segoe UI Semibold", 21F, FontStyle.Bold);

        public static void ApplyForm(Form form)
        {
            form.BackColor = Background;
            form.Font = BaseFont;
        }

        public static void StyleTitle(Label label)
        {
            label.ForeColor = Text;
            label.Font = TitleFont;
        }

        public static void StyleLabel(Label label)
        {
            label.ForeColor = Text;
            label.Font = BaseFont;
        }

        public static void StyleGroupBox(GroupBox groupBox)
        {
            groupBox.BackColor = Surface;
            groupBox.ForeColor = MutedText;
            groupBox.FlatStyle = FlatStyle.Flat;
            groupBox.Font = new Font("Segoe UI Semibold", 10.5F, FontStyle.Bold);
        }

        public static void StyleInput(NudlessNumericUpDown nud)
        {
            nud.BackColor = Surface;
            nud.ForeColor = Text;
            nud.Font = InputFont;
            nud.BorderStyle = BorderStyle.FixedSingle;
        }

        public static void StylePanel(Panel panel)
        {
            panel.BackColor = Surface;
            panel.BorderStyle = BorderStyle.FixedSingle;
        }

        public static void StyleButton(Button button, Color backColor)
        {
            button.BackColor = backColor;
            button.FlatStyle = FlatStyle.Flat;
            button.Font = ButtonFont;
            button.ForeColor = GetReadableTextColor(backColor);
            button.UseVisualStyleBackColor = false;
            button.Cursor = Cursors.Hand;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseDownBackColor = Darken(backColor, 0.12);
            button.FlatAppearance.MouseOverBackColor = Lighten(backColor, 0.08);
            button.Resize += (sender, e) => ApplyRoundedRegion((Button)sender, 10);
            button.HandleCreated += (sender, e) => ApplyRoundedRegion((Button)sender, 10);
        }

        public static void UpdateButtonColor(Button button, Color backColor)
        {
            button.BackColor = backColor;
            button.ForeColor = GetReadableTextColor(backColor);
            button.FlatAppearance.MouseDownBackColor = Darken(backColor, 0.12);
            button.FlatAppearance.MouseOverBackColor = Lighten(backColor, 0.08);
        }

        public static Color GetReadableTextColor(Color backColor)
        {
            int brightness = (backColor.R * 299 + backColor.G * 587 + backColor.B * 114) / 1000;
            return brightness < 145 ? Color.White : Text;
        }

        public static Color Lighten(Color color, double amount)
        {
            return Color.FromArgb(
                color.A,
                (int)Math.Min(255, color.R + (255 - color.R) * amount),
                (int)Math.Min(255, color.G + (255 - color.G) * amount),
                (int)Math.Min(255, color.B + (255 - color.B) * amount));
        }

        public static Color Darken(Color color, double amount)
        {
            return Color.FromArgb(
                color.A,
                (int)Math.Max(0, color.R * (1 - amount)),
                (int)Math.Max(0, color.G * (1 - amount)),
                (int)Math.Max(0, color.B * (1 - amount)));
        }

        private static void ApplyRoundedRegion(Button button, int radius)
        {
            if (button.Width <= 0 || button.Height <= 0) return;

            Rectangle rect = new Rectangle(0, 0, button.Width, button.Height);
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddArc(rect.Left, rect.Top, radius, radius, 180, 90);
                path.AddArc(rect.Right - radius, rect.Top, radius, radius, 270, 90);
                path.AddArc(rect.Right - radius, rect.Bottom - radius, radius, radius, 0, 90);
                path.AddArc(rect.Left, rect.Bottom - radius, radius, radius, 90, 90);
                path.CloseFigure();
                button.Region = new Region(path);
            }
        }
    }
}
