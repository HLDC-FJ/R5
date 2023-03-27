using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO.Ports;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;
using System.Management;

using System.IO;
using System.Text;



namespace HRS_R5_V
{
    public partial class Form1 : Form
    {
        private System.Net.Sockets.UdpClient udpClient = null;

        private delegate void Delegate_write(string data);
        private delegate void Delegate_plot();
        //private delegate void Delegate_write();

        public string SerialData = "";          // シリアル受信データバッファ(処理用)
        public bool DoneFlg = false;

        public bool SensorFlg = false;          // センサ動作フラグ
        public bool ResetFlg = false;


        public string ConsoleData = "";
        public bool RxDataReceive = false;
        public int FallTimer = 0;


        public string TestData = "";

        public string OutputFolder = System.AppDomain.CurrentDomain.BaseDirectory;
        public string logFileName = "";

        public string RName = "RaderMaker";

        // HRS-R5 Multi-Vital
        public const string TPower = "30";      // ThresholdPower設定値
        public const string TpowerC = "18";     // ThresholdPowerCloser設定値
        public const string TSpeed = "0.1";     // ThresholdSpeedMS設定値

        public List<VitalItem> VitalData = new List<VitalItem>();



        public int LogED = 0;
        public int LogAL = 0;



        public class VitalItem
        {
            public string ID { get; set; }          // ID
            public string Status { get; set; }      // Status 0:Ready , 1:Moving , 2:Vital
            public string XPos { get; set; }        // x座標
            public string YPos { get; set; }        // y座標
            public string Street { get; set; }      // なぞ
            public string Time { get; set; }        // なぞ
            public string BR { get; set; }          // 呼吸数
            public string HR { get; set; }          // 心拍数
            public string BR_Plot { get; set; }     // 呼吸数 (Plot用)
            public string HR_Plot { get; set; }     // 心拍数 (Plot用)
            public string Power { get; set; }       // Power
            public bool Enable { get; set; }        // 有効/無効

            public string ZPos { get; set; }        // z座標
            public string Xvel { get; set; }        // Velocity X
            public string Yvel { get; set; }        // Velocity Y
            public string Zvel { get; set; }        // Velocity Z
        }

        public string[,] ParameterItem = new string[,]
        {
            {"RepeatPeriod" , "ObjectNumMax" , "BoundaryLeft" , "BoundaryRight" , "BoundaryLimit" , "BoundaryBias" ,
             "ThresholdPower" , "ThresholdPowerCloser" , "ThresholdSpeedMS" , "ThresholdHoldingTime" , "PreparationTime" },
            {"PointCloud","PeopleTracking","FDH","FDS","FDTH","FDXY","FDT","","","","" }
        };

        public int HumanCount = 0;

        private bool LiveFlg = false;
        private bool LiveER = false;
        private int LiveTime = 0;
        private VitalItem VitalOld = new VitalItem();




        #region UDP Task
        private void ReceiveCallback(IAsyncResult ar)
        {
            System.Net.Sockets.UdpClient udp =
                (System.Net.Sockets.UdpClient)ar.AsyncState;

            //非同期受信を終了する
            System.Net.IPEndPoint remoteEP = null;
            byte[] rcvBytes;
            try
            {
                rcvBytes = udp.EndReceive(ar, ref remoteEP);
            }
            catch (System.Net.Sockets.SocketException ex)
            {
                Console.WriteLine("受信エラー({0}/{1})",
                    ex.Message, ex.ErrorCode);
                return;
            }
            catch (ObjectDisposedException ex)
            {
                //すでに閉じている時は終了
                Console.WriteLine("Socketは閉じられています。");
                return;
            }

            //データを文字列に変換する
            string rcvMsg = System.Text.Encoding.UTF8.GetString(rcvBytes);

            if (radioButton1.Checked == true)
            {
                BeginInvoke(new Delegate_write(DataTask), new Object[] { rcvMsg });
            }
            else
            {
                BeginInvoke(new Delegate_write(DataTask_PT), new Object[] { rcvMsg });
            }

            if (SensorFlg == true)
            {
                ConsoleData = ConsoleData + rcvMsg;
                RxDataReceive = true;
            }

            udp.BeginReceive(ReceiveCallback, udp);
        }
        #endregion




        #region COMポート検出
        private void SerialPortSerch()
        {
            //string[] portlist = SerialPort.GetPortNames();
            string[] portlist = GetDeviceNames();
            this.comboBox1.Items.Clear();
            this.comboBox1.Text = "";
            if (portlist != null)
            {
                foreach (string PortName in portlist)
                {
                    comboBox1.Items.Add(PortName);
                }
                if (comboBox1.Items.Count > 0)
                {
                    comboBox1.SelectedIndex = 0;
                }
            }
        }


        public static string[] GetDeviceNames()
        {
            var deviceNameList = new System.Collections.ArrayList();
            var check = new System.Text.RegularExpressions.Regex("(COM[1-9][0-9]?[0-9]?)");

            ManagementClass mcPnPEntity = new ManagementClass("Win32_PnPEntity");
            ManagementObjectCollection manageObjCol = mcPnPEntity.GetInstances();

            //全てのPnPデバイスを探索しシリアル通信が行われるデバイスを随時追加する
            foreach (ManagementObject manageObj in manageObjCol)
            {
                //Nameプロパティを取得
                var namePropertyValue = manageObj.GetPropertyValue("Name");
                if (namePropertyValue == null)
                {
                    continue;
                }

                //Nameプロパティ文字列の一部が"(COM1)～(COM999)"と一致するときリストに追加"
                string name = namePropertyValue.ToString();
                if (check.IsMatch(name))
                {
                    // InziniousのModuleでは2系統のシリアルが見える。
                    // Standardの記載があるものだけを取得する
                    // 直接接続を行う場合は表示されないのでケアする必要あり。
                    if (SerialPort.GetPortNames().Length > 1)
                    {
                        if (name.Contains("Standard") == true)
                        {
                            string aaa = name.Substring(name.IndexOf('(')+1, (name.LastIndexOf(')') - name.IndexOf('('))-1);
                            deviceNameList.Add(aaa);
                        }
                    }
                    else
                    {
                        string aaa = name.Substring(name.IndexOf('(') + 1, (name.LastIndexOf(')') - name.IndexOf('(')) - 1);
                        deviceNameList.Add(aaa);
                    }
                }
            }

            //戻り値作成
            if (deviceNameList.Count > 0)
            {
                string[] deviceNames = new string[deviceNameList.Count];
                int index = 0;
                foreach (var name in deviceNameList)
                {
                    deviceNames[index++] = name.ToString();
                }
                return deviceNames;
            }
            else
            {
                return null;
            }
        }


        #endregion

