# Piloto 3.3.0 — Configurações (#67)

Base: `c74b92b140a61c9688655c102ac42713c3489e8c`, Estatísticas aprovada e integrada na PR #78. Referência: Visual Lab publicação 11, contrato congelado em `docs/design/3.3.0/contract/`. Os arquivos/tokens desse contrato permanecem intactos. Os serviços reais continuam em WPF; este piloto não é o site do Lab.

## Abrir e preservar dados

Saia do piloto anterior pelo menu da bandeja. Extraia este ZIP em pasta separada e execute `Abrir-Piloto-Configuracoes.cmd`. Para manter seus dados e preferências de teste, copie a pasta inteira `SlashDeskPilotData` do piloto anterior antes de iniciar. A pasta da versão oficial continua separada.

O piloto abre Configurações. Atualização automática/manual e inicialização com o Windows ficam desativadas, com explicação na tela; as preferências armazenadas não são substituídas por essa restrição. A versão oficial exibida continua sendo a versão real 3.2.0; a identificação do piloto aparece no título. Sem tag/release.

## Avaliação manual

- **Aparência:** selecione Claro, Preto e Seguir o Windows, confira seleção/foco e demais telas/menus/diálogos. Reabra o piloto e confira a persistência. Movimento reduzido acompanha a preferência do Windows, sem copiar a opção exclusiva do Lab.
- **Comportamento:** teste bandeja ao fechar e sugestões ao digitar `/`; reabra e confira as preferências. Inicialização com o Windows e atualização estão desativadas somente no piloto.
- **Importar:** escolha SlashDesk (`snippets.md`), Text Blaze (JSON) ou Espanso (YAML). Revise avisos e conflitos; cancele para confirmar que nada muda. Quando confirmar, o estado anterior deve ficar em backup antes de salvar. Modo protegido bloqueia a importação; Restaurar ZIP permanece disponível para recuperação.
- **Backup:** adicione um emoji personalizado pela Captura, use **Criar agora**, confira status e abra a pasta. O ZIP inclui `capture-emotes/catalog.json` e PNGs normalizados, além dos dados/imagens existentes. Capturas externas são representadas pelo histórico; seus arquivos externos não são copiados para o ZIP.
- **Restaurar:** em uma cópia de teste, altere atalhos e emojis e restaure um backup válido. Confira identidade, nome e transparência dos emojis. Um ZIP inválido deve manter os dados atuais. As preferências completas são aplicadas ao reabrir; atalhos, tema, histórico e contagens são recarregados após a restauração.
- **Coleções e compatibilidade:** backups antigos sem a coleção mantêm os emojis atuais. Um backup novo com uma coleção vazia restaura esse estado vazio. Não há mistura silenciosa entre a coleção atual e a salva. Use esta versão para restaurar backups novos que contêm emojis; versões anteriores não reconhecem essa coleção.
- **Analisar imagens:** apenas analise primeiro. Cancele a confirmação e confira ausência de exclusão. Confirme somente em uma cópia de teste; o backup deve ser criado antes da limpeza, e somente os assets órfãos dos atalhos devem ser removidos. A coleção Meus emojis fica separada.
- **Operações:** durante importação, criação/verificação de backup, análise/limpeza e restauração, o estado fica visível e as ações incompatíveis são bloqueadas. As partes pesadas usam processamento em segundo plano; a janela não encerra uma transação em andamento. A expansão/captura e inserção de acentos são bloqueadas temporariamente durante manutenção; voltam depois, inclusive em falhas.
- **Dados locais:** confira o caminho real, dica completa e **Explorar dados**. Os botões de pasta abrem os diretórios reais, sem mensagens simuladas.
- **Ajuda:** use **?**, busque emojis/importação/restauração e confira **Mostrar na tela**. A ajuda não executa manutenção.
- **Layout:** confira Claro/Preto/Sistema, janela ampla/mínima, rolagem, Tab/Shift+Tab e foco. Os seis cartões passam para uma coluna na janela menor.

## Escopo e dados

XAML em `MainWindow.xaml`, apresentação/coordenação em `MainWindow.Settings.cs` e estilos explícitos em `Styles/VisualLab/Settings.xaml`. Os handlers existentes de tema, comportamento, importação, backup, limpeza, restauração e atualização são preservados. Dados locais e ajuda são ações novas conectadas às pastas e ao guia existentes.

