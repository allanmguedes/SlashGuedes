# Localização da interface — #82

## Contrato

`AppSettings.Language` usa `pt-BR` por padrão, com `en` e `es` como opções.
Ausência, valor desconhecido ou erro na leitura inicial retorna a português.
A leitura inicial é somente leitura: recuperação de JSON corrompido continua
pertencendo ao store existente. Antes de construir a janela principal,
`UiText.LoadPreference` aplica o catálogo embutido correspondente.

O seletor em Configurações salva usando o store atômico existente, com gate
de manutenção, rollback da preferência em erro e aviso para reabrir. Não troca
o idioma parcialmente durante a execução nem reinicia automaticamente.

Textos estáticos XAML referenciam recursos `Ui.*`. Chamadas C# explícitas usam
`UiText.Get` ou `UiText.Format`. As chaves são derivadas por SHA-256 do texto
português de origem, com os 16 primeiros caracteres hexadecimais; o catálogo
pt-BR é a referência legível. Catálogos completos são incorporados ao EXE,
sem dependência externa ou tradução online. Quando uma mensagem não está
catalogada, seu texto de origem é mantido.

Ao adicionar texto, incluir a mesma chave nos três JSONs e traduzir somente
o formato da mensagem. Placeholders e argumentos permanecem separados.
`UiText.Format` respeita `CurrentCulture`; não modificar cultura regional para
trocar idioma. Os checks validam chaves, referências, placeholders e formatos.

## Conteúdo e identidades

Não passar conteúdo do usuário a `UiText.Get`. Gatilhos, textos, nomes de
grupos, imagens pessoais, valores de campos, caminhos e nomes de dispositivos
são preservados. `ShortcutCategories.DisplayName` traduz apenas o label, com
`Name` e seleção/filtro/salvamento estáveis. Presets de gravação mantêm `Value`
e aliases legados, com getters para nomes/descrições. Grupos da bandeja usam
identidades `Name` independentes do texto, mantendo ícones e busy gates.

Status legados do updater armazenados em português são traduzidos por
`UiText.UpdateStatus` somente na apresentação, usando templates conhecidos e
preservando versões. O formato persistido e notas externas permanecem intactos.

Metadados CLDR de emojis ficam em catálogos próprios `emoji-en/es.json`, com
commit, hashes e cobertura. `NotoEmojiCatalog.DisplayName` seleciona labels de
emojis incorporados sem alterar os registros; nomes customizados retornam
intactos. A busca inclui os termos dos três idiomas, preservando metadados,
valores e categorias canônicas. Attribution e geração constam no NOTICE Noto.

O idioma da interface não altera OCR, idioma dos conjuntos do Acento Rápido,
formatos dos templates, fontes, tema, ícones ou PNGs. Controles nativos do
Windows seguem o sistema. O contrato visual congelado continua intacto.

## Verificação

`LanguagesPilotBuild=true` usa isolamento `SlashDeskPilotData` e abre
Configurações. O workflow Languages Pilot Windows valida catálogos, controles
WPF, publicação portátil, identidade e isolamento do EXE publicado. Roteiro
manual: `docs/languages-pilot-testing.md`. Fixtures não substituem revisão
linguística humana nem a matriz física e integrada #70/#37.
