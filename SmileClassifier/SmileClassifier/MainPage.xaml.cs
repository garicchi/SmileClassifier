using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Media.Capture;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// 空白ページのアイテム テンプレートについては、http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409 を参照してください

namespace SmileClassifier
{

    public sealed partial class MainPage : Page
    {

        //Webカメラのキャプチャをするクラス
        MediaCapture _mediaCapture;
        MediaCaptureInitializationSettings setting;

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
    }
}
