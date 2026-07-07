using System;
using System.Drawing;
using System.Windows.Forms;
using AgOpenGPS.Controls;

namespace AgOpenGPS
{
    public sealed class FormDxfMapPreview : Form
    {
        private readonly FormGPS mf;
        private readonly Panel drawingPanel;
        private readonly Label lblStatus;
        private readonly Label lblZoom;
        private readonly Button btnPickMode;
        private readonly Button btnZoomIn;
        private readonly Button btnZoomOut;
        private const double FourPointInitialZoom = 25.0;
        private const double FourPointZoomStep = 2.0;
        private const int PanDragThreshold = 5;
        private double pickModeZoomMultiplier = 1.0;
        private double previewPanX;
        private double previewPanY;
        private bool isPickMode;
        private bool isMouseDown;
        private bool isDraggingPreview;
        private bool suppressNextClick;
        private Point panStartPoint;
        private Point lastPanPoint;

        public FormDxfMapPreview(FormGPS callingForm)
        {
            mf = callingForm;

            Name = "FormDxfMapPreview";
            Text = "DXF Preview";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(1100, 800);
            MinimumSize = new Size(700, 500);
            WindowState = FormWindowState.Maximized;
            BackColor = Color.White;

            drawingPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White
            };
            drawingPanel.Paint += DrawingPanel_Paint;
            drawingPanel.Resize += (_, __) => drawingPanel.Invalidate();
            drawingPanel.MouseDown += DrawingPanel_MouseDown;
            drawingPanel.MouseMove += DrawingPanel_MouseMove;
            drawingPanel.MouseUp += DrawingPanel_MouseUp;
            drawingPanel.MouseLeave += DrawingPanel_MouseLeave;
            drawingPanel.MouseClick += DrawingPanel_MouseClick;

            Panel zoomPanel = new Panel
            {
                Dock = DockStyle.Right,
                Width = 70,
                Padding = new Padding(8),
                BackColor = Color.FromArgb(235, 238, 242)
            };

            btnZoomIn = MakeZoomButton("+", 10);
            btnZoomIn.Click += (_, __) => ChangePickZoom(FourPointZoomStep);

            lblZoom = new Label
            {
                Left = 8,
                Top = 66,
                Width = 52,
                Height = 28,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Tahoma", 10F, FontStyle.Bold)
            };

            btnZoomOut = MakeZoomButton("-", 102);
            btnZoomOut.Click += (_, __) => ChangePickZoom(1.0 / FourPointZoomStep);

            zoomPanel.Controls.Add(btnZoomIn);
            zoomPanel.Controls.Add(lblZoom);
            zoomPanel.Controls.Add(btnZoomOut);

            FlowLayoutPanel toolbar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 50,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(6),
                BackColor = Color.FromArgb(235, 238, 242)
            };

            Button btnRefresh = MakeToolbarButton("REFRESH", 104);
            btnRefresh.Click += (_, __) => drawingPanel.Invalidate();

            btnPickMode = MakeToolbarButton("4 POINTS", 116);
            btnPickMode.Click += (_, __) =>
            {
                isPickMode = !isPickMode;
                if (isPickMode && pickModeZoomMultiplier < FourPointInitialZoom)
                {
                    pickModeZoomMultiplier = FourPointInitialZoom;
                }
                UpdatePickModeButton();
            };

            Button btnUndo = MakeToolbarButton("UNDO", 86);
            btnUndo.Click += (_, __) =>
            {
                mf.UndoDxfManualFieldPoint();
                UpdateStatus("Selected " + mf.DxfManualPointCount + "/4 points.");
                drawingPanel.Invalidate();
            };

            Button btnClear = MakeToolbarButton("CLEAR POINTS", 138);
            btnClear.Click += (_, __) =>
            {
                mf.ClearDxfManualFieldPoints();
                UpdateStatus("Manual points cleared.");
                drawingPanel.Invalidate();
            };

            Button btnSave = MakeToolbarButton("SAVE FIELD", 120);
            btnSave.BackColor = Color.FromArgb(116, 190, 92);
            btnSave.Click += BtnSave_Click;

