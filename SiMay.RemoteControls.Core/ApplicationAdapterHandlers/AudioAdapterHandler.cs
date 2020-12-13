﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SiMay.Basic;
using SiMay.Core;
using SiMay.ModelBinder;
using SiMay.Net.SessionProvider;

namespace SiMay.RemoteControls.Core
{
    [ApplicationName(ApplicationNameConstant.REMOTE_AUDIO)]
    public class AudioAdapterHandler : ApplicationBaseAdapterHandler
    {
        public event Action<AudioAdapterHandler, byte[]> OnPlayerEventHandler;

        [PacketHandler(MessageHead.C_AUDIO_DATA)]
        private void PlayerData(SessionProviderContext session)
        {
            var payload = session.GetMessage();
            this.OnPlayerEventHandler?.Invoke(this, payload);
        }

        public async Task<(bool? playerEnabled, bool? recordEnabled)> StartRemoteAudio(int samplesPerSecond, int bitsPerSample, int channels)
        {
            var responsed = await SendTo(MessageHead.S_AUDIO_START,
                new AudioOptionsPacket()
                {
                    SamplesPerSecond = samplesPerSecond,
                    BitsPerSample = bitsPerSample,
                    Channels = channels
                });
            if (!responsed.IsNull() && responsed.IsOK)
            {
                var statesPack = responsed.Datas.GetMessageEntity<AudioDeviceStatesPacket>();
                return (statesPack.PlayerEnable, statesPack.RecordEnable);
            }

            return (null, null);
        }

        /// <summary>
        /// 发送声音到远程
        /// </summary>
        /// <param name="payload"></param>
        public void SendVoiceDataToRemote(byte[] payload)
        {
            SendToAsync(MessageHead.S_AUDIO_DATA, payload);
        }


        /// <summary>
        /// 设置远程启用发送语音流
        /// </summary>
        /// <param name="enabled"></param>
        public async Task SetRemotePlayerStreamEnabled(bool enabled)
        {
            await SendTo(MessageHead.S_AUDIO_DEIVCE_ONOFF, enabled ? "1" : "0");
        }
    }
}
