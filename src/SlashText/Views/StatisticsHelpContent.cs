namespace SlashText.Views;

public static class StatisticsHelpContent
{
    public static ScreenHelpDefinition Create() => new(UiText.Get("Como ler as Estatísticas"), UiText.Get("Contagens reais armazenadas no próprio computador."),
    [
        new("shortcuts", UiText.Get("CONTAGENS"), UiText.Get("Expansões e ranking"), "Keyboard", UiText.Get("Uso acumulado dos atalhos."),
            [UiText.Get("Expansões soma os usos registrados; caracteres poupados desconta o tamanho do gatilho."), UiText.Get("O ranking mostra até oito atalhos existentes, em ordem de uso. As barras são relativas ao primeiro colocado."), UiText.Get("Atalhos com uso registrado inclui registros antigos, mesmo se o atalho foi removido; por isso o total pode ser maior que o ranking.")],
            UiText.Get("Os nomes longos têm texto completo ao passar o mouse. Nenhum conteúdo digitado é coletado por esta tela."), "StatisticsRankingCard", Demo: "statistics-shortcuts"),
        new("captures", UiText.Get("CONTAGENS"), UiText.Get("Capturas por tipo"), "Camera", UiText.Get("A base é o histórico salvo disponível."),
            [UiText.Get("O total inclui imagens, capturas longas, GIF e MP4 presentes no histórico."), UiText.Get("Região, Monitor e Janela contam o tipo de área. GIF e MP4 contam a mídia; as categorias podem se sobrepor."), UiText.Get("Excluir uma entrada do histórico reduz a contagem. Estes dados não são um total vitalício.")],
            UiText.Get("As barras mostram a proporção de cada tipo no total atual; os números não devem ser somados como partes exclusivas."), "StatisticsCaptureCard", Demo: "statistics-captures"),
        new("time", UiText.Get("ESTIMATIVA"), UiText.Get("Tempo economizado"), "Clock3", UiText.Get("A fórmula existente estima digitação poupada."),
            [UiText.Get("O tempo equivale aos caracteres poupados divididos por 200, arredondado para cima em minutos."), UiText.Get("Média por expansão usa caracteres poupados divididos pelas expansões, com a mesma formatação existente."), UiText.Get("É uma estimativa, não uma medição do tempo que você passou digitando.")],
            UiText.Get("A tela apresenta uso acumulado; não cria séries mensais, exportação ou nova coleta."), "TimeSavedText", Demo: "statistics-time"),
        new("accents", UiText.Get("CONTAGENS"), UiText.Get("Acentos favoritos"), "Languages", UiText.Get("Caracteres realmente inseridos pelo Acento Rápido."),
            [UiText.Get("O favorito é o caractere com mais inserções registradas. Empates seguem a ordenação existente."), UiText.Get("Os demais caracteres aparecem com suas contagens, até oito no total."), UiText.Get("Cliques na prévia de teste não contam como uso global.")],
            UiText.Get("Sem uso, a tela mostra zero e orientações para começar."), "QuickAccentFavoriteText", Demo: "statistics-accents"),
        new("privacy", UiText.Get("DADOS LOCAIS"), UiText.Get("Privacidade e indisponibilidade"), "ShieldCheck", UiText.Get("A leitura das contagens não altera os dados."),
            [UiText.Get("As estatísticas permanecem locais, sem envio e sem registrar o conteúdo digitado."), UiText.Get("Se o arquivo de uso não puder ser lido, os dados afetados aparecem como indisponíveis, sem apresentar zeros como sucesso."), UiText.Get("Falhas de estatísticas não impedem o uso dos demais recursos."), UiText.Get("As contagens pendentes estão nesta sessão. Tentaremos salvá-las no próximo uso de atalho ou acento. Se o app fechar antes disso, elas poderão ser perdidas.")],
            UiText.Get("Não há mudança de formato ou caminho dos arquivos existentes."), "StatisticsPrivacyCard", Demo: "statistics-privacy")
    ]);
}
