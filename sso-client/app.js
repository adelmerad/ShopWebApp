// Aucun token ici : le cookie (même origine) est envoyé automatiquement.
function show(m,ok){const e=document.getElementById("msg");e.textContent=m;e.className="msg "+(ok?"ok":"ko");}

function login(){location="/auth/login";}
function logout(){location="/auth/logout";}

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
