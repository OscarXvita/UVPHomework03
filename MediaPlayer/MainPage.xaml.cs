using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Networking.BackgroundTransfer;
using Windows.Storage;
using Windows.UI.Core;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Navigation;
using Windows.Web;

// https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x804 上介绍了“空白页”项模板

namespace MediaPlayer
{
    /// <summary>
    /// 可用于自身或导航至 Frame 内部的空白页。
    /// </summary>
    public sealed partial class MainPage : Page
    {
        public MainPage()
        {
            cts = new CancellationTokenSource();

            this.InitializeComponent();

        }
        public void Dispose()
        {
            if (cts != null)
            {
                cts.Dispose();
                cts = null;
            }

            GC.SuppressFinalize(this);
        }
        protected async override void OnNavigatedTo(NavigationEventArgs e)
        {
            // An application must enumerate downloads when it gets started to prevent stale downloads/uploads.
            // Typically this can be done in the App class by overriding OnLaunched() and checking for
            // "args.Kind == ActivationKind.Launch" to detect an actual app launch.
            // We do it here in the sample to keep the sample code consolidated.
            await DiscoverActiveDownloadsAsync();
        }

        private async Task DiscoverActiveDownloadsAsync()
        {
            activeDownloads = new List<DownloadOperation>();

            IReadOnlyList<DownloadOperation> downloads = null;
            try
            {
                downloads = await BackgroundDownloader.GetCurrentDownloadsAsync();
            }
            catch (Exception ex)
            {
                if (!IsExceptionHandled("Discovery error", ex))
                {
                    throw;
                }
                return;
            }

            Debug.WriteLine("Loading background downloads: " + downloads.Count);

            if (downloads.Count > 0)
            {
                List<Task> tasks = new List<Task>();
                foreach (DownloadOperation download in downloads)
                {
                    Debug.WriteLine(String.Format(CultureInfo.CurrentCulture,
                        "Discovered background download: {0}, Status: {1}", download.Guid,
                        download.Progress.Status));

                    // Attach progress and completion handlers.
                    tasks.Add(HandleDownloadAsync(download, false));
                }

                // Don't await HandleDownloadAsync() in the foreach loop since we would attach to the second
                // download only when the first one completed; attach to the third download when the second one
                // completes etc. We want to attach to all downloads immediately.
                // If there are actions that need to be taken once downloads complete, await tasks here, outside
                // the loop.
                await Task.WhenAll(tasks);
            }
        }
        private List<DownloadOperation> activeDownloads;
        private CancellationTokenSource cts;
        private async void filePick_Click(object sender, RoutedEventArgs e)
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.Thumbnail;
            picker.SuggestedStartLocation =
                Windows.Storage.Pickers.PickerLocationId.VideosLibrary;
            picker.FileTypeFilter.Add(".mp3");
            picker.FileTypeFilter.Add(".mp4");