        #region COM Port Open / Close
        /*!
         * COMポート：接続を開始します。
         */
        private bool SerialPortOpen(string portname)
        {
            try
            {
                serialPort1.PortName = portname;        // 通信ポート番号
                serialPort1.BaudRate = 921600;          // ボーレート
                serialPort1.DataBits = 8;               // Data bit 8
                serialPort1.StopBits = StopBits.One;    // Stop bit 1
                serialPort1.Parity = Parity.None;       // Parity NONE
                serialPort1.NewLine = "\n";             // 改行コード
                serialPort1.Open();

                return (true);
            }
            catch (Exception)
            {
                MessageBox.Show("Error!!\nポートが開かれてます");

                return (false);
            }
        }
        /****************************************************************************/
        /*!
         * COMポート：接続を終了します。
         */
        private bool SerialPortClose()
        {
            try
            {
                serialPort1.DiscardInBuffer();
                serialPort1.Close();
                return (true);
            }
            catch (Exception)
            {
                return (false);
            }
        }
        #endregion

        #region シリアルデータ出力処理
        // 文字列シリアル出力処理 ※やっつけ関数
        // HRS-R8A は文字列送信時、ウェイトを設けないと正常に受信出来ない様子。
        // 通常は "write"や"writeline"を用いる事で容易にデータ送信が可能だが、
        // 上記の事から1文字出力毎にwaitが必要。(約20ms程度)
        private void SerialDataOut(string dat)
        {
            if (radioButton1.Enabled == true)
            {
                serialPort1.WriteLine(dat);
            }
            else
            {
                int i = dat.Length;
                string buf = "";
                for (int j = 0; j < i; j++)
                {
                    buf = dat.Substring(j, 1);
                    serialPort1.Write(buf);
                    System.Threading.Thread.Sleep(25);
                }
                serialPort1.Write("\n");
                System.Threading.Thread.Sleep(25);
            }
        }
        #endregion

        private string VtoTemp(double vr)
        {
            string dat = "";
            double vv = 0;

            if (vr > 40)
            {
                vv = (vr + 579.5) / 18;
                dat = vv.ToString("0.00");
            }

            return dat;
        }


        #region 受信データ処理 [HRS-R5-V]
        private void DataTask(string work)
        {
            int DetectCount;
            int det;
            int DatLen;
            bool IDcheck = false;
            bool multiLine = false;

            string sbuf = "";

            if (SensorFlg == true)
            {
                if (work != "")
                {
                    string[] dat = work.Split('\n');
                    DatLen = dat.Length;

                    for (int i = 0; i < DatLen; i++)
                    {
                        if (LogAL == 1)
                        {
                            if (LogED == 1)
                            {
                                sbuf = dat[i];
                                logWrite(sbuf);
                            }
                        }

                        if (dat[i].IndexOf("#Y") >= 0)
                        {
                            det = dat[i].IndexOf("#Y");
                            work = dat[i].Substring(det);
                            string[] param = work.Split(',');

                            if (LogAL == 0)
                            {
                                if (LogED == 1)
                                {
                                    sbuf = work;
                                    logWrite(sbuf);
                                }
                            }

                            DetectCount = Convert.ToInt32(param[0].Substring(2, 1));        // 検出数

                            // データ有効フラグを初期化 (無効化)
                            for (int z = 0; z < VitalData.Count; z++)
                            {
                                VitalData[z].Enable = false;
                            }

                            for (int j = 0; j < DetectCount; j++)
                            {
                                if (VitalData.Count != -1)
                                {
                                    IDcheck = false;
                                    for (int ii = 0; ii < VitalData.Count; ii++)
                                    {
                                        if (VitalData[ii].ID == param[(j * 11) + 1])
                                        {
                                            VitalData[ii].Status = param[(j * 11) + 2];
                                            VitalData[ii].XPos = param[(j * 11) + 3];
                                            VitalData[ii].YPos = param[(j * 11) + 4];
                                            VitalData[ii].Street = param[(j * 11) + 5];
                                            VitalData[ii].Time = param[(j * 11) + 6];
                                            VitalData[ii].BR = param[(j * 11) + 7];
                                            VitalData[ii].HR = param[(j * 11) + 8];
                                            VitalData[ii].BR_Plot = param[(j * 11) + 9];
                                            VitalData[ii].HR_Plot = param[(j * 11) + 10];
                                            VitalData[ii].Power = param[(j * 11) + 11];
                                            VitalData[ii].Enable = true;
                                            IDcheck = true;
                                        }
                                    }
                                    if (IDcheck == false)
                                    {
                                        VitalData.Add(new VitalItem
                                        {
                                            // 新規IDデータ
                                            ID = param[(j * 11) + 1],
                                            Status = param[(j * 11) + 2],
                                            XPos = param[(j * 11) + 3],
                                            YPos = param[(j * 11) + 4],
                                            Street = param[(j * 11) + 5],
                                            Time = param[(j * 11) + 6],
                                            BR = param[(j * 11) + 7],
                                            HR = param[(j * 11) + 8],
                                            BR_Plot = param[(j * 11) + 9],
                                            HR_Plot = param[(j * 11) + 10],
                                            Power = param[(j * 11) + 11],
                                            Enable = true
                                        });
                                    }
                                }
                                else
                                {
                                    VitalData.Add(new VitalItem
                                    {
                                        // 新規IDデータ
                                        ID = param[(i * 11) + 1],
                                        Status = param[(i * 11) + 2],
                                        XPos = param[(i * 11) + 3],
                                        YPos = param[(i * 11) + 4],
                                        Street = param[(i * 11) + 5],
                                        Time = param[(i * 11) + 6],
                                        BR = param[(i * 11) + 7],
                                        HR = param[(i * 11) + 8],
                                        BR_Plot = param[(i * 11) + 9],
                                        HR_Plot = param[(i * 11) + 10],
                                        Power = param[(i * 11) + 11],
                                        Enable = true
                                    });
                                }
                            }

                            for (int ii = 0; ii < VitalData.Count; ii++)
                            {
                                if (VitalData[ii].Enable == false)
                                {
                                    VitalData.RemoveAt(ii);
                                }
                            }

                            // ID順にSortしておく ※保険
                            VitalData.Sort((a, b) => string.Compare(a.ID, b.ID));
                            DataPlot();
                        }
                        else
                        {
                            if (dat[i].IndexOf("#N") >= 0)
                            {
                                VitalData.Clear();
                                DataPlot();
                            }
                        }
                    }
                }

            }
            else
            {
                string[] tmp = new string[20];
                string[] dd = new string[20];
                string stp = "";
                int count = 0;
                int cct = 0;

                // センサー停止中 (設定系など)
                multiLine = false;
                if (work.IndexOf("\n") >= 0)
                {
                    dd = work.Split('\n');
                    cct = dd.Length;
                    multiLine = true;
                }
                else
                {
                    stp = work;
                }

                do
                {
                    if (multiLine == false)
                    {
                        tmp = work.Split(' ');
                    }
                    else
                    {
                        tmp = dd[count].Split(' ');
                        stp = dd[count];

                    }

                    // Multi Vital for HRS-R5-V
                    if (stp.IndexOf(ParameterItem[0, 0]) >= 0)
                    {
                        PtextBox1.Text = tmp[1];
                    }
                    else if (stp.IndexOf(ParameterItem[0, 1]) >= 0)
                    {
                        PtextBox2.Text = tmp[1];
                    }
                    else if (stp.IndexOf(ParameterItem[0, 2]) >= 0)
                    {
                        PtextBox3.Text = tmp[1];
                    }
                    else if (stp.IndexOf(ParameterItem[0, 3]) >= 0)
                    {
                        PtextBox4.Text = tmp[1];
                    }
                    else if (stp.IndexOf(ParameterItem[0, 4]) >= 0)
                    {
                        PtextBox5.Text = tmp[1];
                    }
                    else if (stp.IndexOf(ParameterItem[0, 5]) >= 0)
                    {
                        PtextBox6.Text = tmp[1];
                    }
                    else if (stp.IndexOf(ParameterItem[0, 7]) >= 0)
                    {
                        PtextBox8.Text = tmp[1];
                    }
                    else if (stp.IndexOf(ParameterItem[0, 6]) >= 0)
                    {
                        PtextBox7.Text = tmp[1];
                    }
                    else if (stp.IndexOf(ParameterItem[0, 8]) >= 0)
                    {
                        PtextBox9.Text = tmp[1];
                    }
                    else if (stp.IndexOf(ParameterItem[0, 9]) >= 0)
                    {
                        PtextBox10.Text = tmp[1];
                    }
                    else if (stp.IndexOf(ParameterItem[0, 10]) >= 0)
                    {
                        PtextBox11.Text = tmp[1];
                        PtextBox11B.Text = tmp[2];
                    }


                    if (multiLine == true)
                    {
                        count++;
                        if (count == cct)
                        {
                            multiLine = false;
                            break;
                        }
                    }
                    else
                    {
                        break;
                    }
                } while (true);

            }

            // Done Done Done
            if (work.IndexOf("Done") >= 0)
            {
                DoneFlg = true;
            }
        }
        #endregion