            lblStatus = new Label
            {
                AutoSize = false,
                Width = 430,
                Height = 34,
                Margin = new Padding(10, 4, 0, 0),
                TextAlign = ContentAlignment.MiddleLeft,
                Text = "Turn on 4 POINTS, click near four field corners, then SAVE FIELD."
            };

            toolbar.Controls.Add(btnRefresh);
            toolbar.Controls.Add(btnPickMode);
            toolbar.Controls.Add(btnUndo);
            toolbar.Controls.Add(btnClear);
            toolbar.Controls.Add(btnSave);
            toolbar.Controls.Add(lblStatus);

            Controls.Add(drawingPanel);
            Controls.Add(zoomPanel);
            Controls.Add(toolbar);
            UpdatePickModeButton();
            UpdateZoomControls();
        }

        private static Button MakeToolbarButton(string text, int width)
        {
            Button button = new Button
            {
                Text = text,
                Width = width,
                Height = 34,
                BackColor = Color.FromArgb(185, 185, 185),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Tahoma", 10F, FontStyle.Bold),
                Margin = new Padding(3)
            };

            button.FlatAppearance.BorderColor = Color.Black;
            button.FlatAppearance.BorderSize = 1;
            return button;
        }

        private static Button MakeZoomButton(string text, int top)
        {
            Button button = new Button
            {
                Text = text,
                Left = 8,
                Top = top,
                Width = 52,
                Height = 48,
                BackColor = Color.FromArgb(185, 185, 185),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Tahoma", 20F, FontStyle.Bold),
                TabStop = false
            };

            button.FlatAppearance.BorderColor = Color.Black;
            button.FlatAppearance.BorderSize = 1;
            return button;
        }

        private void DrawingPanel_Paint(object sender, PaintEventArgs e)
        {
            mf.DrawDxfMapPreview(e.Graphics, drawingPanel.ClientRectangle, GetPreviewZoomMultiplier(), previewPanX, previewPanY);
        }

        private void DrawingPanel_MouseDown(object sender, MouseEventArgs e)
        {
            if (!isPickMode || e.Button != MouseButtons.Left) return;

            isMouseDown = true;
            isDraggingPreview = false;
            panStartPoint = e.Location;
            lastPanPoint = e.Location;
        }

        private void DrawingPanel_MouseMove(object sender, MouseEventArgs e)
        {
            if (!isMouseDown || e.Button != MouseButtons.Left) return;

            int dragX = e.X - panStartPoint.X;
            int dragY = e.Y - panStartPoint.Y;
            if (!isDraggingPreview && ((dragX * dragX) + (dragY * dragY)) < (PanDragThreshold * PanDragThreshold))
            {
                return;
            }

            isDraggingPreview = true;
            previewPanX += e.X - lastPanPoint.X;
            previewPanY += e.Y - lastPanPoint.Y;
            lastPanPoint = e.Location;
            drawingPanel.Cursor = Cursors.SizeAll;
            drawingPanel.Invalidate();
        }

        private void DrawingPanel_MouseUp(object sender, MouseEventArgs e)
        {
            if (!isMouseDown) return;

            suppressNextClick = isDraggingPreview;
            isMouseDown = false;
            isDraggingPreview = false;
            drawingPanel.Cursor = isPickMode ? Cursors.Cross : Cursors.Default;
        }

        private void DrawingPanel_MouseLeave(object sender, EventArgs e)
        {
            if (!isMouseDown) return;

            isMouseDown = false;
            isDraggingPreview = false;
            drawingPanel.Cursor = isPickMode ? Cursors.Cross : Cursors.Default;
        }

        private void DrawingPanel_MouseClick(object sender, MouseEventArgs e)
        {
            if (suppressNextClick)
            {
                suppressNextClick = false;
                return;
            }

            if (!isPickMode || e.Button != MouseButtons.Left) return;

            if (mf.TryAddDxfManualPointFromPreview(e.Location, drawingPanel.ClientRectangle, GetPreviewZoomMultiplier(), previewPanX, previewPanY, out string message))
            {
                UpdateStatus(message + " Click next corner or save when 4/4.");
            }
            else
            {
                UpdateStatus(message);
            }

            drawingPanel.Invalidate();
        }

        private double GetPreviewZoomMultiplier()
        {
            return pickModeZoomMultiplier;
        }

        public void StartFourPointMode()
        {
            isPickMode = true;
            pickModeZoomMultiplier = FourPointInitialZoom;
            previewPanX = 0.0;
            previewPanY = 0.0;
            UpdatePickModeButton();
            UpdateStatus("4 POINTS mode. Drag map to pan, tap to select point. Zoom x" + pickModeZoomMultiplier.ToString("0.0") + ".");
        }

        private void ChangePickZoom(double factor)
        {
            pickModeZoomMultiplier *= factor;
            if (pickModeZoomMultiplier < 1.0) pickModeZoomMultiplier = 1.0;
            if (pickModeZoomMultiplier > 80.0) pickModeZoomMultiplier = 80.0;

            UpdateZoomControls();
            UpdateStatus("DXF preview zoom x" + pickModeZoomMultiplier.ToString("0.0") + ".");
            drawingPanel.Invalidate();
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            if (mf.DxfManualPointCount != 4)
            {
                UpdateStatus("Select 4 points before saving.");
                return;
            }

            string fieldName = PromptFieldName();
            if (string.IsNullOrWhiteSpace(fieldName)) return;

            FormGPS.DxfMapCreateFieldResult result = mf.CreateFieldFromDxfManualPoints(fieldName);
            UpdateStatus(result.Message);
            drawingPanel.Invalidate();
            if (result.Success)
            {
                Application.OpenForms["FormDxfMapTool"]?.Close();
                Close();
            }
        }

        private string PromptFieldName()
        {
            using (Form form = new Form())
            using (TextBox textBox = new TextBox())
            using (Button btnSave = new Button())
            using (Button btnCancel = new Button())
            using (Label label = new Label())
            {
                form.Text = "Field name";
                form.StartPosition = FormStartPosition.CenterParent;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.ClientSize = new Size(420, 142);
                form.MinimizeBox = false;
                form.MaximizeBox = false;
                form.Font = new Font("Tahoma", 11F, FontStyle.Regular);

                label.Text = "Enter field name";
                label.Left = 16;
                label.Top = 14;
                label.Width = 220;
                label.Height = 24;

                textBox.Left = 16;
                textBox.Top = 44;
                textBox.Width = 388;
                textBox.Height = 30;
                textBox.Text = "DXF Manual " + DateTime.Now.ToString("yyyy-MM-dd HH-mm");
                textBox.Click += (_, __) =>
                {
                    if (mf.isKeyboardOn)
                    {
                        textBox.ShowKeyboard(form);
                    }
                };

                btnSave.Text = "SAVE";
                btnSave.Left = 190;
                btnSave.Top = 92;
                btnSave.Width = 100;
                btnSave.Height = 34;
                btnSave.DialogResult = DialogResult.OK;

                btnCancel.Text = "CANCEL";
                btnCancel.Left = 304;
                btnCancel.Top = 92;
                btnCancel.Width = 100;
                btnCancel.Height = 34;
                btnCancel.DialogResult = DialogResult.Cancel;

                form.Controls.Add(label);
                form.Controls.Add(textBox);
                form.Controls.Add(btnSave);
                form.Controls.Add(btnCancel);
                form.AcceptButton = btnSave;
                form.CancelButton = btnCancel;

                return form.ShowDialog(this) == DialogResult.OK ? textBox.Text.Trim() : string.Empty;
            }
        }

        private void UpdatePickModeButton()
        {
            btnPickMode.BackColor = isPickMode
                ? Color.FromArgb(250, 214, 92)
                : Color.FromArgb(185, 185, 185);
            drawingPanel.Cursor = isPickMode ? Cursors.Cross : Cursors.Default;
            UpdateZoomControls();
            drawingPanel.Invalidate();
        }

        private void UpdateStatus(string text)
        {
            lblStatus.Text = text;
        }

        private void UpdateZoomControls()
        {
            btnZoomIn.Enabled = true;
            btnZoomOut.Enabled = pickModeZoomMultiplier > 1.01;
            btnZoomIn.BackColor = Color.FromArgb(250, 214, 92);
            btnZoomOut.BackColor = btnZoomOut.Enabled
                ? Color.FromArgb(250, 214, 92)
                : Color.FromArgb(185, 185, 185);
            lblZoom.Text = "x" + pickModeZoomMultiplier.ToString("0.0");
        }
    }
}