`BackupService` acrescenta a coleção ao manifesto verificável v2 com o indicador opcional `includesCaptureEmotes`. `settings.json`, `snippets.md`, `usage.json`, histórico e `capture-emotes/catalog.json` mantêm seus formatos/caminhos. Backups v1/v2 anteriores continuam restauráveis. O catálogo e os PNGs são validados em staging antes de substituir a pasta; arquivos/assets/emojis fazem parte da mesma reversão. A cache do catálogo é invalidada após restauração para evitar imagens antigas com a mesma identidade.

O gate existente de captura/expansão é reutilizado durante manutenção. A gravação/atualização em andamento impede manutenção; a coleção de acentos fica temporariamente desativada. A trava de contagens aguarda gravações pendentes antes da operação em segundo plano. A manutenção dos assets continua sem tratar emojis como órfãos.

## Evidência e limites

`--settings-smoke` usa dados sintéticos isolados e controles/serviços reais. Verifica handlers de tema/preferência, persistência, bloqueio e desbloqueio em falha, fontes de importação, caminhos, ajuda, redimensionamento e rolagem; Claro/Preto/Sistema em 1440×900 e 980×680, renderização em 100/125/150/200%. Exercita backup/hash, PNG transparente/identidade, ZIP adulterado, rollback após substituição de assets, preservação da coleção em backups antigos e restauração de coleção vazia. O texto de erro nas imagens é um fixture de apresentação; as falhas de restauração são provocadas no serviço real.

Fixtures não instalam hooks, não escrevem no registro do Windows e não consultam a rede. O monitor geral indisponível nas imagens automáticas é esperado. Regressão existente cobre importação, cancelamento, modo protegido, backup legado/hash/rollback, órfãos e atualização manual/automática e Lembrar depois em dados de teste.

A busca manual e o monitor de atualização da distribuição oficial continuam usando o serviço anterior; o piloto isolado não oferece atualização para não substituir o executável de teste. Não há instalação real de uma release nesta etapa; **#37 permanece independente**. Teclado/hardware, DPI físico/misto e validação integrada permanecem na #70. Aprovação visual e funcional manual continua pendente na #67; nenhuma integração automática. Próxima issue após aprovação: #68 — Sobre.

## Revisão v2 — diálogos e bandeja

As confirmações e mensagens de importação, análise/limpeza, criação e restauração de backup usam `SettingsDialogWindow`, com o tema atual, fundo modal e ações em português. Na importação com conflitos, as ações são **Substituir conflitos**, **Manter atuais** e **Cancelar**. Enter tem Cancelar como padrão; Escape, fechar e clique fora cancelam. Mensagens longas têm rolagem, mantendo os botões acessíveis. Os seletores de arquivos continuam sendo os seletores do Windows.

A bandeja mantém `NotifyIcon` e os menus nativos, preservando posicionamento, teclado, submenus e fechamento pelo Windows. `TrayMenuTheme` aplica os mesmos recursos de cor, fonte e ícones Lucide do app, com espaçamento e cantos arredondados; o tema é atualizado ao abrir cada menu. Capturas, Gravações, Atalhos e Aplicativo conservam suas ações. O novo grupo **Acento Rápido** oferece **Ativar/Inativar Acento Rápido** e **Gerenciar Acento Rápido**, que abre diretamente essa tela. A alternância salva a preferência existente, sincroniza a tela/runtime e mantém o estado anterior se a gravação falhar. Manutenção bloqueia a alternância sem confundir suspensão temporária com a preferência do usuário.

O teste real inclui diálogos com dono e conteúdo do dono desativado, respostas OK/Yes/No/Cancel, Escape, mensagens longas, renderização Claro/Preto; menus nativos renderizados nos dois temas; persistência, sincronia com a tela, erro real de gravação, bloqueio durante manutenção e navegação para Acento Rápido. Não instala NotifyIcon nem hooks nos fixtures. As imagens de menu são dos componentes reais, mas não representam um clique físico no ícone da bandeja.

Na avaliação manual da v2, confira os três avisos dos prints, importação com/sem conflitos e cancelamento; menu principal e submenus em Claro/Preto/Seguir Windows; ativação/inativação pela bandeja, persistência ao reabrir, mudança feita pela tela e Gerenciar Acento Rápido com janela oculta. Confira Escape/clique fora e diferentes monitores/escala do Windows. DPI misto físico permanece na validação integrada #70.

## Revisão v3 — alinhamento da bandeja

O texto dos itens usa a altura completa da linha para centralização vertical, preservando o posicionamento horizontal nativo, ícones, espaçamento e ações. O fixture visual inclui Aplicativo e Configurações selecionados, além dos menus principal/Acento Rápido nos temas Claro/Preto. Confira também estes estados no menu real da bandeja, conforme os prints de avaliação da v2.
