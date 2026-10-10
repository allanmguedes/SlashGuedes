# Inventário e revisão das interfaces auxiliares — #69

Base: main `3f23ad47f10dfe771b8db122d34b8619b0963b57`, após #68/PR #80. Referência visual: publicação 11 congelada; os estilos explícitos `Lab.*`, paleta e ícones existentes são reutilizados. Nenhuma tela principal, formato de dados, backend ou geometria do overlay foi reescrita.

| Superfície / arquivos localizados | Revisão nesta entrega | Evidência / fluxo |
| --- | --- | --- |
| `MainWindow.xaml.cs`, `MainWindow.Auxiliary.cs`, `SettingsDialogWindow`, `ModalBackdrop` | Alertas restantes do app passam pelo diálogo já aprovado, com cancelamento seguro e proprietário visível quando disponível. Fallback fatal de inicialização continua nativo. | Aux smoke verifica descarte; Settings smoke cobre importação, órfãs, backup/restauração e falhas. Fluxos reais manuais no piloto. |
| `VariableInputWindow` | Campos rolam dentro da área disponível; heading e ações fixos; nomes acessíveis; estilos e cores dinâmicos. | 12 campos, valores iniciais/aceitos, foco inicial e Esc. Retorno ao app de destino e teclado global: manual. |
| `PromptDialog` | Campo e botões no tema; foco/seleção e retorno existentes; Esc cancela. | Cancelamento pelo botão real; inicialização de foco. |
| `UpdateAvailableWindow`, `UpdateProgressWindow` | Tema dinâmico, texto com quebra, altura suficiente para status/cancelar. Cancelamento solicitado uma vez; fase de aplicação protegida. | Todas as decisões; fechar/Cancelar/Esc; progresso longo. Sem executar updater nas fixtures. Atualização real: #37/#70. |
| `OnboardingWindow` | Cartões temáticos, conteúdo rolável na janela mínima, instrução correta de Acento Rápido e descrição sem promessa global de offline. | Janela mínima, rolagem, conclusão. Marcador de onboarding do app preservado. |
| `GifPreviewWindow` | Superfícies, textos, campos e ações no tema, mantendo timer e decisões existentes. | Quadros em memória, Descartar/Esc/Salvar. Nenhum serviço de salvamento chamado na fixture. |
| `RecordingControlWindow`, `RecordingAudioPanel` | Barra e popup de áudio com cores dinâmicas; posição inicial no monitor do app em primeiro plano. | Pause/resume/stop-once com controller isolado; RecordingAudio smoke existente. Dispositivos reais e gravação: manual/#70. |
| `SuggestionWindow`, `QuickAccentWindow`, `FloatingWindowPlacement` | Seleção e cores dinâmicas; lista de sugestões com scroll/ellipsis; acentos com wrap; HWND sem ativação; medidas físicas no monitor de destino. | Foreground preservado, flags nativas, bounds do monitor disponível, seleção rolada; clamp de origens negativas/tamanhos grandes. Monitores físicos mistos: #70. |
| `TrayMenuTheme`, `MainWindow.Tray.cs` | Visual, centralização e grupos aprovados na #67 preservados, incluindo Acento Rápido. | Settings smoke existente e roteiro minimizado/bandeja. |
| `CaptureRuleDialog`, `CaptureShortcutDialog`, `CaptureEditorContextPanel` | Erros de validação usam o diálogo temático; operações e regras mantidas. | Inventário de handlers/contrato Captura e regressões existentes; mensagens/recorte manuais. |
| `RegionSelectionWindow` | Somente instrução com recursos temáticos dinâmicos; seleção, geometria, drag e Esc preservados. | Contrato estático; seleção real/DPI físico manual. |
| `CaptureHistoryWindow`, `CaptureEditorWindow`, `CaptureWorkbenchEditor` | Superfícies já temáticas das entregas de Captura preservadas. | Testes existentes e roteiro de salvar/copiar/histórico; não se afirma nova aprovação visual. |
| `RegionCaptureWindow` e parciais `Objects`, `Ocr`, `ToolPanels`; `CaptureOcrWindow`, `CaptureOcrPulse`, `CaptureInkPicker`, `CaptureEmojiPicker` | Barra em blocos, OCR por ícone, privacidade, edições e emojis Google já aprovados; sem revisão de geometria. | Contrato Captura/toolbar, testes funcionais; hardware/manual #70. |
| `ShortcutCodeBlockWindow`, `ShortcutCodeBlockView`, `ShortcutCodeEditor`, `ShortcutConfirmationWindow`, `ShortcutRecorderBox` | Controles e confirmações já temáticos da #64 preservados. | Shortcuts workspace smoke existente. |
| `ScreenHelpWindow`, `ScreenHelpHighlighter`; `CaptureHelpContent`, `ShortcutsHelpContent`, `QuickAccentHelpContent`, `StatisticsHelpContent`, `SettingsHelpContent` | Ajuda, busca e destaque já temáticos preservados. | Smokes das respectivas telas e roteiro manual. |
| `CaptureToolbarPreviewWindow`, `DesignGalleryWindow`, nova `AuxiliaryReviewWindow` | Entrypoints explícitos de revisão, separados do startup normal. | Dados fictícios identificados; revisão não chama updater/gravador/manutenção. |
| Notificações (`ShowTrayBalloon`, `OpenPendingCaptureNotification`) | Lógica existente preservada; callback de evidência valida o destino sem chamar shell. | Consumo único, substituição por mensagem sem arquivo e arquivo apagado. Entrega do Windows: manual. |
| `OpenFileDialog`, `SaveFileDialog`, seletores de pasta, notificações de `NotifyIcon`, titlebars, alertas fatais de `App.xaml.cs` | Superfícies do Windows / recuperação anterior ao carregamento dos recursos. Sem imitação visual própria. | Tema/captions dependem do sistema; exceção documentada. |

## Compatibilidade e alcance

`AuxiliaryWindowTheme` aplica estilos explicitamente às janelas legadas construídas por código; não instala estilos implícitos globais. `DynamicResource` permite trocar o tema sem recriar janelas. Os overlays de captura mantêm as decisões anteriores. A restauração do foco nativo na expansão continua no fluxo existente de `KeyboardHookService`/`TextExpansionService`, sem alterar a ordem de inserção.

O piloto acrescenta somente um marcador de assembly; mantém `SlashDeskPilotData` e bloqueios de updater/registro existentes. Galeria e smoke são acionados por argumentos explícitos, antes do mutex/hook. A prévia usa janelas reais com modelos de teste; decisões nela não chegam a serviços de manutenção.

Evidências visuais em `evidence/auxiliary-*.png`: área cliente real em Claro/Preto. Todos os renders de três temas e quatro escalas ficam no artefato de CI. Renders não substituem a matriz física. A revisão visual no executável e a integração permanecem pendentes de aceite; idiomas é a próxima issue autorizada, após concluir #69.
