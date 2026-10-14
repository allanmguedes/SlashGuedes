# Piloto #126 — falha auxiliar de estatísticas

Validação manual opcional: os testes automatizados já cobrem escrita negada, recuperação sem duplicação e os consumidores reais de UI. O piloto usa somente `SlashDeskPilotData` da pasta extraída e desativa inicialização com Windows/atualizações. Não copiar dados reais para esta pasta.

1. Extraia o pacote em **uma pasta nova**. Saia de qualquer SlashDesk pela bandeja antes de abrir `SlashDesk.exe`. Abra Estatísticas: fixture começa com **2 expansões e 1 acento**. Deixe o app aberto.
2. Execute `Bloquear-Contagens.cmd`. No Bloco de Notas, digite `/teste126` e confirme com Tab **uma vez**: deve aparecer `INSERÇÃO FICTÍCIA 126`, sem popup de falha de inserção e com app aberto. Para o acento, segure `a`, toque em Espaço após o atraso e solte `a`: deve inserir um acento normalmente. Em Estatísticas, o aviso informa que as contagens da sessão ainda não foram salvas. Execute `Verificar-Pendentes.cmd`: resultado **APROVADO**, contagens gravadas anteriores preservadas.
3. Execute `Liberar-Contagens.cmd`, sem fechar o app. Use `/teste126` **mais uma vez**. O aviso deve desaparecer, com **4 expansões e 2 acentos** se fez exatamente as ações anteriores. Execute `Verificar-Salvas.cmd`: **APROVADO**. Saia pela bandeja, reabra e confira os mesmos totais.

As pendências são mantidas em memória nesta sessão. Se fechar antes de liberar a escrita e usar um atalho/acento novamente, elas poderão ser perdidas; na reabertura estarão apenas os totais anteriormente gravados. Repetir uma inserção produz um novo uso; a retomada da gravação das estatísticas não repete a inserção nem seus incrementos anteriores. Restaurar um backup substitui as contagens pelo conjunto escolhido, como antes.

Os comandos verificam a marca da fixture e manipulam somente o arquivo fictício `SlashDeskPilotData/usage.json`. Não altere permissões/atributos em dados reais e não tente provocar falha de disco ou driver. O texto e o acento precisam ser usados em um aplicativo externo; a prévia de teste do Acento Rápido não incrementa estatísticas globais.
