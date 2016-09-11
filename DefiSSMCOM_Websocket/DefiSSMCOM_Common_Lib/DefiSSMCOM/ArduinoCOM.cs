﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.IO.Ports;
using log4net;

namespace DefiSSMCOM
{
    public class ArduinoCOM : COMCommon
    {
        private Arduino_Content_Table content_table;

        // Arduinoから1サイクルで送信されるデータ(行)数(Tacho + Speed + ADC6ch分)
        private const int NUM_ROWS_PER_CYCLE = 8;
        // Defilink received Event
        public event EventHandler ArduinoPacketReceived;

        //コンストラクタ
        public ArduinoCOM()
        {
            content_table = new Arduino_Content_Table();

            //Arduinoシリアルボーレート設定
            DefaultBaudRate = 19200;
            //リセット時のボーレート設定(communticate_reset()参照)
            //FT232RLの場合、許容されるボーレートは3000000/n (nは整数または小数点以下が0.125, 0.25, 0.375, 0.5, 0.625, 0.75, 0.875)
            ResetBaudRate = 9600;

            Parity = Parity.None;
            ReadTimeout = 500;
        }

        //通信部ルーチン実装
        //この実装ではslowread_flagは無視
        protected override void communicate_main(bool slowread_flag)
        {
            int i;
            string readbuf;
            char headerCode;
            //読み込みルーチン

            for (i = 0; i < NUM_ROWS_PER_CYCLE; i++)
            {
                try
                {
                    readbuf = ReadLine();
                    if (readbuf.Length > 0)
                        headerCode = readbuf[0];
                    else
                    {
                        //Headerコード読み込み失敗(行の文字列長0)
                        logger.Warn("Arduino header read failed (0length data packet)");
                        return;
                    }
                }
                catch (TimeoutException ex)
                {
                    //読み出しタイムアウト時はエラーフラグを立て、次のサイクルでリセット処理を入れる
                    logger.Warn("Defi packet timeout. " + ex.GetType().ToString() + " " + ex.Message);
                    communicateRealtimeIsError = true;
                    return;
                }

                //Headerコードを読んで、値を格納
                try
                {
                    foreach (Arduino_Parameter_Code paramCode in Enum.GetValues(typeof(Arduino_Parameter_Code)))
                    {
                        if (headerCode == content_table[paramCode].Header_char)
                        {
                            content_table[paramCode].Raw_Value = int.Parse(readbuf.Remove(0, 1));
                            continue;
                        }
                    }

                    //どのヘッダコードにも該当しない場合、Warningを出す
                    logger.Warn("Header code matching is failed. Header code is : " + headerCode);
                }
                catch (FormatException ex)
                {
                    //DefiPacketが崩れていた場合エラーフラグを立て、次のサイクルでリセット処理を入れる。
                    logger.Warn("Invalid Arduino packet format. " + ex.GetType().ToString() + " " + ex.Message);
                    communicateRealtimeIsError = true;
                    return;
                }
            }
            // Invoke PacketReceived Event
            ArduinoPacketReceived(this, EventArgs.Empty);
        }

        public double get_value(Arduino_Parameter_Code code)
        {
            return content_table[code].Value;
        }

        public int get_raw_value(Arduino_Parameter_Code code)
        {
            return content_table[code].Raw_Value;
        }

        public string get_unit(Arduino_Parameter_Code code)
        {
            return content_table[code].Unit;
        }
    }
}
