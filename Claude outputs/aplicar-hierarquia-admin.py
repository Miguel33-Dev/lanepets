#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
LanePets — aplica a regra de hierarquia administrativa.

O QUE ESTE SCRIPT MUDA
----------------------
"Usuários Administrativos" deixa de ser um módulo que o Administrador Geral
pode delegar e passa a ser área EXCLUSIVA dele. Nem uma permissão individual,
nem o "acesso total" abrem essa porta para um administrador comum.

    ADMINISTRADOR GERAL  ->  controla permissões  ->  ADMINISTRADORES
    ADMINISTRADOR        ->  nunca controla permissões de ninguém

COMO RODAR
----------
    python aplicar-hierarquia-admin.py "C:\\caminho\\para\\LanePetsCSharp"

Sem argumento, usa a pasta onde o script estiver. O script é idempotente:
rodar duas vezes não estraga nada — ele avisa o que já estava aplicado.
"""

import os
import re
import sys

raiz = sys.argv[1] if len(sys.argv) > 1 else os.path.dirname(os.path.abspath(__file__))
erros = []
feitos = []


def caminho(rel):
    return os.path.join(raiz, rel.replace('/', os.sep))


def ler(rel):
    with open(caminho(rel), encoding='utf-8') as f:
        return f.read()


def gravar(rel, texto):
    with open(caminho(rel), 'w', encoding='utf-8') as f:
        f.write(texto)


def trocar(texto, antigo, novo, rotulo):
    """Troca exigindo ocorrência única. Se já estiver trocado, apenas avisa."""
    n = texto.count(antigo)
    if n == 0:
        if novo and novo in texto:
            feitos.append('(já aplicado) ' + rotulo)
            return texto
        erros.append('NÃO ENCONTRADO: ' + rotulo)
        return texto
    if n > 1:
        erros.append('AMBÍGUO (%d ocorrências): %s' % (n, rotulo))
        return texto
    feitos.append(rotulo)
    return texto.replace(antigo, novo)


# ===========================================================================
# 1. Services/PermissaoService.cs — módulo não delegável
# ===========================================================================
s = ler('Services/PermissaoService.cs')

s = trocar(s, '''    public record Definicao(string Chave, string Rotulo, string Grupo);''',
'''    /// <param name="SomenteGeral">
    /// Modulo que NAO pode ser delegado. Ele pertence ao Administrador Geral e
    /// so a ele — nem uma permissao individual, nem o acesso total abrem esta
    /// porta para um administrador comum. E o que impede a hierarquia de se
    /// desfazer: quem administra administradores e sempre o topo.
    /// </param>
    public record Definicao(string Chave, string Rotulo, string Grupo, bool SomenteGeral = false);''',
'Definicao ganha SomenteGeral')

s = trocar(s, '''        new(Usuarios,      "Usuários Administrativos", "Administração"),''',
'''        new(Usuarios,      "Usuários Administrativos", "Administração", SomenteGeral: true),''',
'modulo Usuarios marcado como exclusivo do Geral')

s = trocar(s, '''    public static readonly HashSet<string> Chaves =
        Todos.Select(m => m.Chave).ToHashSet(StringComparer.OrdinalIgnoreCase);''',
'''    public static readonly HashSet<string> Chaves =
        Todos.Select(m => m.Chave).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Modulos exclusivos do Administrador Geral.</summary>
    public static readonly HashSet<string> ExclusivosDoGeral =
        Todos.Where(m => m.SomenteGeral).Select(m => m.Chave).ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static bool EhExclusivoDoGeral(string chave) => ExclusivosDoGeral.Contains(chave ?? "");''',
'ExclusivosDoGeral')

s = trocar(s, '''    public bool Pode(string modulo, AcaoPermissao acao)
    {
        if (AcessoIrrestrito) return true;''',
'''    public bool Pode(string modulo, AcaoPermissao acao)
    {
        // Modulo exclusivo do Geral: nem permissao individual nem acesso total
        // abrem. "Acesso total" libera as FERRAMENTAS do petshop, nunca a
        // autoridade sobre os outros administradores.
        if (ModulosAdmin.EhExclusivoDoGeral(modulo)) return EhGeral;

        if (AcessoIrrestrito) return true;''',
'Pode() bloqueia modulo exclusivo mesmo com acesso total')

s = trocar(s, '''        var rotulo = ModulosAdmin.Todos.FirstOrDefault(m => m.Chave == modulo)?.Rotulo ?? modulo;
        throw new AcessoNegadoException($"Você não possui permissão para {Verbo(acao)} em {rotulo}.");''',
'''        var rotulo = ModulosAdmin.Todos.FirstOrDefault(m => m.Chave == modulo)?.Rotulo ?? modulo;
        if (ModulosAdmin.EhExclusivoDoGeral(modulo))
            throw new AcessoNegadoException($"{rotulo} é uma área exclusiva do Administrador Geral.");
        throw new AcessoNegadoException($"Você não possui permissão para {Verbo(acao)} em {rotulo}.");''',
'mensagem de 403 especifica para area exclusiva')

s = trocar(s, '''                editar = contexto.Pode(m.Chave, AcaoPermissao.Editar),
                excluir = contexto.Pode(m.Chave, AcaoPermissao.Excluir)
            })''',
'''                editar = contexto.Pode(m.Chave, AcaoPermissao.Editar),
                excluir = contexto.Pode(m.Chave, AcaoPermissao.Excluir),
                somenteGeral = m.SomenteGeral
            })''',
'Mapear() expoe somenteGeral')

gravar('Services/PermissaoService.cs', s)


# ===========================================================================
# 2. Controllers/UsuariosAdminController.cs — área toda exclusiva do Geral
# ===========================================================================
s = ler('Controllers/UsuariosAdminController.cs')

antes = s
s = re.sub(
    r'(var contexto = )?await permissoes\.ExigirAsync\((token|req\.Token \?\? ""), ModulosAdmin\.Usuarios, AcaoPermissao\.\w+\);',
    lambda m: (m.group(1) or '') + 'await permissoes.ExigirGeralAsync(%s);' % m.group(2),
    s)
if s != antes:
    feitos.append('todos os endpoints de usuarios exigem AdminGeral')
elif 'ModulosAdmin.Usuarios' not in s:
    feitos.append('(já aplicado) endpoints exigem AdminGeral')
else:
    erros.append('NÃO ENCONTRADO: chamadas ExigirAsync(..., ModulosAdmin.Usuarios, ...)')

if 'ModulosAdmin.Usuarios' in s:
    erros.append('AINDA HÁ referência a ModulosAdmin.Usuarios no controller')

s = trocar(s, '''/// DUAS CAMADAS DE REGRA, e as duas moram aqui:
///
///   1. permissao de modulo  -> quem pode abrir/criar/editar/excluir usuarios
///   2. regras do Administrador Geral -> o que NEM com permissao pode ser feito
///
/// A segunda existe porque a primeira nao basta: um administrador comum a quem
/// se liberou "Usuários Administrativos" poderia, sem ela, se auto-conceder
/// acesso total e virar administrador geral em dois cliques. Por isso conceder
/// permissao, ligar acesso total e mexer em perfil sao exclusivos do
/// Administrador Geral, e ninguem altera as proprias permissoes.''',
'''/// REGRA UNICA E INEGOCIAVEL DESTE CONTROLLER:
///
///     TODOS os endpoints aqui exigem Perfil = AdminGeral.
///
/// Nao existe permissao que libere esta area para um administrador comum, e
/// "acesso total" tambem nao a libera — acesso total abre as FERRAMENTAS do
/// petshop, nunca a autoridade sobre os outros administradores. Um admin comum
/// que chame qualquer rota daqui, pelo navegador ou pelo Postman, leva 403.
///
/// A razao e simples: se administrar administradores fosse uma permissao
/// delegavel, bastaria conceder essa permissao uma vez para que a hierarquia
/// deixasse de existir — o delegado se auto-concederia acesso total no clique
/// seguinte. O topo da hierarquia nao se delega.
///
/// Acima disso valem as travas de integridade: ninguem altera as proprias
/// permissoes (nem o Administrador Geral), ninguem desativa ou exclui a si
/// mesmo, e o ultimo Administrador Geral nao pode ser removido.''',
'cabecalho do controller')

s = trocar(s, '''                if (!ModulosAdmin.Existe(item.Chave)) continue;  // modulo inventado pelo cliente e ignorado''',
'''                if (!ModulosAdmin.Existe(item.Chave)) continue;            // modulo inventado pelo cliente e ignorado
                if (ModulosAdmin.EhExclusivoDoGeral(item.Chave)) continue; // "Usuários Administrativos" nao se delega,
                                                                          // nem com a requisicao forjada pedindo isso''',
'SalvarPermissoes ignora modulo exclusivo')

s = trocar(s, '''                        chave = m.Chave,
                        rotulo = m.Rotulo,
                        grupo = m.Grupo,
                        visualizar = p?.PodeVisualizar ?? false,''',
'''                        chave = m.Chave,
                        rotulo = m.Rotulo,
                        grupo = m.Grupo,
                        somenteGeral = m.SomenteGeral,
                        visualizar = p?.PodeVisualizar ?? false,''',
'LerPermissoes expoe somenteGeral')

gravar('Controllers/UsuariosAdminController.cs', s)


# ===========================================================================
# 3. Middleware — a página só abre para o Administrador Geral
# ===========================================================================
s = ler('Middleware/AdminAreaGuardMiddleware.cs')

s = trocar(s, '''        if (!modulosAceitos.Any(m => contexto.Pode(m, AcaoPermissao.Visualizar)))''',
'''        // Pode() ja devolve false para modulo exclusivo do Geral, entao
        // /usuarios-admin.html cai aqui para qualquer administrador comum —
        // inclusive um com acesso total.
        if (!modulosAceitos.Any(m => contexto.Pode(m, AcaoPermissao.Visualizar)))''',
'comentario do guard da pagina')

gravar('Middleware/AdminAreaGuardMiddleware.cs', s)


# ===========================================================================
# 4. wwwroot/js/admin-permissoes.js — menu só para o Administrador Geral
# ===========================================================================
s = ler('wwwroot/js/admin-permissoes.js')

s = trocar(s, '''  /* A area "Administração > Usuários Administrativos" nao existe no HTML das
     telas antigas. Ela e acrescentada aqui, so para quem tem o modulo, em vez
     de editar o menu de onze paginas na mao. */
  function inserirItemUsuarios(nav) {
    if (!pode('usuarios', 'visualizar')) return;''',
'''  /* A area "Administração > Usuários Administrativos" nao existe no HTML das
     telas antigas. Ela e acrescentada aqui, e SO para o Administrador Geral —
     nem um administrador com acesso total ve este item, porque administrar
     administradores nao e uma ferramenta do petshop, e a autoridade sobre os
     outros administradores. O backend recusa a rota do mesmo jeito. */
  function inserirItemUsuarios(nav) {
    if (!souAdminGeral()) return;''',
'item de menu so para o Geral')

s = trocar(s, '''  function podeVerAlgum(lista) {
    return (lista || []).some(m => pode(m, 'visualizar'));
  }''',
'''  function podeVerAlgum(lista) {
    return (lista || []).some(m => pode(m, 'visualizar'));
  }

  /** Perfil AdminGeral, conforme gravado no banco — nunca deduzido do e-mail. */
  function souAdminGeral() {
    return !!(estado && estado.usuario && estado.usuario.adminGeral);
  }''',
'helper souAdminGeral')

s = trocar(s, '''  function pode(modulo, acao) {
    if (!estado) return false;
    if (estado.acessoIrrestrito) return true;
    const m = estado.modulos && estado.modulos[modulo];
    return !!(m && m[acao || 'visualizar']);
  }''',
'''  function pode(modulo, acao) {
    if (!estado) return false;
    const m = estado.modulos && estado.modulos[modulo];
    // Modulo exclusivo do Administrador Geral: acesso total nao abre.
    if (m && m.somenteGeral) return !!m[acao || 'visualizar'];
    if (estado.acessoIrrestrito) return true;
    return !!(m && m[acao || 'visualizar']);
  }''',
'pode() respeita somenteGeral')

s = trocar(s, '''  window.LanePermissoes = {
    pode,
    podeVerAlgum,''',
'''  window.LanePermissoes = {
    pode,
    podeVerAlgum,
    souAdminGeral,''',
'expor souAdminGeral')

gravar('wwwroot/js/admin-permissoes.js', s)


# ===========================================================================
# 5. wwwroot/js/usuarios-admin.js — botões e modal
# ===========================================================================
s = ler('wwwroot/js/usuarios-admin.js')

s = trocar(s, '''    const botoes = [];
    // Administrador comum nao mexe em Administrador Geral: o backend recusa,
    // e aqui o botao nem aparece.
    if (u.editavel) {''',
'''    const botoes = [];
    // Esta tela inteira e do Administrador Geral. Os testes abaixo cuidam das
    // travas que valem ATE para ele: nao mexer na propria conta, nao editar
    // outro Administrador Geral sem ser um.
    if (u.editavel) {''',
'comentario das acoes')

s = trocar(s, '''    // Conceder permissao e privilegio do Administrador Geral, e ninguem
    // altera as proprias permissoes.
    if (souAdminGeral && !u.adminGeral && !u.ehVoce) {''',
'''    // Ninguem altera as proprias permissoes, e o Administrador Geral ja tem
    // tudo — nao ha o que configurar nele.
    if (souAdminGeral && !u.adminGeral && !u.ehVoce) {''',
'comentario do botao de permissoes')

s = trocar(s, '''  function linhaModulo(m) {
    const caixa = (acao, marcado) =>''',
'''  function linhaModulo(m) {
    // "Usuários Administrativos" aparece na lista, mas travado: e a area do
    // Administrador Geral e nao se delega a ninguem. Mostrar travado explica a
    // regra melhor do que esconder a linha.
    if (m.somenteGeral) {
      return `
        <div style="display:flex;flex-wrap:wrap;gap:var(--sp-3);justify-content:space-between;align-items:center;padding:var(--sp-3) 0;border-bottom:1px solid var(--border);opacity:.55">
          <strong style="min-width:180px">${esc(m.rotulo)}</strong>
          <span class="chip">Exclusivo do Administrador Geral</span>
        </div>`;
    }

    const caixa = (acao, marcado) =>''',
'modal mostra modulo exclusivo travado')

gravar('wwwroot/js/usuarios-admin.js', s)


# ===========================================================================
# 6. testar-permissoes.ps1 — a regra mudou, o teste muda junto
# ===========================================================================
s = ler('testar-permissoes.ps1')

s = trocar(s, '''# mesmo liberando o modulo Usuarios, conceder permissao continua sendo do Geral
Chamar PUT "/admin/usuarios/$idJoao/permissoes" @{
    token = $tokenGeral; acessoTotal = $false
    modulos = @(@{ chave = 'usuarios'; visualizar = $true; criar = $true; editar = $true; excluir = $true })
} | Out-Null
Checar 'com o modulo Usuarios liberado, Joao LE a lista'        ((Chamar GET "/admin/usuarios?token=$tokenJoao" $null).status -eq 200)
Checar 'mas ainda NAO concede permissao a ninguem'              ((Chamar PUT "/admin/usuarios/$idJoao/permissoes" @{ token = $tokenJoao; acessoTotal = $true; modulos = @() }).status -eq 403)''',
'''# A area de Usuarios Administrativos NAO se delega: mesmo que o Administrador
# Geral tente marcar o modulo para Joao, ele continua fora.
Chamar PUT "/admin/usuarios/$idJoao/permissoes" @{
    token = $tokenGeral; acessoTotal = $false
    modulos = @(@{ chave = 'usuarios'; visualizar = $true; criar = $true; editar = $true; excluir = $true })
} | Out-Null
Checar 'o modulo Usuarios nao pode ser concedido -> Joao segue em 403' ((Chamar GET "/admin/usuarios?token=$tokenJoao" $null).status -eq 403)
Checar 'Joao segue sem conceder permissao a ninguem'                   ((Chamar PUT "/admin/usuarios/$idJoao/permissoes" @{ token = $tokenJoao; acessoTotal = $true; modulos = @() }).status -eq 403)

# E nem o acesso total abre essa porta: acesso total libera as ferramentas do
# petshop, nunca a autoridade sobre os outros administradores.
Chamar PUT "/admin/usuarios/$idJoao/permissoes" @{ token = $tokenGeral; acessoTotal = $true; modulos = @() } | Out-Null
Checar 'com ACESSO TOTAL, Joao abre Produtos'                    ((Chamar GET "/admin/produtos?token=$tokenJoao" $null).status -eq 200)
Checar 'com ACESSO TOTAL, Joao AINDA leva 403 em /admin/usuarios' ((Chamar GET "/admin/usuarios?token=$tokenJoao" $null).status -eq 403)
Checar 'com ACESSO TOTAL, Joao AINDA nao cria administrador'      ((Chamar POST '/admin/usuarios' @{ token = $tokenJoao; nome = 'Invasor 2'; email = "invasor2$(Get-Random)@x.com"; senha = 'senha123'; confirmarSenha = 'senha123' }).status -eq 403)
Chamar PUT "/admin/usuarios/$idJoao/permissoes" @{ token = $tokenGeral; acessoTotal = $false; modulos = @() } | Out-Null''',
'teste: modulo Usuarios nao delegavel nem com acesso total')

gravar('testar-permissoes.ps1', s)


# ===========================================================================
print()
print('=' * 62)
for f in feitos:
    print('  [ok]    ' + f)
for e in erros:
    print('  [ERRO]  ' + e)
print('=' * 62)
if erros:
    print('\nAlgumas alterações não foram aplicadas. Nada foi corrompido —')
    print('os arquivos sem a marcação esperada ficaram como estavam.')
    sys.exit(1)
print('\nTudo aplicado. Agora rode:  dotnet run')
