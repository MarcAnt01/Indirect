﻿using System.Linq;
using InstagramAPI.Classes.Direct;
using InstagramAPI.Classes.User;
using Newtonsoft.Json;

namespace InstagramAPI.Classes.Responses
{
    public class RankedRecipientsResponse : BaseStatusResponse
    {
        [JsonProperty("expires")] public long Expires { get; set; }

        [JsonProperty("filtered")] public bool Filtered { get; set; }

        [JsonProperty("rank_token")] public string RankToken { get; set; }

        [JsonProperty("request_id")] public string RequestId { get; set; }

        [JsonProperty("ranked_recipients")] public RankedRecipient[] RankedRecipients { get; set; }

        [JsonIgnore]
        public RankedRecipientThread[] Threads => RankedRecipients.Select(response => response.Thread)
            .Where(thread => thread != null).ToArray();

        [JsonIgnore]
        public InstaUser[] Users => RankedRecipients.Select(response => response.User)
            .Where(user => user != null).ToArray();
    }

    public class RankedRecipient
    {
        [JsonProperty("thread")] public RankedRecipientThread Thread { get; set; }

        [JsonProperty("user")] public InstaUser User { get; set; }
    }
}