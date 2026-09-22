LanPets — loja online de pet shop em ASP.NET Core MVC (C#)

Status do projeto:
- Loja funcional com catálogo, carrinho, checkout, confirmação de pedido e painel administrativo.
- Nome da marca final: LanPets.
- Código original em PHP mantido como referência/backup e não utilizado como app ativo.

Como rodar localmente:
1. Abra a solução no Visual Studio ou rode diretamente a pasta do projeto.
2. Na pasta do projeto, execute:
   dotnet run --urls http://localhost:8080
3. Acesse: http://localhost:8080

Se preferir rodar pelo Visual Studio:
1. Abra o arquivo .sln.
2. Selecione o projeto PetshopCSharp.
3. Execute com F5.
4. O app será servido em http://localhost:8080 conforme o profile configurado.

Credenciais padrão do admin:
- email: admin@petshop.com
- senha: 123456

Funcionalidades principais:
- Catálogo de produtos com busca.
- Detalhes do produto e disponibilidade.
- Carrinho de compras com sessão.
- Checkout com nome, telefone, endereço, observação e pagamento.
- Confirmação de pedido.
- Histórico de pedidos do cliente.
- Painel administrativo para controlar produtos, status e agendamentos.
- Banco SQLite com seed inicial.

Estrutura relevante:
- Solução: C:/Users/fabri/Downloads/Pagina PetShop/PetshopCSharp.sln
- Projeto: C:/Users/fabri/Downloads/Pagina PetShop/PetshopCSharp/PetshopCSharp.csproj
- Banco: C:/Users/fabri/Downloads/Pagina PetShop/PetshopCSharp/petshop.db
- Views: C:/Users/fabri/Downloads/Pagina PetShop/PetshopCSharp/Views/
- CSS principal: C:/Users/fabri/Downloads/Pagina PetShop/PetshopCSharp/wwwroot/css/site.css

Produção / Docker:
1. Publicar:
   dotnet publish PetshopCSharp.csproj -c Release -o ./publish
2. Build da imagem Docker:
   docker build -t lanpets .
3. Rodar o container:
   docker run -p 8080:8080 lanpets

Observações finais:
- O projeto está preparado para apresentação, demonstração e uso local.
- O perfil de desenvolvimento e produção já foi configurado para facilitar execução e publicação.
- Em ambiente Windows, pode ocorrer bloqueio do arquivo .dll em pastas protegidas como Downloads; neste caso, rode a aplicação em um diretório confiável, como C:/Temp/LanPetsApp.
