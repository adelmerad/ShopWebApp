# ShopWebApp

Application web du projet SSO réalisé pendant mon stage chez Mobilis. Une seule application : elle gère la connexion **et** la logique métier (produits, catégories) — pas d'API séparée, comme `ClientApi` chez le binôme.

## Rôle dans l'architecture

```
Navigateur ──cookie──▶  ShopWebApp (:5200)  ──login OIDC──▶  ShopAuth (:5124)
                              │
                              └──▶  base de données ShopDb (produits, catégories)
```

- **Navigateur ↔ ShopWebApp** : cookie de session **HttpOnly**. Le navigateur ne voit jamais de token.
- **ShopWebApp ↔ ShopAuth** : login OpenID Connect (Authorization Code + PKCE), fait à la main.
- **ShopWebApp ↔ base de données** : accès direct via Entity Framework Core — plus de serveur de ressources séparé.

## Stack

- ASP.NET Core (Minimal API)
- Authentification **par cookie**, échange OIDC codé à la main (pas de middleware `AddOpenIdConnect`)
- Entity Framework Core — Code-First + Migrations
- SQL Server (Docker)

## Comment ça marche — PKCE manuel

Volontairement la **même méthode** que `ClientApi` (le BFF du binôme), pour éviter les erreurs de connexion croisée dues à des implémentations différentes :

1. `GET /auth/login` : génère `code_verifier`/`code_challenge`/`state` à la main (`Services/PkceService.cs`), les stocke dans des cookies courts, redirige vers `{Sso:Authority}/connect/authorize`.
2. `GET /auth/callback` : vérifie le `state`, échange le `code` contre les tokens (`client_id` + `client_secret` + `code_verifier`), lit l'`id_token` (**sans vérifier sa signature** — `Services/AuthEndpoints.cs`), pose le cookie de session avec le token stocké comme claim.
3. `GET /auth/me` : infos de l'utilisateur connecté.
4. `POST /auth/logout` : ferme la session.

⚠️ **Limites assumées** (copiées de `ClientApi` à l'identique) : pas de vérification de signature du token, pas de refresh token utilisé — acceptable pour ce projet d'apprentissage, à corriger avant toute mise en prod.

## Prérequis

- **ShopAuth** lancé sur `http://localhost:5124` (profil **http**) — ou le serveur SSO du binôme
- SQL Server accessible, base `ShopDb`
- Le client OpenIddict utilisé doit autoriser le redirect `http://localhost:5200/auth/callback`

## Installation

```powershell
dotnet restore
Copy-Item appsettings.Example.json appsettings.json   # puis renseigner Sso + ConnectionStrings
dotnet ef database update
dotnet run
```

Puis ouvrir **http://localhost:5200**.

## Configuration (`appsettings.json`, non versionné)

```json
{
  "ConnectionStrings": { "DefaultConnection": "..." },
  "Sso": {
    "Authority": "http://localhost:5124",
    "ClientId": "shopwebapp-bff",
    "ClientSecret": "...",
    "RedirectUri": "http://localhost:5200/auth/callback",
    "Scope": "openid email profile offline_access"
  }
}
```

Changer juste `Authority`/`ClientId`/`ClientSecret`/`RedirectUri` permet de basculer entre ton propre `ShopAuth` et un serveur SSO tiers, sans toucher au code.

## Endpoints

| Méthode | Route | Rôle |
|---|---|---|
| GET | `/` | La page web (front) |
| GET | `/auth/login` | Démarre la connexion (PKCE manuel) |
| GET | `/auth/callback` | Reçoit le `code`, échange contre les tokens |
| GET | `/auth/me` | Infos de l'utilisateur connecté (200 / 401) |
| POST | `/auth/logout` | Ferme la session |
| GET | `/api/products`, `/api/categories` | Lecture (public) |
| POST/PUT/DELETE | `/api/products`, `/api/categories` | Écriture (**connexion requise**) |

## Sécurité

- Cookie **HttpOnly** + `SameSite=Lax`.
- PKCE (méthode S256) sur l'échange de code.
- `[Authorize]`/`.RequireAuthorization()` sur les endpoints d'écriture.

## Notes dev

- Session en cookie (claims), pas de stockage serveur séparé → perdue si le cookie expire (8h) ou si l'utilisateur se déconnecte.
- Métadonnées OIDC en **HTTP** pour le dev local.
- `ShopApi` (l'ancienne API séparée) a été retirée le 2026-08-21 et fusionnée ici.
