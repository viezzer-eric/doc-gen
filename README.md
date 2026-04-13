# doc-gen — Arquitetura Viva

> Documentação de arquitetura gerada e mantida automaticamente por IA. Zero esforço manual nas seções automáticas. Controle total nas seções manuais.

---

## Como funciona

```
git push  →  doc-gen update  →  IA analisa delta  →  ARCHITECTURE_MEMORY.md atualizado
```

O `doc-gen` mantém um `ARCHITECTURE_MEMORY.md` no seu repositório com duas camadas:

| Seção | Tag | Quem escreve |
|-------|-----|-------------|
| Automática | `<!-- AUTO:START/END -->` | IA (Claude Sonnet) |
| Manual | `<!-- MANUAL:START/END -->` | Você |

A IA **nunca toca** nas seções manuais. Você **nunca precisa atualizar** as automáticas.

---

## Instalação

### Pré-requisitos
- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- Chave da API Anthropic: [console.anthropic.com](https://console.anthropic.com)

### Como ferramenta global (.NET Tool)

```bash
dotnet tool install --global doc-gen
```

### A partir do código-fonte

```bash
git clone <este-repo>
cd doc-gen/src
dotnet build
dotnet run -- --help
```

---

## Configuração

### 1. Variável de ambiente

```bash
# Linux / macOS
export ANTHROPIC_API_KEY=sk-ant-...

# Windows PowerShell
$env:ANTHROPIC_API_KEY = "sk-ant-..."
```

### 2. .docignore (opcional)

Crie um `.docignore` na raiz do seu projeto (mesma sintaxe do `.gitignore`):

```
node_modules/
bin/
obj/
*.png
tests/
```

Veja o exemplo completo em [`samples/.docignore`](samples/.docignore).

---

## Uso

### Inicialização (primeira vez)

```bash
doc-gen init
```

Analisa seu repositório e gera o `ARCHITECTURE_MEMORY.md` inicial com a visão geral, stack, módulos e decisões arquiteturais.

### Atualização contínua

```bash
# Atualiza com as mudanças desde o último run
doc-gen update

# Simula sem alterar arquivos
doc-gen update --dry-run

# Saída JSON para integrações (Slack, CI, etc.)
doc-gen update --json

# Faz commit automático após atualizar
doc-gen update --auto-commit

# Tudo junto
doc-gen update --json --auto-commit
```

### Status

```bash
doc-gen status
```

Exibe o estado atual: último hash, último commit processado, tokens usados, histórico de execuções.

---

## Arquivos gerados

| Arquivo | Descrição |
|---------|-----------|
| `ARCHITECTURE_MEMORY.md` | O documento vivo de arquitetura |
| `.doc_state.json` | Cache de estado (hash + último commit) |
| `.doc_gen_log.json` | Log de auditoria (últimas 500 execuções) |

Adicione ao `.gitignore` se não quiser versionar os arquivos de estado:
```
.doc_state.json
.doc_gen_log.json
```

Ou versione-os para rastreabilidade completa no histórico git.

---

## Integração CI/CD (GitHub Actions)

```yaml
# .github/workflows/doc-gen.yml
name: Arquitetura Viva

on:
  push:
    branches: [main]

jobs:
  update-architecture:
    runs-on: ubuntu-latest
    permissions:
      contents: write
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'

      - run: dotnet tool install --global doc-gen

      - run: doc-gen update --json --auto-commit
        env:
          ANTHROPIC_API_KEY: ${{ secrets.ANTHROPIC_API_KEY }}

      - run: git config user.name "doc-gen[bot]" && git push
```

Adicione `ANTHROPIC_API_KEY` nos secrets do repositório em:
**Settings → Secrets → Actions → New repository secret**

---

## Estrutura do ARCHITECTURE_MEMORY.md

```markdown
# ARCHITECTURE_MEMORY

<!-- AUTO:START -->
## Visão Geral
...gerado pela IA...

## Stack Tecnológico
...

## Decisões Arquiteturais
...
<!-- AUTO:END -->

---

## Anotações do Time

<!-- MANUAL:START -->
### Contexto de Negócio
Escreva aqui — nunca será sobrescrito.

### Restrições Técnicas
...
<!-- MANUAL:END -->
```

---

## Segurança

- A chave da API é lida apenas da variável de ambiente `ANTHROPIC_API_KEY`
- Nenhum código-fonte é enviado para servidores externos além da API Anthropic
- O diff enviado à IA é limitado às 20 primeiras mudanças de arquivo e 15 linhas por arquivo

---

## Arquitetura do projeto

```
src/
├── Scanner/          # LibGit2Sharp — lê repositório, gera hash, extrai delta git
├── Aggregator/       # Monta contexto para a IA, segmenta seções do documento
├── PromptEngine/     # Chama API Anthropic, parseia resposta + categorização
├── Inserter/         # Valida Markdown (Markdig), substitui seções AUTO com segurança
├── Logger/           # Persiste log de auditoria (.doc_gen_log.json)
├── Models/           # Tipos compartilhados
└── CLI/              # Program.cs — comandos init | update | status
```

---

## Roadmap

- [x] `doc-gen init` — bootstrap inicial
- [x] `doc-gen update` — atualização incremental
- [x] `doc-gen status` — estado atual
- [x] `--dry-run` — simulação
- [x] `--json` — saída estruturada
- [x] `--auto-commit` — commit automático
- [x] `.docignore` — exclusão de arquivos
- [x] Log de auditoria
- [ ] `doc-gen watch` — daemon com debounce
- [ ] Vetor semântico para evitar re-sínteses desnecessárias
- [ ] Webhook para Slack / Teams
- [ ] Suporte a múltiplos documentos (por módulo)

---

## Licença

MIT
