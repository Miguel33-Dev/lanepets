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

O projeto tem 153 testes automatizados em tests/LanePets.Tests (xUnit + WebApplicationFactory).

Eles sobem a aplicação inteira em memória contra um banco SQLite temporário — o banco em uso nunca é tocado — e cobrem:

* Login administrativo e cadastro/login do cliente
* Clientes e produtos no painel, com o livro de estoque
* Agendamentos (capacidade por horário, status, cancelamento)
* Pedidos (baixa e devolução de estoque) e pagamentos (transições e reembolso)
* Permissões (403 para quem não tem o módulo ou a ação)
* Listas paginadas e filtradas no servidor (Pedidos, Clientes, Pets, Agendamentos e Pagamentos)
* Cobrança mensal do Seguro Pet, importação de dados, dashboard e relatório financeiro

Para rodar, de dentro da pasta LanePetsCSharp (não precisa do dotnet run de pé):

dotnet test tests/LanePets.Tests

A cada push no master o GitHub Actions compila o projeto e roda os mesmos testes (selo no topo deste arquivo).

⸻

✉️ E-mail (recuperação de senha e avisos)

O sistema manda e-mail para o tutor que tem conta no site:

* Código de 6 dígitos do "Esqueceu sua senha?" (vale 15 minutos, uso único, 5 tentativas).
* Boas-vindas ao criar a conta e "recebemos o agendamento" quando o cliente agenda.
* Agendamento confirmado ou cancelado pela equipe no painel e lembrete na véspera (a partir das 9h) — o cliente pode desligar estes em Minha Conta > Avisos por e-mail.

Sem SMTP configurado, nada sai para a internet: cada e-mail vira um arquivo .txt na pasta emails, ao lado do banco (Windows: %LOCALAPPDATA%\LanePets\emails) — é a caixa de saída de desenvolvimento.

Para chegar na caixa de entrada de verdade (exemplo com Gmail):

1. Na conta Google, ative a verificação em duas etapas e crie uma senha de app (myaccount.google.com/apppasswords).
2. Crie o arquivo appsettings.Development.json na pasta do projeto (ele está no .gitignore — a senha nunca vai para o GitHub):

{
  "LanePets": {
    "Email": {
      "SmtpHost": "smtp.gmail.com",
      "SmtpPorta": "587",
      "SmtpUsuario": "seu.email@gmail.com",
      "SmtpSenha": "senha de app de 16 letras",
      "Remetente": "LanePets <seu.email@gmail.com>"
    }
  }
}

3. Reinicie o sistema. No console aparece "enviado por SMTP" a cada e-mail. Os testes automatizados ignoram essa configuração e nunca mandam e-mail de verdade.

Em produção, use variáveis de ambiente (LanePets__Email__SmtpHost, LanePets__Email__SmtpPorta, LanePets__Email__SmtpUsuario, LanePets__Email__SmtpSenha, LanePets__Email__Remetente) e, opcional, LanePets__UrlPublica para o link da área do cliente no rodapé.

⸻

🌐 Colocar no ar (Railway, com Docker)

O Dockerfile já está pronto: o Railway (ou qualquer serviço que rode Docker) compila e sobe o LanePets sozinho a partir do GitHub.

1. Faça o push do projeto para o GitHub.
2. Em railway.com, crie uma conta, clique em New Project → Deploy from GitHub repo e escolha o repositório LanePets.
3. No serviço criado, abra Settings → Volumes (ou clique com o botão direito no serviço → Attach volume) e monte um volume em /app/data. É lá que ficam o banco, os backups, os e-mails .txt e os logs — sem o volume, os dados somem a cada deploy.
4. Em Variables, cadastre:

LanePets__AdminSenhaInicial=uma senha forte (8+ com letra e número) para o admin@gmail.com
LanePets__FinancePassword=a senha da área financeira
LanePets__UrlPublica=https://seu-endereco.up.railway.app (opcional: link nos e-mails)

   E-mail de verdade: as mesmas chaves do SMTP da seção de e-mail (LanePets__Email__SmtpHost, ...).
5. Em Settings → Networking, clique em Generate Domain. Pronto: o site abre nesse endereço com HTTPS.

Regras de segurança da hospedagem:

* Em produção o LanePets não sobe com as senhas de exemplo (123456): o log mostra quais variáveis faltam.
* A dica "Credenciais de teste" da tela de login do painel só aparece no modo demonstração.
* Quer uma vitrine em que qualquer pessoa entre com admin@gmail.com / 123456? Use LanePets__Demo=true — mas lembre que o visitante vira Administrador Geral.
* O appsettings.Development.json (com a sua senha de app do Gmail) nunca entra na imagem (.dockerignore).
* Vitrine para recrutadores: LanePets__Visitante=true mostra na tela de login do painel o botão "Entrar como visitante (só leitura)". O visitante navega por todas as telas do petshop, mas nada é gravado (o servidor responde 403), e Usuários, Log de eventos e Configurações ficam fechados. Ele vê os dados do painel: ligue só com dados de exemplo.

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

* [x]	Dashboard administrativo completo
* [x]	Gráficos de vendas
* [x]	Relatórios financeiros
* [x]	Controle de estoque
* [ ]	Sistema de notificações
* [ ]	Confirmação automática de agendamento
* [x]	Recuperação de senha (código por e-mail, 15 minutos, uso único)
* [ ]	E-mail de confirmação
* [x]	Controle de pagamentos (status, reembolso, conciliação)
* [ ]	Integração com gateway de pagamento
* [x]	API REST
* [x]	Testes automatizados
* [x]	Docker
* [ ]	Deploy em ambiente cloud
* [x]	Melhorias de segurança
* [x]	Sistema de permissões administrativas
* [x]	Logs do sistema
* [x]	Backup do banco de dados
* [x]	Paginação e filtros no servidor (o painel não baixa mais o banco inteiro)

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