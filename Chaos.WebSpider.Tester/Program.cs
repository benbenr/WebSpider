using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading; 
using System.Linq;
using System.Diagnostics;

namespace Chaos.WebSpider.Tester
{
    public class A
    {
        public void T(object i)
        {
            Process ps = new Process();
            ps.StartInfo.FileName = i.ToString();
            ps.Start();
        }

        public void shutdown()
        {
            
        }
    }
    class Program
    {
        //public static string headerCookies = null;
        public static int ThreadCount = 10;
        public static int MaxID = 110000;
        public static int StartID = 100000;
        public static Dictionary<int, List<int>> threadRange = new Dictionary<int, List<int>>();
        static void Main(string[] args)
        {
            Test(123);
            List<int> ids = DBHelper.GetIDs(StartID, MaxID);
            Console.WriteLine("一共有" + ids.Count.ToString() + "需要抓取");
            //Test(12346);
            int range = (int)((MaxID - StartID) / ThreadCount);
            int idsStart = 0;
            int idsRange = (int)(ids.Count / ThreadCount);
            for (int i = 0; i < ThreadCount; i++)
            {
                //threadRange.Add(i, new List<int>() { StartID + (range * i), StartID + (range * (i + 1)) });
                threadRange.Add(i, ids.GetRange(idsStart, idsRange));
                if (i == ThreadCount - 1)
                {
                    threadRange[ThreadCount - 1] = ids;
                }
                else
                {
                    ids.RemoveRange(idsStart, idsRange);
                }
                Thread t = new Thread(new ParameterizedThreadStart(TStartByIds));
                t.Start(i);
            }

            while (true)
            {
                Console.ReadKey();
            }
        }

        public static void TStartByIds(object rangeId)
        {
            int range = (int)rangeId;
            foreach (int i in threadRange[range])
            {
                if (DBHelper.CheckThing(i) == 0)
                {
                    Test(i);
                    Thread.Sleep(1000);
                }
            }
            Console.WriteLine("线程：" + range.ToString() + "已完成。");
        }

        public static void TStart(object rangeId)
        {
            int range = (int)rangeId;
            for (int i = threadRange[range][0]; i < threadRange[range][1]; i++)
            {
                if (DBHelper.CheckThing(i) == 0)
                {
                    Test(i);
                    Thread.Sleep(1000);
                }
            }
            Console.WriteLine("线程：" + range.ToString() + "已完成。");
        }

        public static void InitRequest(HttpWebRequest request)
        {
            request.UserAgent = "Mozilla/5.0 (Windows NT 6.1; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/41.0.2272.101 Safari/537.37";
            request.Accept = "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8";
            NameValueCollection collection = new NameValueCollection();
            collection.Add("Accept-Encoding", "gzip, deflate, sdch");
            collection.Add("Accept-Language", "zh-CN,zh;q=0.8");
            collection.Add("Cache-Control", "max-age=0");
            request.KeepAlive = true;
            request.Headers.Add(collection);
        }

