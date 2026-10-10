namespace SlashText.Views;

public static class SettingsHelpContent
{
    public static ScreenHelpDefinition Create() => new("Como usar as Configurações", "Preferências e manutenção dos seus dados locais.",
    [
        new("appearance", "PREFERÊNCIAS", "Aparência e comportamento", "Palette", "Ajustes salvos automaticamente.",
            ["Escolha Claro, Preto ou Seguir o Windows. Telas, menus e diálogos acompanham o tema.", "Manter na bandeja permite continuar usando os atalhos depois de fechar a janela.", "Os movimentos respeitam a acessibilidade do Windows. O piloto não inicia com o Windows nem atualiza a versão oficial."],
            "As preferências existentes continuam em uso; os valores do Visual Lab não são novos defaults.", "SettingsAppearanceCard", Demo: "settings-preferences"),
        new("import", "SEUS TEXTOS", "Importar atalhos", "Upload", "SlashDesk, Text Blaze e Espanso.",
            ["Selecione a origem e o arquivo correspondente. A importação informa compatibilidade e conflitos.", "Você escolhe se substitui conflitos ou mantém os atalhos atuais. Cancelar preserva os dados.", "Antes de salvar, um backup protege o estado anterior. Modo protegido impede escrita até a recuperação."],
            "Revise os avisos de compatibilidade; nenhum formato externo é executado como código.", "SettingsImportCard", Demo: "settings-import"),
        new("backup", "PROTEÇÃO", "Backup e Meus emojis", "Archive", "Arquivos verificados antes de restaurar.",
            ["Criar agora salva um ZIP local com atalhos, preferências, contagens, histórico, imagens e Meus emojis.", "Restaurar ZIP verifica hashes e prepara os arquivos antes de aplicar. Um backup protege o estado atual; falhas durante a aplicação revertem a transação.", "Backups antigos que não incluem a coleção preservam os emojis atuais. Backups novos substituem a coleção pela versão salva, inclusive uma coleção vazia."],
            "Os PNGs e a identidade dos emojis são preservados. Os arquivos de captura externos não são copiados para o ZIP.", "SettingsBackupCard", Demo: "settings-backup"),
        new("assets", "MANUTENÇÃO", "Analisar imagens", "Image", "Revisar primeiro, remover depois.",
            ["A análise procura imagens dos atalhos que não são mais referenciadas.", "Nenhuma exclusão acontece sem confirmação. A confirmação cria backup antes de remover somente os órfãos.", "Meus emojis ficam em uma coleção separada e não entram na limpeza dos assets dos atalhos."],
            "Referências inválidas bloqueiam a exclusão por segurança.", "SettingsAnalyzeAssetsButton", Demo: "settings-assets"),
        new("updates", "APLICATIVO", "Atualizações e dados locais", "RefreshCw", "Estado real da última consulta.",
            ["Verificar agora consulta as releases oficiais. A busca manual continua disponível depois de Lembrar depois.", "A preferência automática usa o monitor existente; o piloto mantém atualizações desativadas e informa isso na tela.", "Explorar dados abre a pasta realmente usada. Preserve a pasta inteira ao mover o portátil."],
            "A tela não simula atualização ou instalação. A validação do aviso de release real continua separada na #37.", "SettingsUpdatesCard", Demo: "settings-updates")
    ]);
}
