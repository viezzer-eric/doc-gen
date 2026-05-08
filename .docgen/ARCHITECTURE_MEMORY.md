# ARCHITECTURE_MEMORY

> Documento de arquitetura vivo — gerado e mantido por IA.
> Atualize as seções `AUTO` via `doc-gen update`. Escreva em `MANUAL` para anotações permanentes.

<!-- AUTO:START -->
```markdown
<!-- AUTO:START -->
# ARCHITECTURE_MEMORY.md - DocGen

## 1. Visão Geral

O projeto `DocGen` é uma ferramenta CLI (Command Line Interface) desenvolvida para automatizar a geração de documentação. Seu objetivo principal é escanear um repositório, agregar contexto relevante e, através de um `PromptEngine` (indicando integração com modelos de linguagem ou IA), gerar ou atualizar conteúdo de documentação. A ferramenta suporta a inserção de conteúdo em arquivos Markdown e a geração de documentos PDF, com um sistema de auditoria para rastrear as operações. É projetado para ser integrado em fluxos de trabalho de CI/CD.

## 2. Stack Tecnológico

*   **Linguagem de Programação:** C#
*   **Plataforma:** .NET (inferido por `.csproj`, `.sln` e estrutura de arquivos C#)
*   **Build System:** MSBuild / dotnet CLI
*   **Containerização:** Docker (via `dockerfile`)
*   **CI/CD:** Integração potencial com sistemas como GitHub Actions (evidenciado por `samples\doc-gen-ci.yml`)
*   **Principais Componentes/Bibliotecas (inferidos pelos nomes):**
    *   `PromptEngine`: Interação com modelos de linguagem (LLMs) ou serviços de IA.
    *   `PDF`: Geração de documentos em formato PDF.
    *   `MarkdownInserter`: Manipulação e inserção de conteúdo em arquivos Markdown.
    *   `RepositoryScanner`: Leitura e análise da estrutura de arquivos do repositório.
    *   `AuditLogger`: Registro de eventos e auditoria das operações.

## 3. Estrutura de Módulos

O projeto adota uma estrutura modular clara, com cada diretório `src\` representando uma responsabilidade específica:

*   **`src\CLI`**: Contém o ponto de entrada da aplicação (`Program.cs`), responsável por parsing de argumentos e orquestração do fluxo principal.
*   **`src\Models`**: Define as estruturas de dados (POCOs) utilizadas para representar informações e contexto entre os módulos.
*   **`src\Scanner`**: Encapsula a lógica para varrer o sistema de arquivos de um repositório, identificando e coletando arquivos e metadados relevantes.
*   **`src\Aggregator`**: Responsável por processar e consolidar os dados brutos coletados pelo `Scanner`, preparando o contexto para o `PromptEngine`.
*   **`src\PromptEngine`**: Gerencia a interação com o serviço de geração de conteúdo (provavelmente um LLM), formatando prompts e processando as respostas.
*   **`src\Inserter`**: Implementa a lógica para integrar o conteúdo gerado em arquivos Markdown existentes ou criar novos.
*   **`src\PDF`**: Contém a funcionalidade para converter o conteúdo gerado ou existente em formato PDF.
*   **`src\Logger`**: Fornece serviços de log e auditoria para registrar as ações e eventos da ferramenta.
*   **`src\nupkg`**: Diretório de saída para pacotes NuGet, indicando que a ferramenta é distribuída como um pacote .NET.
*   **`samples`**: Contém exemplos de uso, configurações e artefatos gerados, como `ARCHITECTURE_MEMORY.md` e `doc-gen-ci.yml`.

## 4. Fluxo Principal

O fluxo de execução da ferramenta `DocGen` segue uma sequência bem definida:

1.  **Iniciação (CLI)**: O usuário executa a ferramenta via `src\CLI\Program.cs`, fornecendo os parâmetros necessários.
2.  **Escaneamento (Scanner)**: O `RepositoryScanner` é invocado para analisar a estrutura do repositório alvo, identificando arquivos e diretórios relevantes, respeitando configurações de exclusão (ex: `.docignore`).
3.  **Agregação de Contexto (Aggregator)**: O `ContextAggregator` processa os dados brutos do scanner, consolidando-os em um formato coeso e otimizado para a geração de prompts.
4.  **Geração de Conteúdo (PromptEngine)**: O `PromptEngine` utiliza o contexto agregado para interagir com um modelo de linguagem externo, solicitando a geração de conteúdo de documentação.
5.  **Saída (Inserter / PDF Generator)**:
    *   Se a saída for Markdown, o `MarkdownInserter` é responsável por integrar o conteúdo gerado nos arquivos Markdown especificados.
    *   Se a saída for PDF, o `PdfGenerator` converte o conteúdo gerado (ou os arquivos Markdown resultantes) em um documento PDF.
6.  **Auditoria (Logger)**: O `AuditLogger` registra todas as etapas críticas do processo, incluindo entradas, saídas e quaisquer erros, garantindo rastreabilidade.

## 5. Decisões Arquiteturais

*   **Modularidade e Separação de Preocupações:** A estrutura de diretórios em `src\` demonstra uma forte adesão ao princípio de separação de preocupações, com cada módulo tendo uma responsabilidade única e bem definida. Isso facilita a manutenção, o teste unitário e a escalabilidade.
*   **Interface de Linha de Comando (CLI):** A escolha de uma CLI como interface primária indica um foco em automação, scriptability e integração fácil em pipelines de CI/CD.
*   **Integração com IA/LLM:** A existência do `PromptEngine` é uma decisão arquitetural central, posicionando a ferramenta como um gerador de documentação assistido por inteligência artificial, dependendo de serviços externos para a inteligência de conteúdo.
*   **Flexibilidade de Saída:** O suporte a múltiplos formatos de saída (Markdown e PDF) demonstra uma decisão de design para atender a diversas necessidades de consumo de documentação.
*   **Extensibilidade:** A arquitetura modular sugere que novos scanners, agregadores, motores de prompt ou formatos de saída poderiam ser adicionados com relativa facilidade, seguindo o padrão existente.
*   **Auditoria e Observabilidade:** A inclusão explícita de um `AuditLogger` reflete uma preocupação com a rastreabilidade das operações e a capacidade de depuração e monitoramento.
*   **Configuração via `.docignore`:** Similar ao `.gitignore`, a presença de `.docignore` indica um mecanismo de configuração para controlar o escopo do escaneamento, uma prática comum em ferramentas de análise de repositórios.
*   **Distribuição como Pacote NuGet:** A geração de `.nupkg` sinaliza que a ferramenta é projetada para ser distribuída e consumida como um pacote .NET, facilitando a instalação e o gerenciamento de versões.
<!-- AUTO:END -->

<!-- MANUAL:START -->
*(Espaço para anotações manuais do desenvolvedor — nunca será sobrescrito pela IA)*
<!-- MANUAL:END -->
```
<!-- AUTO:END -->

---

## Anotações Manuais

<!-- MANUAL:START -->
*(Adicione aqui decisões de negócio, contexto histórico ou restrições importantes.
Este bloco NUNCA será sobrescrito pela IA.)*
<!-- MANUAL:END -->