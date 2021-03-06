﻿using Gfx.GfxDataManagerServer;
using GfxCommonInterfaces;
using GfxServiceContractClient;
using GfxServiceContractTaskExcute;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using LogHelper;

namespace KEDAClient
{
    /// <summary>
    /// 业务逻辑
    /// </summary>
    public class F_Logic
    {
        /// <summary>
        /// 窑尾PLC
        /// </summary>
        F_PLCLine _plcEnd = new F_PLCLine("PLC0000002");

        /// <summary>
        /// 窑头PLC
        /// </summary>
        F_PLCLine _plcHead = new F_PLCLine("PLC0000001");

        /// <summary>
        /// 窑头PLC机械手
        /// </summary>
        F_PLCLine _plcHeadHolder = new F_PLCLine("PLC0000003");

        /// <summary>
        /// 事务处理线程
        /// </summary>
        Thread _thread = null;

        private SynchronizationContext mainThreadSynContext;

        ListBox listBox;

        /// <summary>
        /// 初始启动系统的时候，是否有在等待点和卸载点之间的车要回窑头卸载点
        /// </summary>
        public bool _ToPlcHead = false;

        /// <summary>
        /// 初始启动系统的时候，是否有在等待点和装载点之间的车要回窑尾装载点
        /// </summary>
        public bool _ToPlcEnd = false;

        /// <summary>
        /// 窑头卸载区AGV是否需要充电
        /// </summary>
        public bool _PlcHeadNeedCharge = false;

        /// <summary>
        /// 窑头有无充电完成的AGV
        /// </summary>
        public bool _PlcHeadChargeSuc = false;

        /// <summary>
        /// 异常AGV
        /// </summary>
        Dictionary<string, int> dic = new Dictionary<string, int>();

        /// <summary>
        /// 构造函数
        /// </summary>
        public F_Logic(SynchronizationContext context, ListBox listBoxOutput)
        {
            mainThreadSynContext = context;

            listBox = listBoxOutput;

            _plcHead.Site = Site.窑头4;

            _plcEnd.Site = Site.窑尾1;

            //Thread tr = new Thread(InitToHeadWait);
            //tr.IsBackground = true;
            //tr.Start();

            //Thread tr2 = new Thread(InitToEnd);
            //tr2.IsBackground = true;
            //tr2.Start();

            Thread tr3 = new Thread(ClearTask);
            tr3.IsBackground = true;
            tr3.Start();

            _thread = new Thread(ThreadFunc);

            _thread.IsBackground = true;

            _thread.Start();



        }

        /// <summary>
        /// 展示服务日志到界面
        /// </summary>
        private void sendServerLog(String msg)
        {
            mainThreadSynContext.Post(new SendOrPostCallback(displayLogToUi), msg);

        }

        /// <summary>
        /// 回到主线程，操作日志框，展示日志
        /// </summary>
        private void displayLogToUi(object obj)
        {
            String msg = (String)obj;
            if (string.IsNullOrEmpty(msg)) { msg = "空消息"; }

            if (listBox.Items.Count > 200)
            {
                listBox.Items.RemoveAt(0);
            }

            listBox.Items.Add(string.Format("【{0}】：{1}", DateTime.Now.TimeOfDay.ToString(), msg));

            listBox.SelectedIndex = listBox.Items.Count - 1;
        }


        /// <summary>
        /// 
        /// </summary>
        private void ThreadFunc()
        {
            while (true)
            {
                Thread.Sleep(3000);

                try
                {
                    EndHolder1(); //窑尾机械手1操作

                    EndToEndWait(); // 从窑尾1 去 窑尾等待5

                    EndWaitToHolder2(); // 从窑尾等待5 去 窑尾夹具点2                                     

                    EndHolderToHeadWait();// 窑尾机械手2 到 窑头等待点7

                    PlcHeadCharge();// 窑头等待点7的AGV去充电点50

                    PlcHeadChargeSuc();//充电点50有充电完成的AGV,回到窑头7

                    HeadWaitToHolder(); //  从窑头等待7 去 窑头夹具点3

                    HolderToHeadWait(); // 从窑头机械手3 去 窑头等待8

                    TaskPlcHeadPut();// 窑头8到4，放货任务

                    TaskHeadToEnd();// 窑头卸货完成Agv从窑头4 到 窑尾1
    

                }
                catch { }
            }
        }
       
