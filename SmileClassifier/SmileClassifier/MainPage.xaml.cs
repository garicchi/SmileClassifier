using Microsoft.ProjectOxford.Face;
using Microsoft.ProjectOxford.Face.Contract;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.Storage.Streams;
using Windows.UI.Notifications;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;

// 空白ページのアイテム テンプレートについては、http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409 を参照してください

namespace SmileClassifier
{

    public sealed partial class MainPage : Page
    {

        //Webカメラのキャプチャをするクラス
        MediaCapture _mediaCapture;
        MediaCaptureInitializationSettings setting;
        string apiKey = "275f7ae3c0ca42fda3eca8bee0956fad";

        public MainPage()
        {
            this.InitializeComponent();

            //ページがロードされたら
            this.Loaded += async (s, e) =>
            {
                await InitializeMediaCapture();
            };

            //アプリが一時停止したら
            Application.Current.Suspending += async (s, e) =>
            {
                //Webカメラのキャプチャを止める
                await _mediaCapture.StopPreviewAsync();
                _mediaCapture.Dispose();
            };

            //アプリが再開したら
            Application.Current.Resuming += async (s, e) =>
            {
                //サイドWebカメラを起動する
                await InitializeMediaCapture();
            };
        }

        private async Task InitializeMediaCapture()
        {
            //UIスレッドで実行する
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
            {
                try
                {
                    //デバイス一覧からビデオキャプチャーができるデバイスを取得する
                    DeviceInformationCollection devices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
                    DeviceInformation cameraId = devices.ElementAt(1);
                    //設定に取得したカメラデバイスのIDを登録する
                    setting = new MediaCaptureInitializationSettings();
                    setting.VideoDeviceId = cameraId.Id;

                    //Webカメラのキャプチャーを起動する
                    _mediaCapture = new MediaCapture();
                    await _mediaCapture.InitializeAsync(setting);
                    captureElement.Source = _mediaCapture;
                    await _mediaCapture.StartPreviewAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.Message);
                }
            });
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


        private async void captureElement_Tapped(object sender, TappedRoutedEventArgs e)
        {
            textStatus.Text = "判定中...";
            if (_mediaCapture.VideoDeviceController.FocusControl.Supported)
            {
                await _mediaCapture.VideoDeviceController.FocusControl.FocusAsync();
            }
            
            var list = _mediaCapture.VideoDeviceController.GetMediaStreamProperties(MediaStreamType.VideoPreview).Properties.ToList();
            var stream = new InMemoryRandomAccessStream();
            var prop = ImageEncodingProperties.CreatePng();
            prop.Width = (uint)captureElement.ActualWidth;
            prop.Height = (uint)captureElement.ActualHeight;
            await _mediaCapture.CapturePhotoToStreamAsync(prop, stream);
            stream.Seek(0);

            var faceClient = new FaceServiceClient(apiKey);
            var faces = await faceClient.DetectAsync(stream.AsStream(), true, true);
            if (faces.Count() > 0)
            {
                var face = faces.First();
                var paramList = getFaceFeature(face);
                using (var client = new HttpClient())
                {
                    StringBuilder sb = new StringBuilder();
                    StringWriter sw = new StringWriter(sb);

                    using (var w = new JsonTextWriter(sw))
                    {
                        w.WriteStartObject();
                        w.WritePropertyName("Inputs");
                        w.WriteStartObject();
                        w.WritePropertyName("input1");
                        w.WriteStartArray();
                        w.WriteStartObject();
                        for (int i = 0; i < paramList.Count; i++)
                        {
                            w.WritePropertyName("param" + (i+1));
                            w.WriteValue(paramList[i]);
                        }
                        w.WritePropertyName("label");
                        w.WriteValue("null");
                        w.WriteEndObject();

                        w.WriteEndArray();
                        w.WriteEndObject();
                        w.WritePropertyName("GlobalParameters");
                        w.WriteStartObject();
                        w.WriteEndObject();
                        w.WriteEndObject();
                    }
                    var json = sb.ToString();

                    var apiKey = "vdONRbDzchAzdzlmr+MGez+xw67O0uPIrMng1FrKMOiZlr5sWSHus7Ja+NQiDubDc7BrxattCi2fnDGPyCxvYA=="; // Replace this with the API key for the web service
                    var requestUrl = "https://asiasoutheast.services.azureml.net/subscriptions/25116a6966a94419a84024e51e3fc3ee/services/23115b24669d401b936a44901a754c25/execute?api-version=2.0&format=swagger";
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                    var content = new StringContent(json,Encoding.UTF8,"applicaton/json");
                    var response = await client.PostAsync(requestUrl, content);
                    var jsonResult = await response.Content.ReadAsStringAsync();
                    var jObj = JObject.Parse(jsonResult);
                    var label = jObj.SelectToken("Results.output1[0]['Scored Labels']").Value<string>();
                    Debug.WriteLine("判定結果 = " + label);
                    textStatus.Text = "判定結果 = "+label;
                }
            }
            else
            {
                var dialog = new MessageDialog("顔検出に失敗しました");
                await dialog.ShowAsync();
            }

            

        }
    }
}