using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SlashText.Models;
using SlashText.Services;

namespace SlashText.Views;

/// <summary>
/// Guided creation for the FIRST version: a text snippet or a text form with
/// fillable fields. More advanced field types and .sgshortcut import are future
/// versions. Existing advanced editor remains fully available.
/// </summary>
public sealed class ShortcutCreationWizardWindow : Window
{
    private readonly HashSet<string> _existingTriggers;
    private readonly TemplateEngine _engine = new();
    private readonly Dictionary<string, string> _values =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Grid _page = new();
    private readonly StackPanel _fieldPreview = new();
    private readonly TextBlock _resultPreview = new();
    private readonly TextBlock _feedback = new();
    private readonly TextBox _name = new();
    private readonly TextBox _trigger = new();
    private readonly TextBox _body = new();
    private readonly TextBox _newField = new();
    private readonly ComboBox _category = new();
    private readonly TextBlock _stepTitle = new();
    private bool _formMode;

    public Snippet? CreatedSnippet { get; private set; }
    public bool UseAdvancedEditor { get; private set; }

    public ShortcutCreationWizardWindow(IEnumerable<string> existingTriggers)
    {
        _existingTriggers = new HashSet<string>(existingTriggers, StringComparer.OrdinalIgnoreCase);
        Title = "SlashGuedes · Criar atalho";
        Width = 1050;
        Height = 750;
        MinWidth = 830;
        MinHeight = 590;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "Lab.bg");
        SourceInitialized += (_, _) => ThemeService.ApplyToWindow(this);
        AuxiliaryWindowTheme.Apply(this);

