using System.Text.Json;
using LanePets.Data;
using LanePets.Models;
using LanePets.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LanePets.Controllers;
[Route("api")]
public class DataController(LanePetsDbContext db, PermissaoService permissoes) : ApiControllerBase
{
    /// <summary>
    /// Antes este metodo so perguntava "existe sessao de admin?". Agora ele
    /// pergunta "este admin pode VER este modulo?" e, quando nao pode, a
    /// requisicao morre aqui com 403 — inclusive quando chamada direto na URL,
    /// sem passar pela tela. Cada rota abaixo diz de qual modulo depende.
    /// </summary>
    private async Task ExigirAsync(string token, string modulo)
        => await permissoes.ExigirAsync(token, modulo, AcaoPermissao.Visualizar);
    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard([FromQuery] string token="", [FromQuery] string? unidade=null, [FromQuery] string? data=null)
    { try { await ExigirAsync(token, ModulosAdmin.Dashboard); var ags=await db.Agendamentos.AsNoTracking().ToListAsync(); var u=NormUnit(unidade); var date=data??DateTime.Now.ToString("yyyy-MM-dd"); var baseRows=ags.Where(a=>string.IsNullOrEmpty(u)||NormUnit(a.Unidade)==u).ToList(); Func<Agendamento,bool> today=a=>DateOnly(a.DataHora)==date; var metrics=new { total=baseRows.Count, hoje=baseRows.Count(today), pendentes=baseRows.Count(a=>NormStatus(a.Status)=="Pendente"), emAndamento=baseRows.Count(a=>NormStatus(a.Status)=="Em andamento"), entregues=baseRows.Count(a=>NormStatus(a.Status)=="Entregue"), cancelados=baseRows.Count(a=>NormStatus(a.Status)=="Cancelado")}; var porUnidade=new Dictionary<string,object>(); foreach(var unit in new[]{"Franco","Caieiras"}) {var x=ags.Where(a=>NormUnit(a.Unidade)==unit).ToList(); porUnidade[unit]=new {total=x.Count,hoje=x.Count(today),pendentes=x.Count(a=>NormStatus(a.Status)=="Pendente"),emAndamento=x.Count(a=>NormStatus(a.Status)=="Em andamento"),entregues=x.Count(a=>NormStatus(a.Status)=="Entregue"),cancelados=x.Count(a=>NormStatus(a.Status)=="Cancelado")};} return OkApi(new {ok=true,data=date,unidade=string.IsNullOrEmpty(u)?null:u,metrics,porUnidade,semUnidade=ags.Count(a=>string.IsNullOrWhiteSpace(a.Unidade))}); } catch(Exception ex){return ErrorApi(ex);} }

    [HttpGet("metricas")] public Task<IActionResult> Metricas(string token="",string? unidade=null,string? data=null)=>Dashboard(token,unidade,data);
    [HttpGet("agendamentos")]
    public async Task<IActionResult> Agendamentos(string token="",string? unidade=null,string? status=null,string? data=null,string? de=null,string? ate=null,string? busca=null,int limite=500,int offset=0)
    { try { await ExigirAsync(token, ModulosAdmin.Agendamentos); var q=await db.Agendamentos.AsNoTracking().ToListAsync(); var u=NormUnit(unidade); var s=NormStatus(status); q=q.Where(a=>string.IsNullOrEmpty(u)||NormUnit(a.Unidade)==u).Where(a=>string.IsNullOrEmpty(s)||NormStatus(a.Status)==s).Where(a=>string.IsNullOrEmpty(data)||DateOnly(a.DataHora)==data).Where(a=>string.IsNullOrEmpty(de)||string.CompareOrdinal(DateOnly(a.DataHora), de) >= 0).Where(a=>string.IsNullOrEmpty(ate)||string.CompareOrdinal(DateOnly(a.DataHora), ate) <= 0).Where(a=>string.IsNullOrWhiteSpace(busca)||new[]{a.Dono,a.Pet,a.Telefone,a.Id}.Any(v=>NormText(v).Contains(NormText(busca)))).OrderBy(a=>a.DataHora).ToList(); var total=q.Count; limite=Math.Clamp(limite,1,2000); offset=Math.Max(offset,0); var page=q.Skip(offset).Take(limite).Select(ToAppointment); return OkApi(new {ok=true,total,offset,limite,hasMore=offset+limite<total,agendamentos=page}); } catch(Exception ex){return ErrorApi(ex);} }
    [HttpGet("appointments")] public Task<IActionResult> Appointments(string token="",string? unidade=null,string? status=null,string? data=null,string? de=null,string? ate=null,string? busca=null,int limite=500,int offset=0)=>Agendamentos(token,unidade,status,data,de,ate,busca,limite,offset);

