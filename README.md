# ShopWebApp — BFF (Backend-For-Frontend)

Client web du projet SSO réalisé pendant mon stage chez Mobilis. C'est le **BFF** : il gère la connexion, sert la page, **garde les tokens côté serveur** et n'expose au navigateur qu'un **cookie HttpOnly**. Le navigateur ne voit jamais de token (protection anti-XSS).

## Rôle dans l'architecture

```
Navigateur ──cookie──▶  ShopWebApp (BFF, :5200)  ──token──▶  ShopApi (:5050)  ──valide via──▶  ShopAuth (:5124)
```

- **Navigateur ↔ ShopWebApp** : cookie de session **HttpOnly**.
- **ShopWebApp ↔ ShopApi** : appel serveur-à-serveur avec `Authorization: Bearer <token>`.
- **ShopWebApp ↔ ShopAuth** : login OpenID Connect (Authorization Code + PKCE).

## Stack

- ASP.NET Core 8 (Minimal API)
- Authentification **par cookie** + **OpenID Connect** (`Microsoft.AspNetCore.Authentication.OpenIdConnect`)
- Flow **Authorization Code + PKCE**
- `HttpClient` pour proxifier vers ShopApi

## Comment ça marche

1. **Login** : `GET /auth/login` → redirige vers la page de login de ShopAuth → retour avec un `code` → **échange côté serveur** (PKCE) → pose le cookie de session.
2. **Tokens** : stockés **côté serveur** (`MemoryTicketStore`), jamais dans le navigateur. Le cookie ne contient qu'un identifiant de session.
3. **Proxy** : `/api/*` est relayé vers ShopApi en **injectant le Bearer** côté serveur.

## Prérequis

- **ShopAuth** lancé sur `http://localhost:5124` (profil **http**)
- **ShopApi** lancé sur `http://localhost:5050`
- Le client `postman` de ShopAuth doit autoriser le redirect **`http://localhost:5200/signin-oidc`**

## Lancer

```powershell
dotnet run
```

Puis ouvrir **http://localhost:5200**.

## Endpoints

| Méthode | Route | Rôle |
|---|---|---|
| GET | `/` | La page web (front) |
| GET | `/auth/login` | Démarre la connexion (redirection OIDC) |
| POST | `/auth/logout` | Ferme la session (efface le cookie) |
| GET | `/auth/me` | Infos de l'utilisateur connecté (200 / 401) |
| GET | `/api/products`, `/api/categories` | Proxy lecture (public) |
| POST | `/api/products`, `/api/categories` | Proxy écriture (**connexion requise**) |

## Sécurité

- Cookie **HttpOnly** + `SameSite=Lax` ; **tokens côté serveur** → aucun token accessible au JavaScript.
- PKCE + `state`/`nonce` gérés par le handler OpenID Connect.

## Notes dev

- Session **en mémoire** (`MemoryTicketStore`) → perdue au redémarrage. En prod : magasin distribué (Redis).
- Métadonnées OIDC en **HTTP** (`RequireHttpsMetadata = false`) et cookies de corrélation en `SameSite=Lax` pour le dev local. En prod : HTTPS + certificats persistants côté serveur d'auth.
