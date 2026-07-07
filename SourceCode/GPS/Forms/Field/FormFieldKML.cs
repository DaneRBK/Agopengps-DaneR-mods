using AgLibrary.Logging;
using AgOpenGPS.Controls;
using AgOpenGPS.Core.Models;
using AgOpenGPS.Core.Translations;
using AgOpenGPS.Forms;
using AgOpenGPS.Helpers;
using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace AgOpenGPS
{
    public partial class FormFieldKML : Form
    {
        //class variables
        private readonly FormGPS mf = null;

        private double latK, lonK;
        private string fileName;
        private string[] fileNames = Array.Empty<string>();

        public FormFieldKML(Form _callingForm)
        {
            //get copy of the calling main form
            mf = _callingForm as FormGPS;

            InitializeComponent();

            labelFieldname.Text = gStr.gsEnterFieldName;
            this.Text = gStr.gsCreateNewField;
        }

        private void FormFieldDir_Load(object sender, EventArgs e)
        {
            btnSave.Enabled = false;

            if (!ScreenHelper.IsOnScreen(Bounds))
            {
                Top = 0;
                Left = 0;
            }
        }

        private void tboxFieldName_TextChanged(object sender, EventArgs e)
        {
            TextBox textboxSender = (TextBox)sender;
            int cursorPosition = textboxSender.SelectionStart;
            textboxSender.Text = Regex.Replace(textboxSender.Text, glm.fileRegex, "");
            textboxSender.SelectionStart = cursorPosition;

            if (String.IsNullOrEmpty(tboxFieldName.Text.Trim()))
            {
                btnLoadKML.Enabled = false;
            }
            else
            {
                btnLoadKML.Enabled = true;
            }
        }

        private void btnSerialCancel_Click(object sender, EventArgs e)
        {
            Close();
        }

        private async void btnSave_Click(object sender, EventArgs e)
        {
            if (fileNames.Length > 1)
            {
                int created = 0;
                int failed = 0;

                foreach (string kmlFile in fileNames)
                {
                    if (mf.isJobStarted)
                        await mf.FileSaveEverythingBeforeClosingField();

                    string fieldName = GetUniqueFieldName(GetFieldNameFromFile(kmlFile));
                    if (CreateFieldFromKml(kmlFile, fieldName, true))
                    {
                        created++;
                    }
                    else
                    {
                        failed++;
                    }
                }

                if (created > 0)
                {
                    FormDialog.Show("KML Import", "Created " + created.ToString(CultureInfo.CurrentCulture)
                        + " field(s)." + (failed > 0 ? " Failed: " + failed.ToString(CultureInfo.CurrentCulture) : string.Empty), DialogSeverity.Info);
                    DialogResult = DialogResult.OK;
                    Close();
                }
                else
                {
                    btnSave.Enabled = false;
                }

                return;
            }

            if (mf.isJobStarted)
                await mf.FileSaveEverythingBeforeClosingField();

            if (!CreateFieldFromKml(fileName, tboxFieldName.Text.Trim(), false))
            {
                btnSave.Enabled = false;
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        private void tboxFieldName_Click(object sender, EventArgs e)
        {
            if (mf.isKeyboardOn)
            {
                ((TextBox)sender).ShowKeyboard(this);
                btnSerialCancel.Focus();
            }
        }

        private void btnLoadKML_Click(object sender, EventArgs e)
        {
            //create the dialog instance
            OpenFileDialog ofd = new OpenFileDialog
            {
                //set the filter to text KML only
                Filter = "KML files (*.KML)|*.KML",
                Multiselect = true,

                //the initial directory, fields, for the open dialog
                InitialDirectory = RegistrySettings.fieldsDirectory
            };

            //was a file selected
            if (ofd.ShowDialog() == DialogResult.Cancel) return;

            fileNames = ofd.FileNames;
            if (fileNames.Length == 0) return;

            if (fileNames.Length > 1)
            {
                fileName = null;
                tboxFieldName.Text = fileNames.Length.ToString(CultureInfo.CurrentCulture) + " KML files selected";
                btnSave.Enabled = true;
                btnLoadKML.Enabled = false;
                return;
            }

            fileName = ofd.FileName;
            if (tboxFieldName.Text.Length == 0)
            {
                tboxFieldName.Text = Path.GetFileNameWithoutExtension(fileName);
            }

            //get lat and lon from boundary in kml
            if (!FindLatLon(fileName)) return;

            //check if we can load
            //Load the outer boundary
            LoadKMLBoundary(fileName, false);
        }

        private void btnAddDate_Click(object sender, EventArgs e)
        {
            tboxFieldName.Text += " " + DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        private void btnAddTime_Click(object sender, EventArgs e)
        {
            tboxFieldName.Text += " " + DateTime.Now.ToString("HH-mm", CultureInfo.InvariantCulture);
        }

        private bool LoadKMLBoundary(string filename, bool fieldCreated)
        {
            string coordinates = null;
            int startIndex;

            using (System.IO.StreamReader reader = new System.IO.StreamReader(filename))
            {
                try
                {
                    while (!reader.EndOfStream)
                    {
                        //start to read the file
                        string line = reader.ReadLine();

                        startIndex = line.IndexOf("<coordinates>");

                        if (startIndex != -1)
                        {
                            while (true)
                            {
                                int endIndex = line.IndexOf("</coordinates>");

                                if (endIndex == -1)
                                {
                                    //just add the line
                                    if (startIndex == -1) coordinates += " " + line.Substring(0);
                                    else coordinates += line.Substring(startIndex + 13);
                                }
                                else
                                {
                                    if (startIndex == -1) coordinates += " " + line.Substring(0, endIndex);
                                    else coordinates += line.Substring(startIndex + 13, endIndex - (startIndex + 13));
                                    break;
                                }
                                line = reader.ReadLine();
                                line = line.Trim();
                                startIndex = -1;
                            }

                            line = coordinates;
                            char[] delimiterChars = { ' ', '\t', '\r', '\n' };
                            string[] numberSets = line.Split();

                            //at least 3 points
                            if (numberSets.Length > 2)
                            {
                                CBoundaryList New = new CBoundaryList();

                                foreach (string item in numberSets)
                                {
                                    if (item.Length < 3)
                                        continue;
                                    string[] fix = item.Split(',');
                                    double.TryParse(fix[0], NumberStyles.Float, CultureInfo.InvariantCulture, out lonK);
                                    double.TryParse(fix[1], NumberStyles.Float, CultureInfo.InvariantCulture, out latK);

                                    GeoCoord geoCoord = mf.AppModel.LocalPlane.ConvertWgs84ToGeoCoord(new Wgs84(latK, lonK));
                                    New.fenceLine.Add(new vec3(geoCoord));
                                }

                                //build the boundary, make sure is clockwise for outer counter clockwise for inner
                                New.CalculateFenceArea(mf.bnd.bndList.Count);
                                New.FixFenceLine(mf.bnd.bndList.Count);
                                if (fieldCreated)
                                {
                                    mf.bnd.bndList.Add(New);

                                    mf.btnABDraw.Visible = true;
                                }

                                coordinates = "";
                            }
                            else
                            {
                                FormDialog.Show(gStr.gsErrorreadingKML, gStr.gsChooseBuildDifferentone, DialogSeverity.Error);
                                Log.EventWriter("New Field, Error Reading KML");
                                return false;
                            }
                            break;
                        }
                    }
                    if (fieldCreated)
                    {
                        mf.FileSaveBoundary();
                        mf.bnd.BuildTurnLines();
                        mf.fd.UpdateFieldBoundaryGUIAreas();
                        mf.CalculateMinMax();
                    }

                    btnSave.Enabled = true;
                    btnLoadKML.Enabled = false;
                }
                catch (Exception ee)
                {
                    btnSave.Enabled = false;
                    btnLoadKML.Enabled = false;
                    FormDialog.Show(gStr.gsErrorreadingKML, gStr.gsChooseBuildDifferentone, DialogSeverity.Error);
                    Log.EventWriter("New Field, Error Reading KML" + ee.ToString());
                    return false;
                }
            }

            mf.bnd.isOkToAddPoints = false;
            return true;
        }

        private bool FindLatLon(string filename)
        {
            string coordinates = null;
            int startIndex;

            using (System.IO.StreamReader reader = new System.IO.StreamReader(filename))
            {
                try
                {
                    while (!reader.EndOfStream)
                    {
                        //start to read the file
                        string line = reader.ReadLine();

                        startIndex = line.IndexOf("<coordinates>");

                        if (startIndex != -1)
                        {
                            while (true)
                            {
                                int endIndex = line.IndexOf("</coordinates>");

                                if (endIndex == -1)
                                {
                                    //just add the line
                                    if (startIndex == -1) coordinates += " " + line.Substring(0);
                                    else coordinates += line.Substring(startIndex + 13);
                                }
                                else
                                {
                                    if (startIndex == -1) coordinates += " " + line.Substring(0, endIndex);
                                    else coordinates += line.Substring(startIndex + 13, endIndex - (startIndex + 13));
                                    break;
                                }
                                line = reader.ReadLine();
                                line = line.Trim();
                                startIndex = -1;
                            }

                            line = coordinates;
                            char[] delimiterChars = { ' ', '\t', '\r', '\n' };
                            string[] numberSets = line.Split(delimiterChars);

                            //at least 3 points
                            if (numberSets.Length > 2)
                            {
                                double counter = 0, lat = 0, lon = 0;
                                latK = lonK = 0;
                                foreach (string item in numberSets)
                                {
                                    if (item.Length < 3)
                                        continue;
                                    string[] fix = item.Split(',');
                                    double.TryParse(fix[0], NumberStyles.Float, CultureInfo.InvariantCulture, out lonK);
                                    double.TryParse(fix[1], NumberStyles.Float, CultureInfo.InvariantCulture, out latK);
                                    lat += latK;
                                    lon += lonK;
                                    counter += 1;
                                }
                                lonK = lon / counter;
                                latK = lat / counter;

                                coordinates = "";
                            }
                            else
                            {
                                FormDialog.Show(gStr.gsErrorreadingKML, gStr.gsChooseBuildDifferentone, DialogSeverity.Error);
                                Log.EventWriter("New Field, Error Reading KML ");
                                return false;

                            }
                            //if (button.Name == "btnLoadBoundaryFromGE")
                            //{
                            break;
                            //}
                        }
                    }
                }
                catch (Exception et)
                {
                    FormDialog.Show("Exception", "Error Finding Lat Lon", DialogSeverity.Error);
                    Log.EventWriter("Lat Lon Exception Reading KML " + et.ToString());
                    return false;
                }
            }

            mf.bnd.isOkToAddPoints = false;
            return true;
        }

        private bool CreateFieldFromKml(string kmlFileName, string fieldName, bool useUniqueName)
        {
            if (string.IsNullOrEmpty(kmlFileName)) return false;
            if (!FindLatLon(kmlFileName)) return false;

            string newFieldName = useUniqueName ? GetUniqueFieldName(fieldName) : fieldName;
            if (!CreateNewField(newFieldName)) return false;

            return LoadKMLBoundary(kmlFileName, true);
        }

        private bool CreateNewField(string fieldName)
        {
            //fill something in
            if (string.IsNullOrEmpty(fieldName.Trim()))
            {
                return false;
            }

            //append date time to name
            mf.currentFieldDirectory = fieldName.Trim();

            //get the directory and make sure it exists, create if not
            string directoryName = Path.Combine(RegistrySettings.fieldsDirectory, mf.currentFieldDirectory);

            mf.menustripLanguage.Enabled = false;
            //if no template set just make a new file.
            try
            {
                if ((!string.IsNullOrEmpty(directoryName)) && Directory.Exists(directoryName))
                {
                    FormDialog.Show(gStr.gsChooseADifferentName, gStr.gsDirectoryExists, DialogSeverity.Error);
                    return false;
                }

                //start a new job
                mf.JobNew();

                //create it for first save
                mf.pn.DefineLocalPlane(new Wgs84(latK, lonK), true);

                //make sure directory exists, or create it
                if ((!string.IsNullOrEmpty(directoryName)) && (!Directory.Exists(directoryName)))
                { Directory.CreateDirectory(directoryName); }

                //create the field file header info
                if (!mf.isJobStarted)
                {
                    FormDialog.Show(gStr.gsFieldNotOpen, gStr.gsCreateNewField, DialogSeverity.Error);
                    return false;
                }
                string myFileName;

                //get the directory and make sure it exists, create if not
                directoryName = Path.Combine(RegistrySettings.fieldsDirectory, mf.currentFieldDirectory);

                if ((directoryName.Length > 0) && (!Directory.Exists(directoryName)))
                { Directory.CreateDirectory(directoryName); }

                myFileName = "Field.txt";

                using (StreamWriter writer = new StreamWriter(Path.Combine(directoryName, myFileName)))
                {
                    //Write out the date
                    writer.WriteLine(DateTime.Now.ToString("yyyy-MMMM-dd hh:mm:ss tt", CultureInfo.InvariantCulture));

                    writer.WriteLine("$FieldDir");
                    writer.WriteLine("KML Derived");

                    //write out the easting and northing Offsets
                    writer.WriteLine("$Offsets");
                    writer.WriteLine("0,0");

                    writer.WriteLine("Convergence");
                    writer.WriteLine("0");

                    writer.WriteLine("StartFix");
                    writer.WriteLine(
                        mf.AppModel.LocalPlane.Origin.Latitude.ToString(CultureInfo.InvariantCulture) + "," +
                        mf.AppModel.LocalPlane.Origin.Longitude.ToString(CultureInfo.InvariantCulture));
                }

                mf.FileCreateSections();
                mf.FileCreateRecPath();
                mf.FileCreateContour();
                mf.FileCreateElevation();
                mf.FileSaveFlags();
                //mf.FileSaveABLine();
                //mf.FileSaveCurveLine();
                //mf.FileSaveHeadland();
            }
            catch (Exception ex)
            {
                Log.EventWriter("Creating new kml field " + ex.ToString());

                FormDialog.Show(gStr.gsError, ex.ToString(), DialogSeverity.Error);
                mf.currentFieldDirectory = "";
                return false;
            }

            return true;
        }

        private static string GetFieldNameFromFile(string kmlFileName)
        {
            string fieldName = Regex.Replace(Path.GetFileNameWithoutExtension(kmlFileName), glm.fileRegex, "").Trim();
            return string.IsNullOrEmpty(fieldName) ? "KML Field" : fieldName;
        }

        private static string GetUniqueFieldName(string baseFieldName)
        {
            string safeBaseName = Regex.Replace(baseFieldName, glm.fileRegex, "").Trim();
            if (string.IsNullOrEmpty(safeBaseName)) safeBaseName = "KML Field";

            string candidate = safeBaseName;
            int suffix = 1;
            while (Directory.Exists(Path.Combine(RegistrySettings.fieldsDirectory, candidate)))
            {
                candidate = safeBaseName + " " + suffix.ToString(CultureInfo.InvariantCulture);
                suffix++;
            }

            return candidate;
        }
    }
}
