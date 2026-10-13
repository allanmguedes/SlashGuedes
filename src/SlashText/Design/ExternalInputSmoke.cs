using System.Buffers.Binary;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SlashText.Services;
using SlashText.Views;

namespace SlashText.Design;

internal static class ExternalInputSmoke
{
    internal static async Task RunAsync(string output)
    {
        await CheckImageRejectionAsync(output);
        foreach(var language in new[]{"pt-BR","en","es"})
        foreach(var theme in new[]{"Light","Dark"})
        {
            UiText.Apply(language); ThemeService.Apply(theme);
            var owner=new Window { Width=600,Height=300,Content=new StackPanel(),ShowInTaskbar=false };
            owner.Show(); ((FrameworkElement)owner.Content).IsEnabled=false;
            using var cancellation=new CancellationTokenSource();
            var progress=new ExternalOperationWindow(UiText.Get("Verificando e restaurando o backup…"),cancellation){Owner=owner};progress.Show();
            try
            {
                await Task.Delay(40);
                Require(progress.CancelAction.IsEnabled,"cancel independent of disabled owner");
                var root=(FrameworkElement)progress.Content;root.UpdateLayout();
                var bounds=progress.CancelAction.TransformToAncestor(root).TransformBounds(new Rect(progress.CancelAction.RenderSize));
                Require(bounds.Bottom<=root.ActualHeight && bounds.Right<=root.ActualWidth,"cancel action visible");
                var image=new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth),(int)Math.Ceiling(root.ActualHeight),96,96,PixelFormats.Pbgra32);image.Render(root);
                var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));
                using(var file=File.Create(Path.Combine(output,$"external-{language}-{theme}.png")))png.Save(file);
                progress.Close();Require(cancellation.IsCancellationRequested && progress.IsVisible,"close requests cancellation and waits for safe cleanup");
                progress.Complete();Require(!progress.IsVisible,"complete closes progress");
                using var afterCommit=new CancellationTokenSource();
                var committed=new ExternalOperationWindow(UiText.Get("Processando arquivo"),afterCommit){Owner=owner};committed.Show();
                committed.PreventCancellation();committed.Close();Require(!afterCommit.IsCancellationRequested && committed.IsVisible,"cancel disabled after commit");committed.Complete();
            }
            finally { progress.Complete();owner.Close(); }
        }
        UiText.Apply("pt-BR");ThemeService.Apply("Light");
        using var initial=new System.Drawing.Bitmap(12,10);
        var editor=new CaptureWorkbenchEditor();editor.LoadImage(initial);var document=editor.Document;
        var malformed=Path.Combine(output,"oversized-header.png");initial.Save(malformed,System.Drawing.Imaging.ImageFormat.Png);
        var bytes=File.ReadAllBytes(malformed);BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16),32000);BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20),32000);File.WriteAllBytes(malformed,bytes);
        try { editor.LoadImage(malformed);throw new Exception("Must reject header before decoding"); }
        catch(InvalidDataException) { Require(ReferenceEquals(document,editor.Document) && editor.HasImage,"failed image preserves document"); }
        editor.Dispose();
        initial.Save(malformed,System.Drawing.Imaging.ImageFormat.Png);
        using(var release=new ManualResetEventSlim(false))
        {
            var entered=0;
            var blockers=Enumerable.Range(0,2).Select(_=>ExternalImageReader.DecodeAsync(()=>
            {
                Interlocked.Increment(ref entered);Require(release.Wait(TimeSpan.FromSeconds(10)),"release test decoder slots");return 1;
            })).ToArray();
            while(Volatile.Read(ref entered)!=2)await Task.Delay(10);
            var main=new MainWindow(captureEvidence:true);
            var realEditor=(CaptureWorkbenchEditor)main.FindName("CaptureInlineEditor");realEditor.LoadImage(initial);
            try
            {
                var method=typeof(MainWindow).GetMethod("LoadCaptureWorkbenchImageAsync",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
                var load=(Task<bool>)method.Invoke(main,[malformed,null,"fixture",false])!;
                realEditor.LoadImage(initial);var replacement=realEditor.Document;
                release.Set();await Task.WhenAll(blockers);
                Require(!await load && ReferenceEquals(replacement,realEditor.Document),"older background load cannot overwrite new document");
            }
            finally{release.Set();main.DisposeCaptureEvidence();main.Close();}
        }
        var serviced=false;var dispatcher=Application.Current.Dispatcher;
        await ExternalImageReader.DecodeAsync(()=>
        {
            Require(!dispatcher.CheckAccess(),"decoder executes off the UI thread");
            dispatcher.Invoke(new Action(()=>serviced=true),DispatcherPriority.Normal,CancellationToken.None,TimeSpan.FromSeconds(2));
            return 1;
        });
        Require(serviced,"dispatcher services work during background decode");
        File.WriteAllText(Path.Combine(output,"external-result.txt"),"PASS: actual async image loader rejects oversized/unsupported/truncated input with and without a current image in 3 languages/2 themes; status, document, pixels, details, path and window cleanup preserved; thumbnail fallback; cancellation window, disabled owner, commit boundary, stale async load protection, responsive dispatcher.");
    }

    private static async Task CheckImageRejectionAsync(string output)
    {
        var valid = Path.Combine(output, "blue-image.png");
        using (var blue = new System.Drawing.Bitmap(320, 180))
        {
            using (var graphics = System.Drawing.Graphics.FromImage(blue)) graphics.Clear(System.Drawing.Color.Blue);
            blue.Save(valid, System.Drawing.Imaging.ImageFormat.Png);
        }
        var oversized = Path.Combine(output, "oversized-header.bin");
        // Exact inert 33-byte PNG header from the pilot fixture, including its CRC.
        File.WriteAllBytes(oversized, Convert.FromHexString("89504E470D0A1A0A0000000D4948445200007D0000007D0008020000000CE0455A"));
        var unsupported = Path.Combine(output, "unsupported.bin");
        File.WriteAllBytes(unsupported, new byte[33]);
        var truncated = Path.Combine(output, "truncated.bin");
        File.WriteAllBytes(truncated, new byte[8]);
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var loader = typeof(MainWindow).GetMethod("LoadCaptureWorkbenchImageAsync", flags)!;
        var pathField = typeof(MainWindow).GetField("_captureWorkbenchPath", flags)!;
        var thumbnailLoader = typeof(MainWindow).GetMethod("TryLoadImage", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var historyLoader = typeof(MainWindow).GetMethod("LoadHistoryThumbnailAsync", flags)!;

        foreach (var language in new[] { "pt-BR", "en", "es" })
        foreach (var theme in new[] { "Light", "Dark" })
        {
            UiText.Apply(language); ThemeService.Apply(theme);
            var main = new MainWindow(captureEvidence: true);
            main.Show();
            var realEditor = (CaptureWorkbenchEditor)main.FindName("CaptureInlineEditor");
            var details = (TextBlock)main.FindName("CapturePreviewDetailsText");
            var status = (TextBlock)main.FindName("StatusText");
            Task<bool> Load(string path) => (Task<bool>)loader.Invoke(main, [path, null, "fixture", true])!;
            try
            {
                foreach (var hasImage in new[] { false, true })
                {
                    if (hasImage) Require(await Load(valid), "valid blue image loads through actual async handler path");
                    var document = realEditor.Document;
                    var previousDetails = details.Text;
                    var previousPath = pathField.GetValue(main);
                    var windowCount = Application.Current.Windows.Count;
                    foreach (var invalid in new[] { oversized, unsupported, truncated })
                    {
                        Require(!await Load(invalid), "actual async loader rejects invalid input without faulting");
                        Require(ReferenceEquals(document, realEditor.Document) && realEditor.HasImage == hasImage, "rejection preserves current image or empty editor");
                        if (hasImage)
                        {
                            using var rendered = realEditor.Render();
                            Require(rendered.Width == 320 && rendered.Height == 180 && rendered.GetPixel(160, 90).ToArgb() == System.Drawing.Color.Blue.ToArgb(), "blue pixels preserved after rejection");
                        }
                        Require(details.Text == previousDetails && Equals(previousPath, pathField.GetValue(main)), "rejection preserves details and source path");
                        Require(status.Text.StartsWith(UiText.Get("Não foi possível abrir a imagem selecionada") + ": ", StringComparison.Ordinal), "localized rejection status shown");
                        Require(Application.Current.Windows.Count == windowCount && main.IsVisible, "rejection closes progress and keeps app open");
                        object?[] arguments = [invalid, null];
                        Require(!(bool)thumbnailLoader.Invoke(null, arguments)! && arguments[1] is null, "invalid history thumbnail falls back without faulting");
                        var fallback = new TextBlock { Text = "fixture fallback" };
                        var thumbnail = new Border { Child = fallback };
                        await (Task)historyLoader.Invoke(main, [thumbnail, invalid, new SlashText.Models.CaptureRecord(), CancellationToken.None])!;
                        Require(ReferenceEquals(fallback, thumbnail.Child), "async history thumbnail retains fallback on rejection");
                    }
                }
            }
            finally { main.DisposeCaptureEvidence(); main.Close(); }
        }
    }
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}