        public static void Test(int thingId)
        {
            string headerCookies = string.Empty;
            HttpWebRequest request = null;
            ThingEntity thing = null;
            string mainUrl = string.Format("http://www.thingiverse.com/thing:{0}", thingId);
            try
            {
                request = (HttpWebRequest)WebRequest.Create(mainUrl);
                InitRequest(request);
                //if (!string.IsNullOrEmpty(headerCookies))
                //{
                //    request.Headers["Cookie"] = headerCookies;
                //}
                HttpWebResponse response = (HttpWebResponse)request.GetResponse();
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    string html = string.Empty;
                    if (response.ContentEncoding.ToLower().Contains("gzip"))
                    {
                        using (GZipStream stream = new GZipStream(response.GetResponseStream(), CompressionMode.Decompress))
                        {
                            using (StreamReader reader = new StreamReader(stream))
                            {
                                html = reader.ReadToEnd();
                            }
                        }
                    }

                    thing = new ThingEntity(html, thingId);
                    if (thing.ThingStatus == 0)
                    {
                        // TODO 不是100% 有ZIP包的，需要做判断
                        //  <a href="/download:215339"  没有ZIP包的标示为not contains("Download All Files"); 需要通过前面的download:215339获取要下载的文件ID.
                        // http://www.thingiverse.com/download:215339 通过这个跳转到amazon上的具体文件. 可能存在多个。
                        request = (HttpWebRequest)WebRequest.Create(thing.ZipDownloadUrl);

                        if (!string.IsNullOrEmpty(headerCookies))
                        {
                            request.Headers["Cookie"] = headerCookies;
                        }
                        else
                        {
                            if (!string.IsNullOrEmpty(response.Headers["Set-Cookie"]))
                            {
                                request.Headers["Cookie"] = response.Headers["Set-Cookie"];
                                headerCookies = response.Headers["Set-Cookie"];
                            }
                        }
                        request.CookieContainer = new CookieContainer();
                        //request.CookieContainer.Add(cookies);
                        request.Referer = mainUrl + "/";
                        InitRequest(request);
                        request.Timeout = 1000 * 30;
                        Thread.Sleep(2000);
                        response = (HttpWebResponse)request.GetResponse();
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            thing.ThingStatus = 2; //拿到具体的ZIP URL
                            thing.ZipName = response.ResponseUri.ToString();
                            if (thing.ZipDownloadUrl != thing.ZipName)
                            {
                                List<string> ds = new List<string>(thing.ImagePaths);
                                ds.Add(thing.ZipName);
                                DownloadFiles(thingId, ds);
                                thing.ThingStatus = 3; // zip 下载成功
                            }
                            else
                            {
                                thing.ThingStatus = 4;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("404"))
                {
                    thing = new ThingEntity(thingId);
                }
                else if (ex.Message.Contains("503"))
                {
                    thing = new ThingEntity(thingId, 503);
                }
                else
                {
                    Console.WriteLine("抓取：" + thingId.ToString() + "异常：" + ex.ToString());
                }
                //request.Abort();
                request = null;
            }
            finally
            {
                try
                {
                    DBHelper.DoSql(thing.InsertSQL());
                    Console.WriteLine(string.Format("抓取ID：{0}的状态为：{1}，时间：{2}", thing.Id, thing.ThingStatus, DateTime.Now));
                }
                catch (Exception Ex)
                {
                    if (thing == null) thing = new ThingEntity() { Id = -1 };
                    Console.WriteLine(string.Format("抓取ID：{0}的状态为：{1}，时间：{2};保存异常！！！:{3}", thing.Id, thing.ThingStatus, DateTime.Now, Ex.ToString()));
                }
            }
        }


        public static void DownloadFiles(int thingId, List<string> files)
        {
            string path = "F:/Thing/" + thingId.ToString();
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
            foreach (string file in files)
            {
                if (file.EndsWith(".zip"))
                {
                    WebClient wc = new WebClient();
                    string fileName = ThingEntity.GetFilesPathFromUrl(file, 1);
                    if ("Untitled" == fileName) fileName = thingId.ToString();
                    wc.DownloadFile(file, path + "/" + fileName);
                }
                else
                {
                    string[] filePath = ThingEntity.GetFilesPathFromUrl(file, 2).Split('/');
                    if (!Directory.Exists(path + "/" + filePath[0]))
                    {
                        Directory.CreateDirectory(path + "/" + filePath[0]);
                    }
                    WebClient wc = new WebClient();
                    wc.DownloadFile(file, path + "/" + string.Join("/", filePath));
                }
            }
        }



    }

    public class ThingEntity
    {
        /// <summary>
        /// 从URL中获取文件路径
        /// </summary>
        /// <param name="url">url地址</param>
        /// <param name="deep">深度</param>
        /// <returns></returns>
        public static string GetFilesPathFromUrl(string url, int deep)
        {
            List<string> paths = url.Split('/').ToList<string>();
            return string.Join("/", paths.GetRange(paths.Count - deep, deep));
        }


        /// <summary>
        /// 获取图片和文件关系的
        /// </summary>
        /// <param name="models"></param>
        /// <param name="images"></param>
        /// <returns></returns>
        public static string GetModelImageRelation(List<string> models, List<string> images)
        {
            Dictionary<string, List<string>> relations = new Dictionary<string, List<string>>();
            foreach (string model in models)
            {
                string fileName = model.Contains(".") ? model.Split('.')[0] : model;
                List<string> currentImages = images.FindAll(item => item.Contains(fileName));
                //currentImages.ForEach(GetUrlFileFromTailTwo);
                if (!relations.ContainsKey(model))
                {
                    relations.Add(model, currentImages);
                }
            }
            StringBuilder builder = new StringBuilder();
            foreach (KeyValuePair<string, List<string>> pair in relations)
            {
                builder.AppendFormat("{0};{1}|", pair.Key, string.Join(",", pair.Value));
            }
            return builder.ToString().TrimEnd('|');
        }

        public string ZipDownloadUrl
        {
            get
            {
                return string.Format("http://www.thingiverse.com/thing:{0}/zip", Id);
            }
        }

        public ThingEntity(int thingId, int status = 404)
        {
            Id = thingId;
            ThingStatus = status;
        }

        public ThingEntity()
        {

        }

