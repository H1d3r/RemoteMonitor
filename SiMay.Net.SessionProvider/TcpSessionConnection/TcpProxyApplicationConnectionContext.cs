﻿using System;
using System.Text;
using System.Net.Sockets;
using System.Collections.Generic;
using SiMay.Basic;
using SiMay.Sockets.Tcp.Session;
using SiMay.Net.SessionProvider.Core;
using static SiMay.Net.SessionProvider.Core.ProxyProtocolConstructionHelper;

namespace SiMay.Net.SessionProvider
{
    public class TcpProxyApplicationConnectionContext : SessionProviderContext
    {
        /// <summary>
        /// 消息接受完成
        /// </summary>
        public event Action<TcpProxyApplicationConnectionContext> DataReceivedEventHandler;

        /// <summary>
        /// 消息发送完成
        /// </summary>
        public event Action<TcpProxyApplicationConnectionContext> DataSendEventHandler;

        /// <summary>
        /// 标识
        /// </summary>
        public long Id { get; private set; }

        //private byte[] _decompressData;
        ///// <summary>
        ///// 完成缓冲区
        ///// </summary>
        //public override byte[] CompletedBuffer
        //{
        //    get
        //    {
        //        //缓存解压数据，防止重复调用造成性能低下
        //        if (_decompressData.IsNull())
        //        {
        //            var waitDecompressData = ProxyProtocolConstructionHelper.TakeHeadAndMessage(CurrentSession.CompletedBuffer);
        //            _decompressData = GZipHelper.Decompress(waitDecompressData);
        //        }
        //        return _decompressData;
        //    }

        //    set => _decompressData = value;
        //}

        /// <summary>
        /// 缓冲区
        /// </summary>
        public virtual List<byte> ListByteBuffer
        {
            get;
            set;
        } = new List<byte>();

        /// <summary>
        /// 设置当前会话
        /// </summary>
        /// <param name="session"></param>
        public void SetSession(TcpSocketSaeaSession session, long id, byte[] ackData)
        {
            Id = id;
            CurrentSession = session;
            CompletedBuffer = ackData;
        }

        /// <summary>
        /// 消息处理
        /// </summary>
        public void OnMessage(int receiveLength)
        {
            ReceiveTransferredBytes = receiveLength;

            int defineHeadSize = sizeof(int);
            do
            {
                if (ListByteBuffer.Count < defineHeadSize)
                    return;

                byte[] lenBytes = ListByteBuffer.GetRange(0, defineHeadSize).ToArray();
                int packageLen = BitConverter.ToInt32(lenBytes, 0);

                if (packageLen < 0)
                    throw new Exception("Illegal length!");

                if (packageLen + defineHeadSize > ListByteBuffer.Count)
                    return;

                var cproxyData = ListByteBuffer.GetRange(defineHeadSize, packageLen).ToArray();

                this.CompletedBuffer = GZipHelper.Decompress(TakeHeadAndMessage(cproxyData));
                this.DataReceivedEventHandler?.Invoke(this);
                ListByteBuffer.RemoveRange(0, packageLen + defineHeadSize);

            } while (ListByteBuffer.Count > defineHeadSize);
        }

        public override void SendAsync(byte[] data, int offset, int length)
        {
            var reoffsetData = GZipHelper.Compress(data, offset, length);
            var cproxyData = MessageHelper.CopyMessageHeadTo(MessageHead.APP_MESSAGE_DATA,
                new MessageDataPacket()
                {
                    AccessId = ApplicationConfiguartion.Options.AccessId,
                    DispatcherId = this.Id,
                    Data = WrapAccessId(reoffsetData, ApplicationConfiguartion.Options.AccessId)
                });

            this.CurrentSession.SendAsync(cproxyData);

            this.SendTransferredBytes = cproxyData.Length;
            this.DataSendEventHandler?.Invoke(this);
        }
        /// <summary>
        /// 不支持关闭代理连接
        /// </summary>
        public override void SessionClose(bool notify = true)
        {
            throw new NotImplementedException("not support");
        }

        public override void SetSocketOptions(SocketOptionLevel optionLevel, SocketOptionName optionName, object optionValue)
        {
            throw new NotImplementedException("not support");
        }

        public override void Dispose()
        {
            ListByteBuffer.Clear();
            base.Dispose();
        }
    }
}
