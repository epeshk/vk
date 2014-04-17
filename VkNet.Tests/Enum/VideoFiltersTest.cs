﻿
using VkNet.Enums.Filters;

namespace VkNet.Tests.Enum
{
    using NUnit.Framework;
    using VkNet.Enums;
    using VkNet.Utils.Tests;

    [TestFixture]
    public class VideoFiltersTest
    {
        [Test]
        public void ToString_EveryItems()
        {
            VideoFilters mp4 = VideoFilters.Mp4;
            VideoFilters youtube = VideoFilters.Youtube;
            var vimeo = VideoFilters.Vimeo;
            var _short = VideoFilters.Short;
            var _long = VideoFilters.Long;
            var all = VideoFilters.All;
            var shortLong = _short | _long;

            mp4.ToString().ShouldEqual("mp4");
            youtube.ToString().ShouldEqual("youtube");
            vimeo.ToString().ShouldEqual("vimeo");
            _long.ToString().ShouldEqual("long");
            _short.ToString().ShouldEqual("short");
            all.ToString().ShouldEqual("mp4,youtube,vimeo,short,long");
            shortLong.ToString().ShouldEqual("short,long");
        }
    }
}