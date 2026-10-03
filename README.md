# ShopWebApp

![.NET](https://img.shields.io/badge/ASP.NET_Core-8-512BD4?logo=dotnet&logoColor=white)
![OpenID Connect](https://img.shields.io/badge/OpenID_Connect-PKCE-F78C40?logo=openid&logoColor=white)
![SQL Server](https://img.shields.io/badge/SQL_Server-CC2927?logo=microsoftsqlserver&logoColor=white)

The web application of an SSO project built during my internship at Mobilis. It follows the **Backend-for-Frontend (BFF)** pattern: the browser only holds a session cookie and never sees a token. Users sign in through [ShopAuth](https://github.com/adelmerad/ShopAuth), the OAuth2 / OpenID Connect server.

- **`ClientApi/`** — the BFF: sign-in, session and token handling, the product and category API, and static file hosting for the frontend
- **`sso-client/`** — the frontend, in plain HTML, CSS and JavaScript. It handles no tokens and only makes `fetch()` calls to `ClientApi`

The interface is in French.

## 🏗️ Architecture

```
Browser ──session cookie──▶ ClientApi (:5200) ──serves──▶ sso-client (static files)
                                 │
                                 ├──OIDC sign-in, token refresh──▶ ShopAuth (:5124)
                                 └──Entity Framework Core──▶ ShopDb (SQL Server)
```

## ✨ Features

- **Hand-written OIDC flow:** Authorization Code with PKCE (S256) and `state`, without the `AddOpenIdConnect` middleware
- **Real `id_token` validation:** signature checked against the server's JWKS keys, plus issuer, audience and expiry
- **Tokens stay on the server:** the HttpOnly cookie only carries an opaque session id
- **Proactive token refresh:** the access token is renewed before it expires, with a per-session lock because refresh tokens are rotated
- **Resilient sessions:** a dead refresh token ends the session, while a temporary network or SSO failure doesn't sign the user out
- **Role-based access:** `admin` and `admin-ou-employe` policies from the `role` claims issued by ShopAuth
- **Single logout:** signing out also closes the session on ShopAuth (RP-initiated logout)

## 🔐 Sign-In Flow

1. `GET /auth/login` generates `code_verifier`, `code_challenge` and `state`, stores them in short-lived HttpOnly cookies, and redirects to `{Sso:Authority}/connect/authorize`
2. `GET /auth/callback` checks `state`, exchanges the code for tokens, validates the `id_token`, stores the tokens on the server, and sets the session cookie
3. On each authenticated request, a middleware refreshes the access token if it expires within 60 seconds
4. `GET /auth/logout` removes the session, then redirects to `{Sso:Authority}/connect/logout`

## 🔌 Endpoints

| Method | Route | Access |
|--------|-------|--------|
| GET | `/`, `/style.css`, `/app.js` | Public |
| GET | `/auth/login`, `/auth/callback` | Public |
| GET | `/auth/me` | Signed in (401 otherwise) |
| GET | `/auth/logout` | Public |
| GET | `/api/products`, `/api/products/{id}`, `/api/categories` | Public |
| POST | `/api/products`, `/api/categories` | `admin` or `employe` role |
| PUT | `/api/products/{id}` | `admin` or `employe` role |
| DELETE | `/api/products/{id}`, `/api/categories/{id}` | `admin` role |

## 🚀 Getting Started

Requirements:

- .NET 8 SDK
- SQL Server with a `ShopDb` database
- [ShopAuth](https://github.com/adelmerad/ShopAuth) running on `http://localhost:5124`, with a client that allows the redirect `http://localhost:5200/auth/callback`

```powershell
cd ClientApi
dotnet restore
Copy-Item appsettings.Example.json appsettings.json   # then fill in Sso and ConnectionStrings
dotnet ef database update
dotnet run
```

Then open http://localhost:5200. The frontend needs no build step.

Changing `Authority`, `ClientId`, `ClientSecret` and `RedirectUri` in `appsettings.json` is enough to switch to another SSO server.

## ⚠️ Notes

- Tokens are held in server memory, so sessions are lost when the app restarts. Running several instances would need a shared store
- OIDC metadata over HTTP is allowed for local development only and must use HTTPS in production

## 👤 Author

**Adel Merad** — [GitHub](https://github.com/adelmerad)
