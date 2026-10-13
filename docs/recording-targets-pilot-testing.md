# Destinos MP4 — piloto #114

Foi corrigida uma falha real: os blocos Monitor/Região/Janela alteravam apenas o
modo visual, enquanto Novo iniciava o vídeo usando um seletor legado que podia
continuar em Monitor. Piloto e versão final compartilhavam esse caminho.

Agora os blocos, o seletor de destino e o início do vídeo usam a mesma seleção.
Mudar o destino preserva FPS, qualidade e cursor. Captura longa fica desabilitada
em Vídeo; permanece disponível em Imagem. GIF continua com seleção de região.
Janela abre uma seleção por clique com contorno, evitando escolher acidentalmente
o desktop abaixo da antiga posição do botão Novo. Escape cancela. A região é
convertida de coordenadas WPF para pixels; uma seleção fora de um único monitor
é recusada. Uma janela inválida não vira gravação do monitor.

Extraia o piloto em pasta nova e encerre o anterior pela bandeja. Dados isolados
em SlashDeskPilotData; sem substituir a instalação oficial ou publicar release.
Use janelas e conteúdo fictícios nos vídeos.

1. **Vídeo → Monitor → Novo:** iniciar, gravar poucos segundos e parar. O MP4
   deve conter o monitor inteiro.
2. **Vídeo → Região → Novo:** arrastar uma área pequena dentro de um monitor.
   Iniciar/parar. O MP4 deve mostrar somente esse trecho, sem o restante da tela.
3. **Vídeo → Janela → Novo:** clicar na janela de teste quando a seleção surgir.
   Iniciar/parar. O MP4 deve conter somente essa janela. Coloque outra janela
   colorida ao lado para perceber se conteúdo externo apareceu.
4. Repetir a escolha usando o campo **Destino**: o bloco correspondente deve
   acompanhar a mudança. FPS/qualidade/cursor escolhidos devem permanecer.
5. Em Região e Janela, cancelar com **Esc**: nenhum vídeo deve começar. Testar
   pausa/retomada e a opção de áudio de sua preferência com dados fictícios.

Os testes automáticos exercitam cliques reais da interface, sincronização nos
dois sentidos, configuração da fonte/recorte/tamanho, preservação de preferências,
seleção vazia/inválida e recusa de destino incorreto, nos três idiomas/dois temas.
Não gravam a área de trabalho do usuário. A conferência dos pixels dos MP4
produzidos no seu Windows permanece parte deste reteste; escalas físicas de
monitor e drivers não são substituídos pelos fixtures. A #106 continua separada.
