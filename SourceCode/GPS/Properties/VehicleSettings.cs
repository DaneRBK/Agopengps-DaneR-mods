using AgLibrary.Settings;
using AgOpenGPS.Core.Models;
using System.IO;

namespace AgOpenGPS.Properties
{
    public sealed class VehicleSettings
    {
        private static VehicleSettings settings_ = new VehicleSettings();

        public static VehicleSettings Default
        {
            get { return settings_; }
        }

        // Vehicle dimensions
        public double setVehicle_wheelbase = 3.3;
        public double setVehicle_antennaHeight = 3;
        public double setVehicle_antennaPivot = 0.1;
        public double setVehicle_antennaOffset = 0;
        public double setVehicle_maxSteerAngle = 30;
        public double setVehicle_maxAngularVelocity = 0.64;
        public double setVehicle_trackWidth = 1.9;

        // AutoSteer settings
        public byte setAS_Kp = 50;
        public byte setAS_KpLeft = 50;
        public byte setAS_KpRight = 50;
        public bool setAS_isKpSplit = false;
        public byte setAS_countsPerDegree = 110;
        public byte setAS_minSteerPWM = 25;
        public byte setAS_minSteerPWMLeft = 25;
        public byte setAS_minSteerPWMRight = 25;
        public bool setAS_isMinSteerPWMSplit = false;
        public byte setAS_highSteerPWM = 180;
        public byte setAS_lowSteerPWM = 30;
        public int setAS_wasOffset = 3;
        public bool setAS_xteGuardEnabled = false;
        public double setAS_xteGuardTriggerCmSec = 3.0;
        public double setAS_xteGuardGain = 0.6;
        public double setAS_xteGuardMaxCorrection = 2.0;
        public double setAS_xteGuardDecay = 0.12;
        public double setAS_xteGuardMinSpeed = 1.0;
        public bool setAS_xteGuardInvertDirection = false;
        public bool setAS_adaptiveSteerEnabled = false;
        public double setAS_adaptiveSteerCalmBandCm = 3.0;
        public double setAS_adaptiveSteerCalmResponsePercent = 80.0;
        public double setAS_adaptiveSteerTriggerCmSec = 2.0;
        public double setAS_adaptiveSteerBoostGain = 4.0;
        public double setAS_adaptiveSteerMaxBoostPercent = 35.0;
        public double setAS_adaptiveSteerSmoothingPercent = 25.0;
        public double setAS_adaptiveSteerMinSpeed = 0.5;
        public double setAS_adaptiveSteerBoostDelaySec = 0.35;
        public int setAS_adaptiveSteerRequiredSamples = 3;
        public int setAS_adaptiveAiMode = 0;
        public double setAS_adaptiveAiMinSpeed = 1.5;
        public double setAS_adaptiveAiTargetXteCm = 3.0;
        public double setAS_adaptiveAiEvaluateSec = 20.0;
        public double setAS_adaptiveAiMinConfidence = 70.0;
        public byte setAS_adaptiveAiMinKp = 20;
        public byte setAS_adaptiveAiMaxKp = 180;
        public byte setAS_adaptiveAiMinPwm = 10;
        public byte setAS_adaptiveAiMaxPwm = 80;
        public double setAS_adaptiveAiMaxStepPercent = 3.0;
        public bool setAS_autoWasZeroEnabled = true;
        public double setAS_autoWasZeroMinConfidence = 75.0;
        public int setAS_autoWasZeroMaxStepCounts = 6;
        public int setAS_autoWasZeroMaxTotalCounts = 80;
        public bool setIMU_autoRollLearnEnabled = false;
        public bool setIMU_autoRollLearnAutoApply = false;
        public double setIMU_autoRollLearnMinPassLength = 30.0;
        public double setIMU_autoRollLearnMaxXteCm = 3.0;
        public double setIMU_autoRollLearnMinConfidence = 75.0;
        public double setIMU_autoRollLearnMaxStepDeg = 0.05;
        public bool setAS_bodyLineHoldEnabled = false;
        public double setAS_bodyLineHoldGain = 0.40;
        public double setAS_bodyLineHoldMaxCorrection = 2.0;
        public double setAS_bodyLineHoldFilterPercent = 20.0;
        public double setAS_bodyLineHoldMinSpeed = 1.0;
        public bool setAS_bodyLineHoldInvertDirection = false;
        public double setAS_sideHillComp = 0.0;
        public byte setAS_ackerman = 100;
        public double setAS_ModeXTE = 0.1;
        public int setAS_ModeTime = 1;
        public double setAS_functionSpeedLimit = 12;
        public double setAS_maxSteerSpeed = 15;
        public double setAS_minSteerSpeed = 0;
        public bool setAS_isSteerInReverse = false;

