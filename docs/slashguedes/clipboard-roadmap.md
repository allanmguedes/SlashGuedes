# SlashGuedes — Gerenciador de Área de Transferência

> Planejamento da primeira funcionalidade independente do fork `allanmguedes/SlashGuedes`.
>
> Projeto de origem: `lucasllira/SlashText` (SlashDesk). Manter os créditos e a licença MIT.
>
> Estado: **planejamento aprovado em conversa; implementação e testes pendentes.**
>
> Desenvolvimento isolado na branch `feature/clipboard-manager`; não alterar a `main` até validação.

## Objetivo

Ampliar o SlashDesk com um histórico próprio de área de transferência do Windows, acessível em dois lugares:

1. **Painel rápido**, exibido sobre o aplicativo ativo por um atalho global configurável (sugestão: `Win+Shift+V`). Experiência próxima à do `Win+V`: pesquisar, navegar pelo teclado, visualizar, fixar e reutilizar os itens.
2. **Aba "Área de Transferência"** na janela principal, com pesquisa completa, favoritos, organização em categorias, detalhes, exclusão e preferências.

Não substituir nem desativar o histórico nativo do Windows. O registro deve funcionar independentemente da configuração do histórico do Windows.

## Primeira versão funcional (MVP)

- Detectar mudanças de clipboard em segundo plano, com monitoramento **opt-in**, por `AddClipboardFormatListener` / `WM_CLIPBOARDUPDATE`, mantendo janela de mensagens apropriada.
- Registrar **textos, URLs, imagens**, inclusive prints copiados intencionalmente pelo próprio SlashDesk. Se a captura estiver configurada apenas para salvar em arquivo, não copiar ou abrir arquivos silenciosamente.
- Permitir colar texto/imagem em aplicativos compatíveis por ação explícita do usuário, ou pelo menos recolocar o item no clipboard quando a colagem automática não for viável. Preservar formatos quando suportados; não prometer compatibilidade universal.
- Painel rápido de abertura/fechamento por atalho configurável, com pesquisa instantânea, seleção por teclado, `Enter`, `Esc` e ações de fixar/favoritar.
- Aba principal para pesquisar, inspecionar, favoritar, fixar, excluir e classificar em categorias.
- Persistir itens de forma local, com limite configurável por quantidade, idade e tamanho, expurgo controlado e tratamento de duplicados. Itens fixados protegidos contra expurgo automático, com limite de espaço previsível.
- Preferências: ativar, pausar, limpar, atalhos, limites, aplicativos excluídos e modo de sessão sem gravação em disco.
- Nenhum upload/sincronização automática.

## Interoperabilidade e proteção de dados

- Respeitar a API e os marcadores já definidos em `Services/ExpansionClipboardSession.cs`: `ExcludeClipboardContentFromMonitorProcessing` e `SlashDesk.Expansion.Marker`. Nunca armazenar os payloads temporários produzidos para expansão dos atalhos `/`.
- Tratar ciclos de publicar/restaurar o clipboard sem duplicar entradas nem sobrescrever uma cópia que ocorreu enquanto o SlashDesk trabalhava. Acompanhar sequência/eventos e registrar apenas cópias intencionais compatíveis.
- Proteger contra clipboard ocupado, dados não renderizáveis, formatos desconhecidos, elementos muito grandes, concorrência e falhas na leitura ou persistência. Falha na captura não deve bloquear `Ctrl+C`, `Ctrl+V`, captura de imagem ou expansão de texto.
- Evitar exposição de dados sensíveis: monitoramento desativado por padrão até consentimento explícito; opção de pausa, exclusão de aplicativos e eliminação total dos dados. Não presumir que é possível identificar automaticamente toda senha/token.
- Definir estratégia de proteção local que não quebre a edição portátil. Se usar criptografia vinculada ao Windows, documentar as limitações para migração de computador. **Histórico de clipboard não entra automaticamente nos backups do app até definição explícita de privacidade.**
- Logs devem permanecer restritos a eventos e métricas sem texto, pixels, nomes de arquivo ou conteúdo copiado, segundo `DiagnosticPolicy`.
- Monitoramento deve tratar clipboard de aplicações com diferentes níveis de privilégio de forma conservadora.
- Atualizador oficial: revisar isolamento/branding da versão Guedes para impedir que uma release do projeto original substitua nosso executável sem confirmação.

## Implementação gradual no código atual

| Etapa | Arquivos/áreas | Entrega |
|---|---|---|
| 0. Base | fork, branch, documentação | Projeto isolado; sem alterar a base original |
| 1. Modelo e armazenamento | `Models/`, `Services/`, `AppPaths.cs` | Entidades, limites, retenção e testes com dados fictícios |
| 2. Captura monitorada | novo serviço em `Services/`; integração na inicialização/saída | Detectar cópias reais e filtrar os dados temporários de expansão |
| 3. Painel rápido | nova `Window` em `Views/`; atalhos configuráveis | `Win+Shift+V` (quando disponível), busca e reutilização |
| 4. Aba principal | `MainWindow.xaml`, `MainWindow.xaml.cs`, recursos de tema e localização | Histórico, favoritos, fixados e categorias |
| 5. Ajustes, testes e distribuição | `tests/`, `docs/`, workflows, updater | Verificar regressões, desempenho, persistência, empacotamento |

**Separação:** a melhora na confiabilidade dos gatilhos `/` será conduzida em outra branch, após obter casos reproduzíveis, para evitar misturar duas mudanças sensíveis de teclado/clipboard.

## Contrato visual

Respeitar `docs/design-system.md`: navegação horizontal, tokens semânticos e identidade ciano, temas Claro/Preto/Seguir o Windows, foco por teclado, acessibilidade, escala 100/125/150% e suporte a múltiplos monitores. O painel rápido deve ser compacto e rápido; a aba deve permitir organização avançada sem duplicar a função dos snippets.

## Verificações de aceitação

1. `Ctrl+C` de texto curto e longo registra exatamente um item por cópia intencional, inclusive quando um conteúdo repetido é copiado novamente (com política de deduplicação consistente).
2. Copiar link, imagem, imagem gerada pelo SlashDesk e imagem de navegador; permitir visualização e retorno ao clipboard.
3. Abrir o painel por atalho, pesquisar, fixar, selecionar e inserir/reutilizar; cancelar com `Esc` sem mudar o clipboard.
4. Não armazenar material temporário da expansão `/`, nem desorganizar o clipboard original após uma expansão.
5. Operar corretamente com app pausado, limites atingidos, histórico vazio, clipboard ocupado e após reinício.
6. Testar funcionamento lado a lado com `Win+V`, outros aplicativos, `Ctrl+C`/`Ctrl+V`, captura de tela e teclado ABNT2.
7. `dotnet build SlashText.sln --configuration Release -p:Platform=x64` e smoke tests originais devem continuar aprovados.
8. Revisar atualização, backup/restauração e limpeza de dados antes de uma distribuição.

## Fora do MVP (avaliar depois)

- Arquivos e pastas copiados, HTML/RTF ricos com preservação integral, OCR automático de imagens e compartilhamento entre PCs.
- Criação direta de atalho `/comando` a partir de um item favorito; boa integração futura, mas deve vir após a estabilidade de ambos os módulos.

## Observações de entrega

A documentação não indica que qualquer funcionalidade foi integrada ou testada. Toda implementação deve ser adicionada e validada em etapas pequenas, mantendo intacto o projeto upstream.
