﻿using Newtonsoft.Json;
using SiMay.Basic;
using SiMay.Core;
using SiMay.Net.SessionProvider;
using SiMay.RemoteControls.Core;
using SiMay.RemoteMonitor.Application;
using SiMay.RemoteMonitor.Extensions;
using SiMay.RemoteMonitor.Properties;
using SiMay.RemoteMonitor.UserControls;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace SiMay.RemoteMonitor.MainApplication
{
    public partial class MainApplication : Form
    {
        public MainApplication()
        {

            InitializeComponent();
        }

        private bool _isRun = true;
        private int _connect_count = 0;
        private long _sendTransferredBytes = 0;
        private long _receiveTransferredBytes = 0;
        private const string GROUP_ALL = "全部";

        private System.Timers.Timer _flowCalcTimer;
        private System.Timers.Timer _viewCarouselTimer;
        private DesktopViewSettingContext _viewCarouselContext = new DesktopViewSettingContext();
        private Color _closeScreenColor = Color.FromArgb(127, 175, 219);
        private ImageList _imgList;

        private MainApplicationAdapterHandler _appMainAdapterHandler = new MainApplicationAdapterHandler(new SystemAppConfig());
        private void MainApplication_Load(object sender, EventArgs e)
        {
            //注册应用
            this._appMainAdapterHandler.ApplicationTypes
                .ApplicationRegister<AudioApplication>()
                .ApplicationRegister<FileApplication>()
                .ApplicationRegister<RegEditorApplication>()
                .ApplicationRegister<ScreenApplication>()
                .ApplicationRegister<ShellApplication>()
                .ApplicationRegister<StartupApplication>()
                .ApplicationRegister<SystemApplication>()
                .ApplicationRegister<TcpConnectionApplication>()
                .ApplicationRegister<VideoApplication>();

            var config = File.Exists(SysConstantsExtend.ConfigPath) ? JsonConvert.DeserializeObject<SystemAppConfig>(File.ReadAllText(SysConstantsExtend.ConfigPath)) : new SystemAppConfig();
            AppConfiguration.SetOption(config);

            this.ViewOnAdaptiveHandler();
            this.OnLoadConfiguration();
            this.RegisterMessageHandler();
        }

        /// <summary>
        /// 加载配置信息，及创建主控窗体
        /// </summary>
        private void OnLoadConfiguration()
        {
            this.Text = "SiMay远程监控管理系统-IOASJHD 正式版_" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();

            if (AppConfiguration.GetApplicationConfiguration<SystemAppConfig>().WindowMaximize)
                this.WindowState = FormWindowState.Maximized;

            this._imgList = new ImageList();
            this._imgList.Images.Add("ok", Resources.ok);
            this._imgList.Images.Add("error", Resources.erro);

            #region 计算实时上下传输流量
            //计算实时上下传输流量
            this._flowCalcTimer = new System.Timers.Timer();
            this._flowCalcTimer.Interval = 1000;
            this._flowCalcTimer.Elapsed += (a, b) =>
            {
                if (!_isRun)
                {
                    _flowCalcTimer.Stop();
                    _flowCalcTimer.Dispose();
                    return;
                }

                this.BeginInvoke(new Action(() =>
                {
                    this.strdownflow.Text = (this._receiveTransferredBytes / (float)1024).ToString("0.00");
                    this._receiveTransferredBytes = 0;

                    this.struflow.Text = (this._sendTransferredBytes / (float)1024).ToString("0.00");
                    this._sendTransferredBytes = 0;
                }));

            };
            this._flowCalcTimer.Start();
            #endregion

            if (AppConfiguration.GetApplicationConfiguration<SystemAppConfig>().SessionMode == 1)
            {
                this.stripHost.Text = AppConfiguration.GetApplicationConfiguration<SystemAppConfig>().MiddlerProxyIPAddress;
                this.stripPort.Text = AppConfiguration.GetApplicationConfiguration<SystemAppConfig>().MiddlerProxyPort.ToString();
            }
            else
            {
                this.stripHost.Text = AppConfiguration.GetApplicationConfiguration<SystemAppConfig>().IPAddress;
                this.stripPort.Text = AppConfiguration.GetApplicationConfiguration<SystemAppConfig>().Port.ToString();
            }

            this.rowtrackBar.Value = this._viewCarouselContext.ViewRow;
            this.columntrackBar.Value = this._viewCarouselContext.ViewColum;
            this.viewRow.Text = columntrackBar.Value.ToString();
            this.viewColumn.Text = rowtrackBar.Value.ToString();
            this.splitContainer.SplitterDistance = (splitContainer.Width / 4);

            this.logList.SmallImageList = _imgList;
            this.logList.Columns.Add("发生时间", 150);
            this.logList.Columns.Add("发生事件", 1000);

            this.groupBox.Text = GROUP_ALL;
            string[] columnsTitle = new string[]
            {
                "IP地址",
                "计算机名",
                "操作系统",
                "处理器信息",
                "核心数量",
                "运行内存",
                "系统账户",
                "摄像设备",
                "录音设备",
                "播放设备",
                "备注信息",
                "服务版本",
                "启动时间",
                "分组名称"
            };

            for (int i = 0; i < columnsTitle.Length; i++)
                this.servicesOnlineList.Columns.Insert(i, columnsTitle[i], 150);

            var apps = SysUtil.ApplicationTypes.OrderByDescending(x => x.ApplicationType.GetRank()).ToList();
            apps.ForEach(c =>
            {
                var type = c.ApplicationType;
                var stripMenu = new UToolStripMenuItem(type.GetApplicationName(), c.ApplicationType);
                stripMenu.Click += StripMenu_Click;
                this.cmdContext.Items.Insert(0, stripMenu);

                if (c.ApplicationType.OnTools())
                {
                    var stripButton = new UToolStripButton(type.GetApplicationName(), SysUtilExtend.GetResourceImageByName(type.GetIconResourceName()), type);
                    stripButton.Click += StripButton_Click;
                    this.toolStrip1.Items.Insert(3, stripButton);
                }
            });

            if (AppConfiguration.GetApplicationConfiguration<SystemAppConfig>().Haslock) //锁住主控界面
                LockWindow();

            _viewCarouselTimer = new System.Timers.Timer(_viewCarouselContext.ViewCarouselInterval);
            _viewCarouselTimer.Elapsed += ViewCarouselFunc;

            if (_viewCarouselContext.CarouselEnabled)
                _viewCarouselTimer.Start();

        }
        private void desktopViewLayout_Resize(object sender, EventArgs e)
        {
            this.ViewOnAdaptiveHandler();
        }

        private void ViewOnAdaptiveHandler()
        {
            var viewCount = _viewCarouselContext.ViewColum * _viewCarouselContext.ViewRow;
            var containerRectangle = this.desktopViewLayout.DisplayRectangle;
            var marginalRight = (_viewCarouselContext.ViewColum * 9) / this._viewCarouselContext.ViewColum;
            var width = (this.desktopViewLayout.Width / _viewCarouselContext.ViewColum) - marginalRight;
            var height = (this.desktopViewLayout.Height / _viewCarouselContext.ViewRow) - marginalRight;

            this._viewCarouselContext.ViewWidth = width;
            this._viewCarouselContext.ViewHeight = height;
            foreach (var view in desktopViewLayout.Controls.Cast<UDesktopView>())
            {
                if (view.Width == width && view.Height == height)
                    continue;

                this.InvokeUI(() =>
                {
                    view.Height = height;
                    view.Width = width;
                });
            }

            //if (this.desktopViewLayout.Controls.Count <= 0)
            //    return;

            //var displayTop = Math.Abs(containerRectangle.Y);//容器的可视top(y偏差纠正)
            //var displayBottom = Math.Abs(displayTop + desktopViewLayout.Height);//容器的可视bottom

            //var startControlIndex = (displayTop / height) * this._viewCarouselContext.ViewColum;
            //var endControlIndex = (displayBottom / height) * this._viewCarouselContext.ViewColum;

            //Console.WriteLine(containerRectangle.Top + "|" + containerRectangle.Bottom);
            //Console.WriteLine($"{startControlIndex} | {endControlIndex}");

            //int index = 0;
            //foreach (var view in desktopViewLayout.Controls.Cast<IDesktopView>())
            //{
            //    if (view is Control control)
            //    {
            //        var viewX = Math.Abs(control.Location.X);
            //        var viewY = Math.Abs(control.Location.Y);
            //        var controlRectangle = new Rectangle(viewX, viewY, control.Width, control.Height);
            //        view.InVisbleArea = RectangleHelper.WhetherContainsInDisplayRectangle(new Rectangle(0, displayTop, this.desktopViewLayout.Width, this.desktopViewLayout.Height), controlRectangle);
            //        Console.WriteLine($"{startControlIndex} | {endControlIndex} - " + view.InVisbleArea + " " + index);
            //    }
            //    if (view.Width == width && view.Height == height && index++ == viewCount)
            //        continue;

            //    this.InvokeUI(() =>
            //    {
            //        view.Height = height;
            //        view.Width = width;
            //    });
            //}
        }

        private void ViewCarouselFunc(object sender, System.Timers.ElapsedEventArgs e)
        {
            var viewCount = _viewCarouselContext.ViewColum * _viewCarouselContext.ViewRow;
            if (this.desktopViewLayout.Controls.Count > viewCount)
            {
                this.InvokeUI(() =>
                {
                    var view = this.desktopViewLayout.Controls[this.desktopViewLayout.Controls.Count - 1].ConvertTo<UDesktopView>();
                    if (!_viewCarouselContext.AlwaysViews.Contains(view))
                        this.desktopViewLayout.Controls.SetChildIndex(view, _viewCarouselContext.AlwaysViews.Count);
                });
            }

            this.ViewOnAdaptiveHandler();
        }

        private void InvokeUI(Action action) => this.Invoke(new Action(action));

        /// <summary>
        /// 初始化通信库
        /// </summary>
        private void RegisterMessageHandler()
        {
            this._appMainAdapterHandler.SynchronizationContext = SynchronizationContext.Current;
            this._appMainAdapterHandler.OnProxyNotifyHandlerEvent += OnProxyNotify;
            this._appMainAdapterHandler.OnReceiveHandlerEvent += OnReceiveHandlerEvent;
            this._appMainAdapterHandler.OnTransmitHandlerEvent += OnTransmitHandlerEvent;
            this._appMainAdapterHandler.OnLogOutHandlerEvent += OnLogOutHandlerEvent;
            this._appMainAdapterHandler.OnLoginHandlerEvent += OnLoginHandlerEvent;
            this._appMainAdapterHandler.OnApplicationCreatedEventHandler += OnApplicationCreatedEventHandler;
            this._appMainAdapterHandler.OnLogHandlerEvent += OnLogHandlerEvent;
            this._appMainAdapterHandler.StartApp();
        }

        private bool OnApplicationCreatedEventHandler(IApplication app)
        {
            return true;
        }

        private void OnLogHandlerEvent(string log, LogOutLevel level)
        {
            switch (level)
            {
                case LogOutLevel.Information:
                    this.WriteRuninglog(log, "ok");
                    break;
                case LogOutLevel.Warning:
                    this.WriteRuninglog(log, "error");
                    break;
                case LogOutLevel.Error:
                    break;
                default:
                    break;
            }
        }

        private void OnLoginHandlerEvent(SessionSyncContext syncContext)
        {
            var listItem = new USessionListItem(syncContext);
            syncContext[SysConstantsExtend.SessionListItem] = listItem;

            //是否开启桌面视图
            if (AppConfiguration.GetApplicationConfiguration<SystemAppConfig>().AutoDesktopViewList.Contains(syncContext[SysConstants.IdentifyId].ToString()))
                this.CreateDesktopView(syncContext);
            else
                listItem.BackColor = _closeScreenColor;

            var groupName = syncContext[SysConstants.GroupName].ConvertTo<string>();
            if (!groupBox.Items.Contains(groupName))
                this.groupBox.Items.Add(groupName);

            //分组
            if (groupBox.Text == groupName || groupBox.Text == GROUP_ALL)
                this.servicesOnlineList.Items.Add(listItem);

            this._connect_count++;
            this.stripConnectedNum.Text = _connect_count.ToString();

            Win32Api.FlashWindow(this.Handle, true); //上线任务栏图标闪烁
        }

        private void OnLogOutHandlerEvent(SessionSyncContext syncContext)
        {
            this.CloseDesktopView(syncContext);

            syncContext[SysConstantsExtend.SessionListItem].ConvertTo<USessionListItem>().Remove();

            _connect_count--;
            stripConnectedNum.Text = _connect_count.ToString();
        }
        private void OnTransmitHandlerEvent(SessionProviderContext session)
            => this._sendTransferredBytes += session.SendTransferredBytes;

        private void OnReceiveHandlerEvent(SessionProviderContext session)
            => this._receiveTransferredBytes += session.ReceiveTransferredBytes;


        /// <summary>
        /// 代理协议事件
        /// </summary>
        /// <param name="notify"></param>
        private void OnProxyNotify(ProxyProviderNotify notify, EventArgs arg)
        {
            switch (notify)
            {
                case ProxyProviderNotify.AccessIdOrKeyWrong:
                    this.InvokeUI(() => this.WriteRuninglog("AccessKey错误,与会话服务器的连接自动关闭!", "error"));
                    break;
                case ProxyProviderNotify.LogOut:
                    if (MessageBox.Show($"{arg.ConvertTo<LogOutEventArgs>().Message},本次连接已自动关闭,是否重新连接?", "提示", MessageBoxButtons.OKCancel, MessageBoxIcon.Exclamation, MessageBoxDefaultButton.Button1, MessageBoxOptions.DefaultDesktopOnly) == DialogResult.OK)
                    {
                        this._appMainAdapterHandler.StartApp();
                    }
                    break;
            }
        }

        /// <summary>
        /// 向已选择的桌面墙发送命令
        /// </summary>
        /// <param name="data"></param>
        /// <param name="isBock"></param>
        /// <returns></returns>
        private IEnumerable<UDesktopView> GetSelectedDesktopView()
        {
            foreach (UDesktopView view in desktopViewLayout.Controls)
            {
                if (view.Checked)
                {
                    yield return view;
                    view.Checked = false;
                }
            }
        }

        /// <summary>
        /// 向选择了的列表发送命令
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        private IEnumerable<USessionListItem> GetSelectedListItem()
        {
            if (servicesOnlineList.SelectedItems.Count != 0)
            {
                var SelectItem = servicesOnlineList.SelectedItems;
                for (int i = 0; i < SelectItem.Count; i++)
                    servicesOnlineList.Items[SelectItem[i].Index].Checked = true;

                foreach (USessionListItem item in servicesOnlineList.Items)
                {
                    if (item.Checked)
                    {
                        yield return item;
                        item.Checked = false;
                    }
                }
            }
        }

        private void LockWindow()
        {
            this.Hide();
            AppConfiguration.GetApplicationConfiguration<SystemAppConfig>().Haslock = true;
            AppConfiguration.GetApplicationConfiguration<SystemAppConfig>().Flush();

            LockWindow form = new LockWindow();
            form.ShowDialog();
            this.Show();
        }

        private void StripButton_Click(object sender, EventArgs e)
        {
            var ustripbtn = sender as UToolStripButton;
            string[] appkeys = ustripbtn.ApplicationType.GetActivateApplicationKey();
            this.GetSelectedDesktopView().ForEach(c =>
            {
                foreach (var key in appkeys)
                    this._appMainAdapterHandler.RemoteActivateService(c.SessionSyncContext, key);
            });
        }

        private void StripMenu_Click(object sender, EventArgs e)
        {
            var ustripbtn = sender as UToolStripMenuItem;
            string[] appkeys = ustripbtn.ApplicationType.GetActivateApplicationKey();
            this.GetSelectedListItem().ForEach(c =>
            {
                foreach (var key in appkeys)
                    this._appMainAdapterHandler.RemoteActivateService(c.SessionSyncContext, key);
            });
        }

        /// <summary>
        /// 双击屏幕墙执行一些任务
        /// </summary>
        /// <param name="session"></param>
        private void DesktopViewDbClick(SessionSyncContext syncContext)
        {
            this._appMainAdapterHandler.RemoteActivateService(syncContext, AppConfiguration.GetApplicationConfiguration<SystemAppConfig>().DbClickViewExc);
        }

        /// <summary>
        /// 输出日志
        /// </summary>
        /// <param name="log"></param>
        /// <param name="key"></param>
        private void WriteRuninglog(string log, string key = "ok")
        {
            ListViewItem logItem = new ListViewItem();
            logItem.ImageKey = key;
            logItem.Text = DateTime.Now.ToString();
            logItem.SubItems.Add(log);

            LogHelper.WriteLog(log, "OnRun.log");

            if (logList.Items.Count >= 1)
                logList.Items.Insert(1, logItem);
            else
                logList.Items.Insert(0, logItem);
        }

        /// <summary>
        /// 清除日志
        /// </summary>
        private void Clearlogs()
        {
            int i = 0;
            foreach (ListViewItem item in logList.Items)
            {
                i++;
                if (i > 1)
                    item.Remove();
            }
        }

        private bool CreateDesktopView(SessionSyncContext syncContext)
        {
            if (syncContext[SysConstantsExtend.DesktopView].IsNull())
            {
                var machineName = syncContext[SysConstants.MachineName].ToString();
                var des = syncContext[SysConstants.Remark].ToString();
                var view = new UDesktopView()
                {
                    SessionSyncContext = syncContext,
                    DesktopViewSimpleApplication = this._appMainAdapterHandler.SimpleApplicationCollection.GetSimpleApplication<DesktopViewSimpleApplication>(),
                    Caption = $"{machineName}-{des}",
                    Height = this._viewCarouselContext.ViewHeight,
                    Width = this._viewCarouselContext.ViewWidth
                };
                view.OnDoubleClickEvent += DesktopViewDbClick;
                view.StartPlay();
                this.desktopViewLayout.Controls.Add(view);
                syncContext[SysConstantsExtend.DesktopView] = view;

                var id = syncContext[SysConstants.IdentifyId].ToString();
                if (!AppConfiguration.GetApplicationConfiguration<SystemAppConfig>().AutoDesktopViewList.Any(c => c.Equals(id, StringComparison.OrdinalIgnoreCase)))
                    AppConfiguration.GetApplicationConfiguration<SystemAppConfig>().AutoDesktopViewList.Add(id);

                return true;
            }
            return false;
        }

        public bool CloseDesktopView(SessionSyncContext syncContext)
        {
            if (!syncContext[SysConstantsExtend.DesktopView].IsNull())
            {
                var view = syncContext[SysConstantsExtend.DesktopView] as UDesktopView;
                view.OnDoubleClickEvent -= DesktopViewDbClick;
                view.StopPlay();
                view.Dispose();
                this.desktopViewLayout.Controls.Remove(view);
                syncContext[SysConstantsExtend.DesktopView] = null;
                AppConfiguration.GetApplicationConfiguration<SystemAppConfig>().AutoDesktopViewList.Remove(syncContext[SysConstants.IdentifyId].ToString());
                return true;
            }
            return false;
        }

        private void SystemOption(object sender, EventArgs e)
        {
            AppSetting configForm = new AppSetting();
            configForm.ShowDialog();
        }

        private void CmdContext_Opening(object sender, CancelEventArgs e)
        {
            if (servicesOnlineList.SelectedItems.Count == 0)
                cmdContext.Enabled = false;
            else
                cmdContext.Enabled = true;
        }

        private void CreateService(object sender, EventArgs e)
        {
            ServiceBuilder serviceBuilder = new ServiceBuilder();
            serviceBuilder.ShowDialog();
        }

        private void RemoteShutdown(object sender, EventArgs e)
        {
            if (MessageBox.Show("确定关闭远程计算机吗?", "提示", MessageBoxButtons.OKCancel, MessageBoxIcon.Exclamation) != DialogResult.OK)
                return;
            this.GetSelectedListItem().ForEach(async c =>
            {
                await this._appMainAdapterHandler.SimpleApplicationCollection.GetSimpleApplication<WsStatusSimpleApplication>().SetWsSession(c.SessionSyncContext.Session, WsStatusSimpleApplication.SYS_SHUTDOWN);
            });
        }

        private void RemoteReboot(object sender, EventArgs e)
        {
            if (MessageBox.Show("确定重启远程计算机吗?", "提示", MessageBoxButtons.OKCancel, MessageBoxIcon.Exclamation) != DialogResult.OK)
                return;
            this.GetSelectedListItem().ForEach(async c =>
            {
                await this._appMainAdapterHandler.SimpleApplicationCollection.GetSimpleApplication<WsStatusSimpleApplication>().SetWsSession(c.SessionSyncContext.Session, WsStatusSimpleApplication.SYS_REBOOT);
            });
        }

        private void RemoteStartup(object sender, EventArgs e)
        {
            if (MessageBox.Show("该操作可能导致远程计算机安全软件警示，继续操作吗?", "提示", MessageBoxButtons.OKCancel, MessageBoxIcon.Exclamation) != DialogResult.OK)
                return;
            this.GetSelectedListItem().ForEach(async c =>
            {
                await this._appMainAdapterHandler.SimpleApplicationCollection.GetSimpleApplication<WsStatusSimpleApplication>().SetWsSession(c.SessionSyncContext.Session, WsStatusSimpleApplication.REG_AUTO_START);
            });
        }

        private void RemoteUnStarup(object sender, EventArgs e)
        {
            if (MessageBox.Show("该操作可能导致远程计算机安全软件警示，继续操作吗?", "提示", MessageBoxButtons.OKCancel, MessageBoxIcon.Exclamation) != DialogResult.OK)
                return;

            this.GetSelectedListItem().ForEach(async c =>
            {
                await this._appMainAdapterHandler.SimpleApplicationCollection.GetSimpleApplication<WsStatusSimpleApplication>().SetWsSession(c.SessionSyncContext.Session, WsStatusSimpleApplication.REG_CANCEL_AUTO_START);
            });
        }

        private void RemoteHideServiceFile(object sender, EventArgs e)
        {
            this.GetSelectedListItem().ForEach(async c =>
            {
                await this._appMainAdapterHandler.SimpleApplicationCollection.GetSimpleApplication<WsStatusSimpleApplication>().SetWsSession(c.SessionSyncContext.Session, WsStatusSimpleApplication.ATTRIB_EXE_HIDE);
            });
        }

        private void ToolStripMenuItem2_Click(object sender, EventArgs e)
        {
            this.GetSelectedListItem().ForEach(async c =>
            {
                await this._appMainAdapterHandler.SimpleApplicationCollection.GetSimpleApplication<WsStatusSimpleApplication>().SetWsSession(c.SessionSyncContext.Session, WsStatusSimpleApplication.ATTRIB_EXE_SHOW);
            });
        }

        private void UninstallService(object sender, EventArgs e)
        {
            if (MessageBox.Show("确定解除对该远程计算机的控制吗？", "提示", MessageBoxButtons.OKCancel, MessageBoxIcon.Exclamation) != DialogResult.OK)
                return;
            this.GetSelectedListItem().ForEach(async c =>
            {
                await this._appMainAdapterHandler.SimpleApplicationCollection.GetSimpleApplication<WsStatusSimpleApplication>().SetWsSession(c.SessionSyncContext.Session, WsStatusSimpleApplication.UNINSTALL_SERVICE);
            });
        }

        private void ModifyRemark(object sender, EventArgs e)
        {
            InputDialog f = new InputDialog();
            f.Caption = "请输入备注名称";
            DialogResult result = f.ShowDialog();
            if (f.Value != "" && result == DialogResult.OK)
            {
                this.GetSelectedListItem().ForEach(async c =>
                {
                    await this._appMainAdapterHandler.SimpleApplicationCollection.GetSimpleApplication<ConfiguartionSimpleApplication>().SetDescribe(c.SessionSyncContext.Session, f.Value);
                });
            }
        }

        private void CopyRuningLog(object sender, EventArgs e)
        {
            if (logList.SelectedItems.Count != 0)
                Clipboard.SetText(logList.Items[logList.SelectedItems[0].Index].SubItems[1].Text);
        }

        private void DeleteRuningLog(object sender, EventArgs e)
        {
            if (logList.SelectedItems.Count != 0)
            {
                int Index = logList.SelectedItems[0].Index;
                if (Index >= 1)
                    logList.Items[Index].Remove();
            }
        }

        private void OnlineList_OnSelected(object sender, EventArgs e)
        {
            foreach (ListViewItem item in servicesOnlineList.Items)
                item.Checked = true;
        }

        private void OnileList_OnUnSelected(object sender, EventArgs e)
        {
            foreach (ListViewItem item in servicesOnlineList.Items)
                item.Checked = false;
        }

        private void ClearRuningLog(object sender, EventArgs e)
        {
            this.Clearlogs();
        }

        private void SendMessageBox(object sender, EventArgs e)
        {
            MessageBoxForm dlg = new MessageBoxForm();
            DialogResult result = dlg.ShowDialog();
            if (result == System.Windows.Forms.DialogResult.OK)
            {
                this.GetSelectedListItem().ForEach(async c =>
                {
                    await this._appMainAdapterHandler.SimpleApplicationCollection.GetSimpleApplication<MessageBoxSimpleApplication>().MessageBox(c.SessionSyncContext.Session, dlg.MessageBody, dlg.MessageTitle, dlg.MsgBoxIcon);
                });
            }
        }

        private void statusToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (statusToolStripMenuItem.Checked == true)
            {
                statusToolStripMenuItem.Checked = false;
                statusStrip1.Visible = false;
            }
            else
            {
                statusToolStripMenuItem.Checked = true;
                statusStrip1.Visible = true;
            }
        }

        private void RemoteDownloadExecete(object sender, EventArgs e)
        {
            this.GetSelectedListItem().ForEach(c =>
            {
                var downManger = new DownloadManagement();
                downManger.SessionSyncContext = c.SessionSyncContext;
                downManger.ShellSimpleApplication = _appMainAdapterHandler.SimpleApplicationCollection.GetSimpleApplication<ShellSimpleApplication>();
                downManger.WebSimpleApplication = _appMainAdapterHandler.SimpleApplicationCollection.GetSimpleApplication<WebSimpleApplication>();
                downManger.Show();
            });
        }

        private void About(object sender, EventArgs e)
        {
            AboutForm dlg = new AboutForm();
            dlg.ShowDialog();
        }

        private void onlineToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (this.onlineToolStripMenuItem.Checked == true)
            {
                this.splitContainer1.Panel2Collapsed = true;
                this.onlineToolStripMenuItem.Checked = false;
            }
            else
            {
                this.splitContainer1.Panel2Collapsed = false;
                this.onlineToolStripMenuItem.Checked = true;
            }
        }

        private void toolStripMenuItem4_Click(object sender, EventArgs e)
        {
            this.GetSelectedListItem().ForEach(c =>
            {
                if (this.CreateDesktopView(c.SessionSyncContext))
                    c.BackColor = Color.Transparent;
            });
        }

        private void toolStripMenuItem5_Click(object sender, EventArgs e)
        {
            this.GetSelectedListItem().ForEach(c =>
            {
                if (this.CloseDesktopView(c.SessionSyncContext))
                    c.BackColor = _closeScreenColor;
            });
        }

        private void linkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            this._viewCarouselContext.ViewRow = rowtrackBar.Value;
            this._viewCarouselContext.ViewColum = columntrackBar.Value;

            this.ViewOnAdaptiveHandler();

            this.WriteRuninglog("设置已保存!", "ok");
        }

        private void RowtrackBar_Scroll(object sender, EventArgs e)
        {
            this.viewRow.Text = columntrackBar.Value.ToString();
        }

        private void ColumntrackBar_Scroll(object sender, EventArgs e)
        {
            this.viewColumn.Text = rowtrackBar.Value.ToString();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            foreach (UDesktopView item in desktopViewLayout.Controls)
                item.Checked = true;
        }

        private void button2_Click(object sender, EventArgs e)
        {
            foreach (UDesktopView item in desktopViewLayout.Controls)
                item.Checked = false;
        }
        private void toolStripButton7_Click(object sender, EventArgs e)
        {
            MessageBoxForm dlg = new MessageBoxForm();
            DialogResult result = dlg.ShowDialog();
            if (result == System.Windows.Forms.DialogResult.OK)
            {
                this.GetSelectedDesktopView().ForEach(async c =>
                {
                    await this._appMainAdapterHandler.SimpleApplicationCollection.GetSimpleApplication<MessageBoxSimpleApplication>().MessageBox(c.SessionSyncContext.Session, dlg.MessageBody, dlg.MessageTitle, dlg.MsgBoxIcon);
                });
            }
        }

        private void toolStripButton6_Click(object sender, EventArgs e)
        {
            this.GetSelectedDesktopView().ForEach(c =>
            {
                var downManger = new DownloadManagement();
                downManger.SessionSyncContext = c.SessionSyncContext;
                downManger.ShellSimpleApplication = _appMainAdapterHandler.SimpleApplicationCollection.GetSimpleApplication<ShellSimpleApplication>();
                downManger.WebSimpleApplication = _appMainAdapterHandler.SimpleApplicationCollection.GetSimpleApplication<WebSimpleApplication>();
                downManger.Show();
            });
        }

        private void toolStripButton8_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("确定解除对该远程计算机的控制吗？", "提示", MessageBoxButtons.OKCancel, MessageBoxIcon.Exclamation) != DialogResult.OK)
                return;

            this.GetSelectedDesktopView().ForEach(async c =>
            {
                await this._appMainAdapterHandler.SimpleApplicationCollection.GetSimpleApplication<WsStatusSimpleApplication>().SetWsSession(c.SessionSyncContext.Session, WsStatusSimpleApplication.UNINSTALL_SERVICE);
            });
        }

        private void toolStripButton10_Click(object sender, EventArgs e)
        {
            AppSetting appConfigForm = new AppSetting();
            appConfigForm.ShowDialog();
        }

        private void toolStripButton9_Click(object sender, EventArgs e)
        {
            ServiceBuilder serviceBuilder = new ServiceBuilder();
            serviceBuilder.ShowDialog();
        }

        private void ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (this.ToolStripMenuItem.Checked == true)
            {
                this.ToolStripMenuItem.Checked = false;
                this.toolStrip1.Visible = false;
            }
            else
            {
                this.ToolStripMenuItem.Checked = true;
                this.toolStrip1.Visible = true;
            }
        }

        private void toolStripMenuItem6_Click_1(object sender, EventArgs e)
        {
            InputDialog input = new InputDialog();
            input.Caption = "请输入要打开的网页地址!";
            input.Value = "http://";
            DialogResult result = input.ShowDialog();
            if (!input.Value.IsNullOrEmpty() && result == DialogResult.OK)
            {
                if (!HTTPChecker(input.Value))
                {
                    MessageBox.Show("URL地址不合法，请检查!", "提示", 0, MessageBoxIcon.Information);
                    return;
                }
                this.GetSelectedListItem().ForEach(async c =>
                {
                    await this._appMainAdapterHandler.SimpleApplicationCollection.GetSimpleApplication<ShellSimpleApplication>().ExecuteShell(c.SessionSyncContext.Session, input.Value);
                });
            }
        }

        private bool HTTPChecker(string val)
        {
            return (val.Substring(0, 7) == "http://") || (val.Substring(0, 8) == "https://");
        }

        private void MainApplication_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (MessageBox.Show("是否确认退出系统吗?", "提示", MessageBoxButtons.OKCancel, MessageBoxIcon.Exclamation) == DialogResult.OK)
            {

                try
                {
                    AppConfiguration.GetApplicationConfiguration<SystemAppConfig>().Flush();
                    this._isRun = false;
                    this._appMainAdapterHandler.Dispose();
                }
                catch (Exception) { }
            }
            else
            {
                e.Cancel = true;
            }
        }

        private void toolStripButton14_Click(object sender, EventArgs e)
        {
            this.GetSelectedDesktopView().ForEach(c =>
            {
                if (this.CloseDesktopView(c.SessionSyncContext))
                {
                    var listItem = c.SessionSyncContext[SysConstantsExtend.SessionListItem].ConvertTo<USessionListItem>();
                    listItem.BackColor = _closeScreenColor;
                }
            });
        }
        private void lockToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.LockWindow();
        }

        private void logList_MouseEnter(object sender, EventArgs e)
        {
            this.splitContainer.SplitterDistance = splitContainer.Width - (splitContainer.Width / 4);
        }

        private void onlineList_MouseEnter(object sender, EventArgs e)
        {
            this.splitContainer.SplitterDistance = (splitContainer.Width / 4);
        }

        private void ToolStripMenuItem7_Click(object sender, EventArgs e)
        {
            InputDialog input = new InputDialog();
            input.Caption = "请输入分组名称";
            DialogResult result = input.ShowDialog();
            if (input.Value != "" && result == DialogResult.OK)
            {
                this.GetSelectedListItem().ForEach(async c =>
                {
                    await this._appMainAdapterHandler.SimpleApplicationCollection.GetSimpleApplication<ConfiguartionSimpleApplication>().SetGroupName(c.SessionSyncContext.Session, input.Value);
                });
            }
        }

        private void GroupBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            //界面初始化完成会被触发一次。。

            foreach (var item in this._appMainAdapterHandler.SessionSyncContexts)
                item[SysConstantsExtend.SessionListItem].ConvertTo<USessionListItem>().Remove();

            foreach (var item in this._appMainAdapterHandler.SessionSyncContexts)
            {
                if (item[SysConstants.GroupName].ConvertTo<string>() == groupBox.Text || groupBox.Text == GROUP_ALL)
                    this.servicesOnlineList.Items.Add(item[SysConstantsExtend.SessionListItem].ConvertTo<USessionListItem>());
            }
        }

        private void UpdateClient_Click(object sender, EventArgs e)
        {
            using (var dlg = new RemoteUpdateService())
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    this.GetSelectedListItem().ForEach(async c =>
                    {
                        await this._appMainAdapterHandler.SimpleApplicationCollection.GetSimpleApplication<ExecuteFileUpdateSimpleApplication>().UpdateService(c.SessionSyncContext.Session, dlg.UrlOrFileUpdate, File.ReadAllBytes(dlg.Value), dlg.Value);
                    });
                }
            }
        }

        private void ToolStripMenuItem8_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("确定重新载入被控端吗?", "提示", MessageBoxButtons.OKCancel, MessageBoxIcon.Exclamation) == DialogResult.OK)
            {
                this.GetSelectedListItem().ForEach(async c =>
                {
                    await this._appMainAdapterHandler.SimpleApplicationCollection.GetSimpleApplication<WsStatusSimpleApplication>().SetWsSession(c.SessionSyncContext.Session, WsStatusSimpleApplication.SERVICE_RELOADER);
                });
            }
        }

        private void installServiceMenuItem_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("确定以系统服务方式启动吗?", "提示", MessageBoxButtons.OKCancel, MessageBoxIcon.Exclamation) != DialogResult.OK)
                return;
            this.GetSelectedListItem().ForEach(async c =>
            {
                await this._appMainAdapterHandler.SimpleApplicationCollection.GetSimpleApplication<WsStatusSimpleApplication>().SetWsSession(c.SessionSyncContext.Session, WsStatusSimpleApplication.INSTALL_SYS_SERVICE);
            });
        }

        private void unInstallServiceMenuItem_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("确定卸载系统服务启动吗?", "提示", MessageBoxButtons.OKCancel, MessageBoxIcon.Exclamation) != DialogResult.OK)
                return;

            this.GetSelectedListItem().ForEach(async c =>
            {
                await this._appMainAdapterHandler.SimpleApplicationCollection.GetSimpleApplication<WsStatusSimpleApplication>().SetWsSession(c.SessionSyncContext.Session, WsStatusSimpleApplication.UNINSTALL_SYS_SERVICE);
            });
        }

        private void linkLabel2_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            var dialog = new DesktopViewWallSettingForm(_viewCarouselContext);
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                if (!_viewCarouselContext.CarouselEnabled)
                    this._viewCarouselTimer.Stop();
                else
                {
                    this._viewCarouselTimer.Interval = _viewCarouselContext.ViewCarouselInterval;
                    this._viewCarouselTimer.Start();
                }
                this.ViewOnAdaptiveHandler();
            }
        }
    }
}