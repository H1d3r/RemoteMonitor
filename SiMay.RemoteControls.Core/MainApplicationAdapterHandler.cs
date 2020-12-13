﻿using SiMay.Basic;
using SiMay.Core;
using SiMay.ModelBinder;
using SiMay.Net.SessionProvider;
using SiMay.Sockets.Tcp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;

namespace SiMay.RemoteControls.Core
{
    /// <summary>
    /// 主控端应用适配器
    /// </summary>
    public class MainApplicationAdapterHandler : MainApplicationBaseAdapterHandler
    {
        /// <summary>
        /// 当有数据上传时
        /// </summary>
        public event Action<SessionProviderContext> OnTransmitHandlerEvent;

        /// <summary>
        /// 当正在接收数据时
        /// </summary>

        public event Action<SessionProviderContext> OnReceiveHandlerEvent;

        /// <summary>
        /// 上线登陆处理事件
        /// </summary>
        public event Action<SessionSyncContext> OnLoginHandlerEvent;

        /// <summary>
        /// 离线处理事件
        /// </summary>
        public event Action<SessionSyncContext> OnLogOutHandlerEvent;

        /// <summary>
        /// 代理协议事件
        /// </summary>
        public event Action<ProxyProviderNotify, EventArgs> OnProxyNotifyHandlerEvent;

        /// <summary>
        /// 当应用被创建
        /// </summary>
        public event Func<IApplication, bool> OnApplicationCreatedEventHandler;


        /// <summary>
        /// 监听日志事件
        /// </summary>
        public event Action<string, LogOutLevel> OnLogHandlerEvent;

        /// <summary>
        /// 主线程同步上下文
        /// </summary>
        public SynchronizationContext SynchronizationContext { get; set; }

        /// <summary>
        /// 会话提供对象
        /// </summary>
        public SessionProvider SessionProvider { get; private set; }

        /// <summary>
        /// 主连接同步上下文
        /// </summary>
        public List<SessionSyncContext> SessionSyncContexts { get; } = new List<SessionSyncContext>();

        /// <summary>
        /// 是否已启动
        /// </summary>
        bool _launch;

        public MainApplicationAdapterHandler(AppConfiguration config)
        {
            if (_launch)
                return;

            _launch = true;

            AppConfigurationHelper<AppConfiguration>.SetOption(config);

            TaskScheduleTrigger.StarSchedule(10);

            //注册简单应用
            SimpleApplicationCollection
                .SimpleApplicationRegister<ConfiguartionSimpleApplication>()
                .SimpleApplicationRegister<DesktopViewSimpleApplication>()
                .SimpleApplicationRegister<ExecuteFileUpdateSimpleApplication>()
                .SimpleApplicationRegister<MessageBoxSimpleApplication>()
                .SimpleApplicationRegister<ShellSimpleApplication>()
                .SimpleApplicationRegister<WebSimpleApplication>()
                .SimpleApplicationRegister<ActivateRemoteServiceSimpleApplication>()
                .SimpleApplicationRegister<WsStatusSimpleApplication>();

        }

