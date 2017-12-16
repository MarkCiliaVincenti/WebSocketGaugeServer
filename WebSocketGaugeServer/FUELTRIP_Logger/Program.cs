﻿using System;
using System.Xml;
using System.IO;
using log4net;
using System.Threading;
using System.Collections.Generic;
using DefiSSMCOM.Defi;
using DefiSSMCOM.SSM;
using DefiSSMCOM.Arduino;
using DefiSSMCOM.OBDII;

namespace FUELTRIP_Logger
{
    /// <summary>
    /// Class to store required parameter code.
    /// </summary>
    public class RequiredParameterCode
    {
        public List<DefiParameterCode> DefiCodes = new List<DefiParameterCode>();
        public List<SSMParameterCode> SSMCodes = new List<SSMParameterCode>();
        public List<ArduinoParameterCode> ArduinoCodes = new List<ArduinoParameterCode>();
        public List<OBDIIParameterCode> ELM327OBDCodes = new List<OBDIIParameterCode>();
    }

    /// <summary>
    /// Application setting class.
    /// </summary>
    public class AppSettings
    {
        public string defiserver_url;
        public string ssmserver_url;
        public string arduinoserver_url;
        public string elm327server_url;
        public int websocket_port;
        public int keepalive_interval;
        public calculation Calculation;

        public class calculation
        {
            public FuelCalculationMethod FuelCalculationMethod;
            public DataSource DataSource;
            public FuelTripCalculatorOption CalculationOption;
        }

        public enum DataSourceType
        {
            DEFI,
            SSM,
            ARDUINO,
            ELM327
        }

        public class DataSource
        {
            public DataSourceType VehicleSpeedSource;
            public DataSourceType RPMSource;
            public DataSourceType InjectionPWSource;
            public DataSourceType MassAirFlowSource;
            public DataSourceType AFRatioSource;
            public DataSourceType FuelRateSource;
        }

        /// <summary>
        /// Create the list of parameter code from data source and calculation method setting.
        /// </summary>
        /// <returns>The list of required parameter code.</returns>
        public RequiredParameterCode getRequiredParameterCodes()
        {
            RequiredParameterCode requiredCodes = new RequiredParameterCode();
            FuelCalculationMethod calcMethod = this.Calculation.FuelCalculationMethod;
            DataSource dataSource = this.Calculation.DataSource;
            switch(calcMethod)
            {
                case FuelCalculationMethod.FUEL_RATE:
                {
                    switch(dataSource.FuelRateSource)
                    {
                        case DataSourceType.ELM327:
                            requiredCodes.ELM327OBDCodes.Add(OBDIIParameterCode.Engine_fuel_rate);
                            break;
                        default :
                            throw new ArgumentException("Engine Fuel rate is supported only on ELM327.");
                    }
                    break;
                }
                case FuelCalculationMethod.RPM_INJECTION_PW :
                {
                    switch(dataSource.InjectionPWSource)
                    {
                        case DataSourceType.SSM:
                            requiredCodes.SSMCodes.Add(SSMParameterCode.Fuel_Injection_1_Pulse_Width);
                            break;
                        default :
                            throw new ArgumentException("Fuel injection pulse width is supported only on SSM");
                    }
                    switch(dataSource.RPMSource)
                    {
                        case DataSourceType.DEFI:
                            requiredCodes.DefiCodes.Add(DefiParameterCode.Engine_Speed);
                            break;
                        case DataSourceType.SSM:
                            requiredCodes.SSMCodes.Add(SSMParameterCode.Engine_Speed);
                            break;
                        case DataSourceType.ARDUINO:
                            requiredCodes.ArduinoCodes.Add(ArduinoParameterCode.Engine_Speed);
                            break;
                        case DataSourceType.ELM327:
                            requiredCodes.ELM327OBDCodes.Add(OBDIIParameterCode.Engine_Speed);
                            break;
                    }
                    break;
                }
                case FuelCalculationMethod.MASS_AIR_FLOW_AF:
                {
                    switch(dataSource.MassAirFlowSource)
                    {
                        case DataSourceType.SSM:
                            requiredCodes.SSMCodes.Add(SSMParameterCode.Mass_Air_Flow);
                            break;
                        case DataSourceType.ELM327:
                            requiredCodes.ELM327OBDCodes.Add(OBDIIParameterCode.Mass_Air_Flow);
                            break;
                        default:
                            throw new ArgumentException("Mass air flow is supported only on SSM or ELM327.");
                    }
                    switch(dataSource.AFRatioSource)
                    {
                        case DataSourceType.SSM:
                            requiredCodes.SSMCodes.Add(SSMParameterCode.Air_Fuel_Sensor_1);
                            break;
                        case DataSourceType.ELM327:
                            requiredCodes.ELM327OBDCodes.Add(OBDIIParameterCode.Command_equivalence_ratio);
                            break;
                        default:
                            throw new ArgumentException("A/F ratio is supported only on SSM or ELM327.");
                    }
                    break;
                }
                case FuelCalculationMethod.MASS_AIR_FLOW:
                {
                    switch (dataSource.MassAirFlowSource)
                    {
                        case DataSourceType.SSM:
                            requiredCodes.SSMCodes.Add(SSMParameterCode.Mass_Air_Flow);
                            break;
                        case DataSourceType.ELM327:
                            requiredCodes.ELM327OBDCodes.Add(OBDIIParameterCode.Mass_Air_Flow);
                            break;
                        default:
                            throw new ArgumentException("Mass air flow is supported only on SSM or ELM327.");
                    }
                    break;
                }
            }
            return requiredCodes;
        }

