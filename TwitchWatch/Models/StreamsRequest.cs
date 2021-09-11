using System.Collections.Generic;

namespace TwitchWatch.Services
{
    public partial class TwitchWatchService
    {
        public class StreamsRequest
        {
            public List<Stream> data { get; set; }
            public Pagination pagination { get; set; }
        }
    }
}