# Piloto #119 — preservar versões antigas de atalhos

O objetivo é garantir que criar backups novos não apague versões antigas que ainda podem ser recuperadas. O pacote contém apenas atalhos fictícios, três arquivos `.md` antigos e ZIPs de teste. O app oficial permanece separado.

## Preparação

1. Encerre o piloto anterior pela bandeja: **Aplicativo → Sair**.
2. Extraia todo este ZIP em uma pasta nova. Não misture com os pilotos anteriores nem execute de dentro do ZIP.
3. Abra `Abrir-Piloto.cmd`. O aplicativo deve abrir normalmente, sem aviso de modo reduzido. Em Atalhos deve existir `/atual119`, com **VERSÃO ATUAL 119**.

## Teste 1 — criar backups sem perder os antigos

1. Em **Configurações → Backup e restauração**, confira o aviso sobre preservação dos arquivos antigos `.md`.
2. Clique **Criar agora** duas vezes. Execute `Verificar-Preservacao.cmd`: deve mostrar **APROVADO**, indicando que os três arquivos antigos e o arquivo não gerenciado continuam intactos, com sete ZIPs gerados pelo app.
3. Encerre pela bandeja e reabra o piloto. Execute o verificador novamente: deve continuar **APROVADO** e o app deve abrir normalmente.

## Teste 2 — recuperar uma versão antiga

1. Em **Configurações → Importar atalhos**, selecione a origem **SlashDesk** e escolha `SlashDeskPilotData/Backups/snippets-20200101-000000.md` na pasta do pacote.
2. Confirme **Importar atalhos**. Em Atalhos deve aparecer `/legado119`, com **TEXTO ANTIGO 119 · VERSÃO 1**; `/atual119` continua disponível.
3. Encerre pela bandeja e reabra. Os dois atalhos devem continuar disponíveis, sem aviso de erro.
4. Execute `Verificar-Recuperacao.cmd`: deve mostrar **APROVADO**, incluindo a recuperação e a preservação dos arquivos antigos.

Não selecione `pessoal-backup-ficticio.zip` em Restaurar ZIP: esse arquivo é apenas um marcador fictício para comprovar que arquivos não gerenciados ficam fora da limpeza.

Retorne com **Testes 1 e 2 aprovados**, ou informe o número da etapa e a mensagem que falhou. Nenhuma interrupção forçada ou edição manual de arquivos é necessária.

## Política adotada

A limpeza automática mantém os sete ZIPs mais recentes com nomes gerados pelo app. Os antigos `snippets-*.md` permanecem até remoção manual, fora desse limite. O gravador atual não produz mais esses arquivos antigos; preservar o conjunto existente evita perder versões únicas. A recuperação usa a importação já disponível, com revisão dos conflitos. Arquivos não reconhecidos permanecem fora da limpeza.