        /// <summary>
        /// 启动主控端应用
        /// </summary>
        public void StartApp()
        {
            var providerType = AppConfiguration.GetApplicationConfiguration<AppConfiguration>().SessionMode.ConvertTo<SessionProviderType>();

            string ip = providerType == SessionProviderType.TcpServiceSession
                ? AppConfiguration.GetApplicationConfiguration<AppConfiguration>().IPAddress
                : AppConfiguration.GetApplicationConfiguration<AppConfiguration>().MiddlerProxyIPAddress;

            int port = providerType == SessionProviderType.TcpServiceSession
                ? AppConfiguration.GetApplicationConfiguration<AppConfiguration>().Port
                : AppConfiguration.GetApplicationConfiguration<AppConfiguration>().MiddlerProxyPort;

            int maxConnectCount = AppConfiguration.GetApplicationConfiguration<AppConfiguration>().MaxConnectCount;

            var providerOptions = new SessionProviderOptions
            {
                ServiceIPEndPoint = new IPEndPoint(IPAddress.Parse(ip), port),
                PendingConnectionBacklog = maxConnectCount,
                AccessId = !AppConfiguration.GetApplicationConfiguration<AppConfiguration>().EnabledAnonyMous ? AppConfiguration.GetApplicationConfiguration<AppConfiguration>().AccessId : DateTime.Now.ToFileTimeUtc(),//暂时使用UTC时间作为主控端标识
                AccessKey = AppConfiguration.GetApplicationConfiguration<AppConfiguration>().AccessKey,
                MaxPacketSize = 1024 * 1024 * 2,
                SessionProviderType = providerType
            };

            if (providerType == SessionProviderType.TcpServiceSession)
            {
                if (StartServiceProvider(providerOptions))
                    this.OnLogHandlerEvent?.Invoke($"SiMay远程监控管理系统端口 {port} 监听成功!", LogOutLevel.Information);
                else
                    this.OnLogHandlerEvent?.Invoke($"SiMay远程监控管理系统端口 {port} 启动失败,请检查配置!", LogOutLevel.Warning);
            }
            else
            {
                if (StartProxySessionProvider(providerOptions))
                    this.OnLogHandlerEvent?.Invoke($"SiMay远程监控管理系统初始化成功!", LogOutLevel.Information);
                else
                    this.OnLogHandlerEvent?.Invoke($"SiMay远程监控管理系统初始化发生错误，请注意检查配置!", LogOutLevel.Warning);
            }

            bool StartServiceProvider(SessionProviderOptions options)
            {
                var tcpSessionProvider = SessionProviderFactory.CreateTcpServiceSessionProvider(options);
                tcpSessionProvider.NotificationEventHandler += OnNotifyProc;
                SessionProvider = tcpSessionProvider;
                try
                {
                    tcpSessionProvider.StartSerivce();
                    return true;
                }
                catch (Exception ex)
                {
                    LogHelper.WriteErrorByCurrentMethod(ex);
                    return false;
                }
            }

            bool StartProxySessionProvider(SessionProviderOptions options)
            {
                var proxySessionProvider = SessionProviderFactory.CreateProxySessionProvider(options);
                proxySessionProvider.NotificationEventHandler += OnNotifyProc;
                proxySessionProvider.ProxyProviderNotify += OnProxyNotifyHandlerEvent;
                SessionProvider = proxySessionProvider;

                try
                {
                    proxySessionProvider.StartSerivce();
                    return true;
                }
                catch (Exception ex)
                {
                    LogHelper.WriteErrorByCurrentMethod(ex);
                    return false;
                }
            }
        }