        public ThingEntity(string html, int thingId)
        {
            Id = thingId;
            if (html.Contains("<title>404</title>"))
            {
                ThingStatus = 404;
                return;
            }
            string title = Regex.Match(html, "<title>(.*?) - Thingiverse</title>", RegexOptions.IgnoreCase).Groups[1].Value;
            if (title.Contains("by"))
            {
                string[] titles = title.Replace(" by ", "|").Split('|');
                Name = titles[0];
                Creator = titles[1];
            }

            MatchCollection matchsImg = Regex.Matches(html, "-url=\"(.*?)\"");
            Dictionary<string, string> imgs = new Dictionary<string, string>();
            foreach (Match matchimg in matchsImg)
            {
                if (!ImagePaths.Contains(matchimg.Groups[1].Value) &&
                    !string.IsNullOrEmpty(matchimg.Groups[1].Value) &&
                    matchimg.Groups[1].Value.EndsWith("jpg", true, null))
                {
                    ImagePaths.Add(matchimg.Groups[1].Value);
                    ImageFilePaths.Add(GetFilesPathFromUrl(matchimg.Groups[1].Value, 2));
                }
            }
            /* 图片规则：
             * http://thingiverse-production-new.s3.amazonaws.com/renders/61/25/6e/f7/0a/spacer_32x10x10_preview_featured.jpg
             * 前缀目录固定
             * /xxxxxx_ 固定图片名
             * _display_large.jpg   大图
             * _preview_featured.jpg    中图
             * _preview_tinycard.jpg    小图
             * 
             * */
            // TODO 图片处理逻辑
            // TODO 下载文件路径ZIP即可
            // TODO 模型图片对应
            // 模型图片地址
            MatchCollection matchFiles = Regex.Matches(html, "data-file-name=\"(.*?)\"");
            foreach (Match matchFile in matchFiles)
            {
                ModelFileName.Add(matchFile.Groups[1].Value);
            }
            Description = Regex.Match(html, "<meta property=\"og:description\" content=\"(.*?)\"", RegexOptions.Singleline).Groups[1].Value.Trim();
            Instructions = Regex.Match(html, "<div id=\"instructions\" class=\"thing-info-content\">(.*?)</div>", RegexOptions.Singleline).Groups[1].Value.Trim();
            CreateDate = DateTime.Parse(Regex.Match(html, "<time datetime=\"(.*?) GMT\">").Groups[1].Value);
            //<a href="/tag:3d_printer">3d_printer</a>
            MatchCollection matchTags = Regex.Matches(html, "<a href=\"/tag:(.*?)\">");
            foreach (Match matchTag in matchTags)
            {
                Tags.Add(matchTag.Groups[1].Value);
            }
            string count = Regex.Match(html, "<span class=\"interaction-count collection-count\">([0-9]*?)</span>").Groups[1].Value;
            if (!string.IsNullOrEmpty(count))
            {
                Collect = int.Parse(count);
            }
            count = Regex.Match(html, "<span class=\"interaction-count\">([0-9]*?)</span>").Groups[1].Value;
            if (!string.IsNullOrEmpty(count))
            {
                Likes = int.Parse(count);
            }
            count = Regex.Match(html, "<span class=\"thing-views\"><span class=\"interaction-count\">([0-9]*?)</span></span>").Groups[1].Value;
            if (!string.IsNullOrEmpty(count))
            {
                Views = int.Parse(count);
            }
            count = Regex.Match(html, "<span class=\"thing-downloads\"><span class=\"interaction-count\">([0-9]*?)</span></span>").Groups[1].Value;
            if (!string.IsNullOrEmpty(count))
            {
                Downloads = int.Parse(count);
            }
            ImageAndFile = GetModelImageRelation(ModelFileName, ImageFilePaths);

            if (html.Contains(">Login</a> to Comment</p>") && ModelFileName.Count == 0)
            {
                ThingStatus = 1;
            }
        }

