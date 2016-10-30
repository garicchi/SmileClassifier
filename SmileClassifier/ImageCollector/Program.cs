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
    // Bing APIとFace APIを用いて顔画像学習データを収集するプログラム
    class Program
    {
        //Cognitive Service Bing Image APIのAPI Keyを入れる
        static string _bingApiKey = "6b92b8aafcf4408fbab7ba3bab0550c8";
        //Cognitive Service Face APIのAPI Keyを入れる
        static string _faceApiKey = "275f7ae3c0ca42fda3eca8bee0956fad";

        static void Main(string[] args)
        {
            var watch = new Stopwatch();
            watch.Start();
            var learnDataPath = "data.csv";

            using (var writer = new StreamWriter(learnDataPath, false, Encoding.UTF8))
            {
                //学習データのcsvのヘッダーを作成する
                for (int i = 1; i <= 6; i++)
                {
                    writer.Write(string.Format("param{0},", i));
                }
                writer.WriteLine("label");

                //笑顔の学習データを収集する
                writeFaceFeaturesAsync(writer,"笑顔","smile").Wait();
                //怒りの学習データを収集する
                writeFaceFeaturesAsync(writer, "怒り 写真", "angry").Wait();
                
            }

            watch.Stop();
            Console.WriteLine("elapsed time {0}", watch.Elapsed);
            Console.WriteLine("Complete!");
            Console.ReadKey();
        }

        //指定の検索ワードを用いて画像検索し、特徴データをファイルに書き込む
        private static async Task writeFaceFeaturesAsync(StreamWriter writer,string searchWord,string label)
        {
            //画像検索してURL一覧をimagesに入れる
            var images = new List<string>();
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", _bingApiKey);

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
                    JToken values = jObj.SelectToken("value");
                    foreach (var val in values)
                    {
                        images.Add(val["contentUrl"].ToString());
                    }
                    await Task.Delay(400);
                }
            }

            //画像のURL一覧から特徴量を抽出してファイルに書き込む
            foreach (var url in images)
            {
                try
                {
                    //Face APIで特徴点を抽出する
                    var client = new FaceServiceClient(_faceApiKey);
                    var faces = await client.DetectAsync(url, true, true);
                    await Task.Delay(4000);
                    foreach (var face in faces)
                    {
                        //顔のパーツ座標を比率に変換する
                        var features = getFaceFeature(face);
                        //ファイルに書き込む
                        writer.WriteLine(string.Join(",", features) + ","+label);
                        Console.WriteLine("detect feature {0}", url);
                    }
                }
                catch (FaceAPIException e)
                {
                    Console.WriteLine("face expception "+e.Message);
                }
            }
        }

        //顔の特徴点を比率に変換する
        private static List<double> getFaceFeature(Face face)
        {
            var result = new List<double>();
            var marks = face.FaceLandmarks;
            //左目の眉毛から目までの長さの顔の長さとの比
            result.Add((marks.EyeLeftInner.Y - marks.EyebrowLeftInner.Y) / face.FaceRectangle.Height);
            //右目の眉毛から目までの長さの顔の長さとの比
            result.Add((marks.EyeRightInner.Y - marks.EyebrowRightInner.Y) / face.FaceRectangle.Height);
            //左目の縦向きの長さの顔の長さとの比
            result.Add((marks.EyeLeftBottom.Y - marks.EyeLeftTop.Y) / face.FaceRectangle.Height);
            //右目の縦向きの長さの顔の長さとの比
            result.Add((marks.EyeRightBottom.Y - marks.EyeRightTop.Y) / face.FaceRectangle.Height);
            //上唇から下唇の長さの顔の長さとの比
            result.Add((marks.UnderLipBottom.Y - marks.UpperLipTop.Y) / face.FaceRectangle.Height);
            //口の幅の顔の幅との比
            result.Add((marks.MouthRight.X-marks.MouthLeft.X)/face.FaceRectangle.Width);

            return result;

        }


    }
}
