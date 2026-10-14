# Preferências da Captura — piloto #127

Os testes automatizados exercitam os botões reais de imagem, MP4, GIF e regras de captura com falhas de escrita e retomada. Não há teste manual obrigatório para esta correção. O roteiro abaixo é opcional.

## Preparação

Extraia o ZIP inteiro em uma pasta nova. Execute `SlashDesk.exe` dessa pasta. O piloto usa `SlashDeskPilotData` ao lado do executável e não acessa o perfil da instalação habitual. Inicialização com o Windows e atualizações estão desativadas no piloto. O número interno da versão permanece anterior ao lançamento 3.3.0.

## 1. Falha ao salvar

1. Abra Captura e suas regras. Mude o modelo de nome para `teste127-{date}-{time}` e salve. Feche o SlashDesk pela bandeja para encerrar completamente.
2. Na pasta deste piloto, abra as propriedades de `SlashDeskPilotData/settings.json` e marque **Somente leitura**. Não altere nem substitua o arquivo.
3. Reabra o piloto. Em Captura, mude o modelo de nome para `nao-salvar127-{date}-{time}` e confirme. Deve aparecer o aviso de que as preferências não puderam ser salvas. O app permanece aberto e os controles voltam ao ajuste anterior.
4. Com o arquivo ainda em somente leitura, tente **Novo** nos modos Imagem, Vídeo e GIF. Deve aparecer o mesmo aviso, antes da seleção de uma área/janela ou da abertura da barra de gravação. Nenhuma captura deve começar.

## 2. Retomada

1. Desmarque **Somente leitura** nas propriedades do mesmo arquivo fictício do piloto.
2. Sem fechar o app, ajuste novamente o modelo para `retomada127-{date}-{time}` e confirme. Agora deve aparecer a confirmação de salvamento.
3. Encerre pela bandeja e reabra. O modelo `retomada127-{date}-{time}` deve continuar selecionado, sem aviso de erro ou de dados protegidos.

O roteiro usa somente o arquivo de preferências do piloto. Não é necessário corromper arquivos, gravar áudio ou acessar o clipboard para validar a correção.

## Limites desta entrega

Esta correção trata o salvamento e mantém os leitores protegidos existentes. A reserva da gravação antes da primeira espera e o bloqueio de comandos concorrentes são acompanhados na issue #128. Os testes automatizados desta entrega substituem apenas a ativação de atalhos globais e o início da seleção/gravação por marcadores, sem capturar conteúdo pessoal.
