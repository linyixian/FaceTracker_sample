using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Foundation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

//追加
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using Windows.System.Threading;
using Windows.Media;
using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.Devices.Enumeration;
using Windows.Media.FaceAnalysis;
using Windows.Graphics.Imaging;
using Windows.UI.Xaml.Shapes;

// 空白ページのアイテム テンプレートについては、http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409 を参照してください

namespace FaceTracker_sample
{
    /// <summary>
    /// それ自体で使用できる空白ページまたはフレーム内に移動できる空白ページ。
    /// </summary>
    public sealed partial class MainPage : Page
    {

        private MediaCapture mediaCapture;
        private bool isPreviewing;
        private FaceTracker faceTracker;
        private ThreadPoolTimer timer;
        private SemaphoreSlim semaphore = new SemaphoreSlim(1);

        public MainPage()
        {
            this.InitializeComponent();
            isPreviewing = false;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void Page_Loaded(object sender, RoutedEventArgs e)
        {
            //FaceTrackerオブジェクトの作成
            if (faceTracker == null)
            {
                faceTracker = await FaceTracker.CreateAsync();
            }

            //カメラの初期化
            await InitCameraAsync();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private async Task InitCameraAsync()
        {
            Debug.WriteLine("InitializeCameraAsync");

            try
            {
                //mediaCaptureオブジェクトが有効な時は一度Disposeする
                if (mediaCapture != null)
                {
                    if (isPreviewing)
                    {
                        await mediaCapture.StopPreviewAsync();
                        isPreviewing = false;
                    }

                    mediaCapture.Dispose();
                    mediaCapture = null;
                }

                //キャプチャーの設定
                var captureInitSettings = new MediaCaptureInitializationSettings();
                captureInitSettings.VideoDeviceId = "";
                captureInitSettings.StreamingCaptureMode = StreamingCaptureMode.Video;

                //カメラデバイスの取得
                var cameraDevices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);

                if (cameraDevices.Count() == 0)
                {
                    Debug.WriteLine("No Camera");
                    return;
                }
                else if (cameraDevices.Count() == 1)
                {
                    captureInitSettings.VideoDeviceId = cameraDevices[0].Id;
                }
                else
                {
                    captureInitSettings.VideoDeviceId = cameraDevices[1].Id;
                }

                //キャプチャーの準備
                mediaCapture = new MediaCapture();
                await mediaCapture.InitializeAsync(captureInitSettings);

                VideoEncodingProperties vp = new VideoEncodingProperties();

                vp.Height = 240;
                vp.Width = 320;
                vp.Subtype = "NV12";        //"NV12"のみ対応

                await mediaCapture.VideoDeviceController.SetMediaStreamPropertiesAsync(MediaStreamType.VideoPreview, vp);

                capture.Source = mediaCapture;

                //キャプチャーの開始
                await mediaCapture.StartPreviewAsync();
                isPreviewing = true;

                Debug.WriteLine("Camera Initialized");

                //15FPS毎にタイマーを起動する。
                TimeSpan timerInterval = TimeSpan.FromMilliseconds(66);
                timer = ThreadPoolTimer.CreatePeriodicTimer(new TimerElapsedHandler(CurrentVideoFrame), timerInterval);

            }
            catch (Exception ex)
            {
                Debug.Write(ex.Message);
            }
        }

        /// <summary>
        /// タイマーに従って顔追跡を行う
        /// </summary>
        /// <param name="timer"></param>
        private async void CurrentVideoFrame(ThreadPoolTimer timer)
        {
            //追跡動作中の場合は処理をしない
            if (!semaphore.Wait(0))
            {
                return;
            }

            try
            {
                IList<DetectedFace> faces = null;
                const BitmapPixelFormat inputPixelFormat = BitmapPixelFormat.Nv12;

                
                using (VideoFrame previewFrame = new VideoFrame(inputPixelFormat, 320, 240))
                {
                    //ビデオフレームの取得
                    await this.mediaCapture.GetPreviewFrameAsync(previewFrame);

                    if (FaceDetector.IsBitmapPixelFormatSupported(previewFrame.SoftwareBitmap.BitmapPixelFormat))
                    {
                        //顔認識の実行
                        faces = await this.faceTracker.ProcessNextFrameAsync(previewFrame);
                    }
                    else
                    {
                        throw new System.NotSupportedException("PixelFormat '" + inputPixelFormat.ToString() + "' is not supported by FaceDetector");
                    }

                    //認識に使ったフレームのサイズ取得
                    var previewFrameSize = new Windows.Foundation.Size(previewFrame.SoftwareBitmap.PixelWidth, previewFrame.SoftwareBitmap.PixelHeight);

                    //顔追跡はUIスレッドとは別スレッドなので顔の位置表示のためにUIスレッドに切り替え
                    var ignored = this.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        FaceDraw(previewFrameSize, faces);
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
            finally
            {
                semaphore.Release();
            }
        }

        /// <summary>
        /// 認識した顔の位置に四角を描画
        /// </summary>
        /// <param name="FramePixelSize"></param>
        /// <param name="foundFaces"></param>
        private void FaceDraw(Size FramePixelSize, IList<DetectedFace> foundFaces)
        {
            //Canvasをクリア
            canvas.Children.Clear();

            double actualWidth = canvas.ActualWidth;
            double actualHeight = canvas.ActualHeight;

            if (foundFaces != null && actualWidth != 0 && actualHeight != 0)
            {
                double widthScale = FramePixelSize.Width / actualWidth;
                double heightScale = FramePixelSize.Height / actualHeight;

                //見つかった顔の位置に四角を描画
                foreach (DetectedFace face in foundFaces)
                {
                    Rectangle box = new Rectangle();
                    box.Width = (uint)(face.FaceBox.Width / widthScale);
                    box.Height = (uint)(face.FaceBox.Height / heightScale);
                    box.Fill = new SolidColorBrush(Windows.UI.Colors.Transparent);
                    box.Stroke = new SolidColorBrush(Windows.UI.Colors.Yellow);
                    box.StrokeThickness = 2.0;
                    box.Margin = new Thickness((uint)(face.FaceBox.X / widthScale), (uint)(face.FaceBox.Y / heightScale), 0, 0);

                    this.canvas.Children.Add(box);

                }
            }
        }
    }
}
