﻿using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SiMay.Basic;
using SiMay.Sockets.Tcp;
using SiMay.Sockets.Tcp.Client;
using SiMay.Sockets.Tcp.Session;
using SiMay.Sockets.Tcp.TcpConfiguration;
using ConnectionWorkType = SiMay.Net.SessionProvider.Core.ConnectionWorkType;
using MessageHelper = SiMay.Net.SessionProvider.Core.MessageHelper;
using SiMay.Net.SessionProvider.Core;

namespace SiMay.Net.SessionProvider.Providers
{
    public class TcpProxySessionProvider : SessionProvider
    {
        public event Action<ProxyProviderNotify, EventArgs> ProxyProviderNotify;

        private TcpProxyMainConnectionContext TcpProxyMainConnectionContext { get; set; }

        private const Int16 CHANNEL_LOGOUT = 0;
        private const Int16 CHANNEL_LOGIN = 1;

        private const Int16 C_GLOBAL_CONNECT = 1000;

        private int _currentState = 0;
        private bool _wetherLogOut;
        private IDictionary<long, SessionProviderContext> _proxySessions = new Dictionary<long, SessionProviderContext>();

        private TcpSocketSaeaClientAgent _clientAgent = null;

        /// <summary>
        /// session代理提供器构造函数
        /// </summary>
        /// <param name="options">代理配置设置</param>
        /// <param name="onSessionNotifyProc">session事件通知</param>
        /// <param name="onProxyNotify">代理事件通知</param>
        internal TcpProxySessionProvider(SessionProviderOptions options)
        {
            ApplicationConfiguartion.SetOptions(options);

            var clientConfig = new TcpSocketSaeaClientConfiguration();
            clientConfig.ReuseAddress = true;
            clientConfig.KeepAlive = true;
            clientConfig.KeepAliveInterval = 5000;
            clientConfig.KeepAliveSpanTime = 1000;

            //不启用压缩
            clientConfig.CompressTransferFromPacket = false;
            //停用应用心跳检测线程
            clientConfig.AppKeepAlive = false;

            _clientAgent = TcpSocketsFactory.CreateClientAgent(TcpSocketSaeaSessionType.Packet, clientConfig, (notify, session) =>
            {
                switch (notify)
                {
                    case TcpSessionNotify.OnConnected:
                        this.ConnectedHandler(session);
                        break;
                    case TcpSessionNotify.OnSend:
                        this.OnSend(session);
                        break;
                    case TcpSessionNotify.OnDataReceived:
                        this.OnMessageHandler(session);
                        break;
                    case TcpSessionNotify.OnClosed:
                        this.OnClosed(session);
                        break;
                    default:
                        break;
                }
            });
        }
        private void SendACK(TcpSocketSaeaSession session, ConnectionWorkType type)
        {
            var ackBody = MessageHelper.CopyMessageHeadTo(C_GLOBAL_CONNECT,
                new LoaderAckPacket()
                {
                    Type = (byte)type,
                    AccessId = ApplicationConfiguartion.Options.AccessId,
                    AccessKey = ApplicationConfiguartion.Options.AccessKey
                });

            var dataBuilder = new List<byte>();
            dataBuilder.AddRange(BitConverter.GetBytes(ApplicationConfiguartion.Options.AccessId));
            dataBuilder.AddRange(GZipHelper.Compress(ackBody, 0, ackBody.Length));
            session.SendAsync(dataBuilder.ToArray());//构造发送
            dataBuilder.Clear();
        }

        private void ConnectedHandler(TcpSocketSaeaSession session)
        {
            if (Interlocked.Exchange(ref _currentState, CHANNEL_LOGIN) == CHANNEL_LOGOUT)
            {
                this.TcpProxyMainConnectionContext = new TcpProxyMainConnectionContext(session);
                this.TcpProxyMainConnectionContext.SessionNotifyEventHandler += ProxyMainConnectionSessionNotify;
                this.TcpProxyMainConnectionContext.LogOutEventHandler += LogOutEventHandler;
                this.TcpProxyMainConnectionContext.LaunchApplicationConnectEventHandler += LaunchApplicationConnectEventHandler;
                this.TcpProxyMainConnectionContext.AccessIdOrKeyWrongEventHandler += AccessIdOrKeyWrongEventHandler;
                session.AppTokens = new object[] {
                    this.TcpProxyMainConnectionContext,
                    ConnectionWorkType.MainApplicationConnection
                };
                this.SendACK(session, ConnectionWorkType.MainApplicationConnection);
                this.TcpProxyMainConnectionContext.PulCurrentSessions();
            }
            else
            {
                var tcpSessionContext = new TcpServiceSessionContext(session);

                lock (_proxySessions)
                    this._proxySessions.Add(tcpSessionContext.GetHashCode(), tcpSessionContext);

                session.AppTokens = new object[] {
                    tcpSessionContext,
                    ConnectionWorkType.ApplicationConnection
                };
                this.SendACK(session, ConnectionWorkType.ApplicationConnection);
                this.Notification(tcpSessionContext, TcpSessionNotify.OnConnected);
            }
        }

        private void AccessIdOrKeyWrongEventHandler(TcpProxyMainConnectionContext mainConnectionContext)
            => this.ProxyProviderNotify?.Invoke(Net.SessionProvider.ProxyProviderNotify.AccessIdOrKeyWrong, EventArgs.Empty);

