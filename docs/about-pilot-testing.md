# Piloto Sobre — #68

Configurações foi concluída na PR #79, integrada em `32d702cd74ec1502ddafacf08d6fa254d390f8a4`. Este piloto abre a tela Sobre para avaliação, sem substituir a instalação oficial.

## Abrir e testar

1. Extraia o ZIP em uma pasta nova. Feche outro piloto antes de executar `Abrir-Piloto-Sobre.cmd`.
2. Confira identidade, versão real, descrição dos recursos, privacidade e licença. A base funcional continua em 3.2.0; o nome do piloto 3.3.0 identifica o trabalho visual, não uma versão oficialmente publicada.
3. Teste Claro, Preto e Seguir o Windows em Configurações; volte a Sobre. Reduza a janela até 980 × 680 e percorra o conteúdo. Na largura mínima, os três cartões passam a linhas.
4. Use Tab, Shift+Tab e Enter nos botões. Confira projeto, notas da versão instalada, relato de problema e licença no navegador padrão.
5. As notas da versão instalada derivam da versão do executável. As notas da versão consultada só aparecem quando o serviço já tem um resultado com link. Não se afirma que o aplicativo está atualizado antes da consulta.
6. A verificação e a instalação de atualizações ficam desativadas neste piloto, assim como a inicialização com o Windows. As preferências são preservadas para o aplicativo normal. O resultado e a data exibidos em Sobre são os mesmos de Configurações.

Os dados são isolados em `SlashDeskPilotData`, ao lado do executável portátil. Não copie o EXE sobre a instalação oficial nem use dados de produção para testes. Para conferir preferências anteriores, copie somente dados de outro piloto para esta pasta nova, com os aplicativos fechados.

## Implementação e validação

Referência: publicação 11 congelada em `docs/design/3.3.0/contract/reference/app/page.tsx` e CSS correspondente. O logotipo original substitui o símbolo genérico da referência. Não foram copiados badges demonstrativos, links do Lab, promessas de funcionamento totalmente offline ou versão fictícia.

Componentes: `MainWindow.xaml`, `MainWindow.About.cs`, recursos `Styles/VisualLab/About.xaml`, inicialização e metadados do piloto. `UpdateService`, preferências, backups e formatos de dados existentes são preservados. A ação Verificar agora continua no mesmo serviço, com bloqueio de consultas manuais simultâneas; sua mensagem passa pelo diálogo temático já existente. Nenhum novo monitor foi criado.

`--about-smoke` verifica controles WPF reais em três temas, 1440 × 900 e 980 × 680, quatro escalas de renderização, rolagem, destinos dos handlers, erro de abertura do navegador e ausência de escrita nas preferências ao consultar a tela. O serviço real de atualização usa um transporte HTTP isolado para validar resultado atual/offline e sincronização das duas telas. Isso não testa acesso real ao GitHub nem instalação de uma atualização.

As imagens são evidências de renderização a 100/125/150/200%, não de mudança física de DPI entre monitores. Navegador padrão, leitor de tela, hardware, DPI físico e atualização ponta a ponta continuam na revisão manual e nas issues #69/#70/#37, conforme o escopo de cada uma. A #68 permanece aberta até avaliação e autorização de integração.
