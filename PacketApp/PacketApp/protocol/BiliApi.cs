using System;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Xml.XPath;

namespace PacketApp {
    public class BiliApi {

        internal static class Const {
            public const string AppKey = "<Bilibili App Key Here>";
            public const string SecretKey = "<Bilibili App Secret Key Here>";
            public const string CidUrl = "http://live.bilibili.com/api/player?id=cid:";
            public static readonly string[] DefaultHosts = new string[2] { "livecmt-2.bilibili.com", "livecmt-1.bilibili.com" };
            public const string DefaultChatHost = "chat.bilibili.com";
            public const int DefaultChatPort = 2243;
        }

        public readonly string userAgent;

        public BiliApi (string userAgent) {
            this.userAgent = userAgent;
        }

        public bool getDmServerAddr (string roomId, out string url, out string port, out bool mayNotExist) {
            //Get real danmaku server url
            url = string.Empty;
            port = string.Empty;
            mayNotExist = false;
            //Download xml file
            var client = getBaseWebClient ();
            string xmlText = null;
            try {
                xmlText = client.DownloadString (Const.CidUrl + roomId);
            } catch (WebException e) {
                e.printStackTrace ();
                var errorResponse = e.Response as HttpWebResponse;
                if (errorResponse != null && errorResponse.StatusCode == HttpStatusCode.NotFound) {
                    System.Diagnostics.Debug.WriteLine ("ERROR", $"Maybe {roomId} is not a valid room id.");
                    mayNotExist = true;
                } else {
                    System.Diagnostics.Debug.WriteLine ("ERROR", "Download cid xml fail : " + e.Message);
                }
            } catch (Exception e) {
                e.printStackTrace ();
                System.Diagnostics.Debug.WriteLine ("ERROR", "Download cid xml fail : " + e.Message);
            }
            if (string.IsNullOrEmpty (xmlText)) {
                return false;
            }

            //Analyzing danmaku Xml
            XElement doc = null;
            try {
                doc = XElement.Parse ("<root>" + xmlText + "</root>");
                url = doc.Element ("dm_server").Value;
                port = doc.Element ("dm_port").Value;
                return true;
            } catch (Exception e) {
                e.printStackTrace ();
                System.Diagnostics.Debug.WriteLine ("ERROR", "Analyzing XML fail : " + e.Message);
                return false;
            }
        }

        public bool tryGetRoomIdAndUrl (string roomId, out string realRoomId, out string flvUrl) {
            flvUrl = string.Empty;
            realRoomId = getRealRoomId (roomId);
            if (string.IsNullOrEmpty (realRoomId)) {
                return false;
            }
            //Step2.Get flv url
            if (tryGetRealUrl (realRoomId, out flvUrl))
                return true;
            return false;
        }

        public string getRealRoomId (string originalRoomId) {
            System.Diagnostics.Debug.WriteLine ("INFO", "Trying to get real roomId");

            var roomWebPageUrl = "http://live.bilibili.com/" + originalRoomId;
            var wc = new WebClient ();
            wc.Headers.Add ("Accept: text/html");
            wc.Headers.Add ("User-Agent: " + userAgent);
            wc.Headers.Add ("Accept-Language: zh-CN,zh;q=0.8,en;q=0.6,ja;q=0.4");
            string roomHtml;

            try {
                roomHtml = wc.DownloadString (roomWebPageUrl);
            } catch (Exception e) {
                e.printStackTrace ();
                System.Diagnostics.Debug.WriteLine ("ERROR", "Open live page fail : " + e.Message);
                return null;
            }

            //Get real room id from HTML
            const string pattern = @"(?<=var ROOMID = )(\d+)(?=;)";
            var cols = Regex.Matches (roomHtml, pattern);
            foreach (Match mat in cols) {
                System.Diagnostics.Debug.WriteLine ("INFO", "Real Room Id : " + mat.Value);
                return mat.Value;
            }

            System.Diagnostics.Debug.WriteLine ("ERROR", "Fail Get Real Room Id");
            return null;
        }

        public bool tryGetRealUrl (string realRoomId, out string realUrl) {
            realUrl = string.Empty;
            try {
                realUrl = getRealUrl (realRoomId);
            } catch (Exception e) {
                e.printStackTrace ();
                System.Diagnostics.Debug.WriteLine ("ERROR", "Get real url fail, Msg : " + e.Message);
                return false;
            }
            return !string.IsNullOrEmpty (realUrl);
        }

        public string getRealUrl (string roomId) {
            if (roomId == null) {
                System.Diagnostics.Debug.WriteLine ("ERROR", "Invalid operation, No roomId");
                return string.Empty;
                throw new Exception ("No roomId");
            }
            var apiUrl = getApiUrl (roomId);

            string xmlText;

            //Get xml by API
            var wc = getBaseWebClient ();
            try {
                xmlText = wc.DownloadString (apiUrl);
            } catch (Exception e) {
                System.Diagnostics.Debug.WriteLine ("ERROR", "Fail sending analysis request : " + e.Message);
                throw e;
            }

            //Analyzing xml
            string realUrl = string.Empty;
            try {
                var playUrlXml = XDocument.Parse (xmlText);
                var result = playUrlXml.XPathSelectElement ("/video/result");
                //Get analyzing result
                if (result == null ||"suee" != result.Value) {
                    System.Diagnostics.Debug.WriteLine ("ERROR", "Analyzing url address fail");
                    throw new Exception ("No Avaliable download url in xml information.");
                }
                realUrl = playUrlXml.XPathSelectElement ("/video/durl/url").Value;
            } catch (Exception e) {
                e.printStackTrace ();
                System.Diagnostics.Debug.WriteLine ("ERROR", "Analyzing XML fail : " + e.Message);
                throw e;
            }
            if (!string.IsNullOrEmpty (realUrl)) {
                System.Diagnostics.Debug.WriteLine ("INFO", "Analyzing url address successful : " + realUrl);
            }
            return realUrl;
        }

        private string getApiUrl (string roomId) {
            //Generate parameters
            var apiParams = new StringBuilder ().Append ("appkey=").Append (Const.AppKey).Append ("&")
                .Append ("cid=").Append (roomId).Append ("&")
                .Append ("player=1&quality=0&ts=");
            var ts = DateTime.UtcNow - new DateTime (1970, 1, 1, 0, 0, 0, 0); //UNIX TimeStamp
            apiParams.Append (Convert.ToInt64 (ts.TotalSeconds).ToString ());

            var apiParam = apiParams.ToString (); //Origin parameters string

            //Generate signature
            var waitForSign = apiParam + Const.SecretKey;
            var waitForSignBytes = Encoding.UTF8.GetBytes (waitForSign);
            MD5 md5 = new MD5CryptoServiceProvider ();
            var signBytes = md5.ComputeHash (waitForSignBytes);

            var sign = signBytes.Aggregate ("", (current, t) => current + t.ToString ("x"));

            //Final API
            return "http://live.bilibili.com/api/playurl?" + apiParam + "&sign=" + sign;
        }

        private WebClient getBaseWebClient () {
            var client = new WebClient ();
            client.Headers.Add ("Accept: */*");
            client.Headers.Add ("User-Agent: " + userAgent);
            client.Headers.Add ("Accept-Language: zh-CN,zh;q=0.8,en;q=0.6,ja;q=0.4");
            return client;
        }
    }
}