        /// <summary>
        /// 窑尾取货完成Agv从窑尾夹具2 到 窑头卸载等待点7
        /// </summary>
        private void EndHolderToHeadWait()
        {
            F_AGV agv = F_DataCenter.MDev.IGetDevOnSite(Site.窑尾2);

            // 判断窑尾机械手2号是否完成
          if (agv != null && agv.IsFree && !agv.IsLock                
                && agv.Sta_Material == EnumSta_Material.AGV有货
                && _plcEnd.Sta_Material == EnumSta_Material.窑尾2号机械手完成 
                )
            {
                F_ExcTask task = new F_ExcTask(null, EnumOper.无动作, Site.窑尾2, Site.窑头7);

                task.Id = agv.Id;

                agv.IsLock = true;

                F_DataCenter.MTask.IStartTask(task);


                sendServerLog(agv.Id + "窑尾取货完成Agv从窑尾夹具2 到 窑头等待点7");

                LogFactory.LogDispatch(agv.Id, "送货", "窑尾取货完成Agv从窑尾夹具2 到 窑头等待点7");
            }
        }

        /// <summary>
        /// 从窑头8到窑头4，窑头放货任务
        /// </summary>
        private void TaskPlcHeadPut()
        {
            F_AGV agv = F_DataCenter.MDev.IGetDevOnSite(Site.窑头8);

            /// 窑头AGV未锁定 并且 此次任务没有被响应
            if (!_plcHead.IsLock && agv != null && !agv.IsLock
                && agv.Sta_Material == EnumSta_Material.AGV有货
               )
            {
                // 从窑头卸载等待点2 到 窑头卸载点的放货任务
                if (F_DataCenter.MTask.IStartTask(new F_ExcTask(_plcHead, EnumOper.放货, Site.窑头8, Site.窑头4)))
                {
                    _plcHead.IsLock = true;

                    agv.IsLock = true;

                    sendServerLog(agv.Id + "从窑头卸载等待点8 到 窑头卸载点4的任务");

                    LogFactory.LogDispatch(agv.Id, "窑头卸货", "从窑头卸载等待点8 到 窑头卸载点4的任务");

                }
            }
            else
            {
                _ToPlcHead = false;
            }
        }

        /// <summary>
        /// 窑头卸货完成Agv从窑头4 到 窑尾1
        /// </summary>
        private void TaskHeadToEnd()
        {
            F_AGV agv = F_DataCenter.MDev.IGetDevOnSite(_plcHead.Site);

           if (agv != null && agv.IsFree && !agv.IsLock
                && agv.Sta_Material == EnumSta_Material.AGV无货
               && _plcHead.Sta_Material == EnumSta_Material.窑头接料完成
                )
            {
                F_ExcTask task = new F_ExcTask(_plcEnd, EnumOper.取货, Site.窑头4, Site.窑尾1);

                agv.IsLock = true;

                task.Id = agv.Id;

                F_DataCenter.MTask.IStartTask(task);

                sendServerLog(agv.Id + "从窑头卸载点4 到 窑尾装载点1");

                LogFactory.LogDispatch(agv.Id, "到窑尾接货", "从窑头卸载点4到窑尾装载点1");

            }
        }
        /// <summary>
        /// 窑尾机械手1操作
        /// </summary>
        private void EndHolder1()  
        {
            F_AGV agv = F_DataCenter.MDev.IGetDevOnSite(_plcEnd.Site);

            // AGV已经取货完成，
            if (agv != null && agv.IsFree && !agv.IsLock && !_plcEnd.IsLock
                && agv.Sta_Material == EnumSta_Material.AGV有货
                && !(_plcEnd.Sta_Material== EnumSta_Material.窑尾1号机械手完成)
                && true
               
                )
            {
                F_ExcTask task = new F_ExcTask(_plcEnd, EnumOper.窑尾1号机械手, Site.窑尾1, Site.窑尾1);

                agv.IsLock = true;

                task.Id = agv.Id;

                _plcEnd.IsLock = true;

                F_DataCenter.MTask.IStartTask(task);

                sendServerLog(agv.Id + "窑尾1号机械手启动");

                LogFactory.LogDispatch(agv.Id, "取货完成", " 窑尾1号机械手启动");

            }
        }

