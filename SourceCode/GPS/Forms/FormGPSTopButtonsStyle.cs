using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AgOpenGPS
{
    public partial class FormGPS
    {
        private void ApplyTopButtonRestyle()
        {
            StyleTopTextButton(btnWindowsDeviceReset, Properties.Resources.ResetTool);
            StyleTopTextButton(btnFieldsMap, Properties.Resources.FieldStats);
            StyleTopTextButton(btnDxfMap, Properties.Resources.FileOpen);
            StyleTopTextButton(btnObstacleMarker, Properties.Resources.Warning);
            StyleTopTextButton(btnFixRoll, Properties.Resources.ConDa_RollSetZero);
        }

        private static void StyleTopTextButton(Button button, Image icon)
        {
            if (button == null) return;

            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 80);
            button.FlatAppearance.MouseOverBackColor = LightenButton(button.BackColor);
            button.FlatAppearance.MouseDownBackColor = DarkenButton(button.BackColor);
            button.Font = new Font("Segoe UI Semibold", button.Font.Size, FontStyle.Bold, GraphicsUnit.Point, 0);
            button.Image = ScaleIcon(icon, 20);
            button.ImageAlign = ContentAlignment.MiddleLeft;
            button.TextAlign = ContentAlignment.MiddleRight;
            button.TextImageRelation = TextImageRelation.ImageBeforeText;
            button.Padding = new Padding(7, 0, 7, 0);
            button.UseVisualStyleBackColor = false;
            button.Cursor = Cursors.Hand;
            MakeRounded(button, 9);
        }

        private static Image ScaleIcon(Image source, int size)
        {
            if (source == null) return null;
            return new Bitmap(source, new Size(size, size));
        }

        private static void MakeRounded(Button button, int radius)
        {
            ApplyRoundedRegion(button, radius);
            button.Resize += (sender, e) => ApplyRoundedRegion((Button)sender, radius);
            button.HandleCreated += (sender, e) => ApplyRoundedRegion((Button)sender, radius);
        }

        private static void ApplyRoundedRegion(Button button, int radius)
        {
            if (button.Width <= 0 || button.Height <= 0) return;

            Rectangle rect = new Rectangle(0, 0, button.Width, button.Height);
            using (GraphicsPath path = new GraphicsPath())
            {
                int diameter = radius * 2;
                path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
                path.AddArc(rect.Right - diameter - 1, rect.Top, diameter, diameter, 270, 90);
                path.AddArc(rect.Right - diameter - 1, rect.Bottom - diameter - 1, diameter, diameter, 0, 90);
                path.AddArc(rect.Left, rect.Bottom - diameter - 1, diameter, diameter, 90, 90);
                path.CloseFigure();
                button.Region = new Region(path);
            }
        }

        private static Color LightenButton(Color color)
        {
            return Color.FromArgb(
                color.A,
                (int)(color.R + ((255 - color.R) * 0.18)),
                (int)(color.G + ((255 - color.G) * 0.18)),
                (int)(color.B + ((255 - color.B) * 0.18)));
        }

        private static Color DarkenButton(Color color)
        {
            return Color.FromArgb(
                color.A,
                (int)(color.R * 0.86),
                (int)(color.G * 0.86),
                (int)(color.B * 0.86));
        }
    }
}
