namespace SlashText.Views;

public static class SettingsHelpContent
{
    public static ScreenHelpDefinition Create() => new(UiText.Get("Como usar as Configurações"), UiText.Get("Preferências e manutenção dos seus dados locais."),
    [
        new("appearance", UiText.Get("PREFERÊNCIAS"), UiText.Get("Aparência e comportamento"), "Palette", UiText.Get("Ajustes salvos automaticamente."),
            [UiText.Get("Escolha Claro, Preto ou Seguir o Windows. Telas, menus e diálogos acompanham o tema."), UiText.Get("Manter na bandeja permite continuar usando os atalhos depois de fechar a janela."), UiText.Get("Os movimentos respeitam a acessibilidade do Windows. O piloto não inicia com o Windows nem atualiza a versão oficial.")],
            UiText.Get("As preferências existentes continuam em uso; os valores do Visual Lab não são novos defaults."), "SettingsAppearanceCard", Demo: "settings-preferences"),
        new("clipboard", UiText.Get("PROTEÇÃO"), UiText.Get("Privacidade da área de transferência"), "ShieldCheck", UiText.Get("Expansões temporárias e cópias escolhidas têm comportamentos diferentes."),
            [UiText.Get("Durante uma expansão, o conteúdo temporário é marcado para não entrar no histórico nem na sincronização do Windows."), UiText.Get("Ao terminar, o SlashDesk restaura o conteúdo anterior, inclusive uma área de transferência vazia. Uma nova cópia feita durante a expansão é preservada."), UiText.Get("Copiar uma captura, um resultado de OCR ou um bloco de código segue as preferências de histórico e sincronização do Windows.")],
            UiText.Get("Outros aplicativos podem observar a área de transferência. A proteção não apaga itens já registrados nem garante sigilo absoluto."), "SettingsBehaviorCard"),
        new("import", UiText.Get("SEUS TEXTOS"), UiText.Get("Importar atalhos"), "Upload", UiText.Get("SlashDesk, Text Blaze e Espanso."),
            [UiText.Get("Selecione a origem e o arquivo correspondente. A importação informa compatibilidade e conflitos."), UiText.Get("Você escolhe se substitui conflitos ou mantém os atalhos atuais. Cancelar preserva os dados."), UiText.Get("Antes de salvar, um backup protege o estado anterior. Modo protegido impede escrita até a recuperação.")],
            UiText.Get("Revise os avisos de compatibilidade; nenhum formato externo é executado como código."), "SettingsImportCard", Demo: "settings-import"),
        new("backup", UiText.Get("PROTEÇÃO"), UiText.Get("Backup e Meus emojis"), "Archive", UiText.Get("Arquivos verificados antes de restaurar."),
            [UiText.Get("Criar agora salva um ZIP local com atalhos, preferências, contagens, histórico, imagens e Meus emojis."), UiText.Get("São mantidos até sete ZIPs gerados pelo app. Backups antigos de atalhos (.md) ficam preservados na pasta até remoção manual; para recuperar, use Importar atalhos → SlashDesk."), UiText.Get("Restaurar ZIP verifica hashes e prepara os arquivos antes de aplicar. Um backup protege o estado atual; falhas durante a aplicação revertem a transação."), UiText.Get("Backups antigos que não incluem a coleção preservam os emojis atuais. Backups novos substituem a coleção pela versão salva, inclusive uma coleção vazia.")],
            UiText.Get("Os PNGs e a identidade dos emojis são preservados. Os arquivos de captura externos não são copiados para o ZIP."), "SettingsBackupCard", Demo: "settings-backup"),
        new("assets", UiText.Get("MANUTENÇÃO"), UiText.Get("Analisar imagens"), "Image", UiText.Get("Revisar primeiro, remover depois."),
            [UiText.Get("A análise procura imagens dos atalhos que não são mais referenciadas."), UiText.Get("Nenhuma exclusão acontece sem confirmação. A confirmação cria backup antes de remover somente os órfãos."), UiText.Get("Meus emojis ficam em uma coleção separada e não entram na limpeza dos assets dos atalhos.")],
            UiText.Get("Referências inválidas bloqueiam a exclusão por segurança."), "SettingsAnalyzeAssetsButton", Demo: "settings-assets"),
        new("updates", UiText.Get("APLICATIVO"), UiText.Get("Atualizações e dados locais"), "RefreshCw", UiText.Get("Estado real da última consulta."),
            [UiText.Get("Verificar agora consulta as releases oficiais. A busca manual continua disponível depois de Lembrar depois."), UiText.Get("A preferência automática usa o monitor existente; o piloto mantém atualizações desativadas e informa isso na tela."), UiText.Get("Explorar dados abre a pasta realmente usada. Preserve a pasta inteira ao mover o portátil.")],
            UiText.Get("A tela não simula atualização ou instalação. A validação do aviso de release real continua separada na #37."), "SettingsUpdatesCard", Demo: "settings-updates")
    ]);
}
