# Permissões administrativas — LanePets

Este documento descreve o sistema de níveis de acesso do painel administrativo.

## O caminho de uma requisição

```
Usuário
  ↓
Endpoint da API
  ↓
Sessão válida?          não → 401
  ↓ sim
Conta ativa?            não → 401 (a sessão é derrubada)
  ↓ sim
Tem a permissão?        não → 403 Forbidden
  ↓ sim
Executa e responde 200
```

Esconder o botão no HTML **não** é a proteção. A proteção é o 403 acima:
chamar `DELETE /api/...` na mão, sem passar por tela nenhuma, cai exatamente
no mesmo caminho.

## Perfis

| Perfil | Valor no banco | O que é |
|---|---|---|
| Administrador Geral | `AdminGeral` | Acesso total. Único que concede permissão, liga acesso total e cria outro Administrador Geral. |
| Administrador | `Admin` | Nasce **sem nenhum acesso**. Enxerga apenas o que o Administrador Geral liberar. |

O perfil é uma **coluna do banco** (`UsuariosAdministradores.Perfil`). Em lugar
nenhum a autorização compara `email == "admin@gmail.com"`.

## Módulos

`dashboard`, `agendamentos`, `pedidos`, `clientes`, `pets`, `produtos`,
`servicos`, `seguros`, `avaliacoes`, `pagamentos`, `relatorios`, `usuarios`,
`configuracoes`.

Cada um com quatro ações independentes: **Visualizar, Criar, Editar, Excluir**.
A lista canônica vive em `Services/PermissaoService.cs` → `ModulosAdmin.Todos`.
Menu, tela de permissões e verificação dos endpoints leem todos daí.

## Tabelas

```
UsuariosAdministradores        (JÁ EXISTIA — colunas acrescentadas)
    Id, Email, SenhaHash, SenhaSalt, Ativo, CriadoEm
    + Nome, Telefone, Perfil, AcessoTotal, UltimoAcesso

UsuariosAdminPermissoes        (nova)
    Id, UsuarioAdminId, Modulo,
    PodeVisualizar, PodeCriar, PodeEditar, PodeExcluir
    índice único (UsuarioAdminId, Modulo)

AuditoriasAdmin                (nova)
    Id, DataHora, AutorId, AutorEmail, Acao, AlvoId, AlvoEmail, Detalhes
```

A ausência de linha em `UsuariosAdminPermissoes` significa **nenhum acesso** ao
módulo — o padrão de todo usuário novo.

O schema é criado na subida da aplicação, em `Services/SeedService.cs`, com
`CREATE TABLE IF NOT EXISTS` e `ALTER TABLE`, seguindo o padrão que o projeto
já usava. **O projeto não usa EF Migrations**: o schema sempre nasceu de
`EnsureCreated()` + SQL cru. Nenhum dado existente é apagado ou alterado.

## Regras que o backend garante

- Administrador comum não concede permissão a ninguém — nem a si mesmo.
- Ninguém altera as próprias permissões, nem o Administrador Geral.
- Administrador comum não edita, desativa, exclui nem troca a senha de um
  Administrador Geral.
- O único Administrador Geral do sistema não pode ser excluído nem desativado.
- Conta desativada perde as sessões abertas na hora e não faz login.
- Permissão alterada vale na requisição seguinte, sem precisar deslogar.

## Onde está cada coisa

| Arquivo | Papel |
|---|---|
| `Services/PermissaoService.cs` | Perfis, catálogo de módulos, resolução do usuário da requisição, `ExigirAsync`, auditoria |
| `Controllers/UsuariosAdminController.cs` | CRUD de administradores e permissões |
| `Controllers/ApiControllerBase.cs` | Traduz `AcessoNegadoException` → **403** |
| `Middleware/AdminAreaGuardMiddleware.cs` | Bloqueia o HTML das páginas por módulo |
| `wwwroot/js/admin-permissoes.js` | Menu dinâmico, `data-permissao="modulo:acao"`, bloqueio de página |
| `wwwroot/usuarios-admin.html` + `js/usuarios-admin.js` | Tela Administração › Usuários Administrativos |
| `wwwroot/acesso-negado.html` | Aviso de acesso negado |

## Como testar

```powershell
dotnet run
# em outro terminal
.\testar-permissoes.ps1
```

O script cria um administrador limitado, confere que módulos não liberados
respondem **403** (e não 200), liga e desliga acesso total, tenta escalar
privilégio pela API e limpa tudo ao final.
