# ShopWebApp

Application web du projet SSO réalisé par Adel Merad et Abdelhadi Deffous pendant leur stage chez Mobilis. Elle se connecte au serveur SSO commun du projet : [ShopAuth](https://github.com/adelmerad/ShopAuth).

Deux dossiers frères dans ce repo :

- **`ClientApi/`** — le BFF : connexion (OIDC) **et** logique métier (produits, catégories), plus le serveur de fichiers statiques pour le front.
- **`sso-client/`** — le front (HTML/CSS/JS statique). Il ne gère aucun token : juste des `fetch()` vers `ClientApi`, qui envoie le cookie de session automatiquement.

## Rôle dans l'architecture

```
Navigateur ──cookie──▶  ClientApi (:5200)  ──sert──▶  sso-client (statique)
                              │
                              ├──OIDC + refresh──▶  ShopAuth (:5171)
                              └──▶  base de données ShopDb (produits, catégories)
```

- **Navigateur ↔ ShopWebApp** : cookie de session **HttpOnly** contenant un identifiant de session. Le navigateur ne voit jamais de token.
- **ShopWebApp ↔ ShopAuth** : login OpenID Connect (Authorization Code + PKCE), fait à la main.
- **ShopWebApp ↔ base de données** : accès direct via Entity Framework Core — plus de serveur de ressources séparé.

## Stack

- ASP.NET Core 8 (Minimal API)
- Authentification **par cookie**, échange OIDC codé à la main (pas de middleware `AddOpenIdConnect`)
- Entity Framework Core — Code-First + Migrations
- SQL Server (Docker)

## Comment ça marche

1. `GET /auth/login` : génère `code_verifier`/`code_challenge`/`state` (`Services/PkceService.cs`), les stocke dans des cookies courts, puis redirige vers `{Sso:Authority}/connect/authorize` avec `prompt=login`, ce qui demande l'email et le mot de passe à chaque connexion.
2. `GET /auth/callback` : vérifie le `state`, échange le `code` contre les tokens (`client_id` + `client_secret` + `code_verifier`), puis valide l'`id_token` : signature (clés JWKS du SSO), issuer, audience et expiration (`Endpoints/AuthEndpoints.cs`).
3. Les tokens sont gardés côté serveur dans `TokenStore`. Le cookie de session contient les claims de l'utilisateur et un `session_id` opaque qui sert à retrouver ces tokens.
4. À chaque requête authentifiée, `TokenRefreshService` renouvelle l'access token s'il expire dans moins de 60 secondes :
   - **refresh token invalide** : la session est fermée (401) ;
   - **panne réseau ou SSO** : la requête continue avec le token actuel, l'utilisateur n'est pas déconnecté.
5. `GET /auth/logout` : supprime les tokens et le cookie, puis redirige vers `/connect/logout` pour fermer aussi la session sur ShopAuth.

## Autorisations

Les rôles viennent du claim `role` de l'`id_token`, émis par le SSO. ShopWebApp ne stocke aucun rôle et les applique avec deux politiques :

| Politique | Rôles acceptés | Utilisée pour |
|---|---|---|
| `admin-ou-employe` | `Admin`, `employe` | Créer une catégorie, créer ou modifier un produit |
| `admin` | `Admin` | Supprimer une catégorie ou un produit |

Sur le SSO, `Admin` est un rôle global et `employe` un rôle lié à l'application `shopwebapp-bff`. Les noms sont comparés sans tenir compte de la casse. Un compte sans aucun rôle sur cette application est refusé par le SSO à la connexion.

Un rôle modifié sur le SSO n'est pris en compte qu'à la connexion suivante, car les rôles sont copiés dans le cookie à la connexion.

## Prérequis

- [SDK .NET 8](https://dotnet.microsoft.com/download/dotnet/8.0)
- **[ShopAuth](https://github.com/adelmerad/ShopAuth)** lancé sur `http://localhost:5171` (profil **http**)
- SQL Server accessible, base `ShopDb`

## Enregistrer l'application sur le SSO

À faire une seule fois, dans le panneau d'administration de ShopAuth :

1. Créer une application confidentielle `shopwebapp-bff`, et noter le secret affiché (il n'est visible qu'à la création).
2. Autoriser le redirect `http://localhost:5200/auth/callback` et le post-logout redirect `http://localhost:5200/`.
3. Autoriser les scopes `profile`, `email`, `roles` et `offline_access` (nécessaire pour obtenir un refresh token).
4. Créer le rôle `employe` pour cette application et l'attribuer aux comptes concernés.

## Installation

```powershell
cd ClientApi
dotnet restore
Copy-Item appsettings.Example.json appsettings.json   # puis renseigner Sso + ConnectionStrings
dotnet ef database update
dotnet run
```

Puis ouvrir **http://localhost:5200** (ClientApi sert `sso-client/` directement, aucune étape de build côté front).

## Configuration (`appsettings.json`, non versionné)

```json
{
  "ConnectionStrings": { "DefaultConnection": "..." },
  "Sso": {
    "Authority": "http://localhost:5171",
    "ClientId": "shopwebapp-bff",
    "ClientSecret": "...",
    "RedirectUri": "http://localhost:5200/auth/callback",
    "Scope": "openid email profile offline_access"
  }
}
```

Changer juste `Authority`/`ClientId`/`ClientSecret`/`RedirectUri` permet de basculer vers un autre serveur SSO, sans toucher au code.

## Endpoints

| Méthode | Route | Accès | Rôle |
|---|---|---|---|
| GET | `/`, `/style.css`, `/app.js` | Public | Fichiers statiques de `sso-client/` |
| GET | `/auth/login` | Public | Démarre la connexion |
| GET | `/auth/callback` | Public | Reçoit le `code`, l'échange contre les tokens |
| GET | `/auth/me` | Connecté (sinon 401) | Utilisateur connecté, rôles et expiration du token |
| GET | `/auth/logout` | Public | Ferme la session locale et la session SSO |
| GET | `/api/categories`, `/api/products`, `/api/products/{id}` | Public | Lecture |
| POST | `/api/categories`, `/api/products` | `admin-ou-employe` | Création |
| PUT | `/api/products/{id}` | `admin-ou-employe` | Modification |
| DELETE | `/api/categories/{id}`, `/api/products/{id}` | `admin` | Suppression |

## Sécurité

- Cookie **HttpOnly** + `SameSite=Lax`.
- PKCE (méthode S256) et vérification du `state` sur l'échange de code.
- `id_token` validé avec les clés publiques du SSO.
- Access et refresh tokens gardés côté serveur, jamais envoyés au navigateur.
- Verrou par session sur le refresh : le SSO fait tourner les refresh tokens, deux appels simultanés feraient échouer le second.

## Notes dev

- `TokenStore` garde les tokens en mémoire : redémarrer ShopWebApp ferme les sessions en cours (8h maximum sinon).
- Métadonnées OIDC en **HTTP** pour le dev local (`RequireHttps = false`), à passer en HTTPS avant toute mise en prod.
- `ShopApi` (l'ancienne API séparée) a été retirée le 2026-08-21 et fusionnée ici.