        /// <summary>
        /// 初始化按钮方法
        /// </summary>
        public void initButton()
        {
            Thread tr = new Thread(InitToHeadWait);
            tr.IsBackground = true;
            tr.Start();

            Thread tr2 = new Thread(InitToEnd);
            tr2.IsBackground = true;
            tr2.Start();
        }

        /// <summary>
        /// 如果agv有货 回到卸载等待点7 ，上电后处于等待点8与卸载点4之间的车去到卸载点4
        /// </summary>
        private void InitToHeadWait()
        {
            Thread.Sleep(1000);

            List<F_AGV> agvs = F_DataCenter.MDev.IGetDevNotOnWaitSite();

            if (agvs != null)
            {
                foreach (F_AGV agv in agvs)
                {
                    if (agv.Site != Site.窑头8和4之间 && agv.Site != Site.窑头4)
                    {
                        F_ExcTask task = new F_ExcTask(null, EnumOper.无动作, agv.Site, Site.窑头7);

                        task.Id = agv.Id;

                        F_DataCenter.MTask.IStartTask(task);

                        sendServerLog(agv.Id + " 初始化,回到窑头卸载等待点7");

                        LogFactory.LogDispatch(agv.Id, "车辆初始化", "回到窑头卸载等待点7");
                    }
                    else
                    {   
                        /// 如果agv有货 且位于等待点8和 卸载点4之间，回到窑头卸载点
                         _ToPlcHead = true;

                        F_ExcTask task = new F_ExcTask(_plcHead, EnumOper.放货, agv.Site, Site.窑头4);

                        task.Id = agv.Id;

                        F_DataCenter.MTask.IStartTask(task);

                        sendServerLog(agv.Id + "位于等待点8和卸载点4之间的AGV去卸货");

                        LogFactory.LogDispatch(agv.Id, "车辆初始化", "位于等待点8和卸载点4之间的AGV去卸货");

                    }
                }

            }
        }

        /// <summary>
        /// 如果agv没货 回到装载点1
        /// </summary>
        private void InitToEnd()
        {
            Thread.Sleep(1000);

                List<F_AGV> agvs = F_DataCenter.MDev.IGetDevNotLoadOnWaitSite();

            if (agvs != null)
            {
                foreach (F_AGV agv in agvs)
                {
                    if (agv.IsFree)
                    {
                        _ToPlcEnd = true;

                        F_ExcTask task = new F_ExcTask(_plcEnd, EnumOper.取货, agv.Site, Site.窑尾1);

                        task.Id = agv.Id;

                        F_DataCenter.MTask.IStartTask(task);

                        sendServerLog(agv.Id + " 初始化,回到窑尾装载点1");

                        LogFactory.LogDispatch(agv.Id, "车辆初始化", "回到窑尾装载点1");

                    }                  
                }
            }          
        }

        /// <summary>
        /// 窑头等待点7的AGV去充电
        /// </summary>
        private void PlcHeadCharge()
        {
            F_AGV agv = F_DataCenter.MDev.IGetDevOnSite(Site.窑头7);

            // 让未上锁的、电量低于60且未充电的AGV去充电
            if (agv != null && agv.IsFree && agv.Electicity <= 90 &&//ConstSetBA.最低电量 &&
                agv.ChargeStatus == EnumChargeStatus.未充电                
                )
            {
                _PlcHeadNeedCharge = true;

                F_ExcTask task = new F_ExcTask(null, EnumOper.充电, Site.窑头7, Site.充电点);

                agv.IsLock = true;

                task.Id = agv.Id;

                F_DataCenter.MTask.IStartTask(task);

                sendServerLog(agv.Id + ",去到充电点充电");

                LogFactory.LogDispatch(agv.Id, "充电", "去到充电点充电");

            }
            else
            {
                _PlcHeadNeedCharge = false;

            }

        }