        public void ValidateSettings()
        {
            DataSource datasource = this.Calculation.DataSource;
            switch(datasource.VehicleSpeedSource)
            {
                case DataSourceType.DEFI:
                    throw new ArgumentException("VehicleSpeed is not supported by " + datasource.VehicleSpeedSource.ToString());
            }

            switch(datasource.InjectionPWSource)
            {
                case DataSourceType.DEFI:
                case DataSourceType.ARDUINO:
                case DataSourceType.ELM327:
                    throw new ArgumentException("InjectionPW is not supported by " + datasource.InjectionPWSource.ToString());
            }

            switch(datasource.MassAirFlowSource)
            {
                case DataSourceType.DEFI:
                case DataSourceType.ARDUINO:
                    throw new ArgumentException("MassAirFlow is not supported by " + datasource.MassAirFlowSource.ToString());
            }

            switch(datasource.AFRatioSource)
            {
                case DataSourceType.DEFI:
                case DataSourceType.ARDUINO:
                    throw new ArgumentException("AFRatio is not supported by " + datasource.AFRatioSource.ToString());
            }

            switch(datasource.FuelRateSource)
            {
                case DataSourceType.DEFI:
                case DataSourceType.SSM:
                case DataSourceType.ARDUINO:
                    throw new ArgumentException("FuelRate is not supported by " + datasource.FuelRateSource.ToString());
            }
        }
    }

	class MainClass
	{
        //log4net
        private static readonly ILog logger = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

		public static void Main (string[] args)
		{
            AppSettings appsetting;
            try
            {
                appsetting = LoadSettingXml("fueltriplogger_settings.xml");
                appsetting.ValidateSettings();
            }
            catch (XmlException ex)
            {
                //Console.WriteLine(ex.Message);
                logger.Error(ex.Message);
                return;
            }
            catch (FileNotFoundException ex)
            {
                //Console.WriteLine(ex.Message);
                logger.Error(ex.Message);
                return;
            }
            catch (DirectoryNotFoundException ex)
            {
                //Console.WriteLine(ex.Message);
                logger.Error(ex.Message);
                return;
            }
            catch (System.Security.SecurityException ex)
            {
                //Console.WriteLine(ex.Message);
                logger.Error(ex.Message);
                return;
            }
            catch(ArgumentException ex)
            {
                logger.Error(ex.Message);
                return;
            }
            RequiredParameterCode codelist = appsetting.getRequiredParameterCodes();

			FUELTRIPLogger fueltriplogger1 = new FUELTRIPLogger(appsetting);
            fueltriplogger1.WebsocketServer_ListenPortNo = appsetting.websocket_port;
            fueltriplogger1.KeepAliveInterval = appsetting.keepalive_interval;

			Console.WriteLine("The server started successfully, press key 'q' to stop it!");

			fueltriplogger1.start ();

			while (Console.ReadKey().KeyChar != 'q')
			{
                Thread.Sleep(500);
				continue;
			}

			fueltriplogger1.stop ();
		}

        private static AppSettings LoadSettingXml(string filepath)
        {
            //Construct XmlSerializer
            System.Xml.Serialization.XmlSerializer serializer =
                new System.Xml.Serialization.XmlSerializer(typeof(AppSettings));

            System.IO.FileStream fs =
                new System.IO.FileStream(filepath, System.IO.FileMode.Open, System.IO.FileAccess.Read);

            try
            {
                return (AppSettings)serializer.Deserialize(fs);
            }
            catch (XmlException ex)
            {
                throw ex;
            }
            finally
            {
                fs.Close();
            }
        }

	}
}
