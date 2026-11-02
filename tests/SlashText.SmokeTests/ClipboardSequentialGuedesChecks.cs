using SlashText.Models;
using SlashText.Services;

internal static class ClipboardSequentialGuedesChecks
{
    public static async Task RunAsync()
    {
        var images = Enumerable.Range(1, 5)
            .Select(x => new ClipboardHistoryItem
            {
                Id = "img" + x, Kind = "image", ImageFile = "img" + x + ".png"
            }).ToArray();
        var history = images.Reverse().Append(new ClipboardHistoryItem
        {
            Id = "text", Kind = "text", Text = "não colar texto"
        });
        var ordered = ClipboardSequentialPasteService.ResolveSelection(history,
            ["img3", "img1", "img5", "img3", "text", "missing"]);
        Check(ordered.Select(i => i.Id).SequenceEqual(["img3", "img1", "img5"]),
            "deve preservar ordem dos cliques e ignorar duplicação/texto");

        var actions = new List<string>();
        var status = await ClipboardSequentialPasteService.RunAsync(
            ordered, index => { actions.Add("focus:" + index); return true; },
            item => { actions.Add("copy:" + item.Id); return true; },
            _ => { actions.Add("paste"); return Task.FromResult(ClipboardPasteService.Result.Sent); },
            _ => { actions.Add("wait"); return Task.CompletedTask; },
            CancellationToken.None);
        Check(status.Reason == ClipboardSequentialPasteService.StopReason.Completed &&
              status.Pasted == 3 && status.Total == 3,
            "colagem bem-sucedida envia três imagens separadas");
        Check(actions.SequenceEqual(
            ["focus:0", "copy:img3", "paste", "wait",
             "focus:1", "copy:img1", "paste", "wait",
             "focus:2", "copy:img5", "paste"]),
            "nunca inverter imagens e esperar somente entre colagens");

        var focusedCount = 0;
        status = await ClipboardSequentialPasteService.RunAsync(
            ordered, _ => focusedCount++ == 0,
            _ => true, _ => Task.FromResult(ClipboardPasteService.Result.Sent),
            _ => Task.CompletedTask, CancellationToken.None);
        Check(status.Pasted == 1 &&
              status.Reason == ClipboardSequentialPasteService.StopReason.TargetChanged,
            "perda de foco interrompe o restante da sequência");

        var copyCount = 0;
        status = await ClipboardSequentialPasteService.RunAsync(
            ordered, _ => true,
            _ => ++copyCount <= 1,
            _ => Task.FromResult(ClipboardPasteService.Result.Sent),
            _ => Task.CompletedTask, CancellationToken.None);
        Check(status.Pasted == 1 &&
              status.Reason == ClipboardSequentialPasteService.StopReason.CopyFailed,
            "falha de arquivo interrompe antes de colar imagem incorreta");

        status = await ClipboardSequentialPasteService.RunAsync(
            ordered, _ => true, _ => true,
            _ => Task.FromResult(ClipboardPasteService.Result.TargetUnavailable),
            _ => Task.CompletedTask, CancellationToken.None);
        Check(status.Pasted == 0 &&
              status.Reason == ClipboardSequentialPasteService.StopReason.PasteFailed,
            "falha de colagem não avança o lote");

        using var cancellation = new CancellationTokenSource();
        status = await ClipboardSequentialPasteService.RunAsync(
            ordered, _ => true, _ => true,
            _ => Task.FromResult(ClipboardPasteService.Result.Sent),
            _ => { cancellation.Cancel(); return Task.CompletedTask; },
            cancellation.Token);
        Check(status.Pasted == 1 &&
              status.Reason == ClipboardSequentialPasteService.StopReason.Cancelled,
            "cancelamento após primeiro print impede o segundo");

        var excessive = Enumerable.Repeat(images[0],
            ClipboardSequentialPasteService.MaximumImages + 1).ToArray();
        status = await ClipboardSequentialPasteService.RunAsync(
            excessive, _ => true, _ => true,
            _ => Task.FromResult(ClipboardPasteService.Result.Sent),
            _ => Task.CompletedTask, CancellationToken.None);
        Check(status.Reason == ClipboardSequentialPasteService.StopReason.TooManyImages &&
              status.Pasted == 0, "limite de imagens aplicado sem colagem");

        status = await ClipboardSequentialPasteService.RunAsync(
            [images[0], new ClipboardHistoryItem { Kind = "text" }],
            _ => true, _ => true,
            _ => Task.FromResult(ClipboardPasteService.Result.Sent),
            _ => Task.CompletedTask, CancellationToken.None);
        Check(status.Reason == ClipboardSequentialPasteService.StopReason.InvalidItem &&
              status.Pasted == 0, "não permitir textos intercalados");

        Console.WriteLine("SlashGuedes clipboard: seleção e colagem de múltiplos prints OK.");
    }

    private static void Check(bool passed, string message)
    {
        if (!passed) throw new InvalidOperationException(
            "Teste de colagem sequencial falhou: " + message);
    }
}
