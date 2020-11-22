﻿using SiMay.Basic;
using SiMay.Core;
using SiMay.ModelBinder;
using SiMay.Net.SessionProvider;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;

namespace SiMay.Service.Core
{
    public class WebSimpleService : RemoteSimpleServiceBase
    {
        private IDictionary<int, HttpDownloadTaskItemContext> _downloadContexts = new Dictionary<int, HttpDownloadTaskItemContext>();

        [PacketHandler(MessageHead.S_SIMPLE_JOIN_HTTP_DOWNLOAD)]
        public HttpDownloadTaskItemContext JoinHttpDownload(SessionProviderContext session)
        {
            var downTask = session.GetMessageEntity<JoinHttpDownloadPacket>();
            var targetPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), downTask.FileName);
            if (File.Exists(targetPath))
                throw new ArgumentException("文件已存在");

            var context = new HttpDownloadTaskItemContext()
            {
                FileName = targetPath,
            };
            var web = new WebClient();
            //var responseStream = await web.OpenReadTaskAsync(new Uri(downTask.Url));

            web.DownloadProgressChanged += Web_DownloadProgressChanged;
            web.DownloadFileCompleted += Web_DownloadFileCompleted;
            web.DownloadFileAsync(new Uri(downTask.Url), targetPath, context);
            return _downloadContexts[context.Id] = context;
        }

        private void Web_DownloadFileCompleted(object sender, System.ComponentModel.AsyncCompletedEventArgs e)
        {
            var context = e.UserState as HttpDownloadTaskItemContext;
            if (e.Error.IsNull())
                context.Status = 2;
            else
                context.Status = 1;
        }

        private void Web_DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
        {
            var context = e.UserState as HttpDownloadTaskItemContext;
            context.TotalBytesToReceive = e.TotalBytesToReceive;
            context.BytesReceived = e.BytesReceived;

            if (context.Status == 1)
                sender.ConvertTo<WebClient>().CancelAsync();//任务停止
        }

        [PacketHandler(MessageHead.S_SIMPLE_SET_HTTP_DOWNLOAD_STATUS)]
        public void SetHttpDownloadStatus(SessionProviderContext session)
        {
            if (int.TryParse(session.GetMessage().ToUnicodeString(), out var id) && _downloadContexts.ContainsKey(id))
                _downloadContexts[id].Status = 1;
        }

        [PacketHandler(MessageHead.S_SIMPLE_HTTP_DOWNLOAD_STATUS_LIST)]
        public HttpDownloadStatusList GetHttpDownloadStatusContexts(SessionProviderContext session)
        {
            return new HttpDownloadStatusList()
            {
                httpDownloadTaskItemContexts = _downloadContexts.Values.ToArray()
            };
        }

        [PacketHandler(MessageHead.S_SIMPLE_REMOVE_TASK)]
        public void RemoveTask(SessionProviderContext session)
        {
            var id = int.Parse(session.GetMessage().ToUnicodeString());

            if (_downloadContexts.ContainsKey(id) && _downloadContexts[id].Status != 0)
            {
                File.Delete(_downloadContexts[id].FileName);
                _downloadContexts.Remove(id);
            }
        }

    }
}
