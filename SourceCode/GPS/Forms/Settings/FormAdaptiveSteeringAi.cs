using AgLibrary.Logging;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace AgOpenGPS
{
    public sealed class FormAdaptiveSteeringAi : Form
    {
        private static readonly Color AppleBlue = Color.FromArgb(0, 122, 255);
        private static readonly Color AppleGreen = Color.FromArgb(52, 199, 89);
        private static readonly Color AppleGray = Color.FromArgb(229, 229, 234);
        private static readonly Color AppleOrange = Color.FromArgb(255, 149, 0);
        private const string ParameterKp = "kp";
        private const string ParameterMinPwm = "min_pwm";
        private const string ParameterCalmResponse = "calm_response";
        private const string ParameterWasZero = "was_zero";

        private readonly FormGPS mf;
        private readonly Timer liveTimer = new Timer();
        private readonly Dictionary<string, object> parameterRollbackValues = new Dictionary<string, object>();

        private Button btnOff;
        private Button btnSuggest;
        private Button btnAuto;
        private Button btnApplySuggestion;
        private Button btnRollback;
        private Button btnAutoWas;
        private NumericUpDown nudMinSpeed;
        private NumericUpDown nudTargetXte;
        private NumericUpDown nudEvaluateSec;
        private NumericUpDown nudConfidence;
        private NumericUpDown nudStepPercent;
        private NumericUpDown nudMinKp;
        private NumericUpDown nudMaxKp;
        private Label lblLive;
        private Label lblSuggestion;
        private Label lblDiagnostics;
        private Label lblLastAction;
        private Label lblCurrent;
        private DataGridView dgvSteeringParameterTable;
        private readonly List<SteeringParameterRow> steeringParameterRows = new List<SteeringParameterRow>();

        public FormAdaptiveSteeringAi(Form callingForm)
        {
            mf = callingForm as FormGPS;

            InitializeComponent();
            LoadSettingsToControls();
            UpdateModeButtons();
            UpdateLiveLabels();

            liveTimer.Interval = 300;
            liveTimer.Tick += (sender, e) => UpdateLiveLabels();
            liveTimer.Start();
        }

        private void InitializeComponent()
        {
            Name = "FormAdaptiveSteeringAi";
            Text = "Adaptive Steering AI";
            ClientSize = new Size(980, 710);
            MinimumSize = new Size(980, 710);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.Gainsboro;
            Font = new Font("Tahoma", 11F, FontStyle.Regular);

            Label title = new Label
            {
                Location = new Point(22, 14),
                Size = new Size(610, 58),
                Font = new Font("Tahoma", 13F, FontStyle.Bold),
                Text = "Adaptive Steering AI\r\nLearns from XTE, XTE rate and steering error. Suggest mode is safest for testing."
            };

            btnOff = MakeButton("Off", new Point(25, 88), new Size(120, 58), AppleGray);
            btnSuggest = MakeButton("Suggest", new Point(155, 88), new Size(145, 58), AppleBlue);
            btnAuto = MakeButton("Auto", new Point(310, 88), new Size(120, 58), AppleOrange);
            btnOff.Click += (sender, e) => SetMode(FormGPS.AdaptiveAiMode.Off);
            btnSuggest.Click += (sender, e) => SetMode(FormGPS.AdaptiveAiMode.Suggest);
            btnAuto.Click += (sender, e) => SetMode(FormGPS.AdaptiveAiMode.Auto);

            btnApplySuggestion = MakeButton("Apply suggestion", new Point(470, 88), new Size(175, 58), AppleGreen);
            btnApplySuggestion.Click += (sender, e) =>
            {
                if (mf != null && mf.ApplyAdaptiveAiSuggestion())
                {
                    UpdateLiveLabels();
                }
            };

            btnRollback = MakeButton("Rollback", new Point(655, 88), new Size(150, 58), AppleGray);
            btnRollback.Click += (sender, e) =>
            {
                if (mf != null && mf.RollbackAdaptiveAi())
                {
                    UpdateLiveLabels();
                }
            };

            btnAutoWas = MakeButton("Auto WAS", new Point(815, 88), new Size(120, 58), AppleGray);
            btnAutoWas.Click += (sender, e) => ToggleAutoWasZero();

            GroupBox gbParameterTable = new GroupBox
            {
                Location = new Point(25, 155),
                Size = new Size(910, 250),
                Text = "AI settable steering parameters"
            };

            dgvSteeringParameterTable = MakeParameterGrid(new Point(12, 26), new Size(886, 210));
            dgvSteeringParameterTable.CellClick += DgvSteeringParameterTable_CellClick;
            dgvSteeringParameterTable.CellContentClick += DgvSteeringParameterTable_CellContentClick;
            gbParameterTable.Controls.Add(dgvSteeringParameterTable);

            GroupBox gbSettings = new GroupBox
            {
                Location = new Point(25, 415),
                Size = new Size(910, 158),
                Text = "Limits"
            };

            AddSetting(gbSettings, "Min speed kmh", nudMinSpeed = MakeNud(0M, 15M, 1), 22, 28);
            AddSetting(gbSettings, "Target XTE cm", nudTargetXte = MakeNud(1M, 20M, 1), 205, 28);
            AddSetting(gbSettings, "Evaluate sec", nudEvaluateSec = MakeNud(5M, 120M, 0), 388, 28);
            AddSetting(gbSettings, "Confidence %", nudConfidence = MakeNud(50M, 95M, 0), 571, 28);
            AddSetting(gbSettings, "Max step %", nudStepPercent = MakeNud(1M, 10M, 1), 22, 92);
            AddSetting(gbSettings, "Min Kp", nudMinKp = MakeNud(1M, 250M, 0), 205, 92);
            AddSetting(gbSettings, "Max Kp", nudMaxKp = MakeNud(1M, 250M, 0), 388, 92);

            Button btnSave = MakeButton("Save", new Point(735, 94), new Size(130, 48), AppleGreen);
            btnSave.Click += BtnSave_Click;
            gbSettings.Controls.Add(btnSave);

            GroupBox gbLive = new GroupBox
            {
                Location = new Point(25, 585),
                Size = new Size(910, 72),
                Text = "Live"
            };

            lblCurrent = MakeLabel(new Point(20, 22), new Size(430, 22), FontStyle.Bold);
            lblLive = MakeLabel(new Point(455, 22), new Size(430, 22), FontStyle.Regular);
            lblSuggestion = MakeLabel(new Point(20, 46), new Size(520, 22), FontStyle.Bold);
            lblDiagnostics = MakeLabel(new Point(550, 46), new Size(335, 22), FontStyle.Bold);
            lblLastAction = MakeLabel(new Point(20, 170), new Size(850, 24), FontStyle.Regular);
            lblLastAction.Visible = false;

            gbLive.Controls.Add(lblCurrent);
            gbLive.Controls.Add(lblLive);
            gbLive.Controls.Add(lblSuggestion);
            gbLive.Controls.Add(lblDiagnostics);
            gbLive.Controls.Add(lblLastAction);

            Button btnReset = MakeButton("Reset learned", new Point(25, 665), new Size(160, 34), AppleGray);
            btnReset.Click += (sender, e) =>
            {
                mf?.ResetAdaptiveAiLearning();
                UpdateLiveLabels();
            };

            Controls.Add(title);
            Controls.Add(btnOff);
            Controls.Add(btnSuggest);
            Controls.Add(btnAuto);
            Controls.Add(btnApplySuggestion);
            Controls.Add(btnRollback);
            Controls.Add(btnAutoWas);
            Controls.Add(gbParameterTable);
            Controls.Add(gbSettings);
            Controls.Add(gbLive);
            Controls.Add(btnReset);

            FormClosing += (sender, e) => liveTimer.Stop();
        }

        private void LoadSettingsToControls()
        {
            var settings = Properties.VehicleSettings.Default;
            nudMinSpeed.Value = ClampDecimal((decimal)settings.setAS_adaptiveAiMinSpeed, nudMinSpeed.Minimum, nudMinSpeed.Maximum);
            nudTargetXte.Value = ClampDecimal((decimal)settings.setAS_adaptiveAiTargetXteCm, nudTargetXte.Minimum, nudTargetXte.Maximum);
            nudEvaluateSec.Value = ClampDecimal((decimal)settings.setAS_adaptiveAiEvaluateSec, nudEvaluateSec.Minimum, nudEvaluateSec.Maximum);
            nudConfidence.Value = ClampDecimal((decimal)settings.setAS_adaptiveAiMinConfidence, nudConfidence.Minimum, nudConfidence.Maximum);
            nudStepPercent.Value = ClampDecimal((decimal)settings.setAS_adaptiveAiMaxStepPercent, nudStepPercent.Minimum, nudStepPercent.Maximum);
            nudMinKp.Value = ClampDecimal(settings.setAS_adaptiveAiMinKp, nudMinKp.Minimum, nudMinKp.Maximum);
            nudMaxKp.Value = ClampDecimal(settings.setAS_adaptiveAiMaxKp, nudMaxKp.Minimum, nudMaxKp.Maximum);
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            SaveSettings();
            Log.EventWriter("Adaptive Steering AI settings saved");
            UpdateLiveLabels();
        }

        private void SetMode(FormGPS.AdaptiveAiMode mode)
        {
            Properties.VehicleSettings.Default.setAS_adaptiveAiMode = (int)mode;
            Properties.VehicleSettings.Default.Save();
            if (mode == FormGPS.AdaptiveAiMode.Off) mf?.ResetAdaptiveAiLearning();
            UpdateModeButtons();
        }

        private void SaveSettings()
        {
            var settings = Properties.VehicleSettings.Default;
            settings.setAS_adaptiveAiMinSpeed = (double)nudMinSpeed.Value;
            settings.setAS_adaptiveAiTargetXteCm = (double)nudTargetXte.Value;
            settings.setAS_adaptiveAiEvaluateSec = (double)nudEvaluateSec.Value;
            settings.setAS_adaptiveAiMinConfidence = (double)nudConfidence.Value;
            settings.setAS_adaptiveAiMaxStepPercent = (double)nudStepPercent.Value;
            settings.setAS_adaptiveAiMinKp = (byte)nudMinKp.Value;
            settings.setAS_adaptiveAiMaxKp = (byte)nudMaxKp.Value;
            if (settings.setAS_adaptiveAiMaxKp < settings.setAS_adaptiveAiMinKp)
            {
                settings.setAS_adaptiveAiMaxKp = settings.setAS_adaptiveAiMinKp;
                nudMaxKp.Value = settings.setAS_adaptiveAiMaxKp;
            }

            settings.Save();
        }

        private void UpdateModeButtons()
        {
            var mode = (FormGPS.AdaptiveAiMode)Properties.VehicleSettings.Default.setAS_adaptiveAiMode;
            ApplyButtonColor(btnOff, mode == FormGPS.AdaptiveAiMode.Off ? AppleGreen : AppleGray);
            ApplyButtonColor(btnSuggest, mode == FormGPS.AdaptiveAiMode.Suggest ? AppleGreen : AppleBlue);
            ApplyButtonColor(btnAuto, mode == FormGPS.AdaptiveAiMode.Auto ? AppleGreen : AppleOrange);
            UpdateAutoWasButton();
            UpdateLiveLabels();
        }

        private void ToggleAutoWasZero()
        {
            var settings = Properties.VehicleSettings.Default;
            settings.setAS_autoWasZeroEnabled = !settings.setAS_autoWasZeroEnabled;
            settings.Save();

            if (settings.setAS_autoWasZeroEnabled)
            {
                mf?.smartWAS?.Reset();
                mf?.smartWAS?.Start();
            }
            else
            {
                mf?.smartWAS?.Stop();
                mf?.smartWAS?.Reset();
            }

            UpdateAutoWasButton();
            UpdateLiveLabels();
        }

        private void UpdateAutoWasButton()
        {
            if (btnAutoWas == null) return;

            bool enabled = Properties.VehicleSettings.Default.setAS_autoWasZeroEnabled;
            btnAutoWas.Text = enabled ? "Auto WAS On" : "Auto WAS Off";
            ApplyButtonColor(btnAutoWas, enabled ? AppleGreen : AppleGray);
        }

        private void UpdateLiveLabels()
        {
            var settings = Properties.VehicleSettings.Default;
            string mode = ((FormGPS.AdaptiveAiMode)settings.setAS_adaptiveAiMode).ToString();
            lblCurrent.Text = "Mode: " + mode
                + " | Kp " + settings.setAS_Kp.ToString(CultureInfo.CurrentCulture)
                + " | Min " + settings.setAS_minSteerPWM.ToString(CultureInfo.CurrentCulture)
                + " | Calm response " + settings.setAS_adaptiveSteerCalmResponsePercent.ToString("N0", CultureInfo.CurrentCulture) + "%";

            lblLive.Text = "XTE avg " + (mf?.AdaptiveAiAverageXteCm ?? 0).ToString("N1", CultureInfo.CurrentCulture)
                + " cm | Rate " + (mf?.AdaptiveAiAverageRateCmSec ?? 0).ToString("N1", CultureInfo.CurrentCulture)
                + " cm/s | Steer error " + (mf?.AdaptiveAiAverageSteerErrorDeg ?? 0).ToString("N1", CultureInfo.CurrentCulture)
                + " deg | Confidence " + (mf?.AdaptiveAiConfidence ?? 0).ToString("N0", CultureInfo.CurrentCulture) + "%";

            lblSuggestion.Text = (mf?.AdaptiveAiRecommendation ?? "Waiting.")
                + "  Kp " + FormatSigned(mf?.AdaptiveAiSuggestedKpDelta ?? 0)
                + ", Min " + FormatSigned(mf?.AdaptiveAiSuggestedMinDelta ?? 0)
                + ", Response " + FormatSigned(mf?.AdaptiveAiSuggestedResponseDelta ?? 0) + "%";

            lblDiagnostics.Text = mf?.AdaptiveAiDiagnosticText ?? "Diagnosis: waiting.";
            lblLastAction.Text = "Last action: " + (mf?.AdaptiveAiLastAction ?? "None");
            UpdateParameterTable(settings);
            btnRollback.Enabled = mf?.AdaptiveAiHasRollback == true;
        }

        private void UpdateParameterTable(Properties.VehicleSettings settings)
        {
            if (dgvSteeringParameterTable == null) return;

            int kpDelta = mf?.AdaptiveAiSuggestedKpDelta ?? 0;
            int minDelta = mf?.AdaptiveAiSuggestedMinDelta ?? 0;
            double responseDelta = mf?.AdaptiveAiSuggestedResponseDelta ?? 0;
            double aiConfidence = mf?.AdaptiveAiConfidence ?? 0;
            int recommendedKp = ClampInt(settings.setAS_Kp + kpDelta, settings.setAS_adaptiveAiMinKp, settings.setAS_adaptiveAiMaxKp);
            int recommendedMin = ClampInt(settings.setAS_minSteerPWM + minDelta, settings.setAS_adaptiveAiMinPwm, settings.setAS_adaptiveAiMaxPwm);
            double recommendedCalmResponse = ClampDouble(settings.setAS_adaptiveSteerCalmResponsePercent + responseDelta, 40.0, 120.0);
            string currentKp = FormatSplit(settings.setAS_isKpSplit, settings.setAS_Kp, settings.setAS_KpLeft, settings.setAS_KpRight);
            string currentMin = FormatSplit(settings.setAS_isMinSteerPWMSplit, settings.setAS_minSteerPWM, settings.setAS_minSteerPWMLeft, settings.setAS_minSteerPWMRight);
            string currentCalmResponse = FormatPercent(settings.setAS_adaptiveSteerCalmResponsePercent, 0);
            int autoWasStep = mf?.smartWAS?.GetNextAutomaticStepCounts(Math.Max(1, (int)settings.setAS_countsPerDegree)) ?? 0;
            double wasConfidence = mf?.smartWAS?.Confidence ?? 0;
            bool aiConfidenceReady = aiConfidence >= settings.setAS_adaptiveAiMinConfidence;
            bool wasConfidenceReady = wasConfidence > settings.setAS_autoWasZeroMinConfidence;
            string wasZeroCurrent = settings.setAS_wasOffset.ToString(CultureInfo.CurrentCulture);
            string wasZeroRecommended = autoWasStep == 0
                ? wasZeroCurrent
                : (settings.setAS_wasOffset + autoWasStep).ToString(CultureInfo.CurrentCulture)
                    + " (" + FormatSigned(autoWasStep) + ")";

            steeringParameterRows.Clear();
            dgvSteeringParameterTable.SuspendLayout();
            dgvSteeringParameterTable.Rows.Clear();

            AddParameterRow(ParameterKp, "Proportional gain", currentKp,
                kpDelta == 0 ? currentKp : FormatSteerRecommendation(recommendedKp, kpDelta),
                FormatPercent(aiConfidence, 0), kpDelta != 0, aiConfidenceReady);
            AddParameterRow(ParameterMinPwm, "Min steer PWM", currentMin,
                minDelta == 0 ? currentMin : FormatSteerRecommendation(recommendedMin, minDelta),
                FormatPercent(aiConfidence, 0), minDelta != 0, aiConfidenceReady);
            AddParameterRow(ParameterCalmResponse, "Calm response", currentCalmResponse,
                Math.Abs(responseDelta) < 0.1 ? currentCalmResponse : FormatPercentRecommendation(recommendedCalmResponse, responseDelta, 0),
                FormatPercent(aiConfidence, 0), Math.Abs(responseDelta) >= 0.1, aiConfidenceReady);
            AddParameterRow(ParameterWasZero, "WAS zero", wasZeroCurrent, wasZeroRecommended,
                FormatPercent(wasConfidence, 0), autoWasStep != 0, wasConfidenceReady);

            RenderParameterRows();
            dgvSteeringParameterTable.ResumeLayout();
        }

        private void AddParameterRow(string key, string parameter, string current, string aiValue, string confidence, bool canSet, bool confidenceReady)
        {
            steeringParameterRows.Add(new SteeringParameterRow(key, parameter, current, aiValue, confidence, canSet, confidenceReady, parameterRollbackValues.ContainsKey(key)));
        }

        private void RenderParameterRows()
        {
            for (int i = 0; i < steeringParameterRows.Count; i++)
            {
                SteeringParameterRow item = steeringParameterRows[i];
                int rowIndex = dgvSteeringParameterTable.Rows.Add(item.Parameter, item.Current, item.AiValue, item.Confidence, item.CanSet ? "Set" : string.Empty, item.CanReverse ? "Reverse" : string.Empty);
                DataGridViewRow row = dgvSteeringParameterTable.Rows[rowIndex];
                row.Tag = item.Key;
                if (item.CanSet)
                {
                    row.DefaultCellStyle.BackColor = Color.FromArgb(232, 246, 236);
                    row.Cells[2].Style.ForeColor = Color.FromArgb(18, 115, 42);
                }
                StyleSetCell(row.Cells[4], item.CanSet, item.ConfidenceReady);
                if (!item.CanReverse) row.Cells[5].Style.ForeColor = Color.Gray;
            }
        }

        private static void StyleSetCell(DataGridViewCell cell, bool canSet, bool confidenceReady)
        {
            if (canSet && confidenceReady)
            {
                cell.Style.BackColor = AppleGreen;
                cell.Style.SelectionBackColor = AppleGreen;
                cell.Style.ForeColor = Color.White;
                cell.Style.SelectionForeColor = Color.White;
                return;
            }

            cell.Style.BackColor = AppleGray;
            cell.Style.SelectionBackColor = AppleGray;
            cell.Style.ForeColor = Color.Gray;
            cell.Style.SelectionForeColor = Color.Gray;
        }

        private void DgvSteeringParameterTable_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            string key = dgvSteeringParameterTable.Rows[e.RowIndex].Tag as string;
            if (string.IsNullOrEmpty(key)) return;

            string columnName = dgvSteeringParameterTable.Columns[e.ColumnIndex].Name;
            if (columnName == "colSet")
            {
                ApplyAiParameter(key);
            }
            else if (columnName == "colReverse")
            {
                ReverseAiParameter(key);
            }
        }

        private void DgvSteeringParameterTable_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;

            string columnName = dgvSteeringParameterTable.Columns[e.ColumnIndex].Name;
            if (columnName != "colParameter" && columnName != "colCurrent") return;

            string key = dgvSteeringParameterTable.Rows[e.RowIndex].Tag as string;
            if (string.IsNullOrEmpty(key)) return;

            ShowManualParameterEdit(key);
        }

        private void ShowManualParameterEdit(string key)
        {
            var settings = Properties.VehicleSettings.Default;
            double min;
            double max;
            double current;

            switch (key)
            {
                case ParameterKp:
                    min = 1;
                    max = 250;
                    current = settings.setAS_Kp;
                    break;

                case ParameterMinPwm:
                    min = 0;
                    max = 200;
                    current = settings.setAS_minSteerPWM;
                    break;

                case ParameterCalmResponse:
                    min = 40;
                    max = 120;
                    current = settings.setAS_adaptiveSteerCalmResponsePercent;
                    break;

                case ParameterWasZero:
                    min = -4000;
                    max = 4000;
                    current = settings.setAS_wasOffset;
                    break;

                default:
                    return;
            }

            using (FormNumeric form = new FormNumeric(min, max, current))
            {
                if (form.ShowDialog(this) != DialogResult.OK) return;
                ApplyManualParameterValue(key, form.ReturnValue);
            }
        }

        private void ApplyManualParameterValue(string key, double value)
        {
            var settings = Properties.VehicleSettings.Default;
            switch (key)
            {
                case ParameterKp:
                    parameterRollbackValues[key] = settings.setAS_Kp;
                    byte kp = (byte)ClampInt((int)Math.Round(value), 1, 250);
                    settings.setAS_Kp = kp;
                    settings.setAS_KpLeft = kp;
                    settings.setAS_KpRight = kp;
                    settings.setAS_isKpSplit = false;
                    break;

                case ParameterMinPwm:
                    parameterRollbackValues[key] = settings.setAS_minSteerPWM;
                    byte minPwm = (byte)ClampInt((int)Math.Round(value), 0, 200);
                    settings.setAS_minSteerPWM = minPwm;
                    settings.setAS_minSteerPWMLeft = minPwm;
                    settings.setAS_minSteerPWMRight = minPwm;
                    settings.setAS_isMinSteerPWMSplit = false;
                    break;

                case ParameterCalmResponse:
                    parameterRollbackValues[key] = settings.setAS_adaptiveSteerCalmResponsePercent;
                    settings.setAS_adaptiveSteerCalmResponsePercent = ClampDouble(value, 40.0, 120.0);
                    break;

                case ParameterWasZero:
                    parameterRollbackValues[key] = settings.setAS_wasOffset;
                    settings.setAS_wasOffset = ClampInt((int)Math.Round(value), -4000, 4000);
                    break;

                default:
                    return;
            }

            settings.Save();
            mf?.SendSettings();
            Log.EventWriter("Adaptive Steering AI manual set " + key);
            UpdateLiveLabels();
        }

        private void ApplyAiParameter(string key)
        {
            var settings = Properties.VehicleSettings.Default;
            int kpDelta = mf?.AdaptiveAiSuggestedKpDelta ?? 0;
            int minDelta = mf?.AdaptiveAiSuggestedMinDelta ?? 0;
            double responseDelta = mf?.AdaptiveAiSuggestedResponseDelta ?? 0;
            int autoWasStep = mf?.smartWAS?.GetNextAutomaticStepCounts(Math.Max(1, (int)settings.setAS_countsPerDegree)) ?? 0;
            bool aiConfidenceReady = (mf?.AdaptiveAiConfidence ?? 0) >= settings.setAS_adaptiveAiMinConfidence;
            bool wasConfidenceReady = (mf?.smartWAS?.Confidence ?? 0) > settings.setAS_autoWasZeroMinConfidence;

            switch (key)
            {
                case ParameterKp:
                    if (kpDelta == 0 || !aiConfidenceReady) return;
                    parameterRollbackValues[key] = settings.setAS_Kp;
                    byte kp = (byte)ClampInt(settings.setAS_Kp + kpDelta, settings.setAS_adaptiveAiMinKp, settings.setAS_adaptiveAiMaxKp);
                    settings.setAS_Kp = kp;
                    settings.setAS_KpLeft = kp;
                    settings.setAS_KpRight = kp;
                    settings.setAS_isKpSplit = false;
                    break;

                case ParameterMinPwm:
                    if (minDelta == 0 || !aiConfidenceReady) return;
                    parameterRollbackValues[key] = settings.setAS_minSteerPWM;
                    byte minPwm = (byte)ClampInt(settings.setAS_minSteerPWM + minDelta, settings.setAS_adaptiveAiMinPwm, settings.setAS_adaptiveAiMaxPwm);
                    settings.setAS_minSteerPWM = minPwm;
                    settings.setAS_minSteerPWMLeft = minPwm;
                    settings.setAS_minSteerPWMRight = minPwm;
                    settings.setAS_isMinSteerPWMSplit = false;
                    break;

                case ParameterCalmResponse:
                    if (Math.Abs(responseDelta) < 0.1 || !aiConfidenceReady) return;
                    parameterRollbackValues[key] = settings.setAS_adaptiveSteerCalmResponsePercent;
                    settings.setAS_adaptiveSteerCalmResponsePercent = ClampDouble(settings.setAS_adaptiveSteerCalmResponsePercent + responseDelta, 40.0, 120.0);
                    break;

                case ParameterWasZero:
                    if (autoWasStep == 0 || !wasConfidenceReady) return;
                    parameterRollbackValues[key] = settings.setAS_wasOffset;
                    if (mf?.smartWAS?.QueueWasZeroCorrection(autoWasStep) != true) return;
                    break;

                default:
                    return;
            }

            settings.Save();
            mf?.SendSettings();
            Log.EventWriter("Adaptive Steering AI set " + key);
            UpdateLiveLabels();
        }

        private void ReverseAiParameter(string key)
        {
            if (!parameterRollbackValues.TryGetValue(key, out object rollbackValue)) return;

            var settings = Properties.VehicleSettings.Default;
            switch (key)
            {
                case ParameterKp:
                    byte kp = (byte)rollbackValue;
                    settings.setAS_Kp = kp;
                    settings.setAS_KpLeft = kp;
                    settings.setAS_KpRight = kp;
                    settings.setAS_isKpSplit = false;
                    break;

                case ParameterMinPwm:
                    byte minPwm = (byte)rollbackValue;
                    settings.setAS_minSteerPWM = minPwm;
                    settings.setAS_minSteerPWMLeft = minPwm;
                    settings.setAS_minSteerPWMRight = minPwm;
                    settings.setAS_isMinSteerPWMSplit = false;
                    break;

                case ParameterCalmResponse:
                    settings.setAS_adaptiveSteerCalmResponsePercent = (double)rollbackValue;
                    break;

                case ParameterWasZero:
                    settings.setAS_wasOffset = (int)rollbackValue;
                    break;

                default:
                    return;
            }

            parameterRollbackValues.Remove(key);
            settings.Save();
            mf?.SendSettings();
            Log.EventWriter("Adaptive Steering AI reverse " + key);
            UpdateLiveLabels();
        }

        private static void AddSetting(GroupBox groupBox, string text, NumericUpDown nud, int x, int y)
        {
            Label label = MakeLabel(new Point(x, y), new Size(165, 24), FontStyle.Regular);
            label.Text = text;
            nud.Location = new Point(x, y + 28);
            groupBox.Controls.Add(label);
            groupBox.Controls.Add(nud);
        }

        private static NumericUpDown MakeNud(decimal min, decimal max, int decimals)
        {
            return new NumericUpDown
            {
                Minimum = min,
                Maximum = max,
                DecimalPlaces = decimals,
                Increment = decimals == 0 ? 1M : 0.1M,
                Width = 135,
                Height = 36,
                Font = new Font("Tahoma", 18F, FontStyle.Bold),
                TextAlign = HorizontalAlignment.Center
            };
        }

        private static Label MakeLabel(Point location, Size size, FontStyle style)
        {
            return new Label
            {
                Location = location,
                Size = size,
                Font = new Font("Tahoma", 10.5F, style),
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        private static Label MakeMultiLabel(Point location, Size size, FontStyle style)
        {
            return new Label
            {
                Location = location,
                Size = size,
                Font = new Font("Tahoma", 9.2F, style),
                TextAlign = ContentAlignment.TopLeft
            };
        }

        private static DataGridView MakeParameterGrid(Point location, Size size)
        {
            DataGridView grid = new DataGridView
            {
                Location = location,
                Size = size,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = 32,
                Font = new Font("Tahoma", 9.4F, FontStyle.Regular),
                ReadOnly = true,
                RowHeadersVisible = false,
                RowTemplate = { Height = 46 },
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };

            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "colParameter",
                HeaderText = "Parameter",
                FillWeight = 27,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "colCurrent",
                HeaderText = "Current",
                FillWeight = 18,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "colAiValue",
                HeaderText = "AI value",
                FillWeight = 23,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "colConfidence",
                HeaderText = "Confidence",
                FillWeight = 13,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
            grid.Columns.Add(new DataGridViewButtonColumn
            {
                Name = "colSet",
                HeaderText = "Set",
                FillWeight = 9,
                FlatStyle = FlatStyle.Flat,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
            grid.Columns.Add(new DataGridViewButtonColumn
            {
                Name = "colReverse",
                HeaderText = "Reverse",
                FillWeight = 10,
                FlatStyle = FlatStyle.Flat,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });

            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(210, 230, 255);
            grid.DefaultCellStyle.SelectionForeColor = Color.Black;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(229, 229, 234);
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Tahoma", 8.4F, FontStyle.Bold);
            return grid;
        }

        private static Button MakeButton(string text, Point location, Size size, Color color)
        {
            Button button = new Button
            {
                Text = text,
                Location = location,
                Size = size,
                BackColor = color,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
                UseVisualStyleBackColor = false
            };
            button.FlatAppearance.BorderSize = 1;
            return button;
        }

        private static void ApplyButtonColor(Button button, Color color)
        {
            button.BackColor = color;
            int brightness = (color.R * 299 + color.G * 587 + color.B * 114) / 1000;
            button.ForeColor = brightness < 145 ? Color.White : Color.Black;
        }

        private static decimal ClampDecimal(decimal value, decimal min, decimal max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static string FormatSigned(double value)
        {
            return value >= 0
                ? "+" + value.ToString("N0", CultureInfo.CurrentCulture)
                : value.ToString("N0", CultureInfo.CurrentCulture);
        }

        private static string FormatSplit(bool isSplit, byte standard, byte left, byte right)
        {
            if (!isSplit) return standard.ToString(CultureInfo.CurrentCulture);
            return "L " + left.ToString(CultureInfo.CurrentCulture) + " / R " + right.ToString(CultureInfo.CurrentCulture);
        }

        private static string FormatSteerRecommendation(int recommended, int delta)
        {
            return recommended.ToString(CultureInfo.CurrentCulture)
                + (delta == 0 ? string.Empty : " (" + FormatSigned(delta) + ")");
        }

        private static string FormatDoubleRecommendation(double recommended, double delta, string unit, int decimals)
        {
            return FormatUnit(recommended, unit, decimals)
                + (Math.Abs(delta) < 0.1 ? string.Empty : " (" + FormatSigned(delta) + unit + ")");
        }

        private static string FormatPercentRecommendation(double recommended, double delta, int decimals)
        {
            return FormatPercent(recommended, decimals)
                + (Math.Abs(delta) < 0.1 ? string.Empty : " (" + FormatSigned(delta) + "%)");
        }

        private static string FormatUnit(double value, string unit, int decimals)
        {
            return FormatNumber(value, decimals) + " " + unit;
        }

        private static string FormatPercent(double value, int decimals)
        {
            return FormatNumber(value, decimals) + "%";
        }

        private static string FormatNumber(double value, int decimals)
        {
            string format = "N" + decimals.ToString(CultureInfo.InvariantCulture);
            return value.ToString(format, CultureInfo.CurrentCulture);
        }

        private static int ClampInt(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static double ClampDouble(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private sealed class SteeringParameterRow
        {
            public SteeringParameterRow(string key, string parameter, string current, string aiValue, string confidence, bool canSet, bool confidenceReady, bool canReverse)
            {
                Key = key;
                Parameter = parameter;
                Current = current;
                AiValue = aiValue;
                Confidence = confidence;
                CanSet = canSet;
                ConfidenceReady = confidenceReady;
                CanReverse = canReverse;
            }

            public string Key { get; }
            public string Parameter { get; }
            public string Current { get; }
            public string AiValue { get; }
            public string Confidence { get; }
            public bool CanSet { get; }
            public bool ConfidenceReady { get; }
            public bool CanReverse { get; }
        }

        private static string OnOff(bool isOn)
        {
            return isOn ? "On" : "Off";
        }
    }
}
