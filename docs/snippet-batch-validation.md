# Validação da coleção de atalhos (#135)

A validação em lote mantém um conjunto de gatilhos normalizados para leitura
local, importação, restauração e salvamento. Cada gatilho é normalizado uma vez
por lote, com as mesmas regras NFC + caixa invariável de `TriggerRule.Normalize`.
A ordenação e o formato Markdown, os limites externos, a leitura estrita da
restauração e a escrita atômica permanecem nos caminhos existentes.

## Fluxo automático para esta correção

1. Compilar e executar os testes específicos durante o desenvolvimento.
2. Executar a suíte de regressão antes de abrir a PR. Os novos testes também
   fazem parte da suíte normal de `SlashText.SmokeTests` no **Build Windows**.
3. Aguardar os checks da PR, que verificam regras/dados, interface WPF, idiomas,
   segurança e o pacote publicado. Consultar o resultado de cada check; um teste
   indisponível não equivale a aprovado.
4. Solicitar o aceite para integrar somente após revisar os resultados.

Não é necessário teste manual obrigatório para #135: não há alteração visual,
entrada nativa, áudio ou gravação. Esses cenários continuam exigindo validação
específica nas issues correspondentes e no candidato integrado (#70).

```powershell
dotnet build SlashText.sln -c Release -p:Platform=x64
dotnet run --project tests/SlashText.SmokeTests -c Release --no-build -p:Platform=x64 -- --snippet-batch-smoke "$env:TEMP/slashdesk-batch-$([guid]::NewGuid().ToString('N'))"
dotnet run --project tests/SlashText.SmokeTests -c Release --no-build -p:Platform=x64
```

O teste específico cria somente arquivos fictícios na pasta de saída. Verifica
conflitos por caixa e NFC, legados incompatíveis, mensagens, campos/metadados,
limite de importação, cancelamento, arquivo incompleto, salvamento sem alteração
e falha de substituição com o arquivo bloqueado. Os testes existentes de proteção,
compatibilidade e recuperação continuam na suíte completa.

## Medição reproduzível

```powershell
dotnet run --project tests/SlashText.SmokeTests -c Release --no-build -p:Platform=x64 -- --snippet-batch-benchmark "$env:TEMP/slashdesk-bench-$([guid]::NewGuid().ToString('N'))"
```

Use uma pasta nova/vazia e um processo separado por execução. Para comparar duas
versões, compile o mesmo medidor e conserve uma cópia completa da saída de testes
de cada versão. Execute sem outros testes concorrentes. Não execute o app de
produção nem use a pasta de dados real.

O medidor registra um aquecimento excluído e cinco amostras por operação, para
100, 1.000, 5.000 e 10.000 atalhos. Mede parsing em memória local/importação/
restauração, salvamento idêntico e alterado, incluindo I/O no salvamento.
`results.json` contém tempos, alocações gerenciadas do processo, contagens de GC,
hash do corpus, hash/versão da DLL, runtime e Windows. Compare hashes do corpus,
medianas e intervalos de todas as amostras; registre também os commits e o estado
das fontes usados para gerar cada DLL.

Alocações incluem continuações assíncronas e ruído do runtime; não representam
memória nativa ou pico de uso. Tempos de disco, JIT e carga da máquina variam.
O benchmark é uma ferramenta de diagnóstico, sem limite fixo em milissegundos
no CI. Uma melhora de validação não mede renderização da lista (#134), inicialização
completa, latência de expansão ou desempenho de hardware.
