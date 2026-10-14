# Piloto #118 — recuperação da restauração

Use o pacote desta entrega em uma pasta nova. Ele mantém dados separados em `SlashDeskPilotData`; não substitui a instalação oficial. Inicialização com Windows e atualização ficam desativadas. A versão interna ainda é 3.2.0, na preparação da 3.3.0.

## Preparação

1. Salve o trabalho aberto e saia do piloto anterior pela bandeja → Aplicativo → Sair.
2. Extraia o ZIP novo. Não copie seus dados oficiais para ele.
3. Os dois primeiros testes são demonstrações independentes, com um aviso visível de dados fictícios. Execute-os sem abrir o piloto principal simultaneamente. Eles não carregam seus atalhos nem iniciam monitoramento/captura.

## Teste 1 — recuperação concluída

Abra `TESTE-1-RECUPERAR.cmd` e aguarde alguns segundos. Ele interrompe somente um subprocesso de teste próprio e usa o caminho real de recuperação em dados fictícios.

Esperado: aparece **Dados anteriores restabelecidos**. Pressione Enter ou clique **Entendi**. A demonstração fecha e `TESTES/resultado-recover.txt` começa com `PASS`. Não deve aparecer um erro. Não precisa comparar hashes manualmente.

## Teste 2 — recuperação pendente

Abra `TESTE-2-PENDENTE.cmd`. Ele mantém um arquivo de recuperação fictício temporariamente ocupado para demonstrar a recusa segura.

Esperado: aparece **Recuperação pendente**, com **Fechar SlashDesk** como opção padrão. Pressione Enter; a demonstração fecha. `TESTES/resultado-blocked.txt` começa com `PASS`. O arquivo ocupado é liberado e os dados fictícios são recuperados ao finalizar a demonstração. Se desejar, repita e clique **Abrir pasta de backups**: somente a pasta fictícia deve abrir.

Não encerre seu SlashDesk de uso real à força e não edite/apague arquivos de recuperação. Essa parte do teste já é automatizada com subprocessos isolados.

## Teste 3 — restauração normal e cancelamento no app

1. Abra `Abrir-Piloto.cmd`. Em Atalhos, crie `/teste118` com texto `ANTES 118` e salve.
2. Em Configurações → Backup e restauração, clique **Criar agora**. Clique **Abrir pasta** e anote o ZIP manual recém-criado.
3. Mude o mesmo atalho para `DEPOIS 118` e salve.
4. Selecione o ZIP anotado em **Restaurar ZIP** e escolha **Cancelar** na confirmação. Esperado: o atalho continua `DEPOIS 118`.
5. Repita a seleção do mesmo ZIP e confirme a restauração. Esperado: mensagem de conclusão; o atalho volta a `ANTES 118` e o app permanece utilizável.
6. Saia pela bandeja e reabra. Esperado: `ANTES 118` permanece, sem aviso de recuperação pendente ou de interrupção.

Salve edições de imagem antes de restaurar, conforme a confirmação. O app pode precisar fechar quando a recuperação não for confirmada; não faça esse teste em dados oficiais.

## Como informar o resultado

Responda **“Testes 1, 2 e 3 aprovados”**. Se houver falha, informe o teste/passo, a mensagem e um print da demonstração/piloto. Nos testes 1/2, envie também o conteúdo de `resultado-recover.txt` ou `resultado-blocked.txt`, se existir. Não envie seus backups ou textos pessoais.

Esta validação cobre os avisos e o fluxo normal/cancelado. Os pontos de interrupção, hashes e falhas de filesystem são verificados automaticamente antes da entrega; testes físicos de energia/armazenamento e o candidato completo continuam na #70.
