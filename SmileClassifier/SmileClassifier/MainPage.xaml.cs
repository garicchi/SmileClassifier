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
using Windows.Devices.Gpio;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.Storage.Streams;
using Windows.UI.Core;
using Windows.UI.Notifications;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;

namespace SmileClassifier
{

    public sealed partial class MainPage : Page
    {
        
        MediaCapture _mediaCapture;
        MediaCaptureInitializationSettings setting;
        GpioController _gpioController;
        GpioPin _switchPin;
        GpioPin _ledPin;
        bool processing;

        //Cognitive Service Face APIのAPI Keyを入れる
        string _faceApiKey = "{ your face api key }";
        //デプロイしたAzure Machine LearningのWeb APIのAPI Keyを入れる
        string _mlApiKey = "{ your azure ml web api key }";
        //デプロイしたAzure Machine LearningのWeb APIのURLを入れる
        string _mlWebUrl = "{ your azure ml web api url }";

        //タクトスイッチをつなげたラズパイのGPIOピン番号を入れる
        int _switchPinId = 21;
        //LEDをつなげたラズパイのGPIOピン番号を入れる
        int _ledPinId = 20;

        public MainPage()
        {
            this.InitializeComponent();
            
            //ページがロードされたら
            this.Loaded += async (sender, arg) =>
            {
                //初期化
                await InitCameraAsync();
                InitGpio();
            };

            //アプリが一時停止したら
            Application.Current.Suspending += async (sender, arg) =>
            {
                //Webカメラのキャプチャを止める
                await _mediaCapture.StopPreviewAsync();
                _mediaCapture.Dispose();
            };

            //アプリが再開したら
            Application.Current.Resuming += async (sender, arg) =>
            {
                //再度Webカメラを起動する
                await InitCameraAsync();
            };
            processing = false;
        }

        //Webカメラを初期化する
        private async Task InitCameraAsync()
        {
            //UIスレッドで実行する
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
            {
                //デバイス一覧からビデオキャプチャーができるデバイスを取得する
                var devices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
                var cameraId = devices.ElementAt(0);
                //設定に取得したカメラデバイスのIDを登録する
                setting = new MediaCaptureInitializationSettings();
                setting.VideoDeviceId = cameraId.Id;

                //Webカメラのキャプチャーを起動する
                _mediaCapture = new MediaCapture();
                await _mediaCapture.InitializeAsync(setting);

                var vprops = _mediaCapture.VideoDeviceController.GetAvailableMediaStreamProperties(MediaStreamType.VideoPreview);
                //Webカメラの解像度やフレームレートを設定する
                //うまくWebカメラが動かない場合は解像度やフレームレートを下げる
                foreach (VideoEncodingProperties vprop in vprops)
                {
                    var frameRate = (vprop.FrameRate.Numerator / vprop.FrameRate.Denominator);
                    System.Diagnostics.Debug.WriteLine("{0}: {1}x{2} {3}fps", vprop.Subtype, vprop.Width, vprop.Height, frameRate);
                }
                //4番目の設定を使用。環境によって適切なものを選択
                var selectProp = vprops[3];
                await _mediaCapture.VideoDeviceController.SetMediaStreamPropertiesAsync(MediaStreamType.VideoPreview, selectProp);

                captureElement.Source = _mediaCapture;
                await _mediaCapture.StartPreviewAsync();
            });
        }

        //ラズパイのGPIOを初期化する
        private void InitGpio()
        {
            _gpioController = GpioController.GetDefault();
            _switchPin = _gpioController.OpenPin(_switchPinId);
            _switchPin.SetDriveMode(GpioPinDriveMode.Input);
            _ledPin = _gpioController.OpenPin(_ledPinId);
            _ledPin.SetDriveMode(GpioPinDriveMode.Output);

            //スイッチにつなげたピンの電圧に変化があったなら
            _switchPin.ValueChanged += async(sender, arg) =>
            {
                //ピンの電圧がHighかLowを取得する
                var pinValue = arg.Edge;
                //Lowならスイッチが押されたので判定
                if(pinValue == GpioPinEdge.FallingEdge)
                {
                    //スイッチの2度押し防止用
                    if (processing) return;
                    processing = true;
                    try
                    {
                        await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
                        {
                            textStatus.Text = "判定中...";
                            _ledPin.Write(GpioPinValue.Low);
                            //笑顔判定をする
                            var label = await judgeSmileFaceAsync();

                            if (label == string.Empty)
                            {
                                textStatus.Text = "顔検出に失敗しました";
                            }
                            else if (label == "angry")
                            {
                                textStatus.Text = "判定結果 = 怒り顔";
                            }
                            else if (label == "smile")
                            {
                                textStatus.Text = "判定結果 = 笑顔";
                                //LEDを点灯させる
                                _ledPin.Write(GpioPinValue.High);
                            }
                        });
                    }
                    finally
                    {
                        processing = false;
                    }
                }
            };
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
            result.Add((marks.MouthRight.X - marks.MouthLeft.X) / face.FaceRectangle.Width);

            return result;

        }

        //笑顔かどうかを判定する関数
        private async Task<string> judgeSmileFaceAsync()
        {
            var result = string.Empty;
            //Webカメラから画像を取得する
            var list = _mediaCapture.VideoDeviceController
                .GetMediaStreamProperties(MediaStreamType.VideoPreview).Properties.ToList();
            var stream = new InMemoryRandomAccessStream();
            var prop = ImageEncodingProperties.CreatePng();
            
            prop.Width = (uint)captureElement.ActualWidth;
            prop.Height = (uint)captureElement.ActualHeight;
            await _mediaCapture.CapturePhotoToStreamAsync(prop, stream);
            stream.Seek(0);

            //Face APIを利用して顔の特徴点を取得する
            var faceClient = new FaceServiceClient(_faceApiKey, "https://westcentralus.api.cognitive.microsoft.com/face/v1.0");
            var faces = await faceClient.DetectAsync(stream.AsStream(), true, true);
            if (faces.Count() == 0)
            {
                return result;  //顔を検出できなかった場合string.Emptyを返す
            }
            
            var face = faces.First();
            //特徴点を選定する
            var paramList = getFaceFeature(face);

            //デプロイしたAzure Machine LearningのWeb APIを利用して笑顔判定を行う
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
                    parameter.Inputs.input1[0].Add("param" + (i + 1), paramList[i].ToString());
                }
                parameter.Inputs.input1[0].Add("label", "");
                var json = JsonConvert.SerializeObject(parameter);

                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _mlApiKey);
                var content = new StringContent(json, Encoding.UTF8, "applicaton/json");
                var response = await client.PostAsync(_mlWebUrl, content);
                var jsonResult = await response.Content.ReadAsStringAsync();
                var jObj = JObject.Parse(jsonResult);
                var label = jObj.SelectToken("Results.output1[0]['Scored Labels']").Value<string>();
                //笑顔か怒り顔かの判定結果を返す
                result = label;
                
            }
            return result;
                
        }
    }
}