        /// <summary>
        ///窑头充电点有充电完成的AGV
        ///回到窑头7
        /// </summary>
        public void PlcHeadChargeSuc()
        {
            F_AGV agv = F_DataCenter.MDev.IGetDevOnSite(Site.充电点);

            // 有未上锁的、充电完成的AGV,且窑头卸载点无货、AGV上有货
            if (agv != null && !agv.IsLock && agv.ChargeStatus == EnumChargeStatus.充电完成
                )
            {

                //return;
                _PlcHeadChargeSuc = true;

                F_ExcTask task = new F_ExcTask(_plcHead, EnumOper.无动作, Site.充电点, Site.窑头7);

                agv.IsLock = true;

                _plcHead.IsLock = true;

                task.Id = agv.Id;

                F_DataCenter.MTask.IStartTask(task);

                sendServerLog(agv.Id + ",充电完成，派充电完成的车去卸载等待点7");

                LogFactory.LogDispatch(agv.Id, "充电完成", "派充电完成的车去卸载等待点7");

            }
            else
            {
                _PlcHeadChargeSuc = false;
            }
        }

        /// <summary>
        /// 从窑尾1 去 窑尾等待5   
        /// </summary>
        public void EndToEndWait()
        {
            F_AGV agv = F_DataCenter.MDev.IGetDevOnSite(_plcEnd.Site);

            // 判断窑尾1 号机械手是否完成 
            if (agv != null && !agv.IsLock
                && agv.Sta_Material == EnumSta_Material.AGV有货
                && _plcEnd.Sta_Material== EnumSta_Material.窑尾1号机械手完成 
               
                )
            {
                // 从窑尾1 去 窑尾等待5
                if (F_DataCenter.MTask.IStartTask(new F_ExcTask(_plcEnd, EnumOper.无动作, Site.窑尾1, Site.窑尾5)))
                {

                    sendServerLog(agv.Id + ", 从窑尾1 去 窑尾等待5");

                    LogFactory.LogDispatch(agv.Id, "取货完成", ", 从窑尾1 去 窑尾等待5");

                    agv.IsLock = true;

                }
            }
        }

        /// <summary>
        /// 从窑尾等待5 去 窑尾2号机械手   
        /// </summary>
        public void EndWaitToHolder2()
        {
            F_AGV agv = F_DataCenter.MDev.IGetDevOnSite(Site.窑尾5);

            // 判断窑尾2号机械手的状态 
            if (agv != null && !agv.IsLock 
                && agv.Sta_Material == EnumSta_Material.AGV有货
                )
            {
                // 从窑尾夹具点2 到 窑尾等待点5  
                if (F_DataCenter.MTask.IStartTask(new F_ExcTask(_plcEnd, EnumOper.窑尾2号机械手, Site.窑尾5, Site.窑尾2)))
                {
                    sendServerLog(agv.Id + ",  从窑尾等待5  去 窑尾夹具点2");

                    LogFactory.LogDispatch(agv.Id, "取货完成", "从窑尾等待5  去 窑尾夹具点2");

                    agv.IsLock = true;
                }
            }
        }

