# Lista virtualizada de atalhos (#134)

## Comportamento

`SnippetListPanel` recebe dados `Snippet` em um ListBox com VirtualizingStackPanel
e reciclagem. Os cartões são criados para a área visível/cache, sem construir
milhares de Buttons. O contêiner reutiliza a estrutura visual e atualiza texto,
indicadores, estado, tooltip e nome acessível para o item atual.

Seleção atualiza o editor sem refiltrar/recriar a lista. Busca e filtros mantêm
a fonte de itens quando o resultado não mudou. Cancelar descarte restaura a
seleção e o foco após o evento WPF, preservando o rascunho e respeitando uma interação posterior. Botão direito abre ações sobre o item
atual sem selecionar outra linha; fechar o menu restaura foco validando a
identidade do contêiner. Categorias têm rolagem própria e cedem espaço à lista
quando a janela está baixa. Não há mudança nos dados ou no formato de persistência.

## Testes automatizados

```powershell
dotnet build SlashText.sln -c Release -p:Platform=x64
# Processos separados; executar as janelas de foco sequencialmente.
& ./src/SlashText/bin/x64/Release/net10.0-windows/SlashDesk.exe --shortcut-list-smoke "$env:TEMP/slashdesk-list-$([guid]::NewGuid().ToString('N'))"
& ./src/SlashText/bin/x64/Release/net10.0-windows/SlashDesk.exe --shortcuts-workspace-smoke "$env:TEMP/slashdesk-workspace-$([guid]::NewGuid().ToString('N'))"
dotnet run --project tests/SlashText.SmokeTests -c Release --no-build -p:Platform=x64
```

O modo focado cria dados fictícios e janelas WPF apresentadas, com 10.000 atalhos
em 12 combinações: pt-BR/en/es, Light/Dark, 1440×900/980×680. Não inicia hooks,
bandeja, atualizador ou clipboard do usuário. Produz `result.txt`,
`measurements.json`, checkpoints e capturas PNG. Falhas geram `failure.txt` e
código de saída diferente de zero.

Verifica:

- Cardinalidade de dados separada da quantidade de cartões realizados. O limite
  estrutural de 80 admite várias áreas de cache/foco/alturas; rejeita realização
  proporcional a 10.000. Não há assertion de tempo absoluto.
- Reciclagem por identidade, dados/indicadores/seleção corretos, rolagem distante
  com interseção geométrica no viewport e conservação da fonte de itens.
- Busca por nome/comando/conteúdo/categoria, filtros, IDs e ordenação, busca vazia,
  limpeza, nomes/conteúdo longos e redimensionamento da janela/coluna.
- Seleção cancelada/aceita, rascunho novo ou salvo, caret e foco do editor; cancelamento por Home/End, foco divergente da seleção e foco dado a outro campo antes da restauração diferida.
- Eventos roteados WPF de mouse/teclado e popup: Down/Up/Escape, rascunho em A,
  ação em B, identidade correta persistida e demais itens preservados.
- Categorias acessíveis por rolagem no tamanho mínimo suportado.

`ShortcutsWorkspaceSmoke` chama o modo focado ao final; o Build Windows já executa
esse workspace, além dos testes de salvamento/reabertura, duplicação, favoritos,
fixados, proteção, conteúdo rico e demais funcionalidades existentes.

## Revisão e limites

Um agente prepara/executa os testes; outro revisa independentemente seleção,
reciclagem e foco. Achados precisam de evidência executada, não somente opinião
de IA. Builds e testes de foco no mesmo checkout devem ser sequenciais.

Os eventos e o foco são programáticos em WPF real. Não certificam entrada física,
leitor de tela, transição real de DPI ou fluidez percebida. O julgamento visual
das capturas complementa as asserções. Não é necessário repetir manualmente toda
a bateria quando os cenários foram comprovados; lacunas relevantes seguem para
as análises/validações correspondentes (#90/#70).

Medições antes/depois usam o mesmo corpus e dimensão, com aquecimento, amostras,
dispersão e alocações do Dispatcher. Workloads antigos de 5.000/10.000 itens foram
limitados à construção para evitar renderização excessiva já observada na análise
E; a fixture apresentada de 10.000 prova a realização limitada após a correção.
Esses tempos não são FPS, nem promessa de latência para qualquer computador.

## Evidência local da correção

Comparação com `main` em `3681dcad`, usando o mesmo corpus, raiz WPF de
1440×900 não apresentada, aquecimento e medianas de 3–5 amostras. Alocações
referem-se à thread do Dispatcher; não incluem memória nativa ou de outras threads.
São resultados observados, sem limite de tempo obrigatório no CI.

| Operação | Itens | Antes (ms) | Depois (ms) | Antes/depois (bytes gerenciados) |
| --- | ---: | ---: | ---: | ---: |
| Reconstrução da navegação | 100 | 19,07 | 2,35 | 2.186.344 / 256.488 |
| Navegação e layout | 1.000 | 980,17 | 7,56 | 73.574.944 / 1.491.928 |
| Busca ampla e layout | 1.000 | 2.637,15 | 8,23 | 74.093.488 / 1.504.264 |
| Busca estreita e layout | 1.000 | 12,78 | 14,82 | 1.570.608 / 1.564.976 |
| Construção da navegação | 5.000 | 457,10 | 17,89 | 96.531.896 / 5.164.088 |
| Construção da navegação | 10.000 | 956,77 | 35,19 | 192.911.360 / 10.168.528 |

A busca estreita não mostrou ganho de tempo nesta amostra. Separadamente, a
matriz apresentada de 10.000 itens passou nas 12 combinações, com no máximo
6 cartões realizados na janela grande e 4 na pequena, incluindo cache/foco.
A regressão integrada de Atalhos passou em Light/Dark/System. A revisão
independente identificou a seleção indevida no botão direito durante o
desenvolvimento; a correção e o cenário com rascunho em outro item foram
incluídos na bateria.
