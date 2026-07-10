using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using AgLibrary.Logging;

namespace AgOpenGPS
{
    public partial class FormGPS
    {
        private TcpListener mobileRemoteListener;
        private CancellationTokenSource mobileRemoteCancel;
        private bool mobileRemoteManualSteerEnabled;
        private double mobileRemoteManualSteerAngle;
        private DateTime mobileRemoteManualSteerLastUtc = DateTime.MinValue;
        private const double MobileRemoteManualSteerTimeoutSeconds = 1.5;

        private void StartMobileRemoteServer()
        {
            try
            {
                if (mobileRemoteListener != null) return;

                mobileRemoteCancel = new CancellationTokenSource();
                mobileRemoteListener = new TcpListener(IPAddress.Any, 8765);
                mobileRemoteListener.Start();
                _ = Task.Run(() => MobileRemoteAcceptLoop(mobileRemoteCancel.Token));
                Log.EventWriter("AgOpenGPS Mobile Remote server started on TCP port 8765");
            }
            catch (Exception ex)
            {
                Log.EventWriter("AgOpenGPS Mobile Remote server start failed: " + ex.Message);
            }
        }

        private void StopMobileRemoteServer()
        {
            try
            {
                mobileRemoteCancel?.Cancel();
                mobileRemoteListener?.Stop();
            }
            catch { }
            finally
            {
                mobileRemoteCancel = null;
                mobileRemoteListener = null;
            }
        }

