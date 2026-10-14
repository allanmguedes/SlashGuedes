using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SlashText.Models;
using SlashText.Services;

namespace SlashText.Design;

/// <summary>Real auxiliary consumers/Dispatcher with synthetic insertions; no native input or clipboard.</summary>
internal static class UsageWriteFailureSmoke
{
    internal static void CreateFixtures(string data)
    {
        if (Directory.Exists(data) && Directory.EnumerateFileSystemEntries(data).Any()) throw new IOException("Choose an empty fixture directory.");
        Directory.CreateDirectory(data);
        var snippet=new Snippet { Id=Guid.Parse("12600000-0000-4000-8000-000000000001"), Name="Teste fictício 126",Trigger="/teste126",Content="INSERÇÃO FICTÍCIA 126" };
        File.WriteAllText(Path.Combine(data,"snippets.md"),SnippetMarkdownRepository.Serialize([snippet]));
        if(SnippetMarkdownRepository.ReadRequired(File.ReadAllText(Path.Combine(data,"snippets.md"))).Single().Trigger!=snippet.Trigger) throw new InvalidDataException("Invalid fixture shortcut.");
        var settings=new AppSettings { OnboardingCompleted=true,CloseToTray=false,CheckUpdatesOnStartup=false,QuickAccentEnabled=true };
        settings.Capture.OutputDirectoryTemplate=Path.Combine(data,"Captures");
        File.WriteAllText(Path.Combine(data,"settings.json"),JsonSerializer.Serialize(settings));
        File.WriteAllText(Path.Combine(data,"usage.json"),JsonSerializer.Serialize(new UsageSnapshot { Snippets=[new() { SnippetId=snippet.Id,Count=2 }],QuickAccent=new() { Count=1,Characters=new() { ["á"]=1 } } }));
        File.WriteAllText(Path.Combine(data,"fixture-126.txt"),"SLASHDESK FICTITIOUS USAGE 126");
    }
    internal static async Task RunAsync(string output)
    {
        Directory.CreateDirectory(output);
        var checks = new List<string>();
        void Require(bool value, string label)
        { if (!value) throw new InvalidOperationException(label); checks.Add(label + ": PASS"); }
        var unhandled = 0;
        DispatcherUnhandledExceptionEventHandler onError = (_, args) => { unhandled++; args.Handled = true; };
        Application.Current.DispatcherUnhandledException += onError;
        try
        {
            foreach (var language in new[] { "pt-BR", "en", "es" })
            foreach (var theme in new[] { "Light", "Dark" })
            {
                var root = Path.Combine(output, language + "-" + theme);
                AppPaths.Initialize(new AppDataEnvironment(DistributionMode.Portable, root, Path.Combine(root,"unused"), true));
                AppPaths.EnsureDataLayout(); UiText.Apply(language); ThemeService.Apply(theme);
                var snippet = new Snippet { Id = Guid.Parse("12600000-0000-4000-8000-000000000001"), Trigger = "/fixture126", Name="Fixture 126" };
                var store = new JsonFileStore<UsageSnapshot>(AppPaths.UsageFile);
                await store.SaveAsync(new UsageSnapshot { Snippets = [new() { SnippetId=snippet.Id,Count=2 }],
                    QuickAccent=new() { Count=1,Characters=new() { ["á"]=1 } } });
                var before=await File.ReadAllBytesAsync(AppPaths.UsageFile);
                var window = new MainWindow(captureEvidence:true);
                window.SetStatisticsSnippetsForEvidence([snippet]);
                await window.ReloadStatisticsForEvidenceAsync(980);
                T Control<T>(string name) where T:FrameworkElement => (T)window.FindName(name);
                var flags=BindingFlags.NonPublic|BindingFlags.Instance;
                var accent=typeof(MainWindow).GetMethod("QuickAccentService_OnCharacterInserted",flags)!;
                var expansion=typeof(MainWindow).GetMethod("CompleteSuccessfulExpansionAsync",flags)!;
                using(var locked=File.Open(AppPaths.UsageFile,FileMode.Open,FileAccess.Read,FileShare.Read))
                {
                    accent.Invoke(window,[null,new QuickAccentCharacterInsertedEventArgs('é')]);
                    var clock=Stopwatch.StartNew();
                    while(Control<TextBlock>("QuickAccentTotalText").Text!="2" && clock.Elapsed<TimeSpan.FromSeconds(5)) await Task.Delay(10);
                    Require(Control<TextBlock>("QuickAccentTotalText").Text=="2", "actual accent observer completes " + language+theme);
                    await (Task)expansion.Invoke(window,[snippet,40])!;
                    Require(Control<TextBlock>("TotalExpansionsText").Text=="3", "successful expansion counted in-session " + language+theme);
                    Require(Control<TextBlock>("StatusText").Text==UiText.Format($"{snippet.Trigger} inserido")+" · "+UiText.Get("Estatísticas ainda não salvas."),
                        "no insertion failure or retry-insertion message " + language+theme);
                    Require(Control<Border>("StatisticsErrorPanel").Visibility==Visibility.Visible && Control<TextBlock>("StatisticsStatusText").Text==UiText.Get("Contagens desta sessão ainda não salvas."),
                        "pending status is explicit and localized " + language+theme);
                    Require(AutomationProperties.GetLiveSetting(Control<TextBlock>("StatisticsErrorDescription"))==AutomationLiveSetting.Polite,
                        "pending description accessible " + language+theme);
                    var after=await File.ReadAllBytesAsync(AppPaths.UsageFile);
                    Require(before.SequenceEqual(after), "original stored bytes preserved " + language+theme);
                    var element=(FrameworkElement)window.Content; window.Content=null; LabMotion.SetReduced(element,true);
                    var host=new Border { Child=element,Width=980,Height=680 }; host.Measure(new Size(980,680));host.Arrange(new Rect(0,0,980,680));host.UpdateLayout();
                    var notice=Control<Border>("StatisticsErrorPanel");
                    Require(notice.ActualWidth>0 && notice.ActualWidth<980 && Control<TextBlock>("StatisticsErrorDescription").ActualHeight>0,
                        "pending notice fits compact layout " + language+theme);
                    var image=new RenderTargetBitmap(980,680,96,96,PixelFormats.Pbgra32);image.Render(host);
                    var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));
                    using(var stream=File.Create(Path.Combine(output,language+"-"+theme+"-pending.png")))encoder.Save(stream);
                    host.Child=null;window.Content=element;
                }
                await (Task)expansion.Invoke(window,[snippet,40])!;
                Require(Control<TextBlock>("StatusText").Text==UiText.Format($"{snippet.Trigger} inserido") && Control<Border>("StatisticsErrorPanel").Visibility==Visibility.Collapsed,
                    "next use clears pending warning after save " + language+theme);
                var reloaded=new UsageService();await reloaded.LoadAsync();
                Require(reloaded.For(snippet.Id)!.Count==4 && reloaded.QuickAccent.Count==2 && reloaded.QuickAccent.Characters["é"]==1,
                    "real fresh reader verifies exact accumulated counts " + language+theme);
                var unexpected=typeof(MainWindow).GetMethod("TryUpdateAuxiliaryUsageAsync",flags)!;
                var contained=await (Task<bool>)unexpected.Invoke(window,[(Func<Task>)(()=>Task.FromException(new InvalidOperationException("Synthetic observer error")))])!;
                Require(!contained && Control<TextBlock>("StatusText").Text==UiText.Format($"{snippet.Trigger} inserido"),
                    "unexpected auxiliary failure stays local " + language+theme);
                await File.WriteAllTextAsync(AppPaths.UsageFile,"{invalid");
                await window.ReloadStatisticsForEvidenceAsync(980);
                await (Task)expansion.Invoke(window,[snippet,40])!;
                Require(Control<TextBlock>("StatusText").Text==UiText.Format($"{snippet.Trigger} inserido")+" · "+UiText.Get("Contagens de atalhos e acentos indisponíveis.") &&
                    Control<TextBlock>("TotalExpansionsText").Text=="—" && await File.ReadAllTextAsync(AppPaths.UsageFile)=="{invalid",
                    "read-unavailable differs from pending and preserves the invalid original " + language+theme);
                window.DisposeCaptureEvidence();
            }
            Require(unhandled==0,"no usage failure reaches global Dispatcher");
        }
        finally { Application.Current.DispatcherUnhandledException -= onError; UiText.Apply("pt-BR"); }
        File.WriteAllLines(Path.Combine(output,"result.txt"),checks);
    }
}