        /// <summary>
        /// 从窑头等待7 去 窑头夹具点3
        /// </summary>
        public void HeadWaitToHolder()
        {
            F_AGV agv = F_DataCenter.MDev.IGetDevOnSite(Site.窑头7);

            //窑头等待区7的车不需要充电、没有充电完成的车 、没有初始化时要去窑头装载点的车
            if (agv != null 
                && !agv.IsLock && !_PlcHeadNeedCharge && agv.IsFree 
                &&  !_PlcHeadChargeSuc && !_ToPlcHead 
                && agv.Electicity > ConstSetBA.最低电量
                && agv.Electicity > 90)
            {
                // 判断夹具的状态 及 窑尾货物状态、AGV货物状态
                if (true
                   && agv.Sta_Material == EnumSta_Material.AGV有货
                   && _plcHeadHolder.Sta_Material == EnumSta_Material.窑头机械手就绪
                    )
                {
                    // 从窑头等待7 去 窑头夹具点3
                    if (F_DataCenter.MTask.IStartTask(new F_ExcTask(_plcHeadHolder, EnumOper.窑头机械手, Site.窑头7, Site.窑头3)))
                    {
                        sendServerLog(agv.Id + ",  从窑头等待7 去 窑头夹具点3");

                        LogFactory.LogDispatch(agv.Id, "卸货", "从窑头等待7去窑头夹具点3");

                        agv.IsLock = true;
                    }
                }
            }
        }

        /// <summary>
        /// 从窑头夹具点3 去 窑头等待8 
        /// </summary>
        public void HolderToHeadWait()
        {
            F_AGV agv = F_DataCenter.MDev.IGetDevOnSite(Site.窑头3);

            // 判断窑头机械手是否完成的状态 
            if (agv != null && !agv.IsLock 
                && agv.Sta_Material== EnumSta_Material.AGV有货
                && _plcHeadHolder.Sta_Material == EnumSta_Material.窑头机械手完成
                )
            {   
                // 从窑头机械手3 到窑头8
                if (F_DataCenter.MTask.IStartTask(new F_ExcTask(_plcHeadHolder, EnumOper.无动作, Site.窑头3, Site.窑头8)))
                {
                    sendServerLog(agv.Id + ", 从窑头夹具点3 去 窑头等待8 ");

                    LogFactory.LogDispatch(agv.Id, "卸货", "从窑头夹具点3去窑头等待8");

                    agv.IsLock = true;
                }
            }
        }


        /// <summary>
        /// 发生故障、离线的车，清除其相应的任务
        /// </summary>
        public void ClearTask()
        {
            while (true)
            {
                Thread.Sleep(5000);
                List<F_AGV> agvs = F_DataCenter.MDev.ErrorOrFalse();
                List<DispatchBackMember> dispatchlist = JTWcfHelper.WcfMainHelper.GetDispatchList();
                if (agvs != null&&dispatchlist!=null  && dispatchlist.Count > 0)
                {
                    foreach (var agv in agvs)
                    {
                        foreach (var dispatch in dispatchlist)
                        {
                            // 有故障的车是否对应任务的设备ID
                            if (agv.Id == dispatch.DisDevId)
                            {
                                if (dic.ContainsKey(agv.Id))
                                {
                                    int count = 0;
                                    dic.TryGetValue(agv.Id, out count);
                                    if (count >= 10)
                                    {
                                        // 终止该任务
                                        JTWcfHelper.WcfMainHelper.CtrDispatch(dispatch.DisGuid, DisOrderCtrTypeEnum.Stop);

                                        sendServerLog("终止异常的 " + agv.Id + "正在执行的任务");

                                        LogFactory.LogRunning("终止异常的 " + agv.Id + "正在执行的任务");

                                        count = 0;
                                    }
                                    else
                                    {
                                        count++;

                                        sendServerLog("异常的 " + agv.Id + "已等待处理 " + count + " 次");

                                        LogFactory.LogRunning("异常的 " + agv.Id + "已等待处理 " + count + " 次");
                                    }
                                    dic.Remove(agv.Id);
                                    dic.Add(agv.Id, count);
                                }
                                else
                                {
                                    dic.Add(agv.Id, 0);
                                }
                            }
                        }
                    }
                }
                else
                {
                    dic.Clear();
                }
            }
        }

        /// <summary>
        /// 停止事务线程
        /// </summary>
        public void ThreadStop()
        {
            if (_thread != null) _thread.Abort();

        }
    }
}