        #region 心拍・呼吸 データ表示
        private void DataPlot()
        {
            string dat = "";
            double vh = Convert.ToInt32(textBox6.Text);
            double vv = 0;
            string hr = "";

            switch (VitalData.Count)
            {
                case 0:
                    IDgroup1.Visible = false;
                    IDgroup2.Visible = false;
                    IDgroup3.Visible = false;
                    IDgroup4.Visible = false;
                    break;
                case 1:
                    IDgroup1.Visible = true;
                    IDgroup2.Visible = false;
                    IDgroup3.Visible = false;
                    IDgroup4.Visible = false;
                    break;
                case 2:
                    IDgroup1.Visible = true;
                    IDgroup2.Visible = true;
                    IDgroup3.Visible = false;
                    IDgroup4.Visible = false;
                    break;
                case 3:
                    IDgroup1.Visible = true;
                    IDgroup2.Visible = true;
                    IDgroup3.Visible = true;
                    IDgroup4.Visible = false;
                    break;
                case 4:
                    IDgroup1.Visible = true;
                    IDgroup2.Visible = true;
                    IDgroup3.Visible = true;
                    IDgroup4.Visible = true;
                    break;
            }

            // X-Y Plot
            chart1.Series[RName].Points.Clear();            // ポイントプロット初期化

            for (int i = 0; i < VitalData.Count; i++)
            {
                if (Convert.ToInt32(VitalData[i].Status) == 2)
                {
                    switch (i)
                    {
                        case 0:
                            IDgroup1.Text = "ID:" + VitalData[i].ID;
                            hr = "";
                            vv = Convert.ToDouble(VitalData[i].HR);
                            if (vv >= 40)
                            {
                                vv = vv - vh;
                                hr = vv.ToString();
                            }

                            ID1HR.Text = hr;
                            ID1BR.Text = VitalData[i].BR;

                            if (checkBox6.Checked == true)
                            {
                                ID1TP.Text = VtoTemp(vv);
                            }
                            break;
                        case 1:
                            IDgroup2.Text = "ID:" + VitalData[i].ID;
                            vv = Convert.ToDouble(VitalData[i].HR);
                            hr = "";
                            if (vv >= 40) {
                                vv = vv - vh;
                                hr = vv.ToString();
                            }

                            ID2HR.Text = hr;
                            ID2BR.Text = VitalData[i].BR;

                            if (checkBox6.Checked == true)
                            {
                                ID2TP.Text = VtoTemp(vv);
                            }
                            break;
                        case 2:
                            IDgroup3.Text = "ID:" + VitalData[i].ID;

                            hr = "";
                            vv = Convert.ToDouble(VitalData[i].HR);
                            if (vv >= 40)
                            {
                                vv = vv - vh;
                                hr = vv.ToString();
                            }

                            ID3HR.Text = hr;
                            ID3BR.Text = VitalData[i].BR;

                            if (checkBox6.Checked == true)
                            {
                                ID3TP.Text = VtoTemp(vv);
                            }
                            break;
                        case 3:
                            IDgroup4.Text = "ID:" + VitalData[i].ID;

                            hr = "";
                            vv = Convert.ToDouble(VitalData[i].HR);
                            if (vv >= 40)
                            {
                                vv = vv - vh;
                                hr = vv.ToString();
                            }

                            ID4HR.Text = hr;
                            ID4BR.Text = VitalData[i].BR;

                            if (checkBox6.Checked == true)
                            {
                                ID4TP.Text = VtoTemp(vv);
                            }
                            break;
                    }

                }
                else if (Convert.ToInt32(VitalData[i].Status) == 1)
                {
                    switch (i)
                    {
                        case 0:
                            IDgroup1.Text = "ID:" + VitalData[i].ID;
                            ID1HR.Text = "Moving";
                            ID1BR.Text = "";
                            ID1TP.Text = "";
                            break;
                        case 1:
                            IDgroup2.Text = "ID:" + VitalData[i].ID;
                            ID2HR.Text = "Moving";
                            ID2BR.Text = "";
                            ID2TP.Text = "";
                            break;
                        case 2:
                            IDgroup3.Text = "ID:" + VitalData[i].ID;
                            ID3HR.Text = "Moving";
                            ID3BR.Text = "";
                            ID3TP.Text = "";
                            break;
                        case 3:
                            IDgroup4.Text = "ID:" + VitalData[i].ID;
                            ID4HR.Text = "Moving";
                            ID4BR.Text = "";
                            ID4TP.Text = "";
                            break;
                    }
                }
                else if (Convert.ToInt32(VitalData[i].Status) == 0)
                {
                    switch (i)
                    {
                        case 0:
                            IDgroup1.Text = "ID:" + VitalData[i].ID;
                            ID1HR.Text = "Ready";
                            ID1BR.Text = "";
                            ID1TP.Text = "";
                            break;
                        case 1:
                            IDgroup2.Text = "ID:" + VitalData[i].ID;
                            ID2HR.Text = "Ready";
                            ID2BR.Text = "";
                            ID2TP.Text = "";
                            break;
                        case 2:
                            IDgroup3.Text = "ID:" + VitalData[i].ID;
                            ID3HR.Text = "Ready";
                            ID3BR.Text = "";
                            ID3TP.Text = "";
                            break;
                        case 3:
                            IDgroup4.Text = "ID:" + VitalData[i].ID;
                            ID4HR.Text = "Ready";
                            ID4BR.Text = "";
                            ID4TP.Text = "";
                            break;
                    }
                }


                // X-Y Plot
                double workx = Convert.ToDouble(VitalData[i].XPos);
                if (workx == 0) workx = 0.001;
                double worky = Convert.ToDouble(VitalData[i].YPos);
                DataPoint dp = new DataPoint(workx, worky);

                if (Convert.ToInt32(VitalData[i].Status) == 1)
                {
                    dp.Label = "ID:" + VitalData[i].ID + " / Moving";
                }
                else if (Convert.ToInt32(VitalData[i].Status) == 0)
                {
                    dp.Label = "ID:" + VitalData[i].ID + " / 測定中";
                }
                else
                {
                    if (checkBox2.Checked == true)
                    {
                        dp.Label = "ID:" + VitalData[i].ID + "\r\n心:" + VitalData[i].HR + " / 呼:" + VitalData[i].BR + "\r\n P:" + VitalData[i].Power;
                    }
                    else
                    {
                        dp.Label = "ID:" + VitalData[i].ID + "\r\n心:" + VitalData[i].HR + " / 呼:" + VitalData[i].BR;
                    }
                }

                chart1.Series[RName].Points.Add(dp);

                chart1.Series[RName].Points[i].MarkerSize = 20;
                chart1.Series[RName].Points[i].Font = new System.Drawing.Font("Arial", 16F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));

                switch (i)
                {
                    case 0:
                        chart1.Series[RName].Points[0].Color = Color.WhiteSmoke;
                        break;
                    case 1:
                        chart1.Series[RName].Points[1].Color = Color.DeepSkyBlue;
                        break;
                    case 2:
                        chart1.Series[RName].Points[2].Color = Color.MediumSeaGreen;
                        break;
                    case 3:
                        chart1.Series[RName].Points[3].Color = Color.Yellow;
                        break;
                }
            }

        }
        #endregion


