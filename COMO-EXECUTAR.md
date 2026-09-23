# Executar o LanePets

O LanePets e uma unica aplicacao ASP.NET Core: ela fornece o frontend e a API.
Nao abra os arquivos HTML por duplo clique, pois nesse modo nao existe API para atender `/api/*`.

## Inicio rapido

No PowerShell, dentro desta pasta, execute:

```powershell
./INICIAR-LANEPETS.ps1
```

Ou execute:

```powershell
dotnet run --launch-profile LanePets
```

Abra a URL mostrada no terminal. O perfil de desenvolvimento atual define `http://localhost:5180` e abre o navegador automaticamente.

## Credenciais administrativas iniciais

- E-mail: `admin@gmail.com`
- Senha: `123456`

O login do administrador fica em `/admin-login.html`; apos autenticar, o sistema encaminha para `/index.html`.
