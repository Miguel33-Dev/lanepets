🐾 LanePets

[![LanePets CI](https://github.com/Miguel33-Dev/lanpets/actions/workflows/dotnet-desktop.yml/badge.svg)](https://github.com/Miguel33-Dev/lanpets/actions/workflows/dotnet-desktop.yml)

Sistema de gerenciamento para pet shop desenvolvido para centralizar o atendimento aos clientes, gerenciamento de produtos, pets, agendamentos e operações administrativas.

O projeto possui uma área pública para os clientes, autenticação de usuários e uma área administrativa para gerenciamento das informações do sistema.

⸻

📌 Sobre o projeto

O LanePets foi desenvolvido com o objetivo de criar uma plataforma completa para gerenciamento de um pet shop.

O sistema permite que clientes criem suas contas, cadastrem seus pets, realizem agendamentos e acompanhem suas informações.

A área administrativa permite que os responsáveis pelo sistema acompanhem e gerenciem os dados cadastrados pelos clientes, produtos, pedidos e agendamentos.

O projeto está sendo desenvolvido utilizando C# e ASP.NET Core, com banco de dados SQLite e arquitetura organizada para facilitar a manutenção e evolução do sistema.

⸻

🚀 Funcionalidades

👤 Área pública

A área pública permite que qualquer visitante tenha acesso às principais informações do LanePets.

Funcionalidades:

* Página inicial do pet shop
* Apresentação dos serviços
* Apresentação dos produtos
* Informações sobre seguro para pets
* Área de comentários
* Acesso ao login do cliente
* Acesso ao login administrativo
* Acesso ao cadastro de cliente
* Acesso ao agendamento

⸻

🐶 Área do cliente

Após criar uma conta e realizar o login, o cliente poderá utilizar as funcionalidades destinadas aos usuários.

Funcionalidades planejadas/implementadas:

* Cadastro de cliente
* Login e logout
* Gerenciamento da conta
* Cadastro de pets
* Visualização dos pets cadastrados
* Agendamento de serviços
* Escolha da unidade
* Escolha do serviço
* Escolha de data e horário
* Escolha entre retirada ou entrega do pet
* Forma de pagamento
* Visualização dos agendamentos
* Acompanhamento dos pedidos
* Visualização dos produtos disponíveis
* Contratação/visualização de seguro para pets

⸻

🛠️ Área administrativa

A área administrativa é responsável pelo gerenciamento das informações do sistema.

O administrador poderá visualizar e gerenciar:

👥 Clientes

* Visualizar clientes cadastrados
* Consultar informações dos clientes
* Acompanhar contas criadas no sistema
* Visualizar pets associados aos clientes

Sempre que um novo cliente criar uma conta, suas informações deverão aparecer automaticamente na área administrativa.

📅 Agendamentos

O administrador poderá:

* Visualizar novos agendamentos
* Consultar cliente responsável pelo agendamento
* Consultar pet
* Visualizar serviço escolhido
* Visualizar unidade
* Visualizar data e horário
* Visualizar forma de pagamento
* Visualizar opção de retirada ou entrega
* Atualizar o status do atendimento

Quando um cliente criar um novo agendamento, a informação deverá ser refletida automaticamente no painel administrativo.

📦 Produtos

O administrador poderá:

* Cadastrar produtos
* Editar produtos
* Excluir produtos
* Visualizar produtos cadastrados
* Controlar informações dos produtos
* Disponibilizar produtos para a área pública e área do cliente

Todo produto cadastrado no painel administrativo deverá ficar disponível automaticamente nas áreas correspondentes do sistema.

🏪 Unidades

O sistema poderá trabalhar com diferentes unidades do pet shop.

Cada unidade poderá possuir informações próprias, permitindo organizar:

* Atendimentos
* Agendamentos
* Serviços
* Pedidos
* Clientes

⸻

🔄 Sincronização entre Cliente e Administrador

Um dos principais objetivos do LanePets é manter os dados sincronizados entre as áreas do sistema.

Exemplo:

Cliente cria uma conta:

Cliente
   ↓
Cadastro
   ↓
Banco de dados
   ↓
Painel Administrativo
   ↓
Novo cliente aparece automaticamente

Cliente realiza um agendamento:

Cliente
   ↓
Novo agendamento
   ↓
Banco de dados
   ↓
Painel Administrativo
   ↓
Administrador visualiza o agendamento

Administrador cadastra um produto:

Administrador
   ↓
Novo produto
   ↓
Banco de dados
   ↓
Área pública
   ↓
Área do cliente

Dessa forma, as diferentes áreas do sistema trabalham utilizando a mesma fonte de dados.

⸻

🔐 Autenticação

O sistema possui autenticação para controlar o acesso às áreas restritas.

Existem diferentes níveis de acesso:

Cliente

Acesso às funcionalidades relacionadas à sua própria conta.

Administrador

Acesso ao painel administrativo e gerenciamento das informações do sistema.

A autenticação utiliza controle de sessão e proteção de senha.

As senhas dos usuários são armazenadas utilizando hash, evitando que sejam salvas diretamente em texto puro.

⸻

🏗️ Tecnologias utilizadas

Backend

* C#
* ASP.NET Core
* Entity Framework Core
* ASP.NET Core MVC
* Razor Pages/Views

Banco de dados

* SQLite
* Entity Framework Core

Segurança

* BCrypt
* Sessões
* Autenticação
* Controle de acesso por perfil

Desenvolvimento

* Visual Studio / Visual Studio Code
* .NET SDK
* Git
* GitHub

⸻

📁 Estrutura do projeto

Estrutura principal:

LanePetsCSharp/
│
├── Controllers/
│   ├── AccountController.cs
│   ├── AdminController.cs
│   └── ...
│
├── Models/
│   ├── Cliente.cs
│   ├── Pet.cs
│   ├── Produto.cs
│   ├── Pedido.cs
│   ├── Agendamento.cs
│   └── ...
│
├── Data/
│   ├── ApplicationDbContext.cs
│   └── ...
│
├── Views/
│   ├── Account/
│   ├── Admin/
│   ├── Home/
│   └── ...
│
├── wwwroot/
│   ├── css/
│   ├── js/
│   ├── images/
│   └── ...
│
├── Properties/
│   └── launchSettings.json
│
├── Program.cs
├── appsettings.json
├── LanePets.csproj
└── petshop.db

A estrutura pode sofrer alterações conforme novas funcionalidades forem adicionadas ao projeto.

⸻

🗄️ Banco de dados

O projeto utiliza SQLite.

Banco:

petshop.db

A comunicação com o banco é realizada através do Entity Framework Core.

Exemplo de configuração:

Data Source=petshop.db

O banco é responsável por armazenar informações como:

* Usuários
* Clientes
* Pets
* Produtos
* Pedidos
* Agendamentos
* Serviços
* Unidades
* Configurações

⸻

🔑 Segurança

O LanePets utiliza mecanismos de segurança para proteger as informações dos usuários.

Entre eles:

* Hash de senhas
* BCrypt
* Controle de sessão
* Separação entre cliente e administrador
* Proteção de páginas administrativas
* Validação de dados
* Controle de acesso

O objetivo é impedir que usuários comuns tenham acesso às funcionalidades administrativas.

⸻

▶️ Como executar o projeto

1. Clonar o repositório

git clone https://github.com/Miguel33-Dev/LanePetsCSharp.git

Depois entre na pasta:

cd LanePetsCSharp

⸻

2. Restaurar as dependências

Execute:

dotnet restore

⸻

3. Compilar o projeto

dotnet build

⸻

4. Executar o projeto

dotnet run

Ou, caso o perfil LanePets esteja configurado:

dotnet run --launch-profile LanePets

⸻

5. Acessar no navegador

O projeto pode ser executado localmente através do endereço configurado no launchSettings.json.

Exemplo:

http://localhost:5180

⸻

🧪 Testes automatizados

O projeto tem testes automatizados em tests/LanePets.Tests (xUnit + WebApplicationFactory).

Eles sobem a aplicação inteira em memória contra um banco SQLite temporário — o banco em uso nunca é tocado — e cobrem:

* Login administrativo e cadastro/login do cliente
* Clientes e produtos no painel, com o livro de estoque
* Agendamentos (capacidade por horário, status, cancelamento)
* Pedidos (baixa e devolução de estoque) e pagamentos (transições e reembolso)
* Permissões (403 para quem não tem o módulo ou a ação)

Para rodar, de dentro da pasta LanePetsCSharp (não precisa do dotnet run de pé):

dotnet test tests/LanePets.Tests

A cada push no master o GitHub Actions compila o projeto e roda os mesmos testes (selo no topo deste arquivo).

⸻

⚙️ Configuração

Antes de executar o projeto, confira:

* .NET SDK instalado
* Banco SQLite configurado
* Connection String configurada
* Dependências restauradas
* Perfil de execução configurado
* Permissões de acesso às páginas administrativas

⸻

🧪 Testes

Durante o desenvolvimento, recomenda-se testar os principais fluxos:

Cadastro

Criar conta
   ↓
Validar dados
   ↓
Salvar no banco
   ↓
Cliente aparece no Admin

Login

Informar usuário
   ↓
Validar senha
   ↓
Criar sessão
   ↓
Redirecionar para área correspondente

Agendamento

Cliente
   ↓
Escolhe serviço
   ↓
Escolhe unidade
   ↓
Escolhe data/horário
   ↓
Confirma
   ↓
Salva no banco
   ↓
Aparece no Admin

Produto

Administrador
   ↓
Cadastra produto
   ↓
Salva no banco
   ↓
Produto aparece no sistema

⸻

📊 Fluxo geral do sistema

                         ┌─────────────────┐
                         │    LanePets     │
                         └────────┬────────┘
                                  │
                 ┌────────────────┴────────────────┐
                 │                                 │
          ┌──────▼──────┐                   ┌──────▼──────┐
          │   Cliente   │                   │Administrador │
          └──────┬──────┘                   └──────┬──────┘
                 │                                 │
        ┌────────▼────────┐               ┌────────▼────────┐
        │ Cadastro/Login  │               │  Login Admin    │
        └────────┬────────┘               └────────┬────────┘
                 │                                 │
        ┌────────▼────────┐               ┌────────▼────────┐
        │     Pets       │               │    Clientes     │
        │  Agendamentos  │               │    Produtos     │
        │    Pedidos     │               │  Agendamentos   │
        └────────┬────────┘               │    Pedidos      │
                 │                        └────────┬────────┘
                 │                                 │
                 └────────────┬────────────────────┘
                              │
                       ┌──────▼──────┐
                       │   SQLite    │
                       │  petshop.db │
                       └─────────────┘

⸻

🎯 Objetivos do projeto

O LanePets tem como principais objetivos:

* Digitalizar o gerenciamento do pet shop
* Facilitar o atendimento aos clientes
* Centralizar informações
* Reduzir processos manuais
* Organizar agendamentos
* Facilitar o gerenciamento de produtos
* Centralizar clientes e pets
* Criar uma experiência integrada entre cliente e administrador
* Aplicar conhecimentos de desenvolvimento de software
* Evoluir a aplicação utilizando boas práticas de programação

⸻

🔮 Próximas melhorias

Possíveis melhorias futuras:

* [ ]	Dashboard administrativo completo
* [ ]	Gráficos de vendas
* [ ]	Relatórios financeiros
* [ ]	Controle de estoque
* [ ]	Sistema de notificações
* [ ]	Confirmação automática de agendamento
* [ ]	Recuperação de senha
* [ ]	E-mail de confirmação
* [ ]	Integração com pagamentos
* [ ]	API REST
* [x]	Testes automatizados
* [ ]	Docker
* [ ]	Deploy em ambiente cloud
* [ ]	Melhorias de segurança
* [ ]	Sistema de permissões administrativas
* [ ]	Logs do sistema
* [ ]	Backup do banco de dados

⸻

🧑‍💻 Autor

Fabrício Miguel da Silva

Desenvolvedor com foco em Backend e Engenharia de Software.

Tecnologias de interesse

* C#
* Java
* JavaScript
* Node.js
* PHP
* SQL
* MySQL
* PostgreSQL
* MongoDB
* Docker
* Git
* GitHub

⸻

📄 Licença

Este projeto foi desenvolvido para fins acadêmicos, de aprendizado e desenvolvimento profissional.

⸻

🚧 Status do projeto

Em desenvolvimento 🚧

O LanePets está em constante evolução. Novas funcionalidades, correções e melhorias podem ser adicionadas ao projeto conforme seu desenvolvimento avança.