        private void LaunchApplicationConnectEventHandler(TcpProxyMainConnectionContext mainConnectionContext)
            => this._clientAgent.ConnectToServer(ApplicationConfiguartion.Options.ServiceIPEndPoint);

        private void LogOutEventHandler(TcpProxyMainConnectionContext mainConnectionContext, string message)
        {
            _wetherLogOut = true;
            this.ProxyProviderNotify?.Invoke(Net.SessionProvider.ProxyProviderNotify.LogOut, new LogOutEventArgs(message));
        }

        private void ProxyMainConnectionSessionNotify(TcpProxyApplicationConnectionContext proxyContext, TcpSessionNotify type)
            => this.Notification(proxyContext, type);


        private void OnSend(TcpSocketSaeaSession session)
        {
            if (session.AppTokens.IsNull())
                return;

            var type = session.AppTokens[SysContanct.INDEX_WORKTYPE].ConvertTo<ConnectionWorkType>();
            if (type == ConnectionWorkType.ApplicationConnection)
            {
                var tcpSessionContext = session.AppTokens[SysContanct.INDEX_WORKER].ConvertTo<TcpServiceSessionContext>();
                this.Notification(tcpSessionContext, TcpSessionNotify.OnSend);
            }
        }

        private void OnMessageHandler(TcpSocketSaeaSession session)
        {
            var type = session.AppTokens[SysContanct.INDEX_WORKTYPE].ConvertTo<ConnectionWorkType>();
            if (type == ConnectionWorkType.MainApplicationConnection)
            {
                var sessionContext = session.AppTokens[SysContanct.INDEX_WORKER].ConvertTo<TcpProxyMainConnectionContext>();
                sessionContext.OnProcess(session.CompletedBuffer);
            }
            else if (type == ConnectionWorkType.ApplicationConnection)
            {
                var sessionContext = session.AppTokens[SysContanct.INDEX_WORKER].ConvertTo<TcpServiceSessionContext>();
                this.Notification(sessionContext, TcpSessionNotify.OnDataReceived);
            }
        }

        private void OnClosed(TcpSocketSaeaSession session)
        {
            if (session.AppTokens.IsNull() && this._currentState == CHANNEL_LOGOUT)
            {
                session.AppTokens = new object[] {
                    null,
                    ConnectionWorkType.MainApplicationConnection
                };
            }
            else if (session.AppTokens.IsNull() && this._currentState == CHANNEL_LOGIN)
                return;

            var type = session.AppTokens[SysContanct.INDEX_WORKTYPE].ConvertTo<ConnectionWorkType>();
            if (type == ConnectionWorkType.MainApplicationConnection)
            {
                if (!TcpProxyMainConnectionContext.IsNull())
                {
                    this.TcpProxyMainConnectionContext.CloseCurrentSession();
                    this.TcpProxyMainConnectionContext.LaunchApplicationConnectEventHandler -= LaunchApplicationConnectEventHandler;
                    this.TcpProxyMainConnectionContext.AccessIdOrKeyWrongEventHandler -= AccessIdOrKeyWrongEventHandler;
                    this.TcpProxyMainConnectionContext.SessionNotifyEventHandler -= ProxyMainConnectionSessionNotify;
                    this.TcpProxyMainConnectionContext.LogOutEventHandler -= LogOutEventHandler;
                }
                this.TcpProxyMainConnectionContext = null; _currentState = CHANNEL_LOGOUT;


                foreach (var proxySession in _proxySessions.Select(c => c.Value).ToArray())//操作安全临时解决方案
                    this.Notification(proxySession, TcpSessionNotify.OnClosed);

                lock (_proxySessions)
                    this._proxySessions.Clear();

                if (!_wetherLogOut)
                {
                    var timer = new System.Timers.Timer();
                    timer.Interval = 5000;
                    timer.Elapsed += (s, e) =>
                    {
                        this._clientAgent.ConnectToServer(ApplicationConfiguartion.Options.ServiceIPEndPoint);

                        timer.Stop();
                        timer.Dispose();
                    };
                    timer.Start();
                }
            }
            else if (type == ConnectionWorkType.ApplicationConnection)
            {
                lock (_proxySessions)
                {
                    var sessionContext = session.AppTokens[SysContanct.INDEX_WORKER].ConvertTo<TcpServiceSessionContext>();
                    this.Notification(sessionContext, TcpSessionNotify.OnClosed);
                    this._proxySessions.Remove(sessionContext.GetHashCode());
                }
            }
        }

        public void StartSerivce()
        {
            this._clientAgent.ConnectToServer(ApplicationConfiguartion.Options.ServiceIPEndPoint);
        }

        public override void BroadcastAsync(byte[] data, int offset, int lenght)
        {
            foreach (var session in this._proxySessions.Select(c => c.Value).ToArray())//操作安全临时解决方案
                session.SendAsync(data, offset, lenght);
        }

        public override void CloseService()
        {
            this._wetherLogOut = true;

        }

        public override void DisconnectAll()
        {
            foreach (var session in this._proxySessions.Select(c => c.Value).ToArray())//操作安全临时解决方案
                session.SessionClose();
        }
    }
}
