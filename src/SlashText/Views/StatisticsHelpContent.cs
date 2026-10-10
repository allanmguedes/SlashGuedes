namespace SlashText.Views;

public static class StatisticsHelpContent
{
    public static ScreenHelpDefinition Create() => new("Como ler as Estatísticas", "Contagens reais armazenadas no próprio computador.",
    [
        new("shortcuts", "CONTAGENS", "Expansões e ranking", "Keyboard", "Uso acumulado dos atalhos.",
            ["Expansões soma os usos registrados; caracteres poupados desconta o tamanho do gatilho.", "O ranking mostra até oito atalhos existentes, em ordem de uso. As barras são relativas ao primeiro colocado.", "Atalhos com uso registrado inclui registros antigos, mesmo se o atalho foi removido; por isso o total pode ser maior que o ranking."],
            "Os nomes longos têm texto completo ao passar o mouse. Nenhum conteúdo digitado é coletado por esta tela.", "StatisticsRankingCard", Demo: "statistics-shortcuts"),
        new("captures", "CONTAGENS", "Capturas por tipo", "Camera", "A base é o histórico salvo disponível.",
            ["O total inclui imagens, capturas longas, GIF e MP4 presentes no histórico.", "Região, Monitor e Janela contam o tipo de área. GIF e MP4 contam a mídia; as categorias podem se sobrepor.", "Excluir uma entrada do histórico reduz a contagem. Estes dados não são um total vitalício."],
            "As barras mostram a proporção de cada tipo no total atual; os números não devem ser somados como partes exclusivas.", "StatisticsCaptureCard", Demo: "statistics-captures"),
        new("time", "ESTIMATIVA", "Tempo economizado", "Clock3", "A fórmula existente estima digitação poupada.",
            ["O tempo equivale aos caracteres poupados divididos por 200, arredondado para cima em minutos.", "Média por expansão usa caracteres poupados divididos pelas expansões, com a mesma formatação existente.", "É uma estimativa, não uma medição do tempo que você passou digitando."],
            "A tela apresenta uso acumulado; não cria séries mensais, exportação ou nova coleta.", "TimeSavedText", Demo: "statistics-time"),
        new("accents", "CONTAGENS", "Acentos favoritos", "Languages", "Caracteres realmente inseridos pelo Acento Rápido.",
            ["O favorito é o caractere com mais inserções registradas. Empates seguem a ordenação existente.", "Os demais caracteres aparecem com suas contagens, até oito no total.", "Cliques na prévia de teste não contam como uso global."],
            "Sem uso, a tela mostra zero e orientações para começar.", "QuickAccentFavoriteText", Demo: "statistics-accents"),
        new("privacy", "DADOS LOCAIS", "Privacidade e indisponibilidade", "ShieldCheck", "A leitura das contagens não altera os dados.",
            ["As estatísticas permanecem locais, sem envio e sem registrar o conteúdo digitado.", "Se o arquivo de uso não puder ser lido, os dados afetados aparecem como indisponíveis, sem apresentar zeros como sucesso.", "Falhas de estatísticas não impedem o uso dos demais recursos."],
            "Não há mudança de formato ou caminho dos arquivos existentes.", "StatisticsPrivacyCard", Demo: "statistics-privacy")
    ]);
}