            Windows.Storage.StorageFile file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                var stream = await file.OpenAsync(Windows.Storage.FileAccessMode.Read);
                mediaElement.SetSource(stream, file.ContentType);
                mediaElement.Play();
                mediaElement.AreTransportControlsEnabled = true;
                Debug.WriteLine("Playing: " + file.Name);
            }
            else
            {
                Debug.WriteLine("Operation cancelled.");
            }

        }
        private bool checkURI(String input)
        {
            Uri uri;
            try
            {
                uri = new Uri(input);
                return uri.IsWellFormedOriginalString();
            }
            catch
            {
                return false;
            }

        }
        private void urlPick_Click(object sender, RoutedEventArgs e)
        {


            mediaElement.Stop();

            mediaElement.Source = new Uri(this.music_url.Text);
            mediaElement.Play();
            mediaElement.AreTransportControlsEnabled = true;


        }

        private void music_url_TextChanged(object sender, TextChangedEventArgs e)
        {
            this.urlPick.IsEnabled = checkURI(music_url.Text);
            this.downPick.IsEnabled = checkURI(music_url.Text);
        }

        private void neusong_Click(object sender, RoutedEventArgs e)
        {
            this.music_url.Text = "http://www.neu.edu.cn/indexsource/neusong.mp3";
        }

        private void downPick_Click(object sender, RoutedEventArgs e)
        {
            mediaElement.Stop();
            StartDownload();
            // mediaElement.Source = new Uri(this.music_url.Text);
            //          mediaElement.Play();
            //        mediaElement.AreTransportControlsEnabled = true;

        }
        private async void StartDownload()
        {


            
            Uri source = new Uri(music_url.Text.Trim());
            if (!Uri.TryCreate(music_url.Text.Trim(), UriKind.Absolute, out source))
            {
                Debug.WriteLine("Invalid URI.");
                return;
            }
            string destination = music_url.Text.Substring(music_url.Text.LastIndexOf('/') + 1, music_url.Text.Length - music_url.Text.LastIndexOf('/') - 1);

            StorageFile destinationFile = await KnownFolders.MusicLibrary.CreateFileAsync(
                destination, CreationCollisionOption.GenerateUniqueName);

            BackgroundDownloader downloader = new BackgroundDownloader();
            DownloadOperation download = downloader.CreateDownload(source, destinationFile);
            download.Priority = BackgroundTransferPriority.High;
            // Attach progress and completion handlers.
            await HandleDownloadAsync(download, true);
       
            var stream = await destinationFile.OpenAsync(Windows.Storage.FileAccessMode.Read);
            mediaElement.SetSource(stream, "audio");
            MessageDialog diag = new MessageDialog(String.Format("File saved to {0}, Now Playing...", destinationFile.Path));
            Status.Text = "Completed!";
            var x=diag.ShowAsync();
            mediaElement.Play();
            return;
        }


        private void DownloadProgress(DownloadOperation download)
        {
            Debug.WriteLine(String.Format(CultureInfo.CurrentCulture, "Progress: {0}, Status: {1}", download.Guid,
                download.Progress.Status));

            double percent = 100;
            if (download.Progress.TotalBytesToReceive > 0)
            {
                percent = download.Progress.BytesReceived * 100 / download.Progress.TotalBytesToReceive;
            }

            Debug.WriteLine(String.Format(CultureInfo.CurrentCulture, " - Transfered bytes: {0} of {1}, {2}%",
                download.Progress.BytesReceived, download.Progress.TotalBytesToReceive, percent));

            if (download.Progress.HasRestarted)
            {
                Debug.WriteLine(" - Download restarted");
            }

            if (download.Progress.HasResponseChanged)
            {
                // We've received new response headers from the server.
                Debug.WriteLine(" - Response updated; Header count: " + download.GetResponseInformation().Headers.Count);

                // If you want to stream the response data this is a good time to start.
                // download.GetResultStreamAt(0);
            }
        }
        private async Task HandleDownloadAsync(DownloadOperation download, bool start)
        {
            try
            {
                Debug.WriteLine("Running: " + download.Guid);//, NotifyType.StatusMessage);

                // Store the download so we can pause/resume.
                activeDownloads.Add(download);

                Progress<DownloadOperation> progressCallback = new Progress<DownloadOperation>(DownloadProgress);
                if (start)
                {
                    progressCallback.ProgressChanged += ProgressCallback_ProgressChanged;
                    
                    // Start the download and attach a progress handler.
                    await download.StartAsync().AsTask(cts.Token, progressCallback);
                 
                }
                else
                {
                    // The download was already running when the application started, re-attach the progress handler.
                    await download.AttachAsync().AsTask(cts.Token, progressCallback);
                }

                ResponseInformation response = download.GetResponseInformation();

                Debug.WriteLine(String.Format(CultureInfo.CurrentCulture, "Completed: {0}, Status Code: {1}",
                    download.Guid, response.StatusCode));//, NotifyType.StatusMessage);
            }
            catch (TaskCanceledException)
            {
                Debug.WriteLine("Canceled: " + download.Guid);//, NotifyType.StatusMessage);
            }
            catch (Exception ex)
            {
                if (!IsExceptionHandled("Execution error", ex, download))
                {
                    throw;
                }
            }
            finally
            {
                activeDownloads.Remove(download);
            }
        }

        private async void ProgressCallback_ProgressChanged(object sender, DownloadOperation e)
        {
            this.downloadBar.Visibility = Visibility.Visible;
            double percentage = 100 * (e.Progress.BytesReceived / e.Progress.TotalBytesToReceive);

            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
                {

                 this.download_progress.Value = percentage;
           });
            
        }

        private bool IsExceptionHandled(string title, Exception ex, DownloadOperation download = null)
        {
            WebErrorStatus error = BackgroundTransferError.GetStatus(ex.HResult);
            if (error == WebErrorStatus.Unknown)
            {
                return false;
            }

            if (download == null)
            {
                Debug.WriteLine(String.Format(CultureInfo.CurrentCulture, "Error: {0}: {1}", title, error));
                //  NotifyType.ErrorMessage);
            }
            else
            {
                Debug.WriteLine(String.Format(CultureInfo.CurrentCulture, "Error: {0} - {1}: {2}", download.Guid, title,
                    error));//, NotifyType.ErrorMessage);
            }

            return true;
        }




    }
}
