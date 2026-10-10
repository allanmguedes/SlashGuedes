# Piloto Interfaces auxiliares — #69

Sobre foi concluída na PR #80, integrada em `3f23ad47f10dfe771b8db122d34b8619b0963b57`. Este piloto reúne os ajustes às janelas e barras que ficam fora das seis telas principais. A base funcional continua em 3.2.0; 3.3.0 identifica o piloto, sem lançamento oficial.

## Abrir e revisar

1. Extraia o ZIP em uma pasta nova e feche outro piloto. `Abrir-Piloto-Interfaces.cmd` abre o app em Configurações. Os dados ficam em `SlashDeskPilotData`, ao lado do EXE. Para manter seus testes anteriores, copie somente dados de outro piloto, com ambos fechados.
2. `Conferir-Janelas.cmd` abre uma prévia separada com dados explicitamente fictícios. As janelas são as mesmas do app, mas a prévia não aciona serviços de atualização, gravação ou manutenção. Serve para revisar campos, nome de grupo, boas-vindas, atualização, progresso, GIF, confirmação, sugestões e acentos. Feche a prévia para continuar nos fluxos reais.
3. Em Configurações, altere Claro/Preto/Seguir o Windows. Confira os diálogos de importação, análise de imagens, backup e restauração. Use apenas arquivos de teste. Revise erros, mensagens longas e cancelamento; confirme operações apenas quando desejar executá-las nos dados do piloto.
4. Em Atalhos, crie um texto com campos e expanda num editor de texto: primeiro campo com foco, valores iniciais, Tab/Shift+Tab, rolagem em muitos campos, Inserir e Esc. Esc não insere o texto; ao fechar, confira o retorno ao destino original. Teste também edição do nome de grupo e sugestões ao digitar `/`.
5. Ative Acento Rápido, digite em outro app e confira seleção e informações Unicode. A barra e as sugestões devem preservar o foco da digitação. Teste as posições da barra no monitor em uso.
6. Em Captura, teste erros de regras/atalhos, descarte de edições, histórico, salvar/copiar e feedback. Faça um GIF, pause/continue/finalize, revise a prévia e teste Descartar/Esc antes de salvar outro GIF. Faça MP4 com as fontes de áudio desejadas; confira preparação, pausa, finalização e painel de dispositivos. Os serviços de gravação e privacidade existentes foram preservados.
7. Clique numa notificação de captura salva: deve abrir esse arquivo. Depois teste uma notificação sem arquivo, por exemplo uma captura apenas copiada: não deve abrir o arquivo anterior. Arquivo apagado também não deve ser aberto.
8. Minimize/feche para bandeja. Confira Capturas, Gravações, Atalhos, Acento Rápido e Aplicativo, incluindo submenus, ativação e gerenciamento. A centralização das linhas e opções já aprovadas na #67 foi preservada.
9. Reduza as janelas, revise textos longos e percorra o conteúdo. Teste Esc, Enter e navegação por teclado. Nas confirmações de exclusão/manutenção, fechar ou cancelar nunca aceita a operação. No progresso de atualização, cancelar solicita encerramento do download; a aplicação do pacote não pode ser interrompida.
10. Confira movimento reduzido, tema após reiniciar e dois monitores com escalas diferentes, incluindo monitor à esquerda do principal. Essas verificações físicas e leitor de tela fazem parte da revisão manual e da #70.

Inicialização automática e verificação/instalação de atualizações continuam bloqueadas no piloto; as preferências são preservadas. Não substitua a instalação oficial. O Windows controla seletores de arquivo, notificações nativas e barras de título: eles não recebem um tema próprio do SlashDesk.

## Evidências e limites

`--auxiliary-smoke` usa controles WPF reais, dados e callbacks isolados: campos/foco/rolagem, cancelamento, decisões de atualização, proteção da fase de aplicação, boas-vindas, decisões do GIF, pausa/continuação/finalização, barras sem ativação, seleção visível, tema em janelas abertas e destino da notificação. Não faz download/instalação, gravação real nem abertura de arquivo no shell. Os snapshots mostram a área cliente, sem a barra de título nativa.

Três temas e renders a 100/125/150/200% não comprovam DPI físico entre monitores, entrega nativa das notificações, áudio de hardware, atualização ponta a ponta ou leitor de tela. Esses pontos continuam no roteiro manual, na #70 e na #37. O contrato congelado, inventário de controles e handlers, testes funcionais e testes de regressão das telas anteriores também são executados.

A #69 permanece aberta para sua avaliação. Depois de aprovação e integração explícita, criaremos a issue de inglês e espanhol; ao concluir idiomas, reavaliaremos validação integrada e lançamento da v3.3.0.