    [HttpGet("pets")] public Task<IActionResult> Pets(string token="",string? busca=null,int limite=500,int offset=0)=>List(token, ModulosAdmin.Pets, busca, limite, offset, async()=>await db.Pets.AsNoTracking().ToListAsync(), p=>new {id=p.Id,dono=p.Dono,pet=p.PetNome,tipo=p.Tipo,raca=p.Raca,telefone=p.Telefone,endereco=p.Endereco,pacote_json=p.PacoteJson,unidade=p.Unidade,cliente_id=p.ClienteId});
    [HttpGet("clientes")] public Task<IActionResult> Clientes(string token="",string? busca=null,int limite=500,int offset=0)=>List(token,ModulosAdmin.Clientes,busca,limite,offset,async()=>await db.Clientes.AsNoTracking().ToListAsync(),c=>new{id=c.Id,nome=c.Nome,telefone=c.Telefone,endereco=c.Endereco,observacoes=c.Observacoes,origem=c.Origem,status=c.Status});
    [HttpGet("servicos")] public Task<IActionResult> Servicos(string token="",string? busca=null,int limite=500,int offset=0)=>List(token,ModulosAdmin.Servicos,busca,limite,offset,async()=>await db.Servicos.AsNoTracking().ToListAsync(),s=>new{id=s.Id,nome=s.Nome,preco=s.Preco,porte=s.Porte,adicionais_json=s.AdicionaisJson,pacote=s.Pacote,adicional=s.Adicional});
    [HttpGet("pedidos")]
    public async Task<IActionResult> Pedidos(string token="") { try { await ExigirAsync(token, ModulosAdmin.Pedidos); return OkApi(await db.Pedidos.AsNoTracking().OrderByDescending(p => p.CriadoEm).ToListAsync()); } catch(Exception ex) { return ErrorApi(ex); } }
    [HttpGet("unidades")] public async Task<IActionResult> Unidades(string token=""){try{await permissoes.ResolverAsync(token);return OkApi(new{unidades=new[]{"Franco","Caieiras"}});}catch(Exception ex){return ErrorApi(ex);}}

    private async Task<IActionResult> List<T>(string token,string modulo,string? busca,int limite,int offset,Func<Task<List<T>>> load,Func<T,object> map){try{await ExigirAsync(token, modulo);var rows=await load();var b=NormText(busca);var items=rows.Where(x=>string.IsNullOrEmpty(b)||JsonSerializer.Serialize(x).ToLowerInvariant().Contains(b)).ToList();var total=items.Count;limite=Math.Clamp(limite,1,2000);offset=Math.Max(0,offset);return OkApi(new{ok=true,total,offset,limite,items=items.Skip(offset).Take(limite).Select(map)});}catch(Exception ex){return ErrorApi(ex);}}
    private static object ToAppointment(Agendamento a)=>new{id=a.Id,pet=a.Pet,dono=a.Dono,telefone=a.Telefone,dataHora=DateTime.TryParse(a.DataHora,out var d)?d.ToUniversalTime().ToString("O"):a.DataHora,servicos=Parse(a.ServicosJson),total=a.Total,transporte=a.Transporte,valorTransporte=a.ValorTransporte,status=a.Status,statusCanonico=NormStatus(a.Status),pagamentoStatus=a.PagamentoStatus,formaPagamento=a.FormaPagamento,obs=a.Obs,unidade=NormUnit(a.Unidade),cliente_id=a.ClienteId,pet_id=a.PetId};
    private static object Parse(string s){try{return JsonSerializer.Deserialize<JsonElement>(s);}catch{return Array.Empty<object>();}}
    /* As regras de normalizacao agora vivem em Services/Normalizador.cs para que
       o painel e a area do cliente contem exatamente da mesma forma. Os nomes
       antigos continuam aqui como atalho: nada mais neste arquivo mudou. */
    private static string NormText(string? s)=>Normalizador.Texto(s);
    private static string NormUnit(string? s)=>Normalizador.Unidade(s);
    private static string NormStatus(string? s)=>Normalizador.Status(s);
    private static string DateOnly(string? s)=>Normalizador.Data(s);
}
