# Preparação da gravação — piloto #128

Este piloto usa uma pasta fictícia `SlashDeskPilotData`, separada da instalação habitual. Os áudios do computador e microfone começam desativados, assim como a cópia para o clipboard. Atualizações e inicialização com o Windows estão desativadas no piloto. O número interno da versão ainda é anterior ao lançamento 3.3.0.

## Preparação

Extraia o ZIP inteiro em uma pasta nova. Execute `Abrir-Piloto.cmd`. A pasta de saída fictícia é configurada pelo inicializador, sempre dentro deste piloto. Abra `TESTES/area-ficticia.txt` no Bloco de Notas para usar como referência ao selecionar uma área. Não é necessário gravar nem ativar áudio nestes testes.

## Teste 1 — MP4: preparação, bloqueio e retomada

1. Em Captura, escolha **Vídeo → Região → Novo**. Selecione uma área pequena do texto fictício. Deve aparecer uma única barra MP4 aguardando **Iniciar**. **Não clique em Iniciar**.
2. Abra o SlashDesk pelo ícone da bandeja. **Novo** e os controles de modo/gravação devem estar indisponíveis. Na bandeja, **Gravações → Vídeo MP4/GIF animado** e **Capturas** devem estar indisponíveis; **Cancelar preparação** deve estar disponível.
3. Em Configurações, tente **Criar agora** na área de backups. O app deve pedir para finalizar a gravação antes da manutenção. A quantidade de backups não deve aumentar. Feche o aviso.
4. Use **Gravações → Cancelar preparação**. A barra deve fechar; Novo deve voltar a funcionar. Repita Novo, selecione a área e cancele pelo **X da barra**, sem clicar em Iniciar. Deve haver somente uma barra por tentativa e nenhum MP4 salvo.

## Teste 2 — GIF: cancelar seleção e repetir

1. Escolha **GIF → Novo**. Quando aparecer a seleção, pressione **Esc**, sem selecionar uma área. O GIF começa a gravar depois de escolher a área, por isso este teste cancela antes.
2. Repita pela bandeja, em **Gravações → GIF animado**, e pressione Esc novamente. Cada tentativa deve abrir somente um seletor e permitir outra tentativa após cancelar. Nenhum GIF deve ser salvo.
3. Com todas as preparações fechadas, **Criar agora** deve funcionar novamente e aumentar a quantidade de backups. Os testes automatizados cobrem também a reserva durante a gravação e a finalização, sem capturar conteúdo pessoal.

## Teste 3 — encerrar durante a preparação

1. Repita **Vídeo → Região → Novo**, selecione a área fictícia e deixe a barra aguardando Iniciar.
2. Use **Aplicativo → Sair** na bandeja. A barra e o SlashDesk devem fechar sem começar uma gravação. O app não deve reaparecer sozinho.
3. Reabra pelo mesmo inicializador. Não deve existir barra de gravação, mensagem de erro nem MP4/GIF criado pelos testes. Faça outra preparação e cancele; Novo deve continuar disponível após o cancelamento.

Informe **“Testes 1, 2 e 3 aprovados”** ou o número e a etapa que falharam. Não é necessário alterar permissões de arquivos, corromper dados, ativar microfone ou testar parada nativa nesta entrega. A confirmação de parada em hardware permanece na #106; cancelamento da API limitada de GIF permanece na #129.