        #region Start Init Task
        private void StartInitTask()
        {
            if (VitalData.Count > 0)
            {
                VitalData.RemoveRange(0, VitalData.Count);
            }

            RadarChart();

            textBox1.Clear();
            this.groupBox1.Enabled = false;
            this.groupBox2.Enabled = false;
            this.tabControl1.SelectedIndex = 1;

            SensorFlg = true;

            if (radioButton2.Checked == true)
            {
                this.label1.Text = "";
                this.label1.Visible = true;
            }
            else
            {
                this.label1.Visible = false;
            }
        }
        #endregion


        #region パラメータ値チェック処理
        private bool ParamCheck(string[] Dat, int No)
        {
            bool flg = false;
            double work;
            try
            {
                work = Convert.ToDouble(Dat[(No * 12) + 2]);                // ID
                if ((work >= 0) && (work < 99))
                {
                    work = Convert.ToDouble(Dat[(No * 12) + 3]);            // x
                    if ((work > -6) && (work < 6))
                    {
                        work = Convert.ToDouble(Dat[(No * 12) + 4]);        // y
                        if ((work > -6) && (work < 6))
                        {
                            work = Convert.ToDouble(Dat[(No * 12) + 5]);    // z
                            if ((work >= -2) && (work <= 10))
                            {
                                flg = true;
                            }
                        }

                    }
                }
            }
            catch (Exception ex)
            {
                flg = false;
            }
            return flg;
        }
        #endregion

        private bool LiveCheck(string[] Dat, int No, int sel)
        {
            bool flg = false;

            if (LiveFlg == false)
            {
                if (VitalData[sel].XPos == Dat[(No * 12) + 3])
                {
                    if (VitalData[sel].YPos == Dat[(No * 12) + 4])
                    {
                        if (VitalData[sel].ZPos == Dat[(No * 12) + 5])
                        {
                            VitalOld.XPos = Dat[(No * 12) + 3];
                            VitalOld.YPos = Dat[(No * 12) + 4];
                            VitalOld.ZPos = Dat[(No * 12) + 5];
                            LiveFlg = true;
                            LiveER = false;
                            LiveTime = 0;
                            flg = true;
                        }
                    }
                }
            }
            else
            {
                if (VitalOld.XPos == Dat[(No * 12) + 3])
                {
                    if (VitalOld.YPos == Dat[(No * 12) + 4])
                    {
                        if (VitalOld.ZPos == Dat[(No * 12) + 5])
                        {
                            flg = true;
                        }
                    }
                }
            }

            if (flg == false)
            {
                LiveFlg = false;
            }
            return (flg);
        }



