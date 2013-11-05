using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using WG3000_COMM.Core;
using System.Threading;
using System.IO;

namespace vms
{
    public partial class vmsn : ServiceBase
    {
        private wgWatchingService service = new wgWatchingService();
        private FileStream fs = new FileStream("d:\\vms.log", FileMode.Create);
        private StreamWriter sw;
        vms.RecordWebServiceImplService client = new vms.RecordWebServiceImplService();
        System.Collections.Hashtable map = new System.Collections.Hashtable();
        System.Collections.Hashtable ctrlmaps = new System.Collections.Hashtable();//存放控制器读卡器映射
       
        public vmsn()
        {
            InitializeComponent();
        }

        protected override void OnStart(string[] args)
        {
            sw = new StreamWriter(this.fs);

            if (File.Exists(@"maps.txt"))
            {
                string[] maps = File.ReadAllLines(@"maps.txt");
                foreach (string map in maps)
                {
                    string[] ms = map.Split('=');
                    ctrlmaps[ms[0]] = ms[1];
                }
            }
            else
            {
                sw.WriteLine("no maps.txt file");
            }

            Dictionary<int, wgMjController> controllers = new Dictionary<int, wgMjController>();

            string[] list = client.getController();

            foreach (string s in list)
            {
                string[] ss = s.Split(':');
                if (!ss[0].Trim().Equals(""))
                {
                    sw.WriteLine(s);
                    sw.Flush();
                
                    wgMjController ctrl = new wgMjController();
                    ctrl.IP = ss[0].Trim();
                    ctrl.ControllerSN = System.Int32.Parse(ss[1].Trim());
                    ctrl.PORT = 60000;
                    controllers.Add(ctrl.ControllerSN, ctrl);
                }
            }

            service.EventHandler += new OnEventHandler(evtNewInfoCallBack);
            service.WatchingController = controllers;
        }

        private void debug(string debug)
        {
            sw.WriteLine("debug");
            sw.Flush();
        }

        private void evtNewInfoCallBack(string recd)
        {
            wgMjControllerSwipeRecord rec = new wgMjControllerSwipeRecord(recd);
            onEvent(rec.ControllerSN, rec.CardID, rec.ReadDate, rec.ReaderNo);
        }

        private void onEvent(uint ControllerSN, uint CardID, DateTime ReadDate, byte ReaderNo) {
            if (CardID > 1)
            {
                DateTime dt;

                if (map.ContainsKey("" + CardID))
                {
                    dt = (DateTime)map["" + CardID];
                }
                else
                {
                    dt = DateTime.Now.AddDays(-1); //如果没有信息就相当于一小时前刷过卡
                }

                // 如果上次读卡时间 距离本次读卡时间超过5秒 则应该记录本次刷卡
                if (dt.AddSeconds(10) < ReadDate)
                {
                    string from = "" + ControllerSN + "/" + ReaderNo;
                    string[] to = getmap(from);

                    sw.WriteLine("F: Date=" + ReadDate.ToString("yyyy-MM-dd HH:mm:ss") + " CardId=" + CardID +
                             " ControllerSN=" + (int)ControllerSN + " ReaderNo=" + ReaderNo);
                    sw.WriteLine("T: Date=" + ReadDate.ToString("yyyy-MM-dd HH:mm:ss") + " CardId=" + CardID +
                        " ControllerSN=" + to[0] + " ReaderNo=" + to[1]);
                    sw.Flush();

                    if (client.upload(to[0], ReadDate.ToString("yyyy-MM-dd HH:mm:ss"), "" + CardID, to[1]).success && to.Length > 2)
                    {
                        sw.WriteLine("G: Date=" + ReadDate.ToString("yyyy-MM-dd HH:mm:ss") + " CardId=" + CardID +
                            " ControllerSN=" + (int)ControllerSN + " Gan=" + to[2]);
                        sw.Flush();
                        service.WatchingController[(int)ControllerSN].RemoteOpenDoorIP(Int32.Parse(to[2]));//抬杆
                    }
                }

                map["" + CardID] = ReadDate;
            }
            sw.WriteLine("");
            sw.Flush();
        }

        private string[] getmap(string from)
        {
            if (ctrlmaps.ContainsKey(from))
            {
                return ((string)ctrlmaps[from]).Split('/');
            }
            return from.Split('/');
        }

        protected override void OnStop()
        {
            sw.Close();
            fs.Close();
        }
    }
}