        /// <summary>
        /// 获取InsertSQL
        /// </summary>
        /// <returns></returns>
        public string InsertSQL()
        {
            return string.Format(@"INSERT INTO [Chaos].[dbo].[Thing]
                                       ([ID]
                                       ,[Status]
                                       ,[Name]
                                       ,[ImagePathMain]
                                       ,[Images]
                                       ,[ModelFileNames]
                                       ,[Description]
                                       ,[Instructions]
                                       ,[Creator]
                                       ,[CreateDate]
                                       ,[Tags]
                                       ,[Collect]
                                       ,[Likes]
                                       ,[Views]
                                       ,[Downloads]
                                       ,[ZipFile]
                                       ,[ImageAndFile]
                                       ,[ImagePaths])
                                 VALUES
                                       ({0}     --<ID, int,>
                                       ,{1}     --<Status, int,>
                                       ,'{2}'   --<Name, nvarchar(400),>
                                       ,'{3}'    --<ImagePathMain, nvarchar(500),>
                                       ,'{4}'     --<Images, nvarchar(max),>
                                       ,'{5}'     --<ModelFileNames, nvarchar(1000),>
                                       ,'{6}'     --<Description, nvarchar(max),>
                                       ,'{7}'     --<Instructions, nvarchar(max),>
                                       ,'{8}'     --<Creator, nvarchar(100),>
                                       ,'{9}'     --<CreateDate, datetime,>
                                       ,'{10}'     --<Tags, nvarchar(1000),>
                                       ,{11}     --<Collect, int,>
                                       ,{12}     --<Likes, int,>
                                       ,{13}     --<Views, int,>
                                       ,{14},'{15}','{16}','{17}')     --<Downloads, int,>) ",
                                Id, ThingStatus, Name.Replace("'", "''"), ImagePaths.Count > 0 ? ImagePaths[0] : string.Empty, string.Join(";", ImagePaths),
                             string.Join(";", ModelFileName), Description.Replace("'", "''"), Instructions.Replace("'", "''"),
                             Creator.Replace("'", "''"), CreateDate, string.Join(";", Tags), Collect, Likes, Views, Downloads, ZipName.Replace("'", "''"), ImageAndFile, string.Join(",", ImageFilePaths));
        }

        /// <summary>
        /// Name <para />
        /// <title>Princess Bubblegum Crown by CarryTheWhat - Thingiverse</title>
        /// </summary>
        public string Name = string.Empty;
        /// <summary>
        /// 具体ID。
        /// </summary>
        public int Id = 0;
        /// <summary>
        /// -url="http://thingiverse-production-new.s3.amazonaws.com/renders/7a/75/07/a0/3f/DSC_9728_preview_tinycard.JPG"
        /// </summary>
        public List<string> ImagePaths = new List<string>();
        /// <summary>
        /// 模型下载路径 包括下面的图片和文件的匹配
        /// <div class="thing-file">
        /// </summary>
        public List<string> ModelFileName = new List<string>();
        /// <summary>
        /// zip包文件名
        ///  <a href="/thing:728531/zip" 打包下载
        /// </summary>
        public string ZipName = string.Empty;
        /// <summary>
        /// 同模型
        /// fileName : data-file-name="spacer_32x10x5.stl"
        /// imgPath : https://thingiverse-production-new.s3.amazonaws.com/renders/ff/9b/82/f0/a3/spacer_32x10x5_preview_tinycard.jpg .Contains(fileName+"_preview_tinycard")
        /// </summary>
        public Dictionary<string, string> Image_Model = new Dictionary<string, string>();
        /// <summary>
        /// <meta property="og:description" content=".....
        /// </summary>
        public string Description = string.Empty;
        /// <summary>
        /// <div id="thing-instructions" class="tab-content-holder">...
        /// </summary>
        public string Instructions = string.Empty;
        /// <summary>
        /// 创建者
        /// title - 后的
        /// </summary>
        public string Creator = string.Empty;
        /// <summary>
        /// 创建时间
        /// <time datetime="2015-03-18 07:59:56 GMT">
        /// </summary>
        public DateTime CreateDate = DateTime.Parse("1900-01-01");
        /// <summary>
        /// 标签
        /// <div class="tags">
        /// </summary>
        public List<string> Tags = new List<string>();
        /// <summary>
        /// 收藏数
        /// <span class="interaction-count collection-count">169</span>
        /// </summary>
        public int Collect = 0;
        /// <summary>
        /// 喜欢数
        /// <span class="interaction-count">172</span>
        /// </summary>
        public int Likes = 0;
        /// <summary>
        /// 
        /// <span class="thing-views"><span class="interaction-count">11145</span></span>
        /// </summary>
        public int Views = 0;
        /// <summary>
        /// 
        /// <span class="thing-downloads"><span class="interaction-count">1559</span></span>
        /// </summary>
        public int Downloads = 0;

        // 下载所有的文件
        //<div class="thing-download-zip">

        /// <summary>
        /// 0 正常可以访问 1 需要登录 404 无产品
        /// </summary>
        public int ThingStatus = 0;

        /// <summary>
        /// 按照要求要的文件和图片的关系数据
        /// </summary>
        public string ImageAndFile = string.Empty;
        /// <summary>
        /// 按照要求要的图片实际路径
        /// </summary>
        public List<string> ImageFilePaths = new List<string>();
    }
}
