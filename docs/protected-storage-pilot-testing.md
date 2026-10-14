# Piloto #116 — dados protegidos

Este pacote usa apenas dados fictícios nas pastas `TESTES`. Não importe esses arquivos no aplicativo oficial. Nenhuma atualização ou inicialização com o Windows será ativada.

## Objetivo dos testes

Os cenários simulam uma falha nos arquivos de preferências ou do histórico, usando dados fictícios. Antes da recuperação, o app deve proteger o original. Depois de confirmar a recuperação, deve abrir normalmente e conservar o que foi recuperado. No terceiro cenário, uma pasta vazia impede propositalmente criar a cópia; o comando de desbloqueio remove esse obstáculo fictício, e **Tentar ler novamente** verifica a cópia antes de habilitar a recuperação.

O aviso **SlashDesk iniciado em modo reduzido — Atalhos** não faz parte do resultado esperado. A revisão 2 corrige o arquivo de atalhos fictícios do pacote anterior.

Se você já concluiu a recuperação nos três cenários do pacote anterior, use a correção pequena entregue junto dele: encerre o app, extraia os arquivos da correção na mesma pasta que contém `SlashDesk.exe` e `TESTES`, execute `Corrigir-Atalhos-Ficticios.cmd` e reabra os três cenários. Não é necessário repetir as recuperações já concluídas. Essa correção só substitui o arquivo fictício inválido conhecido, guardando uma cópia; não modifica preferências nem histórico.

## Preparação

1. Salve seu trabalho e encerre o SlashDesk anterior usando **Aplicativo → Sair** na bandeja.
2. Extraia todo o ZIP em uma pasta nova, com permissão de escrita. Não execute de dentro do ZIP.
3. Execute um teste de cada vez. Antes do próximo, encerre o piloto usando **Aplicativo → Sair**. A pasta de dados de cada cenário é independente.

## Teste 1 — histórico legível

1. Abra `1-Historico.cmd`. Em Configurações, deve aparecer **O original está protegido**, com a opção **Recuperar histórico legível** disponível.
2. Clique **Tentar ler novamente**. A proteção deve continuar. Abra Captura: existe uma entrada legível; a limpeza e a remoção devem direcionar para a revisão dos dados. Atalhos deve continuar disponível, com `/fixture116`.
3. Execute `Verificar-1-Antes.cmd`: o resultado deve ser **APROVADO**, confirmando que o original e a imagem fictícia continuam intactos.
4. Clique **Recuperar histórico legível** e primeiro **Cancelar**: a proteção deve continuar. Repita e confirme **Recuperar**.
5. O aviso deve desaparecer e a entrada legível deve permanecer. Execute `Verificar-1-Depois.cmd`: deve aparecer **APROVADO**, incluindo a cópia do original.
6. Encerre o piloto e reabra `1-Historico.cmd`: o aviso não deve voltar e a entrada deve continuar disponível.

## Teste 2 — preferências

1. Abra `2-Preferencias.cmd`. O aviso deve explicar que as preferências não puderam ser lidas. Aparência, Comportamento e preferências de Acento Rápido ficam indisponíveis; Atalhos e Captura continuam acessíveis.
2. Execute `Verificar-2-Antes.cmd`: resultado **APROVADO**. Clique **Redefinir preferências** e **Cancelar**; o aviso deve continuar.
3. Repita, confirme **Redefinir** e execute `Verificar-2-Depois.cmd`: resultado **APROVADO**. O aviso deve desaparecer e as preferências voltar a aceitar alterações.
4. Encerre e reabra `2-Preferencias.cmd`: o app deve abrir normalmente, sem o aviso.

## Teste 3 — cópia indisponível

1. Abra `3-Copia-Bloqueada.cmd`. **Recuperar histórico legível** deve estar desabilitado e o aviso deve explicar que a cópia não foi verificada.
2. Clique **Tentar ler novamente**: a recuperação continua bloqueada. Execute `Verificar-3-Antes.cmd`: resultado **APROVADO**. Os outros módulos continuam disponíveis.
3. Execute `Desbloquear-Teste-3.cmd`, que move somente a pasta vazia fictícia preparada para esse teste. Volte ao app e clique **Tentar ler novamente**.
4. A recuperação agora deve estar habilitada, mas o original ainda fica protegido. Confirme **Recuperar histórico legível** e execute `Verificar-3-Depois.cmd`: resultado **APROVADO**.

Retorne com **Testes 1, 2 e 3 aprovados** ou com o número da etapa que falhou e a mensagem exibida. Mantenha as pastas do pacote para conferir os resultados. A integração na main acontece após o aceite.
