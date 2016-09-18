using Microsoft.ProjectOxford.Face;
using Microsoft.ProjectOxford.Face.Contract;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace ImageCollector
{
    class Program
    {
        static string bingApiKey = "6b92b8aafcf4408fbab7ba3bab0550c8";
        static string faceApiKey = "275f7ae3c0ca42fda3eca8bee0956fad";
        static void Main(string[] args)
        {
            var start = DateTime.Now;
            var learnDataPath = "data.csv";
            Task.Run(async () =>
            {

                using (var writer = new StreamWriter(learnDataPath, false, Encoding.UTF8))
                {
                    for(int i = 1; i <= 12; i++)
                    {
                        writer.Write(string.Format("param{0},",i));
                    }
                    writer.WriteLine("label");
                    var smileImages = await getSearchImageAsync("笑顔");
                    foreach (var url in smileImages)
                    {
                        try
                        {
                            var client = new FaceServiceClient(faceApiKey);
                            var faces = await client.DetectAsync(url, true, true);
                            await Task.Delay(4000);
                            foreach (var face in faces)
                            {
                                var features = getFaceFeature(face);
                                writer.WriteLine(string.Join(",", features) + ",smile");
                                Console.WriteLine("detect feature {0}", url);
                            }
                        }
                        catch (FaceAPIException e)
                        {
                            Console.WriteLine("face expception");
                        }
                    }

                    var angryImages = await getSearchImageAsync("怒り 写真");
                    foreach (var url in angryImages)
                    {
                        try
                        {
                            var client = new FaceServiceClient(faceApiKey);
                            var faces = await client.DetectAsync(url, true, true);
                            await Task.Delay(4000);
                            foreach (var face in faces)
                            {
                                var features = getFaceFeature(face);
                                writer.WriteLine(string.Join(",", features) + ",angry");
                                Console.WriteLine("detect feature {0}", url);
                            }
                        }
                        catch (FaceAPIException e)
                        {
                            Console.WriteLine("face expception");
                        }
                    }
                }

            }).Wait();
            Console.WriteLine();
            Console.WriteLine("elapsed time {0}",(DateTime.Now - start).ToString());
            Console.ReadKey();
        }

        private static async Task<List<string>> getSearchImageAsync(string searchWord)
        {
            var images = new List<string>();
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", bingApiKey);

                int count = 150;
                for (int offset = 0; offset <= 300; offset += count)
                {
                    var uri = "https://api.cognitive.microsoft.com/bing/v5.0/images/search"
                        + "?q=" + searchWord
                        + "&count=" + count
                        + "&offset=" + offset
                        + "&mkt=ja-JP";

                    var response = await client.GetAsync(uri);
                    var json = await response.Content.ReadAsStringAsync();
                    var jObj = JObject.Parse(json);
                    JToken value = jObj.SelectToken("value");
                    images.AddRange(value.Select(q => q["contentUrl"].ToString()).ToList());
                    await Task.Delay(400);
                }
            }
                
            return images;
        }

        private static List<double> getFaceFeature(Face face)
        {
            var result = new List<double>();
            var marks = face.FaceLandmarks;

            //Topから目までの長さの顔の長さとの比
            result.Add((marks.EyeRightTop.Y - face.FaceRectangle.Top) / face.FaceRectangle.Height);
            //目から鼻までの長さの顔の長さとの比
            result.Add((marks.NoseTip.Y - marks.EyeRightTop.Y) / face.FaceRectangle.Height);
            //鼻から顎までの長さの顔の長さとの比
            result.Add((face.FaceRectangle.Top + face.FaceRectangle.Height - marks.NoseTip.Y)
                / face.FaceRectangle.Height);
            //下唇の位置の鼻から顎までの長さとの比(上)
            result.Add((marks.UnderLipBottom.Y - marks.NoseTip.Y)
                / (face.FaceRectangle.Top + face.FaceRectangle.Height - marks.NoseTip.Y));
            //下唇の位置の鼻から顎までの長さとの比(下)
            result.Add((face.FaceRectangle.Top + face.FaceRectangle.Height - marks.UnderLipBottom.Y)
                / (face.FaceRectangle.Top + face.FaceRectangle.Height - marks.NoseTip.Y));
            //唇の位置の鼻から顎までの長さとの比(上)
            result.Add((marks.MouthRight.Y - marks.NoseTip.Y)
                / (face.FaceRectangle.Top + face.FaceRectangle.Height - marks.NoseTip.Y));
            //唇の位置の鼻から顎までの長さとの比(下)
            result.Add((face.FaceRectangle.Top + face.FaceRectangle.Height - marks.MouthRight.Y)
                / (face.FaceRectangle.Top + face.FaceRectangle.Height - marks.NoseTip.Y));

            //左耳から左目までの顔の横幅との比
            result.Add((marks.EyeLeftOuter.X - face.FaceRectangle.Left) / face.FaceRectangle.Width);
            //左目の幅の顔の横幅との比
            result.Add((marks.EyeLeftInner.X - marks.EyeLeftOuter.X) / face.FaceRectangle.Width);
            //目の間の顔の横幅との比
            result.Add((marks.EyeRightInner.X - marks.EyeLeftInner.X) / face.FaceRectangle.Width);
            //右目の幅の顔の横幅との比
            result.Add((marks.EyeRightOuter.X - marks.EyeRightInner.X) / face.FaceRectangle.Width);
            //右目から右耳までの顔の横幅との比
            result.Add((face.FaceRectangle.Left + face.FaceRectangle.Width - marks.EyeRightOuter.X)
                / face.FaceRectangle.Width);

            return result;

        }


    }
}
