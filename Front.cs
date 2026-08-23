// Page front servie par le BFF. Aucune gestion de token en JavaScript :
// le navigateur n'a qu'un cookie (HttpOnly), et le BFF fait le reste.
static class Front
{
    public const string Html =
"""
<!doctype html>
<html lang="fr">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Boutique — BFF</title>
<style>
  :root{--bg:#f4f6f9;--surface:#fff;--ink:#151922;--muted:#5a6472;--border:#e1e6ee;--accent:#0d8b82;--good:#1a7f4b;--err:#c0293b;}
  *{box-sizing:border-box;}
  body{margin:0;font-family:system-ui,-apple-system,"Segoe UI",Roboto,sans-serif;background:var(--bg);color:var(--ink);}
  header{background:var(--surface);border-bottom:1px solid var(--border);padding:14px 24px;display:flex;align-items:center;justify-content:space-between;gap:16px;flex-wrap:wrap;}
  header h1{font-size:18px;margin:0;}
  header .sub{font-size:12px;color:var(--muted);margin-top:2px;}
  .auth{display:flex;align-items:center;gap:10px;font-size:14px;}
  main{max-width:820px;margin:0 auto;padding:24px;display:flex;flex-direction:column;gap:22px;}
  .card{background:var(--surface);border:1px solid var(--border);border-radius:12px;padding:18px 20px;box-shadow:0 1px 2px rgba(20,25,40,.04),0 6px 20px rgba(20,25,40,.05);}
  .card h2{margin:0 0 14px;font-size:15px;display:flex;align-items:center;gap:8px;}
  button{padding:9px 14px;border:0;border-radius:8px;background:var(--accent);color:#fff;font-size:14px;cursor:pointer;}
  button.ghost{background:transparent;color:var(--accent);border:1px solid var(--border);}
  input,select{padding:9px;border:1px solid var(--border);border-radius:8px;font-size:14px;width:100%;background:var(--surface);color:var(--ink);}
  .row{display:flex;gap:10px;flex-wrap:wrap;align-items:end;}
  .field{display:flex;flex-direction:column;gap:5px;flex:1;min-width:130px;}
  .field label{font-size:12px;color:var(--muted);}
  table{width:100%;border-collapse:collapse;font-size:14px;}
  th,td{text-align:left;padding:9px 10px;border-bottom:1px solid var(--border);}
  th{font-size:11px;text-transform:uppercase;letter-spacing:.05em;color:var(--muted);}
  .badge{font-size:12px;padding:3px 9px;border-radius:999px;background:#eef1f7;color:var(--muted);}
  .badge.on{background:#dbf1e4;color:var(--good);}
  .msg{font-size:13px;padding:8px 12px;border-radius:8px;margin-top:12px;display:none;}
  .msg.ok{background:#dbf1e4;color:var(--good);display:block;}
  .msg.ko{background:#fdeaec;color:var(--err);display:block;}
  .locked{color:var(--muted);font-size:13px;}
</style>
</head>
<body>
<header>
  <div><h1>🛍️ Boutique</h1><div class="sub">Navigateur → <b>cookie</b> → ShopWebApp (auth + données)</div></div>
  <div class="auth" id="authArea"></div>
</header>
<main>
  <div class="card">
    <h2>Produits</h2>
    <table>
      <thead><tr><th>Id</th><th>Nom</th><th>Prix</th><th>Catégorie</th><th></th></tr></thead>
      <tbody id="productsBody"><tr><td colspan="5" class="locked">Chargement…</td></tr></tbody>
    </table>
  </div>

  <div class="card">
    <h2>Catégories</h2>
    <table>
      <thead><tr><th>Id</th><th>Nom</th><th></th></tr></thead>
      <tbody id="categoriesBody"><tr><td colspan="3" class="locked">Chargement…</td></tr></tbody>
    </table>
  </div>

  <div class="card">
    <h2>Ajouter <span class="badge">🔒 connexion requise</span></h2>
    <div id="addLocked" class="locked">Connecte-toi pour ajouter un produit ou une catégorie.</div>
    <div id="addForm" style="display:none;">
      <div class="row">
        <div class="field"><label>Nom du produit</label><input id="pName" placeholder="nom de produit"></div>
        <div class="field"><label>Prix (€)</label><input id="pPrice" type="number" step="0.01" placeholder="49.90"></div>
        <div class="field"><label>Catégorie</label><select id="pCat"></select></div>
        <div class="field" style="flex:0;"><label>&nbsp;</label><button id="saveBtn" onclick="addProduct()">Ajouter</button></div>
        <div class="field" id="cancelField" style="flex:0;display:none;"><label>&nbsp;</label><button class="ghost" onclick="cancelEdit()">Annuler</button></div>
      </div>
      <div class="row" style="margin-top:12px;">
        <div class="field"><label>Nouvelle catégorie</label><input id="cName" placeholder="Informatique"></div>
        <div class="field" style="flex:0;"><label>&nbsp;</label><button class="ghost" onclick="addCategory()">Créer</button></div>
      </div>
      <div id="msg" class="msg"></div>
    </div>
  </div>
</main>

<script>
// Aucun token ici : le cookie (même origine) est envoyé automatiquement.
function show(m,ok){const e=document.getElementById("msg");e.textContent=m;e.className="msg "+(ok?"ok":"ko");}

function login(){location="/auth/login";}
async function logout(){await fetch("/auth/logout",{method:"POST"});location.reload();}

let isAuthenticated=false;
async function renderAuth(){
  const a=document.getElementById("authArea");
  const form=document.getElementById("addForm"),lock=document.getElementById("addLocked");
  const r=await fetch("/auth/me");
  isAuthenticated=r.ok;
  if(r.ok){
    const u=await r.json();
    a.innerHTML=`<span class="badge on">● ${u.email||u.name}</span><button class="ghost" onclick="logout()">Déconnexion</button>`;
    form.style.display="block";lock.style.display="none";
  }else{
    a.innerHTML=`<button onclick="login()">Se connecter</button>`;
    form.style.display="none";lock.style.display="block";
  }
}
async function loadProducts(){
  const r=await fetch("/api/products");const list=await r.json();
  const b=document.getElementById("productsBody");
  const actions=p=>isAuthenticated
    ?`<button class="ghost" onclick='editProduct(${p.id},${JSON.stringify(p.name)},${p.price},${p.categoryId})'>Éditer</button><button class="ghost" onclick="deleteProduct(${p.id})">Supprimer</button>`
    :`<span class="locked">🔒 Connecte-toi</span>`;
  b.innerHTML=Array.isArray(list)&&list.length?list.map(p=>`<tr><td>${p.id}</td><td>${p.name}</td><td>${p.price} €</td><td>${p.category?p.category.name:p.categoryId}</td><td style="display:flex;gap:6px;align-items:center;">${actions(p)}</td></tr>`).join(""):`<tr><td colspan="5" class="locked">Aucun produit.</td></tr>`;
}
let editingId=null;
function editProduct(id,name,price,categoryId){
  editingId=id;
  document.getElementById("pName").value=name;
  document.getElementById("pPrice").value=price;
  document.getElementById("pCat").value=categoryId;
  document.getElementById("saveBtn").textContent="Mettre à jour";
  document.getElementById("cancelField").style.display="";
}
function cancelEdit(){
  editingId=null;
  document.getElementById("pName").value="";
  document.getElementById("pPrice").value="";
  document.getElementById("saveBtn").textContent="Ajouter";
  document.getElementById("cancelField").style.display="none";
}
async function deleteProduct(id){
  const r=await fetch("/api/products/"+id,{method:"DELETE"});
  if(r.status===401){show("Non autorisé — reconnecte-toi.",false);return;}
  if(r.status===403){show("Réservé aux administrateurs.",false);return;}
  if(r.ok||r.status===204){show("Produit supprimé ✓",true);loadProducts();}
  else show("Erreur ("+r.status+")",false);
}
async function loadCategories(){
  const r=await fetch("/api/categories");const list=await r.json();
  document.getElementById("pCat").innerHTML=Array.isArray(list)&&list.length?list.map(c=>`<option value="${c.id}">${c.name}</option>`).join(""):`<option value="">(crée une catégorie)</option>`;
  const b=document.getElementById("categoriesBody");
  const actions=c=>isAuthenticated?`<button class="ghost" onclick="deleteCategory(${c.id})">Supprimer</button>`:`<span class="locked">🔒 Connecte-toi</span>`;
  b.innerHTML=Array.isArray(list)&&list.length?list.map(c=>`<tr><td>${c.id}</td><td>${c.name}</td><td>${actions(c)}</td></tr>`).join(""):`<tr><td colspan="3" class="locked">Aucune catégorie.</td></tr>`;
}
async function deleteCategory(id){
  if(!confirm("Supprimer cette catégorie ? Les produits liés seront supprimés aussi."))return;
  const r=await fetch("/api/categories/"+id,{method:"DELETE"});
  if(r.status===401){show("Non autorisé — reconnecte-toi.",false);return;}
  if(r.status===403){show("Réservé aux administrateurs.",false);return;}
  if(r.ok||r.status===204){show("Catégorie supprimée ✓",true);loadCategories();loadProducts();}
  else show("Erreur ("+r.status+")",false);
}
async function addProduct(){
  const name=document.getElementById("pName").value.trim();
  const price=parseFloat(document.getElementById("pPrice").value);
  const categoryId=parseInt(document.getElementById("pCat").value);
  if(!name||isNaN(price)||isNaN(categoryId)){show("Renseigne nom, prix et catégorie.",false);return;}
  const editing=editingId!==null;
  const url=editing?"/api/products/"+editingId:"/api/products";
  const r=await fetch(url,{method:editing?"PUT":"POST",headers:{"Content-Type":"application/json"},body:JSON.stringify({name,price,categoryId})});
  if(r.status===401){show("Non autorisé — reconnecte-toi.",false);return;}
  if(r.status===403){show("Réservé aux administrateurs.",false);return;}
  if(r.ok){
    show(editing?"Produit mis à jour ✓":"Produit ajouté ✓",true);
    if(editing)cancelEdit();else{document.getElementById("pName").value="";document.getElementById("pPrice").value="";}
    loadProducts();
  }else show("Erreur ("+r.status+")",false);
}
async function addCategory(){
  const name=document.getElementById("cName").value.trim();
  if(!name){show("Nom de catégorie requis.",false);return;}
  const r=await fetch("/api/categories",{method:"POST",headers:{"Content-Type":"application/json"},body:JSON.stringify({name})});
  if(r.status===401){show("Non autorisé — reconnecte-toi.",false);return;}
  if(r.status===403){show("Réservé aux administrateurs.",false);return;}
  if(r.ok){show("Catégorie créée ✓",true);document.getElementById("cName").value="";loadCategories();}
  else show("Erreur ("+r.status+")",false);
}

(async function(){await renderAuth();await loadProducts();await loadCategories();})();
</script>
</body>
</html>
""";
}
