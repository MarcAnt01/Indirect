﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using InstaSharper.API;
using InstaSharper.Classes.Models.Media;

namespace InstantMessaging.Wrapper
{
    class InstaMediaWrapper : InstaMedia
    {
        private readonly IInstaApi _instaApi;

        public new List<InstaImageWrapper> Images { get; } = new List<InstaImageWrapper>();

        public new List<InstaVideoWrapper> Videos { get; } = new List<InstaVideoWrapper>();

        public new InstaUserWrapper User { get; set; }

        public InstaMediaWrapper(InstaMedia source, IInstaApi api)
        {
            _instaApi = api;
            // todo: finish MediaWrapper
        }
    }
}