        Content = _page;
        _page.Margin = new Thickness(24);
        ShowModeSelection();
    }

    private static TextBlock Text(string value, int size = 13, bool strong = false)
        => new()
        {
            Text = value, FontSize = size,
            FontWeight = strong ? FontWeights.SemiBold : FontWeights.Normal,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };

    private static Button Action(string title, Action callback)
    {
        var button = new Button
        {
            Content = title, MinHeight = 38, MinWidth = 96,
            Padding = new Thickness(14, 7, 14, 7),
            Margin = new Thickness(0, 0, 10, 0)
        };
        button.Click += (_, _) => callback();
        return button;
    }

    private static StackPanel Vertical(int gap = 0) => new()
    {
        Orientation = Orientation.Vertical,
        Margin = new Thickness(0, 0, 0, gap)
    };

    private static Border Surface(UIElement child, Thickness? padding = null) => new()
    {
        Child = child, CornerRadius = new CornerRadius(12),
        BorderThickness = new Thickness(1),
        Padding = padding ?? new Thickness(16),
        Margin = new Thickness(0, 0, 0, 12)
    };

    private void SetSurfaceResources(Border border)
    {
        border.SetResourceReference(Border.BackgroundProperty, "Lab.surface");
        border.SetResourceReference(Border.BorderBrushProperty, "Lab.line");
    }

    private void ShowModeSelection()
    {
        _page.Children.Clear();
        _page.RowDefinitions.Clear();
        _page.ColumnDefinitions.Clear();

        var main = Vertical();
        main.Children.Add(Text("SLASHGUEDES / ASSISTENTE", 12, true));
        main.Children.Add(new TextBlock
        {
            Text = "O que vamos criar hoje?", FontSize = 26,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 8, 0, 6)
        });
        main.Children.Add(Text("Escolha como começar. Todos os atalhos continuam editáveis depois.", 14));

        var choices = new UniformGrid
        {
            Columns = 2, Rows = 2,
            Margin = new Thickness(0, 28, 0, 18)
        };
        AddChoice(choices, "01  Texto rápido",
            "Uma frase, assinatura ou resposta pronta. Sem configurações complexas.",
            () => ShowEditor(false));
        AddChoice(choices, "02  Formulário inteligente",
            "Peça nome, assunto e outras informações antes de inserir o texto.",
            () => ShowEditor(true));
        AddChoice(choices, "03  Editor avançado",
            "Abrir o editor tradicional, com formatação, imagens e todas as variáveis existentes.",
            () =>
            {
                UseAdvancedEditor = true;
                DialogResult = true;
            });
        AddChoice(choices, "04  Criar com Gepeto",
            "Estamos preparando arquivos .sgshortcut. Essa importação chega numa próxima versão.",
            () => MessageBox.Show(this,
                "A integração de arquivos .sgshortcut será implementada em outra Preview.\n\n" +
                "Por enquanto, você pode criar o atalho guiado aqui ou usar o editor avançado.",
                "Criar com Gepeto · Próximos passos",
                MessageBoxButton.OK, MessageBoxImage.Information));
        main.Children.Add(choices);
        main.Children.Add(Text(
            "Seus atalhos antigos não serão alterados. Esta experiência utiliza o mesmo motor local do SlashGuedes.", 12));

        var scroll = new ScrollViewer
        {
            Content = main, VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        _page.Children.Add(scroll);
    }

    private void AddChoice(Panel panel, string title, string description, Action action)
    {
        var detail = Vertical();
        detail.Children.Add(Text(title, 17, true));
        detail.Children.Add(new TextBlock
        {
            Text = description, Margin = new Thickness(0, 12, 0, 0),
            FontSize = 13, TextWrapping = TextWrapping.Wrap
        });
        var button = new Button
        {
            Content = detail, Padding = new Thickness(18),
            Margin = new Thickness(0, 0, 12, 12),
            MinHeight = 142,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Top,
            ToolTip = title
        };
        button.Click += (_, _) => action();
        panel.Children.Add(button);
    }

    private void ShowEditor(bool formMode)
    {
        _formMode = formMode;
        _page.Children.Clear();
        _page.RowDefinitions.Clear();
        _page.ColumnDefinitions.Clear();
        _values.Clear();
        _name.Text = "";
        _trigger.Text = "/";
        _body.Text = "";
        _newField.Text = "";
        _category.ItemsSource = ShortcutCategories.All.Select(x => x.Name).ToArray();
        _category.SelectedItem = "Geral";

        _page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 18) };
        var back = Action("← Tipos", ShowModeSelection);
        DockPanel.SetDock(back, Dock.Left);
        header.Children.Add(back);
        var intro = Vertical();
        _stepTitle.Text = formMode ? "Novo formulário inteligente" : "Novo texto rápido";
        _stepTitle.FontSize = 22;
        _stepTitle.FontWeight = FontWeights.SemiBold;
        intro.Children.Add(_stepTitle);
        intro.Children.Add(Text("Configure à esquerda e teste o resultado à direita.", 12));
        header.Children.Add(intro);
        Grid.SetRow(header, 0);
        _page.Children.Add(header);

        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(columns, 1);
        _page.Children.Add(columns);

        var editor = Vertical();
        editor.Children.Add(Text("Nome do atalho", 13, true));
        SetupInput(_name, "Ex.: Resposta de atendimento");
        editor.Children.Add(_name);
        editor.Children.Add(Text("Comando para ativar", 13, true));
        SetupInput(_trigger, "/atendimento");
        editor.Children.Add(_trigger);
        editor.Children.Add(Text("Categoria", 13, true));
        _category.MinHeight = 36;
        _category.Margin = new Thickness(0, 5, 0, 13);
        editor.Children.Add(_category);
        editor.Children.Add(Text("Texto que será inserido", 13, true));
        _body.AcceptsReturn = true;
        _body.TextWrapping = TextWrapping.Wrap;
        _body.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _body.MinHeight = 145;
        _body.Height = 205;
        _body.MaxLength = ShortcutWizardPlan.MaxBodyLength;
        _body.Margin = new Thickness(0, 5, 0, 8);
        _body.Padding = new Thickness(10);
        editor.Children.Add(_body);
        if (formMode)
        {
            editor.Children.Add(Text("Adicionar campo sem escrever códigos", 13, true));
            var inputRow = new DockPanel { Margin = new Thickness(0, 7, 0, 5) };
            _newField.MinWidth = 190;
            _newField.MinHeight = 36;
            _newField.Margin = new Thickness(0, 0, 8, 0);
            _newField.ToolTip = "Ex.: Nome do cliente, protocolo, motivo";
            DockPanel.SetDock(_newField, Dock.Left);
            inputRow.Children.Add(_newField);
            inputRow.Children.Add(Action("+ Adicionar campo", AddField));
            editor.Children.Add(inputRow);
            editor.Children.Add(Text(
                "O campo será inserido na posição do cursor e aparecerá no teste ao lado.", 12));
        }
        var editorScroll = new ScrollViewer
        {
            Content = editor, VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        var editorBorder = Surface(editorScroll);
        editorBorder.Margin = new Thickness(0);
        SetSurfaceResources(editorBorder);
        Grid.SetColumn(editorBorder, 0);
        columns.Children.Add(editorBorder);

        var preview = Vertical();
        preview.Children.Add(Text("TESTAR ATALHO", 12, true));
        preview.Children.Add(new TextBlock
        {
            Text = "Preencha os campos e confira o texto antes de salvar.",
            FontSize = 13, Margin = new Thickness(0, 9, 0, 12),
            TextWrapping = TextWrapping.Wrap
        });
        var fillScroll = new ScrollViewer
        {
            Content = _fieldPreview,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 190
        };
        preview.Children.Add(fillScroll);
        preview.Children.Add(Text("Resultado em tempo real", 13, true));
        _resultPreview.TextWrapping = TextWrapping.Wrap;
        _resultPreview.MinHeight = 150;
        var resultScroll = new ScrollViewer
        {
            Content = _resultPreview,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, 10, 0, 6)
        };
        preview.Children.Add(resultScroll);
        var previewBorder = Surface(preview);
        previewBorder.Margin = new Thickness(0);
        SetSurfaceResources(previewBorder);
        Grid.SetColumn(previewBorder, 2);
        columns.Children.Add(previewBorder);

        var footer = new DockPanel { Margin = new Thickness(0, 18, 0, 0) };
        _feedback.SetResourceReference(TextBlock.ForegroundProperty, "Lab.error");
        _feedback.TextWrapping = TextWrapping.Wrap;
        _feedback.MaxWidth = 530;
        DockPanel.SetDock(_feedback, Dock.Left);
        footer.Children.Add(_feedback);
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        actions.Children.Add(Action("Cancelar", () => DialogResult = false));
        actions.Children.Add(Action("Criar atalho", Commit));
        DockPanel.SetDock(actions, Dock.Right);
        footer.Children.Add(actions);
        Grid.SetRow(footer, 2);
        _page.Children.Add(footer);

        _body.TextChanged -= BodyChanged;
        _body.TextChanged += BodyChanged;
        _name.TextChanged -= RefreshValidity;
        _trigger.TextChanged -= RefreshValidity;
        _name.TextChanged += RefreshValidity;
        _trigger.TextChanged += RefreshValidity;
        SyncFields();
        _name.Focus();
    }

    private static void SetupInput(TextBox box, string hint)
    {
        box.Margin = new Thickness(0, 5, 0, 13);
        box.MinHeight = 38;
        box.MaxLength = 120;
        box.Padding = new Thickness(9, 5, 9, 5);
        box.ToolTip = hint;
    }

    private void RefreshValidity(object sender, TextChangedEventArgs e)
        => RefreshPreview();

    private void BodyChanged(object sender, TextChangedEventArgs e)
        => SyncFields();

    private void AddField()
    {
        var label = _newField.Text.Trim();
        var id = ShortcutWizardPlan.FieldIdentifier(label);
        if (id.Length == 0)
        {
            _feedback.Text = "Escreva o nome do campo. Ex.: Nome do cliente.";
            return;
        }
        if (_engine.GetFillableFields(_body.Text).Count >= ShortcutWizardPlan.MaxFields)
        {
            _feedback.Text = "Máximo de 20 campos no assistente.";
            return;
        }
        if (_engine.GetFillableFields(_body.Text).Any(x =>
                x.Name.Equals(id, StringComparison.OrdinalIgnoreCase)))
        {
            _feedback.Text = "Esse campo já está no texto.";
            return;
        }
        var token = "{{" + id + "}}";
        var pos = _body.CaretIndex;
        _body.Text = _body.Text.Insert(pos, token);
        _body.CaretIndex = pos + token.Length;
        _body.Focus();
        _newField.Clear();
        SyncFields();
    }

    private void SyncFields()
    {
        if (_fieldPreview is null || _resultPreview is null) return;
        var fields = _engine.GetFillableFields(_body.Text);
        var active = new HashSet<string>(
            fields.Select(x => x.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var key in _values.Keys.Where(x => !active.Contains(x)).ToArray())
            _values.Remove(key);

        _fieldPreview.Children.Clear();
        foreach (var field in fields.Take(ShortcutWizardPlan.MaxFields))
        {
            _fieldPreview.Children.Add(Text(
                field.Name.Replace('_', ' '), 12, true));
            var input = new TextBox
            {
                Text = _values.TryGetValue(field.Name, out var old)
                    ? old : field.DefaultValue ?? "",
                MinHeight = 34,
                Margin = new Thickness(0, 4, 0, 10),
                Padding = new Thickness(7, 4, 7, 4)
            };
            var id = field.Name;
            _values[id] = input.Text;
            input.TextChanged += (_, _) =>
            {
                _values[id] = input.Text;
                RefreshPreview();
            };
            _fieldPreview.Children.Add(input);
        }
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        if (_resultPreview is null || _feedback is null) return;
        try
        {
            _resultPreview.Text = _engine.Render(_body.Text, _values)
                .Replace(TemplateEngine.TabMarker, "  → TAB  ");
            var count = _engine.GetFillableFields(_body.Text).Count;
            _feedback.Text = count > ShortcutWizardPlan.MaxFields
                ? "Limite de 20 campos atingido." : "";
        }
        catch (Exception)
        {
            _resultPreview.Text = "Não foi possível renderizar a prévia.";
        }
    }

    private void Commit()
    {
        if (!ShortcutWizardPlan.TryCreate(_name.Text, _trigger.Text,
                _category.SelectedItem as string ?? "Geral",
                _body.Text, _existingTriggers, out var snippet, out var error))
        {
            _feedback.Text = error;
            return;
        }
        CreatedSnippet = snippet;
        DialogResult = true;
    }
}
