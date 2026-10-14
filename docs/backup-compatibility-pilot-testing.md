# Piloto #117 — compatibilidade dos backups

A #118 já foi aprovada, integrada e encerrada. Este piloto mantém sua recuperação de restauração e acrescenta validação dos dados antes de substituir os arquivos ativos. Dados separados em SlashDeskPilotData, sem importar a instalação oficial, iniciar com Windows ou atualizar. Versão interna 3.2.0, preparação da 3.3.0.

## Preparar

1. Salve seu trabalho e saia normalmente do piloto anterior pela bandeja → Aplicativo → Sair.
2. Extraia este ZIP em pasta NOVA e abra Abrir-Piloto.cmd. Não copie dados oficiais para ela.
3. Em Atalhos, crie /teste117 com nome Atual 117 e texto ANTES 117. Aguarde Tudo salvo.
4. Em Captura, clique Abrir imagem e escolha TESTES/azul117.png. Deve aparecer uma imagem azul. Não é necessário capturar sua tela.
5. Volte para Configurações. Os ZIP de TESTES são fictícios, próprios para este piloto. Não os restaure na instalação oficial.

## 1. Três recusas

Em Configurações → Backup e restauração → Restaurar ZIP, selecione TESTES/117-atalhos-incompativeis.zip e confirme Restaurar backup.

Esperado: Não foi possível restaurar, com indicação de snippets.md incompatível e dados atuais não alterados. Clique Entendi. /teste117 deve continuar ANTES 117; a imagem em Captura continua azul, e o app permanece aberto/utilizável.

Repita com TESTES/117-preferencias-incompativeis.zip (mensagem settings.json) e TESTES/117-historico-incompativel.zip (mensagem capture-history.json). Nos três casos, o resultado deve preservar o mesmo atalho, imagem e preferências atuais. Estes ZIP satisfazem a integridade do arquivo; seus dados são propositalmente incompatíveis com os leitores.

## 2. Cancelar e restaurar backup válido

1. Selecione TESTES/117-backup-valido.zip, mas clique Cancelar na confirmação. /teste117 continua ANTES 117.
2. Selecione o mesmo ZIP e confirme Restaurar backup. Esperado: Restauração concluída; o atalho /restaurado117 aparece com BACKUP VÁLIDO #117. Ele substitui o conjunto fictício anterior, portanto /teste117 deixa de estar na lista.
3. O tema passa a escuro. Em Estatísticas, o atalho restaurado tem 4 usos e há 2 acentos inseridos. A aplicação deve continuar utilizável. Há um registro de captura fictício com arquivo indisponível: ele não deve abrir automaticamente; isso mantém a política de aprovação do histórico da #104.
4. Saia pela bandeja e reabra. /restaurado117 e seus dados continuam disponíveis, sem aviso de recuperação pendente. O registro fictício sem data atual pode ser removido pela retenção normal; não é um arquivo de imagem perdido.

## 3. Backup legado

Em Configurações → Restaurar ZIP, selecione TESTES/117-legado-valido.zip e confirme. Esperado: /legado117 aparece. O tema escuro permanece, pois este backup antigo inclui apenas atalhos e contagens, deixando preferências omitidas intactas. Saia e reabra: /legado117 continua na lista.

## Resultado

Responda Testes 1, 2 e 3 aprovados. Se falhar, informe o teste, passo e mensagem/print, sem enviar seus backups ou textos pessoais.

Os testes automatizados comparam bytes, verificam recusa antes de cada aplicação e recarregam os leitores reais; incluem schemas 1/2, dados legados e a recuperação da #118. Este roteiro confirma os controles e mensagens no seu Windows. Falhas físicas de armazenamento e o candidato completo ficam na #70.
