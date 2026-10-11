# Piloto Idiomas — #82

Português (Brasil) continua sendo o padrão. O piloto oferece English e Español
nas seis telas, bandeja, barras, diálogos, mensagens e ajuda do SlashDesk.
3.3.0 identifica o piloto; a versão base continua 3.2.0 até a preparação do lançamento.

## Abrir e escolher o idioma

1. Extraia o ZIP numa pasta nova e feche outro piloto. Abra
   `Abrir-Piloto-Idiomas.cmd`. O app inicia em Configurações.
2. Em Aparência, use **Idioma da interface** e escolha English ou Español.
   A preferência é salva e a mensagem informa que é necessário reabrir o app.
   Ao escolher um idioma diferente do atual, o aviso pulsa em vermelho três vezes
   durante 3,6 segundos e permanece vermelho e em destaque até reabrir.
   Com movimentos reduzidos no Windows, o aviso fica estático. Se voltar ao
   idioma atual antes de sair, o destaque é retirado.
3. Saia pelo menu **Aplicativo → Sair** da bandeja e abra novamente.
   Fechar a janela pode apenas mantê-la na bandeja. Salve ou finalize seu trabalho antes de sair.
4. Percorra Atalhos, Captura, Acento Rápido, Estatísticas, Configurações e Sobre.
   Revise também as ajudas, confirmações, importação, backup, bandeja e barras.
5. Teste Claro, Preto e Seguir o Windows, janelas menores, textos longos,
   navegação pelo teclado, Esc e Enter. Confirmações mantêm cancelamento seguro.
6. Crie um atalho com campos, acentos e emojis. Mude o idioma e confira que
   gatilho, nome, conteúdo, variáveis, categoria salva e valores dos campos foram preservados.
   Os nomes das categorias apresentados na tela acompanham o idioma.
7. Na Captura, confira regras, OCR, cores, formas, privacidade, histórico,
   seleção de emojis, GIF/MP4 e áudio. A busca de emojis aceita termos dos três
   idiomas, por exemplo `red heart` e `corazón rojo`. Imagens pessoais mantêm seus nomes.
8. Alterne inglês → espanhol → português, saindo e reabrindo a cada alteração.
   Verifique que o idioma é lembrado e que as demais preferências continuam salvas.

Os dados ficam em `SlashDeskPilotData` ao lado do EXE. Para aproveitar testes
anteriores, copie somente os dados de outro piloto, com ambos fechados.
Não substitua sua instalação oficial. Inicialização com Windows e atualizações
automáticas permanecem bloqueadas neste piloto, com as preferências preservadas.

O idioma da interface não altera os modelos OCR, conjuntos do Acento Rápido,
conteúdo dos atalhos, datas/números definidos pelas configurações regionais do Windows,
notas externas de releases ou nomes dos dispositivos. Seletores e notificações
nativos continuam seguindo o Windows. Os 3.972 emojis mantêm suas imagens e
sequências; 3.953 têm nomes CLDR em cada novo idioma e os 19 restantes usam
código Unicode como identificação.

## Validação e limites

`scripts/languages-catalog-smoke.ps1` verifica completude dos três catálogos,
chaves derivadas do texto de origem e referências explícitas XAML/C#.
`--languages-smoke` verifica compatibilidade com configurações antigas, fallback,
interpolação e variáveis, preferência salva/leitura ao iniciar, ausência de troca
parcial do idioma durante a execução, valores persistidos, bandeja, diálogos,
busca de emojis e preservação de conteúdo pessoal. Gera imagens dos controles
WPF reais das seis telas e janelas auxiliares, nos temas e tamanhos previstos.

Renders a 100/125/150/200% são evidência de layout e não comprovação de DPI
físico. Teclado e apps de destino reais, leitor de tela, múltiplos monitores,
áudio, gravação, notificações nativas e atualização ponta a ponta continuam na
validação integrada #70/#37. Depois do aceite deste piloto, reavaliar os próximos
passos do lançamento 3.3.0; não publicar tag/release nesta entrega.
