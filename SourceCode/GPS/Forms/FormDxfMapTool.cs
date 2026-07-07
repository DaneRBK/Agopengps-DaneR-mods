using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace AgOpenGPS
{
    public sealed class FormDxfMapTool : Form
    {
        private readonly FormGPS mf;
        private readonly TextBox tboxFile;
        private readonly NumericUpDown nudZone;
        private readonly Label lblStatus;

        public FormDxfMapTool(FormGPS callingForm)
        {
            mf = callingForm;

            Name = "FormDxfMapTool";
            Text = "DXF Map";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(620, 300);
            MinimumSize = new Size(620, 300);
            WindowState = FormWindowState.Maximized;
            Font = new Font("Tahoma", 11F, FontStyle.Regular);
            BackColor = Color.FromArgb(245, 247, 250);

            Label lblFile = new Label
            {
                Text = "DXF file",
                Left = 16,
                Top = 18,
                Width = 120,
                Height = 26
            };

            tboxFile = new TextBox
            {
                Left = 16,
                Top = 48,
                Width = 460,
                Height = 30,
                ReadOnly = true,
                Text = mf.GetLatestDownloadsDxfFile()
            };

            Button btnBrowse = MakeButton("...", 486, 46, 44, 34);
            btnBrowse.Click += BtnBrowse_Click;

            Label lblZone = new Label
            {
                Text = "UTM zone",
                Left = 16,
                Top = 98,
                Width = 90,
                Height = 26
            };

            nudZone = new NumericUpDown
            {
                Left = 106,
                Top = 94,
                Width = 70,
                Height = 32,
                Minimum = 1,
                Maximum = 60,
                Value = 34,
                TextAlign = HorizontalAlignment.Center
            };

            Button btnLoad = MakeButton("LOAD MAP", 354, 92, 122, 38);
            btnLoad.BackColor = Color.FromArgb(116, 190, 92);
            btnLoad.Click += BtnLoad_Click;

            Button btnClear = MakeButton("CLEAR", 486, 92, 86, 38);
            btnClear.Click += (_, __) =>
            {
                mf.ClearDxfMap();
                lblStatus.Text = mf.GetDxfMapStatusText();
            };

            Button btnManual = MakeButton("4 POINTS", 16, 144, 112, 38);
            btnManual.BackColor = Color.FromArgb(250, 214, 92);
            btnManual.Click += (_, __) => mf.ShowDxfMapPreviewWindow(startFourPointMode: true);

            Button btnClose = MakeButton("CLOSE", 484, 144, 88, 38);
            btnClose.Click += (_, __) => Close();

            lblStatus = new Label
            {
                Left = 16,
                Top = 198,
                Width = 556,
                Height = 58,
                Text = mf.GetDxfMapStatusText(),
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };

            Controls.Add(lblFile);
            Controls.Add(tboxFile);
            Controls.Add(btnBrowse);
            Controls.Add(lblZone);
            Controls.Add(nudZone);
            Controls.Add(btnLoad);
            Controls.Add(btnClear);
            Controls.Add(btnManual);
            Controls.Add(btnClose);
            Controls.Add(lblStatus);
        }

        private Button MakeButton(string text, int left, int top, int width, int height)
        {
            Button button = new Button
            {
                Text = text,
                Left = left,
                Top = top,
                Width = width,
                Height = height,
                BackColor = Color.FromArgb(185, 185, 185),
                ForeColor = Color.Black,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Tahoma", 10F, FontStyle.Bold),
                TabStop = false
            };

            button.FlatAppearance.BorderColor = Color.Black;
            button.FlatAppearance.BorderSize = 1;
            return button;
        }

        private void BtnBrowse_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.InitialDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Downloads");
                dialog.Filter = "DXF files (*.dxf)|*.dxf|All files (*.*)|*.*";
                dialog.RestoreDirectory = true;

                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    tboxFile.Text = dialog.FileName;
                }
            }
        }

        private void BtnLoad_Click(object sender, EventArgs e)
        {
            FormGPS.DxfMapLoadResult result = mf.LoadDxfMap(tboxFile.Text, (int)nudZone.Value);
            lblStatus.Text = result.Success ? mf.GetDxfMapStatusText() : result.Message;
            if (result.Success)
            {
                mf.ShowDxfMapPreviewWindow();
                Close();
            }
        }

    }
}
