# Piloto — retenção local (#108)

O piloto é portátil e usa `SlashDeskPilotData` ao lado do executável. Extraia em
uma pasta nova e encerre o piloto anterior pela bandeja antes de abrir. Não
substitui a instalação oficial, não migra seus dados nem ativa autostart ou
atualizações. Use conteúdo fictício nos testes.

1. Em **Configurações → Dados locais**, confira a política de logs/temporários.
   **Diagnóstico adicional** deve começar desativado. Ative, encerre pela bandeja
   e reabra: deve continuar ativado. Desative e repita: deve ficar desativado.
   Confira também o texto e a leitura no tema e idioma de sua preferência.
2. Abra uma imagem de teste e extraia texto com OCR. Faça um GIF curto, veja a
   prévia e salve ou cancele. Faça um MP4 curto e pare normalmente. Os fluxos
   devem funcionar como antes; vídeos completos não devem gerar aviso de
   gravação interrompida. Se possível, cancele uma extração de OCR em andamento.
3. O pacote privado inclui uma preparação opcional de **aviso fictício** para
   conferir a apresentação sem provocar uma falha real no gravador. Siga o
   `LEIA-ANTES.md` do ZIP. O arquivo desse teste não é um vídeo reproduzível.

Os testes automáticos já cobrem limites/rotação, filtros de privacidade,
encerramento abrupto de processos próprios, sessões ainda vivas, leases de
worker, PID reutilizado, recusa por permissão, junctions, conteúdo desconhecido e
preservação de MP4/capturas/backups/modelos. Eles não manipulam o clipboard
pessoal. O OCR real e a interface são verificados em três idiomas, dois temas,
janelas compactas/largas e escalas renderizadas de 100/150%.

Não é necessário matar uma gravação pessoal ou alterar permissões das suas
pastas para validar este piloto. O teste de driver anormal da #106 continua
separado. Não há tag ou release de produção nesta entrega.

Detalhes da política: [local-data-retention.md](local-data-retention.md).
