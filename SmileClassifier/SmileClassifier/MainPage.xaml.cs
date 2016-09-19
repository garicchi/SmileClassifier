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
        string _faceApiKey = "275f7ae3c0ca42fda3eca8bee0956fad";
        string _mlApiKey = "vdONRbDzchAzdzlmr+MGez+xw67O0uPIrMng1FrKMOiZlr5sWSHus7Ja+NQiDubDc7BrxattCi2fnDGPyCxvYA==";
        string _mlWebUrl = "https://asiasoutheast.services.azureml.net/subscriptions/25116a6966a94419a84024e51e3fc3ee/services/23115b24669d401b936a44901a754c25/execute?api-version=2.0&format=swagger";

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
                //再度Webカメラを起動する
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
                    DeviceInformation cameraId = devices.ElementAt(0);
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
            
            var list = _mediaCapture.VideoDeviceController.GetMediaStreamProperties(MediaStreamType.VideoPreview).Properties.ToList();
            var stream = new InMemoryRandomAccessStream();
            var prop = ImageEncodingProperties.CreatePng();
            prop.Width = (uint)captureElement.ActualWidth;
            prop.Height = (uint)captureElement.ActualHeight;
            await _mediaCapture.CapturePhotoToStreamAsync(prop, stream);
            stream.Seek(0);

            var faceClient = new FaceServiceClient(_faceApiKey);
            var faces = await faceClient.DetectAsync(stream.AsStream(), true, true);
            if (faces.Count() > 0)
            {
                var face = faces.First();
                var paramList = getFaceFeature(face);
                using (var client = new HttpClient())
                {
                    var parameter = new
                    {
                        Inputs = new
                        {
                            input1 = new[]
                            {
                                new Dictionary<string,string>()
                            }
                        },
                        GlobalParameters = new { }
                    };
                    for (int i = 0; i < paramList.Count; i++)
                    {
                        parameter.Inputs.input1[0].Add("param" + (i+1), paramList[i].ToString());
                    }
                    parameter.Inputs.input1[0].Add("label", "");
                    var json = JsonConvert.SerializeObject(parameter);
                    
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _mlApiKey);
                    var content = new StringContent(json,Encoding.UTF8,"applicaton/json");
                    var response = await client.PostAsync(_mlWebUrl, content);
                    var jsonResult = await response.Content.ReadAsStringAsync();
                    var jObj = JObject.Parse(jsonResult);
                    var label = jObj.SelectToken("Results.output1[0]['Scored Labels']").Value<string>();
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