        /// <summary>
        /// 通信完成消息处理方法
        /// </summary>
        /// <param name="session"></param>
        /// <param name="notify"></param>
        private void OnNotifyProc(SessionProviderContext session, TcpSessionNotify notify)
        {
            if (SynchronizationContext.IsNull())
                NotifyProc(null);
            else
                SynchronizationContext.Send(NotifyProc, null);

            void NotifyProc(object @object)
            {
                try
                {
                    switch (notify)
                    {
                        case TcpSessionNotify.OnConnected:
                            //先分配好工作类型，等待工作指令分配新的工作类型
                            session.AppTokens = new object[SysConstants.INDEX_COUNT];
                            session.AppTokens[SysConstants.INDEX_WORKER] = null;
                            session.AppTokens[SysConstants.INDEX_WORKTYPE] = SessionKind.NONE_SESSION;//未经验证的状态
                            session.AppTokens[SysConstants.INDEX_SYNC_SEQUENCE] = new ConcurrentDictionary<int, ApplicationSyncAwaiter>();
                            break;
                        case TcpSessionNotify.OnSend:
                            //耗时操作会导致性能严重降低
                            this.OnTransmitHandlerEvent?.Invoke(session);
                            break;
                        case TcpSessionNotify.OnDataReceiveing:
                            //耗时操作会导致性能严重降低
                            this.OnReceiveHandlerEvent?.Invoke(session);
                            break;
                        case TcpSessionNotify.OnDataReceived:
                            this.OnReceiveComplete(session);
                            break;
                        case TcpSessionNotify.OnClosed:
                            this.OnClosed(session);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.WriteErrorByCurrentMethod(ex);
                }
            }
        }
        private void OnReceiveComplete(SessionProviderContext session)
        {
            //Console.WriteLine($"Id:{session.Id},Type:{session.AppTokens[SysConstants.INDEX_WORKTYPE]},MessageHead:{(int)session.GetMessageHead()}");
            // Tokens参数说明
            // [0]为该连接工作类型，MainWork为主连接，Work工作连接，NONE为未知连接
            // [1]如果连接为Work类型，则是消息处理器，否则是主连接上下文对象
            var appTokens = session.AppTokens;
            var sessionWorkType = appTokens[SysConstants.INDEX_WORKTYPE].ConvertTo<SessionKind>();

            if (sessionWorkType == SessionKind.APP_SERVICE_SESSION)
            {
                var adapter = appTokens[SysConstants.INDEX_WORKER].ConvertTo<ApplicationBaseAdapterHandler>();
                if (adapter.IsManualClose())
                    return;
            }

            /// <summary>
            /// 同步完成
            /// </summary>
            if ((sessionWorkType == SessionKind.APP_SERVICE_SESSION || sessionWorkType == SessionKind.MAIN_SERVICE_SESSION) && session.GetMessageHead() == MessageHead.C_GLOBAL_SYNC_RESULT)
            {
                var asyncOperationSequence = session.AppTokens[SysConstants.INDEX_SYNC_SEQUENCE].ConvertTo<ConcurrentDictionary<int, ApplicationSyncAwaiter>>();
                var syncResult = session.GetMessageEntity<CallSyncResultPacket>();

                if (asyncOperationSequence.ContainsKey(syncResult.Id))
                    asyncOperationSequence[syncResult.Id].Complete(syncResult);
            }
            else if (sessionWorkType == SessionKind.APP_SERVICE_SESSION)
            {
                //消息传给消息适配器,由消息适配器进行处理，通过事件反馈数据到展示层
                var adapter = appTokens[SysConstants.INDEX_WORKER].ConvertTo<ApplicationBaseAdapterHandler>();

                var messageHead = session.GetMessageHead();
                var operationResult = adapter.HandlerBinder.CallFunctionPacketHandler(session, messageHead, adapter);
                if (!operationResult.successed)
                    LogHelper.WriteErrorByCurrentMethod(operationResult.ex);
            }
            else if (sessionWorkType == SessionKind.MAIN_SERVICE_SESSION)
            {
                var operationResult = this.HandlerBinder.CallFunctionPacketHandler(session, session.GetMessageHead(), this);
                if (!operationResult.successed)
                    LogHelper.WriteErrorByCurrentMethod(operationResult.ex);
            }
            else if (sessionWorkType == SessionKind.NONE_SESSION) //未经过验证的连接的消息只能进入该方法块处理，连接密码验证正确才能正式处理消息
            {
                switch (session.GetMessageHead())
                {
                    case MessageHead.C_GLOBAL_CONNECT://连接确认包
                        this.ValiditySession(session);
                        break;
                    default://接收到其他数据包的处理
                        session.SessionClose();//伪造包,断开连接
                        break;
                }
            }
        }

        /// <summary>
        /// 确认连接包
        /// </summary>
        /// <param name="session"></param>
        private void ValiditySession(SessionProviderContext session)
        {
            var ack = session.GetMessageEntity<AcknowledPacket>();
            long accessKey = ack.AccessKey;
            if (accessKey != int.Parse(AppConfiguration.GetApplicationConfiguration<AppConfiguration>().ValidatePassWord))
            {
                session.SessionClose();
                return;
            }
            else
            {
                //连接密码验证通过，设置成为主连接，正式接收处理数据包
                session.AppTokens[SysConstants.INDEX_WORKTYPE] = SessionKind.MAIN_SERVICE_SESSION;

                if (!ack.AssemblyLoad)
                    SendToAssemblyCoreFile(session);

                //告诉服务端一切就绪
                session.SendTo(MessageHead.S_GLOBAL_OK);
            }
        }

        /// <summary>
        /// 启动工作应用
        /// </summary>
        /// <param name="type"></param>
        /// <param name="session"></param>
        /// <param name="identifyId"></param>
        [PacketHandler(MessageHead.C_MAIN_ACTIVE_APP)]
        private void OnActivateStartApp(SessionProviderContext session)
        {
            lock (this)
            {
                var activateResponse = session.GetMessageEntity<ActivateResponsdApplicationPacket>();
                string originName = activateResponse.OriginName;
                string appKey = activateResponse.ApplicationKey;
                string identifyId = activateResponse.IdentifyId;

                var applicationName = activateResponse.ActivatedCommandText.Split('.')[0];

                //离线适配器TaskName格式:identifyId,APP.AdapterKey，其中 identifyId 表示被控端唯一标识，用于区分重连任务中不同被控端,APP为应用名称，AdapterKey为适配器Id
                //等待应用TaskName格式:identifyId,APP
                //优先级说明:等待应用优先匹配，应用创建时如有多个适配器，第一个适配器完成初始化后会被创建为等待应用加入任务调度队列，直至所有适配器连接完成，否则超时应用会被判定创建失败。

                //查找任务调度队列,如果有对应的任务则继续工作
                if (TaskScheduleTrigger.FindOutScheduleTask(c => c.Topic.Contains(identifyId) && (c.Topic.Split(',').ElementAt(1).Equals(applicationName, StringComparison.OrdinalIgnoreCase) || c.Topic.Split(',').ElementAt(1).Equals(activateResponse.ActivatedCommandText, StringComparison.OrdinalIgnoreCase)), out var taskSchedule))
                {
                    //如果是匹配到了离线适配器
                    if (taskSchedule.Topic.Equals($"{identifyId},{activateResponse.ActivatedCommandText}") && taskSchedule is ICustomEvent task)
                        task.Invoke(this, new SuspendTaskResumEventArgs()
                        {
                            Session = session
                        });
                    else if (taskSchedule.Topic.Equals($"{identifyId},{applicationName}") && taskSchedule is ApplicationCreatingTimeOutSuspendTaskContext creatingTimeOutContext)
                    {
                        var application = creatingTimeOutContext.Application;
                        var property = application.GetApplicationAdapterPropertyByName(appKey);
                        if (property.IsNull())
                            throw new ArgumentNullException("adapter not found!");

                        var adapter = Activator.CreateInstance(property.PropertyType).ConvertTo<ApplicationBaseAdapterHandler>();
                        adapter.App = application;
                        adapter.IdentifyId = identifyId;
                        adapter.OriginName = originName;
                        adapter.StartParamenter = activateResponse.StartParameter;
                        adapter.SetSession(session);
                        //property.SetValue(application, adapter);

                        if (internalApplicationReadyExamine(adapter, application))
                            TaskScheduleTrigger.RemoveScheduleTask(taskSchedule);
                    }
                    else
                        throw new ApplicationException();
                }
                else
                {
                    //查找应用
                    var context = SysUtil.ApplicationTypes.FirstOrDefault(c => c.ApplicationType.Name.Equals(applicationName, StringComparison.OrdinalIgnoreCase));
                    if (!context.IsNull())
                    {
                        //根据appKey查找该应用适配器
                        var appAdapterProperty = context.ApplicationType.GetApplicationAdapterPropertyByName(appKey);

                        if (appAdapterProperty.IsNull())
                            throw new ApplicationException("adapter not declaration!");

                        ApplicationBaseAdapterHandler appHandlerBase = Activator.CreateInstance(appAdapterProperty.PropertyType).ConvertTo<ApplicationBaseAdapterHandler>();
                        IApplication app = Activator.CreateInstance(context.ApplicationType).ConvertTo<IApplication>();

                        appHandlerBase.App = app;
                        appHandlerBase.IdentifyId = identifyId;
                        appHandlerBase.OriginName = originName;
                        appHandlerBase.StartParamenter = activateResponse.StartParameter;
                        //appHandlerBase.ApplicationKey = context.Type.GetApplicationKey();
                        appHandlerBase.SetSession(session);

                        internalApplicationReadyExamine(appHandlerBase, app);
                    }
                    else
                    {
                        session.SessionClose();
                        LogHelper.WriteErrorByCurrentMethod("a working connection was closed because the control whose appkey is :{0} could not be found!".FormatTo(appKey));
                        return;
                    }


                }

                //应用资源情况检查
                bool internalApplicationReadyExamine(ApplicationBaseAdapterHandler adapter, IApplication app)
                {
                    session.AppTokens[SysConstants.INDEX_WORKTYPE] = SessionKind.APP_SERVICE_SESSION;
                    session.AppTokens[SysConstants.INDEX_WORKER] = adapter;

                    var handlerFieders = app
                        .GetApplicationAdapterProperty()
                        .ToDictionary(key => key.PropertyType.GetApplicationName(), val => val);

                    if (handlerFieders.ContainsKey(appKey) && handlerFieders.TryGetValue(appKey, out var property))
                        property.SetValue(app, adapter);
                    else
                        throw new ApplicationException();

                    //检查所有适配器属性
                    var prepareCompleted = handlerFieders.Where(c => !c.Value.GetValue(app).IsNull()/* && c.Value.GetValue(app).ConvertTo<ApplicationAdapterHandler>().AttachedConnection*/);
                    if (prepareCompleted.Count() != handlerFieders.Count)
                    {
                        //创建超时任务
                        TaskScheduleTrigger.AddScheduleTask(new ApplicationCreatingTimeOutSuspendTaskContext()
                        {
                            Application = app,
                            Topic = $"{identifyId},{app.GetType().Name}"
                        });
                        return false;
                    }

                    var successed = this.OnApplicationCreatedEventHandler.Invoke(app);
                    if (successed)
                        app.Start();
                    else
                        session.SessionClose();

                    return successed;
                }
            }
        }

        /// <summary>
        /// 登陆信息更改
        /// </summary>
        /// <param name="syncContext"></param>
        /// <param name="login"></param>
        private void UpdateSyncContextHandler(SessionSyncContext syncContext, LoginPacket login)
        {
            syncContext[SysConstants.IPV4] = login.IPV4;
            syncContext[SysConstants.MachineName] = login.MachineName;
            syncContext[SysConstants.Remark] = login.Describe;
            syncContext[SysConstants.ProcessorInfo] = login.ProcessorInfo;
            syncContext[SysConstants.ProcessorCount] = login.ProcessorCount;
            syncContext[SysConstants.MemorySize] = login.MemorySize;
            syncContext[SysConstants.StartRunTime] = login.RunTime;
            syncContext[SysConstants.ServiceVison] = login.ServiceVison;
            syncContext[SysConstants.UserName] = login.UserName;
            syncContext[SysConstants.OSVersion] = login.OSVersion;
            syncContext[SysConstants.GroupName] = login.GroupName;
            syncContext[SysConstants.ExistCameraDevice] = login.ExistCameraDevice;
            syncContext[SysConstants.ExitsRecordDevice] = login.ExitsRecordDevice;
            syncContext[SysConstants.ExitsPlayerDevice] = login.ExitsPlayerDevice;
            syncContext[SysConstants.IdentifyId] = login.IdentifyId;
        }

        /// <summary>
        /// 添加用户信息到上线列表，并根据用户信息无人值守打开任务
        /// </summary>
        /// <param name="session"></param>
        [PacketHandler(MessageHead.C_MAIN_LOGIN)]
        private void LoginHandler(SessionProviderContext session)
        {
            try
            {
                var login = session.GetMessageEntity<LoginPacket>();
                if (!session.AppTokens[SysConstants.INDEX_WORKER].IsNull())//如果主连接同步对象存在，则对该对象更新
                {
                    this.UpdateSyncContextHandler(session.AppTokens[SysConstants.INDEX_WORKER].ConvertTo<SessionSyncContext>(), login);
                    return;
                }

                var dictions = new Dictionary<string, object>()
                {
                    { SysConstants.IPV4, login.IPV4 },
                    { SysConstants.MachineName, login.MachineName },
                    { SysConstants.Remark, login.Describe },
                    { SysConstants.ProcessorInfo, login.ProcessorInfo },
                    { SysConstants.ProcessorCount, login.ProcessorCount },
                    { SysConstants.MemorySize, login.MemorySize },
                    { SysConstants.StartRunTime, login.RunTime },
                    { SysConstants.ServiceVison, login.ServiceVison },
                    { SysConstants.UserName, login.UserName },
                    { SysConstants.OSVersion, login.OSVersion },
                    { SysConstants.GroupName, login.GroupName },
                    { SysConstants.ExistCameraDevice, login.ExistCameraDevice },
                    { SysConstants.ExitsRecordDevice, login.ExitsRecordDevice },
                    { SysConstants.ExitsPlayerDevice, login.ExitsPlayerDevice },
                    { SysConstants.IdentifyId, login.IdentifyId },
                };
                var syncContext = new SessionSyncContext(session, dictions);
                session.AppTokens[SysConstants.INDEX_WORKER] = syncContext;
                SessionSyncContexts.Add(syncContext);

                this.OnLoginHandlerEvent?.Invoke(syncContext);

                this.OnLogHandlerEvent?.Invoke($"计算机:{syncContext[SysConstants.MachineName].ConvertTo<string>()}({syncContext[SysConstants.Remark].ConvertTo<string>()}) -->已连接控制端!", LogOutLevel.Information);
            }
            catch (Exception ex)
            {
                LogHelper.WriteErrorByCurrentMethod(ex);
                //可能是旧版本上线包
            }
        }

        /// <summary>
        /// 移除在线信息
        /// </summary>
        /// <param name="session"></param>
        private void OnClosed(SessionProviderContext session)
        {
            try
            {
                var asyncSequence = session.AppTokens[SysConstants.INDEX_SYNC_SEQUENCE].ConvertTo<ConcurrentDictionary<int, ApplicationSyncAwaiter>>().Values.ToArray();
                //释放所有异步任务
                foreach (var operation in asyncSequence)
                    operation.Complete(null);

                Array.Clear(asyncSequence, 0, asyncSequence.Length);

                object[] arguments = session.AppTokens;
                var worktype = arguments[SysConstants.INDEX_WORKTYPE].ConvertTo<SessionKind>();
                if (worktype == SessionKind.APP_SERVICE_SESSION)
                {
                    var adapterHandler = arguments[SysConstants.INDEX_WORKER].ConvertTo<ApplicationBaseAdapterHandler>();

                    if (adapterHandler.IsManualClose())//如果是手动结束任务
                        return;

                    adapterHandler.State = "工作连接已断开,正在重新连接中....";
                    adapterHandler.SessionClosed(session);

                    //非手动结束任务，将该任务扔到重连线程中
                    var appName = adapterHandler.App.GetType().Name;
                    TaskScheduleTrigger.AddScheduleTask(new SuspendTaskContext()
                    {
                        //DisconnectTimePoint = DateTime.Now,
                        ApplicationAdapterHandler = adapterHandler,
                        SessionSyncContexts = SessionSyncContexts,
                        Topic = $"{adapterHandler.IdentifyId},{appName}.{adapterHandler.GetApplicationName()}"
                    });
                }
                else if (worktype == SessionKind.MAIN_SERVICE_SESSION)
                {
                    var syncContext = arguments[SysConstants.INDEX_WORKER].ConvertTo<SessionSyncContext>();
                    if (syncContext.IsNull())
                        return;

                    SessionSyncContexts.Remove(syncContext);

                    this.OnLogOutHandlerEvent?.Invoke(syncContext);

                    this.OnLogHandlerEvent?.Invoke($"计算机:{syncContext[SysConstants.MachineName].ConvertTo<string>()}({syncContext[SysConstants.Remark].ConvertTo<string>()}) --已与控制端断开连接!", LogOutLevel.Warning);
                }
                else if (worktype == SessionKind.NONE_SESSION)
                {
                    LogHelper.WriteErrorByCurrentMethod("NONE Session Close");
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteErrorByCurrentMethod(ex);
            }
        }

        /// <summary>
        /// 开打远程应用服务
        /// </summary>
        /// <param name="syncContext"></param>
        /// <param name="appKey"></param>
        public void RemoteActivateService(SessionSyncContext syncContext, string appKey)
        {
            syncContext.Session.SendTo(MessageHead.S_SIMPLE_ACTIVATE_REMOTE_SERVICE,
                new ActivateRemoteServicePacket()
                {
                    CommandText = appKey
                });
        }

        public override void Dispose()
        {
            this._launch = false;
            this.SessionProvider.CloseService();
            base.Dispose();
        }
    }
}
