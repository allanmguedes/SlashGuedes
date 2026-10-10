# Piloto 3.3.0 — Estatísticas (#66)

Base: Acento Rápido aprovado e integrado pela PR #77 em `3467b02d79ccc5c4b0127243186a9bed8435c600`. Referência: Visual Lab publicação 11, contrato congelado em `docs/design/3.3.0/contract/`. WPF e os serviços existentes permanecem em uso; os tokens e arquivos do contrato não foram alterados.

## Avaliação no piloto

Feche o piloto anterior, extraia o ZIP em uma pasta separada e execute `Abrir-Piloto-Estatisticas.cmd`. O piloto abre diretamente em Estatísticas, usa `SlashDeskPilotData` e não oferece atualização automática. A versão oficial e seus dados permanecem separados. Para manter preferências e contagens do piloto anterior, copie a pasta inteira `SlashDeskPilotData` para a nova pasta; mantenha os destinos de captura existentes.

- Confira Claro, Preto e Sistema, janela ampla/mínima, rolagem, Tab e foco. Os quatro cartões passam para duas colunas na janela menor; os demais blocos se empilham e continuam acessíveis.
- Sem uso, os valores são zero e os rankings mostram orientações, sem dados ilustrativos.
- Expanda um atalho, insira um acento real e faça uma captura. Ao abrir Estatísticas, confira os incrementos. Cliques na prévia local do Acento Rápido não são contabilizados como inserção global.
- O ranking mostra até oito atalhos existentes. O número de atalhos com uso registrado inclui registros de atalhos removidos, como antes; os totais também os incluem. Nomes/gatilhos longos mantêm conteúdo completo na dica e na leitura acessível.
- Capturas refletem o histórico salvo disponível. Excluir uma entrada reduz o total. Região/Monitor/Janela e GIF/MP4 são categorias sobrepostas; as barras usam o total do histórico como referência, sem sugerir uma soma exclusiva. Captura longa e demais tipos seguem incluídos no total.
- Tempo economizado é uma estimativa: caracteres poupados/200, arredondado para cima em minutos. A média continua sendo caracteres poupados por expansão, com a formatação existente. Não há comparação mensal, exportação ou coleta adicional.
- Em dados indisponíveis, a tela informa a falha e usa `—` nas métricas afetadas, mantendo capturas e demais módulos disponíveis. Apenas abrir/ver a tela não escreve nos arquivos de uso/histórico.
- Use **?** para o guia local, busque estimativa/ranking/capturas/privacidade e confira **Mostrar na tela**. A ajuda só realça controles e não executa operações de uso/captura.

## Mapeamento para dados reais

| Apresentação | Fonte/fórmula preservada |
|---|---|
| Expansões | Soma de `UsageRecord.Count` em `UsageService.Records` |
| Caracteres poupados | Soma de `UsageRecord.CharactersSaved` |
| Acentos inseridos | `UsageService.QuickAccent.Count` |
| Capturas | Quantidade de entradas em `CaptureService.History` |
| Atalhos com uso | Registros cujo `Count > 0`, inclusive removidos |
| Ranking de atalhos | Atalhos atuais com contagem positiva, ordem decrescente, até oito |
| Tipos de captura | Comparação sem distinguir maiúsculas de `Type`/`MediaKind`, como na tela anterior |
| Tempo estimado | `Math.Ceiling(characters / 200d)` minutos |
| Média por expansão | `characters / (double)total`, ou zero, formatado como antes |
| Acentos favoritos | Até oito caracteres com contagem positiva, ordem de uso e desempate existente |

A camada de apresentação está em `MainWindow.Statistics.cs`, XAML em `MainWindow.xaml` e estilos explícitos em `Statistics.xaml`. A ajuda usa o componente comum. `UsageService.IsAvailable` expõe a falha de leitura apenas em memória; JSON legado e atual, campos e caminhos são preservados. Se o arquivo estiver ilegível, as atualizações auxiliares de contagem ficam suspensas para não substituí-lo por um snapshot incompleto; a expansão/inserção continua funcionando. Uma leitura posterior bem-sucedida restaura a contagem. Registros nulos e totais impossíveis não derrubam o aplicativo. O painel não substitui dados desconhecidos por números fictícios.

## Evidências automáticas e limites

`--statistics-smoke` usa controles WPF reais e leitores de uso/histórico sobre dados isolados. Verifica fórmulas anteriores, registros de atalhos removidos, legados em array, contagens por tipo, barras, ausência de escrita ao visualizar, persistência/incremento real nos serviços, zeros, números grandes, nome longo, JSON inválido, registros nulos, overflow, arquivo bloqueado e recuperação. Renderiza Claro/Preto/Sistema em 1440×900 e 980×680, nas escalas 100/125/150/200%; testa acesso aos blocos inferiores e ajuda pesquisável.

São dados sintéticos de teste carregados por serviços reais, sem instalação de hooks, captura de tela ou coleta de conteúdo. As imagens de galeria mostram o monitor geral indisponível porque o fixture não instala hooks; isso não é indicação do estado do piloto normal. Renderização em escala não substitui o teste físico de monitores/DPI misto, que permanece na #70. A aprovação visual da #66 acontece no executável do piloto pelo usuário.

Sem tag/release ou integração da #66 nesta etapa. Próxima issue após aprovação: #67 — Configurações.
