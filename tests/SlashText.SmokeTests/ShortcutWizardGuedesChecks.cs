using SlashText.Models;
using SlashText.Services;

internal static class ShortcutWizardGuedesChecks
{
    public static void Run()
    {
        Require(ShortcutWizardPlan.FieldIdentifier("Nome do Cliente") ==
            "nome_do_cliente", "campo convertido sem sintaxe técnica");
        Require(ShortcutWizardPlan.FieldIdentifier("Número / Protocolo") ==
            "numero_protocolo", "identificador aceita acentos e pontuação");
        Require(ShortcutWizardPlan.TryCreate("Atendimento", "/atendimento",
            "Trabalho", "Olá, {{nome}}! Protocolo: {{protocolo}}.",
            [], out var snippet, out _), "criar formulário válido");
        Require(snippet is not null && snippet.Category == "Trabalho" &&
            snippet.Format == SnippetFormat.Plain, "plano usa modelo legado de snippet");
        var engine = new TemplateEngine();
        var fields = engine.GetFillableFields(snippet!.Content);
        Require(fields.Count == 2, "template com dois campos preenchíveis");
        var rendered = engine.Render(snippet.Content,
            new Dictionary<string, string>
            {
                ["nome"] = "Mariana",
                ["protocolo"] = "GA-154"
            });
        Require(rendered == "Olá, Mariana! Protocolo: GA-154.",
            "prévia renderizada pelo motor real");
        Require(!ShortcutWizardPlan.TryCreate("Atendimento", "/atendimento",
            "Trabalho", "Novo", ["/atendimento"], out _, out _),
            "gatilho repetido deve ser rejeitado");
        Require(!ShortcutWizardPlan.TryCreate("Teste", "teste",
            "Geral", "Conteúdo", [], out _, out _),
            "mesmas regras de gatilho existentes");
        Require(!ShortcutWizardPlan.TryCreate("", "/novo",
            "Geral", "Conteúdo", [], out _, out _),
            "nome é obrigatório");
        Require(!ShortcutWizardPlan.TryCreate("Teste", "/novo",
            "Geral", "", [], out _, out _),
            "conteúdo é obrigatório");
        Require(!ShortcutWizardPlan.TryCreate("Teste", "/novo",
            "Geral", new string('a', ShortcutWizardPlan.MaxBodyLength + 1),
            [], out _, out _), "limite seguro do corpo do atalho");

        var overflow = string.Join(" ", Enumerable.Range(1, 21)
            .Select(i => "{{campo" + i + "}}"));
        Require(!ShortcutWizardPlan.TryCreate("Teste", "/novo",
            "Geral", overflow, [], out _, out _),
            "limite dos campos de formulário");

        Console.WriteLine("SlashGuedes: criador visual, validação e prévia dinâmica OK.");
    }
    private static void Require(bool value, string reason)
    {
        if (!value) throw new InvalidOperationException(
            "Teste do assistente de atalhos falhou: " + reason);
    }
}
