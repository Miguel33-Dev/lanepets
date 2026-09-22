using LanePets.Data;
using LanePets.Models;
using LanePets.Services;
using Microsoft.AspNetCore.Mvc;
namespace LanePets.Controllers;
[Route("api")]
public class TestWriteController(LanePetsDbContext db, SessionService sessions) : ApiControllerBase
{
    [HttpPost("teste_criar")]
    public async Task<IActionResult> Create([FromBody] TestBody b) => await Execute(b, "teste_criar");
    [HttpPost("teste_atualizar")]
    public async Task<IActionResult> Update([FromBody] TestBody b) => await Execute(b, "teste_atualizar");
    [HttpPost("teste_excluir")]
    public async Task<IActionResult> Delete([FromBody] TestBody b) => await Execute(b, "teste_excluir");
    private async Task<IActionResult> Execute(TestBody b,string action){try{sessions.RequireAdmin(b.Token??"");if(b.Confirmacao!="LANE_PETS_TESTE_4A")throw new Exception("Escrita bloqueada. Informe confirmacao=LANE_PETS_TESTE_4A para o teste controlado.");if(action=="teste_criar"){var id="TEST4A-"+Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();var a=new Agendamento{Id=id,Pet="TESTE API 4A",Dono="TESTE API 4A",Telefone="0000000000",DataHora=b.DataHora??"2099-12-31T12:00:00-03:00",ServicosJson="[]",Status=Canon(b.Status??"Pendente"),Unidade=Unit(b.Unidade??"Franco"),Obs="REGISTRO DE TESTE — ETAPA 4A"};db.Agendamentos.Add(a);await db.SaveChangesAsync();return OkApi(new{ok=true,action,id,entidade="agendamentos",unidade=a.Unidade,status=a.Status,message="Registro de teste criado com sucesso. Nenhum registro real foi alterado."});}var idReq=(b.Id??"").Trim().ToUpperInvariant();if(!idReq.StartsWith("TEST4A-"))throw new Exception("Operação bloqueada: somente IDs TEST4A-* podem ser alterados nesta etapa.");var a2=await db.Agendamentos.FindAsync(idReq);if(a2 is null)throw new Exception("Registro de teste não encontrado: "+idReq);if(action=="teste_atualizar"){a2.Dono="TESTE API 4A ATUALIZADO";a2.Pet="TESTE API 4A ATUALIZADO";a2.Status=Canon(b.Status??"Em andamento");a2.Unidade=Unit(b.Unidade??"Caieiras");a2.Obs="REGISTRO DE TESTE — ETAPA 4A — ATUALIZADO";await db.SaveChangesAsync();return OkApi(new{ok=true,action,id=idReq,alteracoes=new{dono=a2.Dono,pet=a2.Pet,status=a2.Status,unidade=a2.Unidade,obs=a2.Obs},message="Registro de teste atualizado com sucesso. Nenhum registro real foi alterado."});}db.Agendamentos.Remove(a2);await db.SaveChangesAsync();return OkApi(new{ok=true,action,id=idReq,message="Registro de teste excluído com sucesso. Nenhum registro real foi alterado."});}catch(Exception ex){return ErrorApi(ex);}}
    private static string Unit(string s)=>s.Trim().ToLowerInvariant() switch {"franco"=>"Franco","caieiras"=>"Caieiras",_=>"Franco"};
    private static string Canon(string s)=>s.Trim().ToLowerInvariant() switch {"pendente" or "aguardando" or "agendado"=>"Pendente","em andamento" or "em_andamento"=>"Em andamento","entregue" or "finalizado" or "concluido"=>"Entregue","cancelado"=>"Cancelado",_=>"Pendente"};
    public record TestBody(string? Token,string? Confirmacao,string? Id,string? DataHora,string? Status,string? Unidade);
}
