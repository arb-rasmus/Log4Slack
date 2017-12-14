using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace Log4Slack {
    /// <summary>
    /// Simple client for Stack using incoming webhooks.
    /// </summary>
    public class SlackClient {
        private readonly Uri _uri;
        private readonly Encoding _encoding = Encoding.UTF8;
        private readonly string _username;
        private readonly string _channel;
        private readonly string _iconUrl;
        private readonly string _iconEmoji;
        private readonly ArrayList _requests = ArrayList.Synchronized(new ArrayList(4));

        /// <summary>
        /// Creates a new instance of SlackClient.
        /// </summary>
        /// <param name="urlWithAccessToken">The incoming webhook URL with token.</param>
        public SlackClient(string urlWithAccessToken) {
            _uri = new Uri(urlWithAccessToken);
        }

        /// <summary>
        /// Post a message to Slack.
        /// </summary>
        /// <param name="text">The text of the message.</param>
        /// <param name="proxyAddress">If provided, uses this proxy address when posting payloads.</param>
        /// <param name="attachments">Optional collection of attachments.</param>
        /// <param name="linknames">Whether or not to link names in the Slack message.</param>
        public void PostMessageAsync(string text, string proxyAddress, List<Attachment> attachments = null) {
            var payload = BuildPayload(text, attachments);
            PostPayloadAsync(payload, proxyAddress);
        }

        /// <summary>
        /// Builds a payload for Slack.
        /// </summary>
        /// <param name="text"></param>
        /// <param name="attachments"></param>
        /// <param name="linknames"></param>
        /// <returns></returns>
        private Payload BuildPayload(string text, List<Attachment> attachments = null, bool linknames = false) {

            var payload = new Payload {
                Text = text,
                Attachments = attachments
            };

            return payload;
        }

        /// <summary>
        /// Posts a payload to Slack.
        /// </summary>
        private void PostPayloadAsync(Payload payload, string proxyAddress) {
            var data = JsonSerializeObject(payload);
            //data = "{ \"text\": \"Awesome text\", \"attachments\":[{\"title\": \"Awesome title\",\"description\": \"Awesome description\",\"color\": \"#0ABE51\",\"views\": {\"flockml\": \"<strong>Test</strong>\"}}]}";
            PostPayloadAsync(data, proxyAddress);
        }

        protected virtual void PostPayloadAsync(string json, string proxyAddress) {
            HttpWebRequest request = null;

            try {
                request = (HttpWebRequest)WebRequest.Create(_uri);
                request.Method = "POST";
                request.ContentType = "application/x-www-form-urlencoded";

                if (!string.IsNullOrEmpty(proxyAddress))
                {
                    Uri uri = new Uri(proxyAddress);
                    request.Proxy = new WebProxy(uri);
                }

                var data = _encoding.GetBytes(json);
                request.ContentLength = data.Length;
                request.Method = "POST";
                request.ContentType = "application/json";

                // Get the request stream into which the form data is to 
                // be written. This is done asynchronously to free up this
                // thread.

                // NOTE: We maintain a (possibly paranoid) list of 
                // outstanding requests and add the request to it so that 
                // it does not get treated as garbage by GC. In effect, 
                // we are creating an explicit root. It is also possible
                // for this module to get disposed before a request
                // completes. During the callback, no other member should
                // be touched except the requests list!

                _requests.Add(request);

                var ar = request.BeginGetRequestStream(OnGetRequestStreamCompleted, AsyncArgs(request, data));
            }
            catch (Exception localException) {
                OnWebPostError(request, localException);
            }
        }

        private void OnWebPostError(WebRequest request, Exception e) {
            if (request != null) _requests.Remove(request);
        }

        private static object[] AsyncArgs(params object[] args) {
            return args;
        }

        private void OnGetRequestStreamCompleted(IAsyncResult ar) {
            if (ar == null) throw new ArgumentNullException("ar");
            var args = (object[])ar.AsyncState;
            OnGetRequestStreamCompleted(ar, (WebRequest)args[0], (byte[])args[1]);
        }

        private void OnGetRequestStreamCompleted(IAsyncResult ar, WebRequest request, byte[] data)
        {
            try {
                using (var output = request.EndGetRequestStream(ar)) {
                    output.Write(data, 0, data.Length);
                }
                request.BeginGetResponse(OnGetResponseCompleted, request);
            }
            catch (Exception e) {
                OnWebPostError(request, e);
            }
        }

        private void OnGetResponseCompleted(IAsyncResult ar) {
            if (ar == null) throw new ArgumentNullException("ar");
            OnGetResponseCompleted(ar, (WebRequest)ar.AsyncState);
        }

        private void OnGetResponseCompleted(IAsyncResult ar, WebRequest request) {
            try {
                request.EndGetResponse(ar).Close(); // Not interested; assume OK
                _requests.Remove(request);
            }
            catch (Exception e) {
                OnWebPostError(request, e);
            }
        }

        private static string JsonSerializeObject(object obj) {
            var serializer = new DataContractJsonSerializer(obj.GetType());
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, obj);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }
    }


    /// <summary>
    /// The payload to send to Stack, which will be serialized to JSON before POSTing.
    /// </summary>
    [DataContract]
    public class Payload {
        [DataMember(Name = "text")]
        public string Text { get; set; }
        [DataMember(Name = "attachments")]
        public List<Attachment> Attachments { get; set; }
    }

    /// <summary>
    /// It is possible to create more richly-formatted messages using Attachments.
    /// https://api.slack.com/docs/attachments
    /// </summary>
    [DataContract]
    public class Attachment {
        /// <summary>
        /// Optional text that should appear within the attachment.
        /// </summary>
        [DataMember(Name = "title")]
        public string Title { get; set; }

        /// <summary>
        /// Can either be one of 'good', 'warning', 'danger', or any hex color code.
        /// </summary>
        [DataMember(Name = "color")]
        public string Color { get; set; }

        /// <summary>
        /// Fields are displayed in a table on the message.
        /// </summary>
        [DataMember(Name = "views")]
        public View Views { get; set; }

        public Attachment(string title)
        {
            Title = title;
        }
        public Attachment()
        { }
    }

    /// <summary>
    /// Fields are displayed in a table on the message.
    /// </summary>
    [DataContract]
    public class View {
        [DataMember(Name = "html")]
        public Html Html { get; set; }

        public View(Html html) {
            Html = html;
        }
    }

    [DataContract]
    public class Html
    {
        [DataMember(Name = "inline")]
        public string Inline { get; set; }
        [DataMember(Name = "width")]
        public int Width { get; set; }

        [DataMember(Name = "height")]
        public int Height { get; set; }
    }
}