        private async Task MobileRemoteAcceptLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    TcpClient client = await mobileRemoteListener.AcceptTcpClientAsync();
                    _ = Task.Run(() => MobileRemoteHandleClient(client, token));
                }
                catch
                {
                    if (!token.IsCancellationRequested) await Task.Delay(500, token).ConfigureAwait(false);
                }
            }
        }

        private async Task MobileRemoteHandleClient(TcpClient client, CancellationToken token)
        {
            using (client)
            {
                try
                {
                    client.NoDelay = true;
                    using (NetworkStream stream = client.GetStream())
                    {
                        byte[] buffer = new byte[4096];
                        int read = await stream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);
                        if (read <= 0) return;

                        string header = Encoding.ASCII.GetString(buffer, 0, read);
                        string requestLine = header.Split(new[] { "\r\n" }, StringSplitOptions.None)[0];
                        string[] parts = requestLine.Split(' ');
                        string target = parts.Length > 1 ? parts[1] : "/";
                        Uri uri = new Uri("http://localhost" + target);
                        string path = uri.AbsolutePath.ToLowerInvariant();

                        if (path == "/" || path == "/ping")
                        {
                            await MobileRemoteWriteText(stream, "ok", "text/plain; charset=utf-8").ConfigureAwait(false);
                            return;
                        }

                        if (path == "/status")
                        {
                            string json = await MobileRemoteInvoke(BuildMobileRemoteStatusJson).ConfigureAwait(false);
                            await MobileRemoteWriteText(stream, json, "application/json; charset=utf-8").ConfigureAwait(false);
                            return;
                        }

                        if (path == "/cmd")
                        {
                            string command = MobileRemoteQueryValue(uri.Query, "c");
                            string angleText = MobileRemoteQueryValue(uri.Query, "angle");
                            string json = await MobileRemoteInvoke(() =>
                            {
                                ExecuteMobileRemoteCommand(command, angleText);
                                return BuildMobileRemoteStatusJson();
                            }).ConfigureAwait(false);
                            await MobileRemoteWriteText(stream, json, "application/json; charset=utf-8").ConfigureAwait(false);
                            return;
                        }

                        await MobileRemoteWriteNotFound(stream).ConfigureAwait(false);
                    }
                }
                catch
                {
                    // Remote clients are best-effort; ignore dropped browser/phone connections.
                }
            }
        }

        private Task<T> MobileRemoteInvoke<T>(Func<T> func)
        {
            var completion = new TaskCompletionSource<T>();

            try
            {
                if (IsDisposed)
                {
                    completion.SetException(new ObjectDisposedException(nameof(FormGPS)));
                    return completion.Task;
                }

                BeginInvoke((MethodInvoker)(() =>
                {
                    try { completion.SetResult(func()); }
                    catch (Exception ex) { completion.SetException(ex); }
                }));
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }

            return completion.Task;
        }

        private void ExecuteMobileRemoteCommand(string command, string angleText)
        {
            switch ((command ?? "").Trim().ToLowerInvariant())
            {
                case "status":
                    break;

                case "autosteer_toggle":
                case "ab_cycle":
                    btnAutoSteer.PerformClick();
                    break;

                case "stop":
                    DisableMobileRemoteManualSteer();
                    if (isBtnAutoSteerOn) btnAutoSteer.PerformClick();
                    sim.stepDistance = 0;
                    break;

                case "nudge_left":
                    if (trk.idx > -1) trk.NudgeTrack((double)Properties.ToolSettings.Default.setAS_snapDistance * -0.01);
                    break;

                case "nudge_right":
                    if (trk.idx > -1) trk.NudgeTrack((double)Properties.ToolSettings.Default.setAS_snapDistance * 0.01);
                    break;

                case "nudge_center":
                    if (trk.idx > -1) trk.SnapToPivot();
                    break;

                case "manual_sections":
                    btnSectionMasterManual.PerformClick();
                    break;

                case "auto_sections":
                    btnSectionMasterAuto.PerformClick();
                    break;

                case "zoom_in":
                    camera.ZoomIn();
                    break;

                case "zoom_out":
                    camera.ZoomOut();
                    break;

                case "sim_steer":
                    if (double.TryParse(angleText, NumberStyles.Float, CultureInfo.InvariantCulture, out double angle))
                    {
                        SetMobileRemoteSimSteer(angle);
                    }
                    break;

                case "sim_steer_center":
                    SetMobileRemoteSimSteer(0);
                    break;

                case "manual_steer_start":
                    EnableMobileRemoteManualSteer();
                    break;

                case "manual_steer":
                    if (double.TryParse(angleText, NumberStyles.Float, CultureInfo.InvariantCulture, out double manualAngle))
                    {
                        SetMobileRemoteManualSteer(manualAngle);
                    }
                    break;

                case "manual_steer_center":
                    SetMobileRemoteManualSteer(0);
                    break;

                case "manual_steer_stop":
                    DisableMobileRemoteManualSteer();
                    break;
            }
        }

        private void EnableMobileRemoteManualSteer()
        {
            mobileRemoteManualSteerEnabled = true;
            mobileRemoteManualSteerLastUtc = DateTime.UtcNow;
        }

        private void DisableMobileRemoteManualSteer()
        {
            mobileRemoteManualSteerEnabled = false;
            mobileRemoteManualSteerAngle = 0;
            mobileRemoteManualSteerLastUtc = DateTime.MinValue;
        }

        private void SetMobileRemoteManualSteer(double angle)
        {
            if (angle > 40) angle = 40;
            if (angle < -40) angle = -40;

            mobileRemoteManualSteerAngle = angle;
            mobileRemoteManualSteerEnabled = true;
            mobileRemoteManualSteerLastUtc = DateTime.UtcNow;

            SetMobileRemoteSimSteer(angle);
        }

        private bool IsMobileRemoteManualSteerActive()
        {
            if (!mobileRemoteManualSteerEnabled) return false;

            if ((DateTime.UtcNow - mobileRemoteManualSteerLastUtc).TotalSeconds > MobileRemoteManualSteerTimeoutSeconds)
            {
                DisableMobileRemoteManualSteer();
                return false;
            }

            return true;
        }

        private short GetMobileRemoteManualSteerCommand()
        {
            return (short)Math.Round(mobileRemoteManualSteerAngle * 100.0, MidpointRounding.AwayFromZero);
        }

        private void SetMobileRemoteSimSteer(double angle)
        {
            if (!timerSim.Enabled) return;

            if (angle > 40) angle = 40;
            if (angle < -40) angle = -40;

            sim.steerAngle = angle;
            sim.steerAngleScrollBar = angle;
            btnResetSteerAngle.Text = angle.ToString("N1", CultureInfo.InvariantCulture);
            hsbarSteerAngle.Value = Math.Max(hsbarSteerAngle.Minimum, Math.Min(hsbarSteerAngle.Maximum, (int)(10 * angle) + 400));
        }

        private string BuildMobileRemoteStatusJson()
        {
            double steerAngle = timerSim.Enabled ? sim.steerAngle : mc.actualSteerAngleDegrees;
            double xte = vehicle?.modeActualXTE ?? 0;
            bool remoteManual = IsMobileRemoteManualSteerActive();
            int abLines = trk?.gArr?.Count ?? 0;
            int abLineIndex = trk.idx >= 0 ? trk.idx + 1 : 0;

            return "{"
                + "\"autosteer\":" + (isBtnAutoSteerOn ? "true" : "false") + ","
                + "\"speed\":\"" + JsonEscape(avgSpeed.ToString("N1", CultureInfo.InvariantCulture)) + "\","
                + "\"field\":\"" + JsonEscape(displayFieldName) + "\","
                + "\"steerAngle\":\"" + JsonEscape(steerAngle.ToString("N1", CultureInfo.InvariantCulture)) + "\","
                + "\"xte\":\"" + JsonEscape(xte.ToString("N2", CultureInfo.InvariantCulture)) + "\","
                + "\"remoteManual\":" + (remoteManual ? "true" : "false") + ","
                + "\"simulator\":" + (timerSim.Enabled ? "true" : "false") + ","
                + "\"manualSections\":" + (manualBtnState == btnStates.On ? "true" : "false") + ","
                + "\"autoSections\":" + (autoBtnState == btnStates.Auto ? "true" : "false") + ","
                + "\"abLines\":" + abLines.ToString(CultureInfo.InvariantCulture) + ","
                + "\"abLineIndex\":" + abLineIndex.ToString(CultureInfo.InvariantCulture)
                + "}";
        }

        private static string JsonEscape(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", " ");
        }

        private static string MobileRemoteQueryValue(string query, string key)
        {
            if (string.IsNullOrEmpty(query)) return "";
            string trimmed = query[0] == '?' ? query.Substring(1) : query;
            string[] pairs = trimmed.Split('&');

            foreach (string pair in pairs)
            {
                int equals = pair.IndexOf('=');
                string name = equals >= 0 ? pair.Substring(0, equals) : pair;
                if (!string.Equals(WebUtility.UrlDecode(name), key, StringComparison.OrdinalIgnoreCase)) continue;

                string value = equals >= 0 ? pair.Substring(equals + 1) : "";
                return WebUtility.UrlDecode(value.Replace("+", " ")) ?? "";
            }

            return "";
        }

        private static async Task MobileRemoteWriteText(NetworkStream stream, string text, string contentType)
        {
            byte[] body = Encoding.UTF8.GetBytes(text);
            await MobileRemoteWriteHeader(stream, "200 OK", contentType, body.Length).ConfigureAwait(false);
            await stream.WriteAsync(body, 0, body.Length).ConfigureAwait(false);
        }

        private static async Task MobileRemoteWriteNotFound(NetworkStream stream)
        {
            byte[] body = Encoding.UTF8.GetBytes("not found");
            await MobileRemoteWriteHeader(stream, "404 Not Found", "text/plain; charset=utf-8", body.Length).ConfigureAwait(false);
            await stream.WriteAsync(body, 0, body.Length).ConfigureAwait(false);
        }

        private static async Task MobileRemoteWriteHeader(NetworkStream stream, string status, string contentType, int contentLength)
        {
            string header =
                "HTTP/1.1 " + status + "\r\n" +
                "Content-Type: " + contentType + "\r\n" +
                "Content-Length: " + contentLength.ToString(CultureInfo.InvariantCulture) + "\r\n" +
                "Cache-Control: no-store\r\n" +
                "Access-Control-Allow-Origin: *\r\n" +
                "Connection: close\r\n\r\n";
            byte[] bytes = Encoding.ASCII.GetBytes(header);
            await stream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
        }
    }
}