        #region 人数データ表示
        private void DataPlot_PT()
        {
            string dat = "";
            string s1, s2;


            chart1.Series[RName].Points.Clear();

            for (int i = 0; i < VitalData.Count; i++)
            {
                double workx = Convert.ToDouble(VitalData[i].XPos);
                if (workx == 0) workx = 0.001;
                double worky = Convert.ToDouble(VitalData[i].YPos);
                DataPoint dp = new DataPoint(workx, worky);
                s1 = "ID:" + VitalData[i].ID + "\r\n X:" + VitalData[i].XPos + " Y:" + VitalData[i].YPos;
                s2 = "\r\nXvel:" + VitalData[i].Xvel + " Yvel:" + VitalData[i].Yvel;

                // Z Position
                if (checkBox4.Checked == true)
                {
                    s1 = s1 + " Z:" + VitalData[i].ZPos;
                }

                // Velocity
                if (checkBox5.Checked == true)
                {
                    s1 = s1 + s2;
                }

                dp.Label = s1;

                chart1.Series[RName].Points.Add(dp);
                chart1.Series[RName].Points[i].MarkerSize = 25;
                chart1.Series[RName].SmartLabelStyle.CalloutLineColor = Color.White;
                chart1.Series[RName].Points[i].Font = new System.Drawing.Font("Arial", 16F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));

                switch (i)
                {
                    case 0:
                        chart1.Series[RName].Points[0].Color = Color.White;
                        break;
                    case 1:
                        chart1.Series[RName].Points[1].Color = Color.DeepSkyBlue;
                        break;
                    case 2:
                        chart1.Series[RName].Points[2].Color = Color.MediumSeaGreen;
                        break;
                    case 3:
                        chart1.Series[RName].Points[3].Color = Color.Yellow;
                        break;
                    case 4:
                        chart1.Series[RName].Points[3].Color = Color.Red;
                        break;
                    case 5:
                        chart1.Series[RName].Points[3].Color = Color.PaleGreen;
                        break;
                    case 6:
                        chart1.Series[RName].Points[3].Color = Color.Green;
                        break;
                    case 7:
                        chart1.Series[RName].Points[3].Color = Color.Aqua;
                        break;
                    case 8:
                        chart1.Series[RName].Points[3].Color = Color.Pink;
                        break;
                    case 9:
                        chart1.Series[RName].Points[3].Color = Color.Violet;
                        break;
                    case 10:
                        chart1.Series[RName].Points[3].Color = Color.SeaShell;
                        break;
                    default:
                        chart1.Series[RName].Points[3].Color = Color.BlueViolet;
                        break;
                }
            }
        }
        #endregion

        #region 人数カウント [HRS-R8A]
        private void DataTask_PT(string work)
        {
            int ct, det;

            int DatLen;
            bool IDcheck = false;
            bool multiLine = false;
            string sbuf = "";

            try
            {
                /*
                 * ここから本番処理
                 */
                if (SensorFlg == true)
                {
                    if (work != "")
                    {
                        string[] dat = work.Split('\n');                            // 改行コードにてデータ分割
                        DatLen = dat.Length;                                        // データ分割数取得

                        for (int i = 0; i < DatLen; i++)
                        {
                            if (LogAL == 1)
                            {
                                if (LogED == 1)
                                {
                                    sbuf = dat[i];
                                    logWrite(sbuf);
                                }
                            }

                            if (dat[i].IndexOf("#T") >= 0)
                            {
                                // "T" データ処理
                                det = dat[i].IndexOf("#T");
                                work = dat[i].Substring(det);
                                string[] param = work.Split(',');

                                if (LogAL == 0)
                                {
                                    if (LogED == 1)
                                    {
                                        sbuf = work;
                                        logWrite(sbuf);
                                    }
                                }

                                ct = Convert.ToInt32(param[1]);
                                HumanCount = ct;

                                // データ長確認。　※人数に対してデータ長がおかしい場合は処理を行わない。
                                // 単に馬鹿避け。
                                if (param.Length != (HumanCount * 12) + 2)
                                {
                                    break;
                                }

                                if (ct == 0)
                                {
                                    // 検出数が ゼロ だった場合、データ初期化
                                    VitalData.Clear();
                                }
                                else
                                {
                                    for (int z = 0; z < VitalData.Count; z++)
                                    {
                                        VitalData[z].Enable = false;
                                    }


                                    for (int j = 0; j < ct; j++)
                                    {
                                        if (VitalData.Count != -1)
                                        {
                                            // Vital Dataあり
                                            IDcheck = false;
                                            for (int ii = 0; ii < VitalData.Count; ii++)
                                            {
                                                if (VitalData[ii].ID == param[(j * 12) + 2])
                                                {
                                                    if (true == ParamCheck(param, j))
                                                    {
                                                        VitalData[ii].ID = param[(j * 12) + 2];         // ID
                                                        VitalData[ii].XPos = param[(j * 12) + 3];       // X
                                                        VitalData[ii].YPos = param[(j * 12) + 4];       // Y
                                                        VitalData[ii].ZPos = param[(j * 12) + 5];       // Z
                                                        VitalData[ii].Xvel = param[(j * 12) + 6];       // Vel X
                                                        VitalData[ii].Yvel = param[(j * 12) + 7];       // vel Y
                                                        VitalData[ii].Enable = true;
                                                        IDcheck = true;
                                                    }
                                                }
                                            }

                                            if (IDcheck == false)
                                            {
                                                if (true == ParamCheck(param, j))
                                                {
                                                    VitalData.Add(new VitalItem
                                                    {
                                                        ID = param[(j * 12) + 2],
                                                        XPos = param[(j * 12) + 3],
                                                        YPos = param[(j * 12) + 4],
                                                        ZPos = param[(j * 12) + 5],
                                                        Xvel = param[(j * 12) + 6],
                                                        Yvel = param[(j * 12) + 7],
                                                        Enable = true
                                                    });
                                                }
                                            }
                                        }
                                        else
                                        {
                                            if (true == ParamCheck(param, 0))
                                            {
                                                // Vital Data なし
                                                VitalData.Add(new VitalItem
                                                {
                                                    ID = param[2],
                                                    XPos = param[3],
                                                    YPos = param[4],
                                                    ZPos = param[5],
                                                    Xvel = param[6],
                                                    Yvel = param[7],
                                                    Enable = true
                                                });
                                            }
                                        }
                                    }
                                    for (int ii = 0; ii < VitalData.Count; ii++)
                                    {
                                        if (VitalData[ii].Enable == false)
                                        {
                                            VitalData.RemoveAt(ii);
                                        }
                                    }

                                    // ID順にSortしておく ※保険
                                    VitalData.Sort((a, b) => string.Compare(a.ID, b.ID));
                                    HumanCount = VitalData.Count;
                                }
                            }
                            else
                            {
                                if (dat[i].IndexOf("#F") >= 0)
                                {
                                    if (checkBox3.Checked == true)
                                    {
                                        // "F" データ処理
                                        string[] fall = dat[i].Split(',');
                                        if (Convert.ToInt32(fall[1]) != 0)
                                        {
                                            this.pictureBox_fall.Visible = true;    // 転んでいる絵表示
                                            FallTimer = (10 * 5) * 1;               // 0.5秒表示 (10ms x10 x5 x1)
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                else
                {
                    string[] tmp = new string[20];
                    string[] dd = new string[20];
                    string stp = "";
                    int count = 0;
                    int cct = 0;
                    int dl = 0;

                    // センサー停止中 (設定系など)
                    multiLine = false;
                    if (work.IndexOf("\n") >= 0)
                    {
                        dd = work.Split('\n');
                        cct = dd.Length;
                        multiLine = true;
                    }
                    else
                    {
                        stp = work;
                    }

                    do
                    {
                        if (multiLine == false)
                        {
                            tmp = work.Split(' ');
                        }
                        else
                        {
                            tmp = dd[count].Split(' ');
                            stp = dd[count];

                        }

                        if (tmp.Length > 2)
                        {
                            dl = tmp.Length - 1;
                        }
                        else
                        {
                            dl = 1;
                        }

                        if (stp.IndexOf(ParameterItem[1, 0]) >= 0)
                        {
                            PtextBox1.Text = tmp[dl];
                        }
                        else if (stp.IndexOf(ParameterItem[1, 1]) >= 0)
                        {
                            PtextBox2.Text = tmp[dl];
                        }
                        else if (stp.IndexOf(ParameterItem[1, 2]) >= 0)
                        {
                            PtextBox3.Text = tmp[dl];
                        }
                        else if (stp.IndexOf(ParameterItem[1, 3]) >= 0)
                        {
                            PtextBox4.Text = tmp[dl];
                        }
                        else if (stp.IndexOf(ParameterItem[1, 4]) >= 0)
                        {
                            PtextBox5.Text = tmp[dl];
                        }
                        else if (stp.IndexOf(ParameterItem[1, 5]) >= 0)
                        {
                            PtextBox6.Text = tmp[dl];
                        }
                        else if (stp.IndexOf(ParameterItem[1, 6]) >= 0)
                        {
                            PtextBox7.Text = tmp[dl];
                        }

                        if (multiLine == true)
                        {
                            count++;
                            if (count == cct)
                            {
                                multiLine = false;
                                break;
                            }
                        }
                        else
                        {
                            break;
                        }
                    } while (true);

                }

                // Done Done Done
                if (work.IndexOf("Done") >= 0)
                {
                    DoneFlg = true;
                }
            }
            catch (Exception ex)
            {
                VitalData.RemoveAt(VitalData.Count - 1);
            }
        }
        #endregion






        #region パラメータ読出し
        private void ParameterReadTask(int No)
        {
            string com = "";
            bool flg = true;
            int sel = 0;

            if (radioButton1.Checked == true)
            {
                sel = 0;
            }
            else
            {
                sel = 1;
            }

            switch (No)
            {
                case 1:
                    com = ParameterItem[sel, 0];
                    break;
                case 2:
                    com = ParameterItem[sel, 1];
                    break;
                case 3:
                    com = ParameterItem[sel, 2];
                    break;
                case 4:
                    com = ParameterItem[sel, 3];
                    break;
                case 5:
                    com = ParameterItem[sel, 4];
                    break;
                case 6:
                    com = ParameterItem[sel, 5];
                    break;
                case 7:
                    com = ParameterItem[sel, 6];
                    break;
                case 8:
                    com = ParameterItem[sel, 7];
                    break;
                case 9:
                    com = ParameterItem[sel, 8];
                    break;
                case 10:
                    com = ParameterItem[sel, 9];
                    break;
                case 11:
                    com = ParameterItem[sel, 10];
                    break;
                default:
                    flg = false;
                    break;
            }

            if (flg == true)
            {
                SerialDataOut(com);
            }
        }
        #endregion

        #region パラメータ設定
        private void ParameterSetTask(int No)
        {
            string com = "";
            bool flg = true;
            int sel = 0;

            if (radioButton1.Checked == true)
            {
                sel = 0;
            }
            else
            {
                sel = 1;
            }

            switch (No)
            {
                case 1:
                    com = ParameterItem[sel, 0] + " " + PtextBox1.Text;
                    break;
                case 2:
                    com = ParameterItem[sel, 1] + " " + PtextBox2.Text;
                    break;
                case 3:
                    com = ParameterItem[sel, 2] + " " + PtextBox3.Text;
                    break;
                case 4:
                    com = ParameterItem[sel, 3] + " " + PtextBox4.Text;
                    break;
                case 5:
                    com = ParameterItem[sel, 4] + " " + PtextBox5.Text;
                    break;
                case 6:
                    com = ParameterItem[sel, 5] + " " + PtextBox6.Text;
                    break;
                case 7:
                    com = ParameterItem[sel, 6] + " " + PtextBox7.Text;
                    break;
                case 8:
                    if (sel == 0)
                    {
                        com = ParameterItem[sel, 7] + " " + PtextBox8.Text;
                    }
                    break;
                case 9:
                    if (sel == 0)
                    {
                        com = ParameterItem[sel, 8] + " " + PtextBox9.Text;
                    }
                    break;
                case 10:
                    if (sel == 0)
                    {
                        com = ParameterItem[sel, 9] + " " + PtextBox10.Text;
                    }
                    break;
                case 11:
                    if (sel == 0)
                    {
                        com = ParameterItem[sel, 10] + " " + PtextBox11.Text + " " + PtextBox11B.Text;
                    }
                    break;
                default:
                    flg = false;
                    break;
            }

            if (flg == true)
            {
                SerialDataOut(com);
            }
        }
        #endregion




        #region シリアルデータ受信イベント
        private void serialPort1_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            int det = 0;
            string work = "";
            string data = serialPort1.ReadExisting();

            SerialData = SerialData + data;                             // 受信データ結合

            /*
             * 受信データが空でない時、最後の"LF"までを抽出。
             * 最後のLF以降を次の処理に回すようにバッファ処理を行う。
             * 
             * 1.改行コード(LF)が存在している。
             * 2.改行コードまでを作業用に取得
             * 3.改行コード以降を次の処理用に残す。
             * ※データ長が0の場合は初期化する。
             */
            if (SerialData != "")
            {
                det = SerialData.LastIndexOf("\n");
                if (det != -1)
                {
                    work = SerialData.Substring(0, det);

                    if (det < SerialData.Length)
                    {
                        SerialData = SerialData.Substring(det + 1);
                    }
                    else
                    {
                        SerialData = "";
                    }
                }
            }


            if (radioButton1.Checked == true)
            {
                BeginInvoke(new Delegate_write(DataTask), new Object[] { work });
            }
            else
            {
                BeginInvoke(new Delegate_write(DataTask_PT), new Object[] { work });
            }

            if (SensorFlg == true)
            {
                ConsoleData = ConsoleData + data;
                RxDataReceive = true;
            }
        }
        #endregion


        #region Item 表示
        private void ItemListSet()
        {
            PtextBox1.Text = "";
            PtextBox2.Text = "";
            PtextBox3.Text = "";
            PtextBox4.Text = "";
            PtextBox5.Text = "";
            PtextBox6.Text = "";
            PtextBox7.Text = "";
            PtextBox8.Text = "";
            PtextBox9.Text = "";
            PtextBox10.Text = "";
            PtextBox11.Text = "";
            PtextBox11B.Text = "";

            if (radioButton1.Checked == true)
            {
                // Vital
                Plabel1.Text = "RepeatPeriod";
                Plabel2.Text = "ObjectNumMax (1～4)";
                Plabel3.Text = "Boundary Left(-5～0)";
                Plabel4.Text = "Boundary Right(0～5)";
                Plabel5.Text = "Boundary Limit(0.1～6)";
                Plabel6.Text = "Boundary Bias(0.1～6)";
                Plabel7.Text = "ThresholdPower(1～500)";
                Plabel8.Text = "ThresholdPowerCloser(1～500)";
                Plabel9.Text = "ThresholdSpeed(0.111～1)";
                Plabel10.Text = "ThresholdHoldingTime(5k～50k)";
                Plabel11.Text = "PreparationTime(3k～60k)";
            }
            else
            {
                // People Count
                Plabel1.Text = "PointCloud";
                Plabel2.Text = "PeopleTracking";
                Plabel3.Text = "FDH";
                Plabel4.Text = "FDS";
                Plabel5.Text = "FDTH";
                Plabel6.Text = "FDXY";
                Plabel7.Text = "FDT";
                Plabel8.Text = "";
                Plabel9.Text = "";
                Plabel10.Text = "";
                Plabel11.Text = "";

            }
        }
        #endregion

        #region ### Chart Init ###
        private void RadarChart()
        {
            double x = Convert.ToDouble(textBox2.Text);
            double y = Convert.ToDouble(textBox3.Text);

            double mez = Convert.ToDouble(textBox4.Text);

            int tb2x = tabPage2.Size.Width;
            int tb2y = tabPage2.Size.Height;

            chart1.Series.Clear();              // メンバークリア
            chart1.ChartAreas.Clear();          // 描画エリアクリア
            chart1.Legends.Clear();             // 凡例非表示

            chart1.Series.Add(RName);
            chart1.ChartAreas.Add(RName);

            chart1.Series[RName].ChartType = SeriesChartType.Point;
            chart1.Series[RName].MarkerSize = 12;
            chart1.Series[RName].MarkerStyle = MarkerStyle.Circle;


            // x軸 , y軸 目盛表示設定
            chart1.ChartAreas[RName].AxisX.LabelStyle.Enabled = true;
            chart1.ChartAreas[RName].AxisY.LabelStyle.Enabled = true;

            // 余白設定 (true:有り / false:無し)
            chart1.ChartAreas[RName].AxisX.IsMarginVisible = false;
            chart1.ChartAreas[RName].AxisY.IsMarginVisible = false;

            if (radioButton1.Checked == true)
            {
                //chart1.Width = 431;
                chart1.Width = tb2x - 220;

                // Y座標
                chart1.ChartAreas[RName].AxisY.Maximum = y;
                chart1.ChartAreas[RName].AxisY.IsReversed = true;           // Y軸 反転表示

                // X座標
                chart1.ChartAreas[RName].AxisX.Minimum = -x;
                chart1.ChartAreas[RName].AxisX.Maximum = x;
                chart1.ChartAreas[RName].AxisX.IsReversed = true;

                if (checkBox6.Checked == true)
                {
                    // 体温表示
                    label_TP_A.Visible = true;
                    label_TP_B.Visible = true;
                    label_TP_C.Visible = true;
                    label_TP_D.Visible = true;
                    ID1TP.Visible = true;
                    ID2TP.Visible = true;
                    ID3TP.Visible = true;
                    ID4TP.Visible = true;
                } else
                {
                    label_TP_A.Visible = false;
                    label_TP_B.Visible = false;
                    label_TP_C.Visible = false;
                    label_TP_D.Visible = false;
                    ID1TP.Visible = false;
                    ID2TP.Visible = false;
                    ID3TP.Visible = false;
                    ID4TP.Visible = false;
                }
            }
            else
            {
                //chart1.Width = 643;
                chart1.Width = tb2x - 8;

                // Y座標
                chart1.ChartAreas[RName].AxisY.Maximum = 5;
                chart1.ChartAreas[RName].AxisY.Minimum = -5;
                //chart1.ChartAreas[RName].AxisY.IsReversed = true;

                // X座標
                chart1.ChartAreas[RName].AxisX.Minimum = -5;
                chart1.ChartAreas[RName].AxisX.Maximum = 5;
                //chart1.ChartAreas[RName].AxisX.IsReversed = true;
            }



            // x,y座標　クロス位置設定
            chart1.ChartAreas[RName].AxisX.Crossing = 0;
            chart1.ChartAreas[RName].AxisY.Crossing = 0;

            // 目盛インタバル値設定
            chart1.ChartAreas[RName].AxisX.Interval = mez;
            chart1.ChartAreas[RName].AxisY.Interval = mez;


            // Y軸グリッド線表示設定
            chart1.ChartAreas[RName].AxisY.MajorGrid.Enabled = true;
            chart1.ChartAreas[RName].AxisY.MinorGrid.Enabled = false;

            // X軸グリッド線表示設定
            chart1.ChartAreas[RName].AxisX.MajorGrid.Enabled = true;
            chart1.ChartAreas[RName].AxisX.MinorGrid.Enabled = false;

            chart1.Series[RName].IsXValueIndexed = false;


            chart1.Series[RName].Points.AddXY(0.00001, 0);
            chart1.Series[RName].Font = new Font("Arial", 8);


            chart1.Series[RName].LabelForeColor = Color.Orange;                     // ラベル色

            chart1.ChartAreas[RName].AxisX.LineColor = Color.White;                 // X軸　ベース色
            chart1.ChartAreas[RName].AxisX.MajorGrid.LineColor = Color.White;       // X軸色

            chart1.ChartAreas[RName].AxisY.LineColor = Color.White;                 // Y軸　ベース色
            chart1.ChartAreas[RName].AxisY.MajorGrid.LineColor = Color.White;       // Y軸色

            chart1.ChartAreas[RName].AxisX.LabelStyle.ForeColor = Color.White;      // X軸 文字色
            chart1.ChartAreas[RName].AxisY.LabelStyle.ForeColor = Color.White;      // Y軸 文字色

            chart1.ChartAreas[RName].BackColor = Color.Black;                       // 背景色
        }
        #endregion

        #region Radar Parameter Init
        private void RadarParameterInit()
        {
            SerialDataOut("stop");
            SerialDataOut("stop");

/*
            if (radioButton1.Checked == true)
            {
                // HRS-R5-V for Exhibition
                SerialDataOut("ThresholdPower " + TPower);
                SerialDataOut("ThresholdPowerCloser " + TpowerC);
                SerialDataOut("ThresholdSpeedMS " + TSpeed);
            }
            else
            {
                // HRS-R8A-PT
            }
*/
        }
        #endregion


        public Form1()
        {
            InitializeComponent();
        }





        private void Form1_Load(object sender, EventArgs e)
        {
            SerialPortSerch();
            textBox1.Clear();
            ItemListSet();
        }

        private void radioButton1_CheckedChanged(object sender, EventArgs e)
        {
            ItemListSet();
        }


        // ボタン処理関係

        #region COMポート再検索
        private void button2_Click(object sender, EventArgs e)
        {
            SerialPortSerch();
        }
        #endregion

        #region COMポート接続/切断 ボタン処理
        private void button1_Click(object sender, EventArgs e)
        {
            if (serialPort1.IsOpen == false)
            {
                // ポート開く
                SerialPortOpen(comboBox1.Text);
                button1.Text = "切断";
                serialPort1.DiscardInBuffer();      // シリアル通信用バッファ初期化
                textBox1.Clear();
                button3.Enabled = true;
                button4.Enabled = true;
                button5.Enabled = true;
                groupBox2.Enabled = true;
                SerialDataOut("stop");
            }
            else
            {
                // ポート閉じる
                SerialPortClose();
                if (serialPort1.IsOpen == false)
                {
                    button1.Text = "接続";
                    button3.Enabled = false;
                    button4.Enabled = false;
                    button5.Enabled = false;
                    groupBox2.Enabled = false;
                }
            }
        }
        #endregion

        #region "START"ボタン処理
        private void button3_Click(object sender, EventArgs e)
        {
            SensorFlg = true;
            RadarParameterInit();               // Radar 初期値設定

            SerialDataOut("reset");

            HumanCount = 0;
            SerialData = "";
            ConsoleData = "";

            StartInitTask();
        }
        #endregion

        #region "STOP"ボタン処理
        private void button4_Click(object sender, EventArgs e)
        {
            SensorFlg = false;
            SerialDataOut("STOP");
            SerialData = "";
            this.groupBox1.Enabled = true;
            this.groupBox2.Enabled = true;
        }
        #endregion

        #region "RESET"ボタン処理
        private void button5_Click(object sender, EventArgs e)
        {
            serialPort1.DiscardInBuffer();      // シリアル通信用バッファ初期化
            ResetFlg = true;
            SensorFlg = false;
            DoneFlg = false;
            SerialDataOut("RESET");

            button4.PerformClick();             // "STOP"ボタン呼出
        }
        #endregion


        #region パラメータ 全読出し
        private void button_AllRead_Click(object sender, EventArgs e)
        {
            for (int i = 1; i < 12; i++)
            {
                ParameterReadTask(i);
            }
        }
        #endregion

        #region console log
        private void button6_Click(object sender, EventArgs e)
        {
            Clipboard.SetDataObject(textBox1.Text, true);
        }

        private void button7_Click(object sender, EventArgs e)
        {
            textBox1.Clear();
        }
        #endregion

        #region パラメータ読出し
        private void PRead1_Click(object sender, EventArgs e)
        {
            ParameterReadTask(1);
        }

        private void PRead2_Click(object sender, EventArgs e)
        {
            ParameterReadTask(2);
        }

        private void PRead3_Click(object sender, EventArgs e)
        {
            ParameterReadTask(3);
        }

        private void PRead4_Click(object sender, EventArgs e)
        {
            ParameterReadTask(4);
        }

        private void PRead5_Click(object sender, EventArgs e)
        {
            ParameterReadTask(5);
        }

        private void PRead6_Click(object sender, EventArgs e)
        {
            ParameterReadTask(6);
        }

        private void PRead7_Click(object sender, EventArgs e)
        {
            ParameterReadTask(7);
        }

        private void PRead8_Click(object sender, EventArgs e)
        {
            ParameterReadTask(8);
        }

        private void PRead9_Click(object sender, EventArgs e)
        {
            ParameterReadTask(9);
        }

        private void PRead10_Click(object sender, EventArgs e)
        {
            ParameterReadTask(10);
        }

        private void PRead11_Click(object sender, EventArgs e)
        {
            ParameterReadTask(11);
        }
        #endregion

        #region パラメータ設定
        private void PSet1_Click(object sender, EventArgs e)
        {
            ParameterSetTask(1);
        }

        private void PSet2_Click(object sender, EventArgs e)
        {
            ParameterSetTask(2);
        }

        private void PSet3_Click(object sender, EventArgs e)
        {
            ParameterSetTask(3);
        }

        private void PSet4_Click(object sender, EventArgs e)
        {
            ParameterSetTask(4);
        }

        private void PSet5_Click(object sender, EventArgs e)
        {
            ParameterSetTask(5);
        }

        private void PSet6_Click(object sender, EventArgs e)
        {
            ParameterSetTask(6);
        }

        private void PSet7_Click(object sender, EventArgs e)
        {
            ParameterSetTask(7);
        }

        private void PSet8_Click(object sender, EventArgs e)
        {
            ParameterSetTask(8);
        }

        private void PSet9_Click(object sender, EventArgs e)
        {
            ParameterSetTask(9);
        }

        private void PSet10_Click(object sender, EventArgs e)
        {
            ParameterSetTask(10);
        }

        private void PSet11_Click(object sender, EventArgs e)
        {
            ParameterSetTask(11);
        }



        private void button8_Click(object sender, EventArgs e)
        {
            SerialDataOut("SAVE");
        }



        #endregion

        private void timer1_Tick(object sender, EventArgs e)
        {
            string data = "";

            if (RxDataReceive == true)
            {
                // Text Boxへ表示
                // 改行コードを CRLF とする必要あり
                data = ConsoleData.Replace("\n", "\r\n");
                ConsoleData = "";
                textBox1.AppendText(data);
                data = "";
                if (checkBox1.Checked == true)
                {
                    if (textBox1.Text.Length > 30000)
                    {
                        textBox1.Clear();
                    }
                }

                if (radioButton1.Checked == true)
                {
                    // Vital
                    DataPlot();
                }
                else
                {
                    // Human Count
                    label1.Text = HumanCount.ToString();
                    DataPlot_PT();
                }
            }

            if (FallTimer > 0)
            {
                FallTimer--;
                if (FallTimer == 0)
                {
                    this.pictureBox_fall.Visible = false;
                }
            }

            RxDataReceive = false;

        }



        private void button9_Click(object sender, EventArgs e)
        {
            if (udpClient != null)
            {
                udpClient.Close();
                udpClient.Dispose();
                udpClient = null;
                return;
            }

            //radioButton2.Checked = true;
            StartInitTask();

            //UdpClientを作成し、ポート番号(4001)にバインドする
            System.Net.IPEndPoint localEP = new System.Net.IPEndPoint(
                System.Net.IPAddress.Any, int.Parse(textBox5.Text));

            udpClient = new System.Net.Sockets.UdpClient(localEP);

            //非同期的なデータ受信を開始する
            udpClient.BeginReceive(ReceiveCallback, udpClient);
        }





        #region ログ書き出し
        private void logWrite(string dat)
        {
            FileStream FS_log = new FileStream(logFileName, FileMode.Append, FileAccess.Write);
            StreamWriter SR_log = new StreamWriter(FS_log, Encoding.GetEncoding("UTF-8"));

            SR_log.WriteLine(dat);

            SR_log.Close();
            FS_log.Close();
        }
        #endregion

        #region ログ出力フォルダ設定
        private void outputFolderSetToolStripMenuItem_Click(object sender, EventArgs e)
        {
            FolderBrowserDialog fol = new FolderBrowserDialog();
            fol.Description = "log出力先フォルダを指定してください";
            fol.RootFolder = Environment.SpecialFolder.Desktop;
            if (OutputFolder != "")
            {
                fol.SelectedPath = OutputFolder;
            }
            fol.ShowNewFolderButton = true;

            if (fol.ShowDialog(this) == DialogResult.OK)
            {
                OutputFolder = fol.SelectedPath;
            }
        }
        #endregion

        #region ログ出力 有効/無効
        private void enableDisableToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (enableDisableToolStripMenuItem.Checked == false)
            {
                enableDisableToolStripMenuItem.Checked = true;
                LogED = 1;
                string d = DateTime.Now.ToString("yyyy/MM/dd,HH:mm");
                d = d.Replace("/", "");
                d = d.Replace(":", "");
                d = d.Replace(",", "_");
                logFileName = OutputFolder + "HRS_" + d + ".csv";
                logWrite("");
                //logWrite("ID,X,Y,Z,VelX,VelY");
            }
            else
            {
                enableDisableToolStripMenuItem.Checked = false;
                LogED = 0;
            }
        }



        #endregion

        private void singleALLToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (singleALLToolStripMenuItem.Checked == false)
            {
                singleALLToolStripMenuItem.Checked = true;
                LogAL = 1;
            }
            else
            {
                singleALLToolStripMenuItem.Checked = false;
                LogAL = 0;
            }
        }
    }
}
