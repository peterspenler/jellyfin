﻿using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Extensions;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.LiveTv;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.MediaInfo;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MediaBrowser.Server.Implementations.LiveTv.TunerHosts
{
    public class M3UTunerHost : BaseTunerHost, ITunerHost
    {
        public M3UTunerHost(IConfigurationManager config, ILogger logger)
            : base(config, logger)
        {
        }

        public override string Type
        {
            get { return "m3u"; }
        }

        public string Name
        {
            get { return "M3U Tuner"; }
        }

        private const string ChannelIdPrefix = "m3u_";

        protected override async Task<IEnumerable<ChannelInfo>> GetChannelsInternal(TunerHostInfo info, CancellationToken cancellationToken)
        {
            var url = info.Url;
            var urlHash = url.GetMD5().ToString("N");

            int position = 0;
            string line;
            // Read the file and display it line by line.
            var file = new StreamReader(url);
            var channels = new List<M3UChannel>();
            while ((line = file.ReadLine()) != null)
            {
                line = line.Trim();
                if (!String.IsNullOrWhiteSpace(line))
                {
                    if (position == 0 && !line.StartsWith("#EXTM3U"))
                    {
                        throw new ApplicationException("wrong file");
                    }
                    if (position % 2 == 0)
                    {
                        if (position != 0)
                        {
                            channels.Last().Path = line;
                        }
                        else
                        {
                            line = line.Replace("#EXTM3U", "");
                            line = line.Trim();
                            var vars = line.Split(' ').ToList();
                            foreach (var variable in vars)
                            {
                                var list = variable.Replace('"', ' ').Split('=');
                                switch (list[0])
                                {
                                    case ("id"):
                                        //_id = list[1];
                                        break;
                                }
                            }
                        }
                    }
                    else
                    {
                        if (!line.StartsWith("#EXTINF:")) { throw new ApplicationException("Bad file"); }
                        line = line.Replace("#EXTINF:", "");
                        var nameStart = line.LastIndexOf(',');
                        line = line.Substring(0, nameStart);
                        var vars = line.Split(' ').ToList();
                        vars.RemoveAt(0);
                        channels.Add(new M3UChannel());
                        foreach (var variable in vars)
                        {
                            var list = variable.Replace('"', ' ').Split('=');
                            switch (list[0])
                            {
                                case "tvg-id":
                                    channels.Last().Id = ChannelIdPrefix + urlHash + list[1];
                                    channels.Last().Number = list[1];
                                    break;
                                case "tvg-name":
                                    channels.Last().Name = list[1];
                                    break;
                            }
                        }
                    }
                    position++;
                }
            }
            file.Close();
            return channels;
        }

        public Task<List<LiveTvTunerInfo>> GetTunerInfos(CancellationToken cancellationToken)
        {
            var list = GetConfiguration().TunerHosts
            .Where(i => i.IsEnabled && string.Equals(i.Type, Type, StringComparison.OrdinalIgnoreCase))
            .Select(i => new LiveTvTunerInfo()
            {
                Name = Name,
                SourceType = Type,
                Status = LiveTvTunerStatus.Available,
                Id = i.Url.GetMD5().ToString("N"),
                Url = i.Url
            })
            .ToList();

            return Task.FromResult(list);
        }

        protected override async Task<MediaSourceInfo> GetChannelStream(TunerHostInfo info, string channelId, string streamId, CancellationToken cancellationToken)
        {
            var urlHash = info.Url.GetMD5().ToString("N");
            var prefix = ChannelIdPrefix + urlHash;
            if (!channelId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            channelId = channelId.Substring(prefix.Length);

            var channels = await GetChannels(info, true, cancellationToken).ConfigureAwait(false);
            var m3uchannels = channels.Cast<M3UChannel>();
            var channel = m3uchannels.FirstOrDefault(c => c.Id == channelId);
            if (channel != null)
            {
                var path = channel.Path;
                MediaProtocol protocol = MediaProtocol.File;
                if (path.StartsWith("http"))
                {
                    protocol = MediaProtocol.Http;
                }
                else if (path.StartsWith("rtmp"))
                {
                    protocol = MediaProtocol.Rtmp;
                }
                else if (path.StartsWith("rtsp"))
                {
                    protocol = MediaProtocol.Rtsp;
                }

                return new MediaSourceInfo
                {
                    Path = channel.Path,
                    Protocol = protocol,
                    MediaStreams = new List<MediaStream>
                    {
                        new MediaStream
                        {
                            Type = MediaStreamType.Video,
                            // Set the index to -1 because we don't know the exact index of the video stream within the container
                            Index = -1,
                            IsInterlaced = true
                        },
                        new MediaStream
                        {
                            Type = MediaStreamType.Audio,
                            // Set the index to -1 because we don't know the exact index of the audio stream within the container
                            Index = -1

                        }
                    },
                    RequiresOpening = false,
                    RequiresClosing = false
                };
            }
            throw new ApplicationException("Host doesnt provide this channel");
        }

        class M3UChannel : ChannelInfo
        {
            public string Path { get; set; }

            public M3UChannel()
            {
            }
        }

        public async Task Validate(TunerHostInfo info)
        {
            if (!File.Exists(info.Url))
            {
                throw new FileNotFoundException();
            }
        }

        protected override bool IsValidChannelId(string channelId)
        {
            return channelId.StartsWith(ChannelIdPrefix, StringComparison.OrdinalIgnoreCase);
        }

        protected override Task<List<MediaSourceInfo>> GetChannelStreamMediaSources(TunerHostInfo info, string channelId, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        protected override Task<bool> IsAvailableInternal(TunerHostInfo tuner, string channelId, CancellationToken cancellationToken)
        {
            return Task.FromResult(true);
        }
    }
}
