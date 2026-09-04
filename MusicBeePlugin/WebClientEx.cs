using System;
using System.Net;

namespace MusicBeePlugin
{
    class WebClientEx : WebClient
    {
        public string PreviousReferer { get; private set; }
        public CookieContainer CookieContainer { get; set; }
        public int TimeoutMs { get; set; } = 8000;

        public WebClientEx()
            : base()
        {
            CookieContainer = new CookieContainer();
            ServicePointManager.Expect100Continue = false;
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
            }
            catch { }

            Headers[HttpRequestHeader.UserAgent] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36";
            Headers[HttpRequestHeader.Accept] = "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8";
            Headers["Accept-Language"] = "ja,en-US;q=0.9,en;q=0.8";
        }

        protected override WebRequest GetWebRequest(Uri address)
        {
            var request = base.GetWebRequest(address);

            if (request is HttpWebRequest req)
            {
                if (PreviousReferer != null)
                {
                    req.Referer = PreviousReferer;
                }
                req.CookieContainer = CookieContainer;
                req.Timeout = TimeoutMs;
                req.ReadWriteTimeout = TimeoutMs;
            }
            return request;
        }

        protected override WebResponse GetWebResponse(WebRequest request)
        {
            var response = base.GetWebResponse(request);
            PreviousReferer = response.ResponseUri.AbsoluteUri;

            return response;
        }
    }
}
