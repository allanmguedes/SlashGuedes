namespace SlashText.Views;

public static class QuickAccentHelpContent
{
    public static ScreenHelpDefinition Create() => new(UiText.Get("Como usar Acento Rápido"), UiText.Get("Caracteres especiais com as suas preferências — guia local."),
    [
        new("activate", UiText.Get("COMEÇAR"), UiText.Get("Ativar e digitar"), "Keyboard", UiText.Get("Use os caracteres nos aplicativos onde você digita."),
            [UiText.Get("Ative o Acento Rápido no topo e escolha Espaço, seta esquerda ou seta direita."), UiText.Get("Pronto para usar confirma que o recurso foi iniciado. Pausado neste aplicativo indica uma exclusão; Indisponível indica falha na inicialização."), UiText.Get("Segure uma letra e toque na tecla de ativação. Cada novo toque avança uma opção; solte a letra para inserir. Esc cancela.")],
            UiText.Get("Caps Lock e Shift determinam maiúsculas. Desligar preserva suas preferências. Se estiver indisponível, desligue e ative novamente para tentar iniciar."), "QuickAccentEnabledCheckBox", Demo: "accent"),
        new("preview", UiText.Get("EXPERIMENTAR"), UiText.Get("Prévia e campo de teste"), "Type", UiText.Get("Confira os caracteres dos conjuntos selecionados."),
            [UiText.Get("Escolha a letra no topo da prévia."), UiText.Get("Clique em um caractere ou use Tab e Espaço para inseri-lo no campo de teste."), UiText.Get("Limpar teste apaga somente o texto desse campo.")],
            UiText.Get("Os cliques da prévia são locais e não registram uso global. O campo também permite testar a ativação real quando o piloto estiver aberto."), "QuickAccentPreviewLetterBox", Demo: "accent-preview"),
        new("preferences", UiText.Get("PERSONALIZAR"), UiText.Get("Atraso e posição"), "Clock3", UiText.Get("Adapte o painel ao seu ritmo."),
            [UiText.Get("Escolha 100, 200 ou 500 ms, ou informe um valor de 0 a 2.000 milissegundos. Enter confirma o número."), UiText.Get("Abra Aparência e ordem dos caracteres para escolher a posição do painel."), UiText.Get("Mostrar código Unicode identifica o caractere; priorizar mais usados usa o histórico de digitação real.")],
            UiText.Get("As preferências são salvas automaticamente. O campo aceita até quatro dígitos entre 0 e 2.000; entradas inválidas são recusadas por inteiro, inclusive ao colar. Você pode apagar o campo para redigitar; deixá-lo vazio preserva o último atraso salvo."), "QuickAccentDelayBox", Demo: "accent-preferences"),
        new("sets", UiText.Get("PERSONALIZAR"), UiText.Get("Conjuntos de caracteres"), "Languages", UiText.Get("Selecione idiomas, moedas e símbolos."),
            [UiText.Get("Abra Conjuntos de caracteres e use Somente PT-BR ou Selecionar todos para os nove conjuntos."), UiText.Get("Marque os idiomas, moedas e símbolos que usa."), UiText.Get("A prévia é atualizada com as opções reais para a letra escolhida.")],
            UiText.Get("Ao desmarcar o último conjunto, Português (Brasil) permanece selecionado. Letras sem opções mostram uma mensagem."), "QuickAccentSetsPanel", Demo: "accent-sets"),
        new("panel", UiText.Get("PERSONALIZAR"), UiText.Get("Aparência e ordem dos caracteres"), "SlidersHorizontal", UiText.Get("Posição, Unicode e prioridade de uso."),
            [UiText.Get("Abra Aparência e ordem dos caracteres para ajustar o painel."), UiText.Get("Escolha centro superior, centro da tela ou centro inferior."), UiText.Get("Ative o código Unicode ou priorize os caracteres mais usados conforme sua preferência.")],
            UiText.Get("Mostrar na tela abre automaticamente a seção recolhida."), "QuickAccentPositionBox", Demo: "accent-preferences"),
        new("exclude", UiText.Get("PERSONALIZAR"), UiText.Get("Aplicativos excluídos"), "AppWindow", UiText.Get("Evite a ativação em aplicativos específicos."),
            [UiText.Get("Abra Aplicativos excluídos e informe os nomes dos processos, como mstsc.exe ou game.exe."), UiText.Get("Separe os nomes por ponto e vírgula ou por linha."), UiText.Get("Saia do campo para salvar a lista.")],
            UiText.Get("Use o nome do processo, sem o caminho da pasta. As preferências são preservadas ao reiniciar o piloto."), "QuickAccentExcludedAppsBox", Demo: "accent-exclude")
    ]);
}