        // IMU settings
        public double setIMU_rollZero = 0.0;
        public double setIMU_rollFilter = 0.0;
        public bool setIMU_invertRoll = false;
        public bool setIMU_isDualAsIMU = false;
        public double setIMU_fusionWeight2 = 0.06;
        public bool setIMU_rollCutEnabled = false;
        public double setIMU_rollCutAngleDeg = 2.0;
        public double setIMU_rollCutWindowSec = 0.5;
        public double setIMU_rollCutRateDegSec = 4.0;
        public double setIMU_rollCutHoldSec = 0.8;
        public double setIMU_rollCutRecoveryPercent = 10.0;

        // GPS settings
        public string setGPS_headingFromWhichSource = "Fix";
        public double setGPS_forwardComp = 0.15;
        public double setGPS_reverseComp = 0.3;
        public double setGPS_dualHeadingOffset = 0.0;
        public double setGPS_dualReverseDetectionDistance = 0.25;
        public double setGPS_minimumStepLimit = 0.05;

        // Arduino Steer
        public byte setArdSteer_setting0 = 56;
        public byte setArdSteer_setting1 = 0;
        public byte setArdSteer_maxPulseCounts = 3;
        public bool setArdMac_isDanfoss = false;

        // Brands
        public TractorBrand setBrand_TBrand = TractorBrand.AGOpenGPS;
        public HarvesterBrand setBrand_HBrand = HarvesterBrand.AgOpenGPS;
        public ArticulatedBrand setBrand_WDBrand = ArticulatedBrand.AgOpenGPS;

        // Vehicle type
        public int setVehicle_vehicleType = 0;
        public double setVehicle_panicStopSpeed = 0;

        public LoadResult Load(string vehicleFileName)
        {
            string path = Path.Combine(RegistrySettings.vehiclesDirectory, vehicleFileName + ".xml");
            var result = XmlSettingsHandler.LoadXMLFile(path, this);
            if (result == LoadResult.MissingFile)
            {
                // Try loading from old format and migrate
                result = CSettingsMigration.MigrateVehicle(vehicleFileName, this);
                NormalizeSplitSteerGain();
                return result;
            }

            // Update registry with the loaded vehicle file name
            if (result == LoadResult.Ok)
            {
                RegistrySettings.vehicleProfileName = vehicleFileName;
                NormalizeSplitSteerGain();
            }

            return result;
        }

        public void SetSplitSteerGain(byte kpLeft, byte kpRight, byte minLeft, byte minRight)
        {
            setAS_KpLeft = kpLeft;
            setAS_KpRight = kpRight;
            setAS_Kp = kpLeft;
            setAS_isKpSplit = true;

            setAS_minSteerPWMLeft = minLeft;
            setAS_minSteerPWMRight = minRight;
            setAS_minSteerPWM = minLeft;
            setAS_isMinSteerPWMSplit = true;
        }

        public void SetStandardSteerGain(byte kp, byte min)
        {
            setAS_Kp = kp;
            setAS_KpLeft = kp;
            setAS_KpRight = kp;
            setAS_isKpSplit = false;

            setAS_minSteerPWM = min;
            setAS_minSteerPWMLeft = min;
            setAS_minSteerPWMRight = min;
            setAS_isMinSteerPWMSplit = false;
        }

        public void NormalizeSplitSteerGain()
        {
            SetStandardSteerGain(setAS_Kp, setAS_minSteerPWM);
        }

        /// <summary>
        /// Save vehicle settings to file using the file name from registry.
        /// Uses "DefaultVehicle" as fallback if registry value is empty.
        /// </summary>
        public void Save()
        {
            // Read file name from registry, use fallback if empty
            string fileName = string.IsNullOrEmpty(RegistrySettings.vehicleProfileName)
                ? "DefaultVehicle"
                : RegistrySettings.vehicleProfileName;

            string path = Path.Combine(RegistrySettings.vehiclesDirectory, fileName + ".xml");
            XmlSettingsHandler.SaveXMLFile(path, this);
        }

        /// <summary>
        /// Save vehicle settings to a specific file (used during migration).
        /// This overload saves to a custom file name without updating the registry.
        /// </summary>
        /// <param name="fileName">The file name to save to (without extension)</param>
        public void Save(string fileName)
        {
            string path = Path.Combine(RegistrySettings.vehiclesDirectory, fileName + ".xml");
            XmlSettingsHandler.SaveXMLFile(path, this);
        }

        public void Reset()
        {
            settings_ = new VehicleSettings();
        }
    }
}
