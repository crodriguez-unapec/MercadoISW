using IswStore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);
var lab = builder.Configuration.GetValue<bool>("LAB_MODE");
if (lab && !builder.Environment.IsDevelopment()) throw new InvalidOperationException("LAB_MODE solo está permitido en Development.");
var cs = builder.Configuration.GetConnectionString("Default") ?? throw new Exception("Falta ConnectionStrings:Default");
builder.Services.AddDbContext<StoreDb>(o => o.UseSqlServer(cs));
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o => {
 o.Cookie.Name = "isw_session"; o.Cookie.HttpOnly = !lab; o.Cookie.SameSite = SameSiteMode.Lax; o.Cookie.SecurePolicy = lab ? CookieSecurePolicy.None : CookieSecurePolicy.SameAsRequest;
 o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode=401; return Task.CompletedTask; };
 o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode=403; return Task.CompletedTask; };
});
builder.Services.AddAuthorization();
var app=builder.Build();
app.Use(async (ctx,next)=>{
 if(!lab) { ctx.Response.Headers["Content-Security-Policy"]="default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' blob:; object-src 'none'; base-uri 'self'; frame-ancestors 'none'"; ctx.Response.Headers["X-Content-Type-Options"]="nosniff"; }
 await next();
});
app.UseDefaultFiles(); app.UseStaticFiles(); app.UseAuthentication(); app.UseAuthorization();
// Crea las tablas para simplificar la entrega académica. Para producción usar migraciones.
using(var scope=app.Services.CreateScope()) {
 var db=scope.ServiceProvider.GetRequiredService<StoreDb>();
 var attempts=0;
 while(true) { try { await db.Database.EnsureCreatedAsync(); break; } catch(Exception) when(++attempts<15) { await Task.Delay(2000); } }
 // EnsureCreated no migra esquemas existentes; agregamos ambas columnas solo si faltan.
 await db.Database.ExecuteSqlRawAsync(@"IF COL_LENGTH('dbo.Products','ImageData') IS NULL ALTER TABLE dbo.Products ADD ImageData varbinary(max) NULL;");
 await db.Database.ExecuteSqlRawAsync(@"IF COL_LENGTH('dbo.Products','ImageContentType') IS NULL ALTER TABLE dbo.Products ADD ImageContentType nvarchar(30) NULL;");
 if(!await db.Users.AnyAsync()) {
  db.Users.AddRange(new User {Name="Administrador",Email="admin@demo.local",PasswordHash=Hash("Admin123!"),Role="Admin"},
  new User{Name="Vendedor",Email="vendedor@demo.local",PasswordHash=Hash("Vendedor123!"),Role="Vendedor"},
  new User{Name="Cliente A",Email="cliente@demo.local",PasswordHash=Hash("Cliente123!"),Role="Cliente"},
  new User{Name="Cliente B",Email="cliente2@demo.local",PasswordHash=Hash("Cliente123!"),Role="Cliente"});
  await db.SaveChangesAsync();
  var seller=await db.Users.SingleAsync(x=>x.Role=="Vendedor");
  db.Products.AddRange(new Product{Name="Teclado mecánico",Description="Teclado compacto",Price=2499,Stock=12,SellerId=seller.Id},new Product{Name="Mouse inalámbrico",Description="Mouse ergonómico",Price=899,Stock=18,SellerId=seller.Id},new Product{Name="Monitor 24 pulgadas",Description="Full HD",Price=7999,Stock=8,SellerId=seller.Id});
  await db.SaveChangesAsync();
 }
}
var failures=new ConcurrentDictionary<string,(int Count,DateTime Until)>();
app.MapGet("/api/config",()=> Results.Ok(new { labMode=lab }));
app.MapPost("/api/login",async(LoginRequest req,StoreDb db,HttpContext ctx)=>{
 var email=(req.Email??"").Trim().ToLowerInvariant();
 var key=$"{ctx.Connection.RemoteIpAddress}:{email}";
 if(!lab && failures.TryGetValue(key,out var f) && f.Count>=5 && f.Until>DateTime.UtcNow) return Results.Problem("Cuenta temporalmente bloqueada.",statusCode:429);
 var user=await db.Users.SingleOrDefaultAsync(x=>x.Email==email);
 if(user==null || !Verify(req.Password??"",user.PasswordHash)) {
  if(!lab) failures.AddOrUpdate(key,_=>(1,DateTime.UtcNow.AddMinutes(15)),(_,f)=>(f.Count+1,DateTime.UtcNow.AddMinutes(15)));
  return Results.Json(new {message=lab && user==null?"Usuario no existe":"Credenciales inválidas"},statusCode:401);
 }
 failures.TryRemove(key,out _);
 var identity=new ClaimsIdentity(new[]{new Claim(ClaimTypes.NameIdentifier,user.Id.ToString()),new Claim(ClaimTypes.Name,user.Name),new Claim(ClaimTypes.Role,user.Role)},CookieAuthenticationDefaults.AuthenticationScheme);
 await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,new ClaimsPrincipal(identity));
 return Results.Ok(new {user.Id,user.Name,user.Role});
});
app.MapPost("/api/logout",async(HttpContext ctx)=>{await ctx.SignOutAsync();return Results.Ok();}).RequireAuthorization();
app.MapGet("/api/me",(ClaimsPrincipal u)=>Results.Ok(new{id=Uid(u),name=u.Identity!.Name,role=u.FindFirstValue(ClaimTypes.Role)})).RequireAuthorization();
app.MapGet("/api/products",async(string? q,StoreDb db)=>{
 if(lab && !string.IsNullOrWhiteSpace(q)) {
  // LAB: interpolación SQL insegura intencional. Solo entorno local aislado.
  return Results.Ok((await db.Products.FromSqlRaw($"SELECT * FROM Products WHERE Name LIKE '%{q}%'").ToListAsync()).Select(ProductDto));
 }
 return Results.Ok((await db.Products.Where(x=>q==null || x.Name.Contains(q)).OrderBy(x=>x.Id).ToListAsync()).Select(ProductDto));
});
app.MapPost("/api/products",async(ProductInput input,ClaimsPrincipal u,StoreDb db)=>{
 if(!Is(u,"Admin","Vendedor")) return Results.Forbid();
 if(!Valid(input))return Results.BadRequest(new{message="Datos de producto inválidos"});
 var product=new Product{Name=input.Name.Trim(),Description=input.Description.Trim(),Price=input.Price,Stock=input.Stock,SellerId=Uid(u)};
 db.Products.Add(product);await db.SaveChangesAsync();return Results.Ok(ProductDto(product));
}).RequireAuthorization();
app.MapPut("/api/products/{id:int}",async(int id,ProductInput input,ClaimsPrincipal u,StoreDb db)=>{
 var p=await db.Products.FindAsync(id);if(p==null)return Results.NotFound();
 if(!Is(u,"Admin") && !(Is(u,"Vendedor") && p.SellerId==Uid(u)))return Results.Forbid();
 if(!Valid(input))return Results.BadRequest();
 p.Name=input.Name.Trim();p.Description=input.Description.Trim();p.Price=input.Price;p.Stock=input.Stock;await db.SaveChangesAsync();return Results.Ok(ProductDto(p));
}).RequireAuthorization();
// Guardamos la imagen dentro de SQL Server: persiste con el volumen sql_data sin modificar Docker.
app.MapPost("/api/products/{id:int}/image",async(int id,HttpContext ctx,ClaimsPrincipal user,StoreDb db)=>{
 var p=await db.Products.FindAsync(id);
 if(p==null)return Results.NotFound();
 if(!Is(user,"Admin") && !(Is(user,"Vendedor") && p.SellerId==Uid(user)))return Results.Forbid();
 if(!ctx.Request.HasFormContentType)return Results.BadRequest(new {message="Se requiere multipart/form-data."});
 var form=await ctx.Request.ReadFormAsync();
 var file=form.Files.GetFile("image");
 if(file==null)return Results.BadRequest(new {message="Selecciona una imagen."});
 if(file.Length==0 || file.Length>2*1024*1024)return Results.BadRequest(new {message="La imagen debe tener un tamaño máximo de 2 MB."});
 await using var stream=file.OpenReadStream();
 using var mem=new MemoryStream();
 await stream.CopyToAsync(mem);
 var bytes=mem.ToArray();
 string? type=null;
 if(bytes.Length>=8 && bytes.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}))type="image/png";
 else if(bytes.Length>=3 && bytes[0]==255 && bytes[1]==216 && bytes[2]==255)type="image/jpeg";
 else if(bytes.Length>=12 && Encoding.ASCII.GetString(bytes,0,4)=="RIFF" && Encoding.ASCII.GetString(bytes,8,4)=="WEBP")type="image/webp";
 if(type==null)return Results.BadRequest(new {message="Solo se permiten imágenes JPG, PNG o WebP válidas."});
 p.ImageData=bytes;p.ImageContentType=type;
 await db.SaveChangesAsync();
 return Results.Ok(new {imageUrl=$"/api/products/{id}/image"});
}).RequireAuthorization();
app.MapGet("/api/products/{id:int}/image",async(int id,StoreDb db)=>{
 var p=await db.Products.FindAsync(id);
 return p?.ImageData is {Length:>0} && p.ImageContentType!=null
  ? Results.File(p.ImageData,p.ImageContentType,enableRangeProcessing:false)
  : Results.NotFound();
});
app.MapDelete("/api/products/{id:int}",async(int id,ClaimsPrincipal u,StoreDb db)=>{
 var p=await db.Products.FindAsync(id);if(p==null)return Results.NotFound();
 if(!Is(u,"Admin") && !(Is(u,"Vendedor") && p.SellerId==Uid(u)))return Results.Forbid();
 if(await db.CartItems.AnyAsync(x=>x.ProductId==id))return Results.BadRequest(new {message="Producto presente en un carrito"});
 db.Products.Remove(p);await db.SaveChangesAsync();return Results.Ok();
}).RequireAuthorization();
app.MapGet("/api/cart/{userId:int}",async(int userId,ClaimsPrincipal u,StoreDb db)=>{
 if(!lab && Uid(u)!=userId && !Is(u,"Admin"))return Results.Forbid();
 return Results.Ok(await db.CartItems.Where(x=>x.UserId==userId).Join(db.Products,c=>c.ProductId,p=>p.Id,(c,p)=>new{c.Id,c.UserId,c.ProductId,c.Quantity,p.Name,p.Price}).ToListAsync());
}).RequireAuthorization();
app.MapPost("/api/cart/{userId:int}",async(int userId,CartInput input,ClaimsPrincipal u,StoreDb db)=>{
 if(!lab && Uid(u)!=userId && !Is(u,"Admin"))return Results.Forbid();
 var p=await db.Products.FindAsync(input.ProductId);if(p==null || input.Quantity<1 || input.Quantity>p.Stock)return Results.BadRequest();
 var existing=await db.CartItems.SingleOrDefaultAsync(x=>x.UserId==userId && x.ProductId==input.ProductId);
 if(existing!=null)existing.Quantity=input.Quantity;else db.CartItems.Add(new CartItem{UserId=userId,ProductId=input.ProductId,Quantity=input.Quantity});
 await db.SaveChangesAsync();return Results.Ok();
}).RequireAuthorization();
app.MapDelete("/api/cart/{userId:int}/{itemId:int}",async(int userId,int itemId,ClaimsPrincipal u,StoreDb db)=>{
 if(!lab && Uid(u)!=userId && !Is(u,"Admin"))return Results.Forbid();
 var item=await db.CartItems.SingleOrDefaultAsync(x=>x.Id==itemId && x.UserId==userId);if(item==null)return Results.NotFound();
 db.CartItems.Remove(item);await db.SaveChangesAsync();return Results.Ok();
}).RequireAuthorization();
app.MapPost("/api/orders",async(ClaimsPrincipal u,StoreDb db)=>{
 var id=Uid(u);var items=await db.CartItems.Where(x=>x.UserId==id).ToListAsync();if(items.Count==0)return Results.BadRequest(new{message="Carrito vacío"});
 var products=await db.Products.Where(x=>items.Select(i=>i.ProductId).Contains(x.Id)).ToDictionaryAsync(x=>x.Id);
 if(items.Any(x=>!products.ContainsKey(x.ProductId)||products[x.ProductId].Stock<x.Quantity))return Results.BadRequest(new{message="Stock insuficiente"});
 decimal total=0;foreach(var item in items){var product=products[item.ProductId];total+=product.Price*item.Quantity;product.Stock-=item.Quantity;}
 db.Orders.Add(new Order{UserId=id,Total=total});db.CartItems.RemoveRange(items);await db.SaveChangesAsync();return Results.Ok(new{total});
}).RequireAuthorization();
app.MapGet("/api/orders",async(ClaimsPrincipal u,StoreDb db)=>{
 var userId=Uid(u);
 var isAdmin=u.IsInRole("Admin");
 return Results.Ok(await db.Orders.Where(x=>x.UserId==userId || isAdmin).OrderByDescending(x=>x.Id).ToListAsync());
}).RequireAuthorization();
app.MapGet("/api/reviews/{productId:int}",async(int productId,StoreDb db)=>Results.Ok(await db.Reviews.Where(x=>x.ProductId==productId).OrderByDescending(x=>x.Id).ToListAsync()));
app.MapPost("/api/reviews",async(ReviewInput input,ClaimsPrincipal u,StoreDb db)=>{
 if(input.Text.Length>2000 || string.IsNullOrWhiteSpace(input.Text) || !await db.Products.AnyAsync(x=>x.Id==input.ProductId))return Results.BadRequest();
 db.Reviews.Add(new Review{ProductId=input.ProductId,UserId=Uid(u),Text=input.Text});await db.SaveChangesAsync();return Results.Ok();
}).RequireAuthorization();
app.MapGet("/api/admin",async(ClaimsPrincipal u,StoreDb db)=>{
 if(!lab && !Is(u,"Admin"))return Results.Forbid();
 return Results.Ok(new{users=await db.Users.CountAsync(),products=await db.Products.CountAsync(),orders=await db.Orders.CountAsync()});
}).RequireAuthorization();
app.MapFallbackToFile("index.html");
app.Run();
static object ProductDto(Product x)=>new {x.Id,x.Name,x.Description,x.Price,x.Stock,x.SellerId,imageUrl=x.ImageData is {Length:>0} ? $"/api/products/{x.Id}/image" : (string?)null};
static bool Valid(ProductInput x)=>!string.IsNullOrWhiteSpace(x.Name)&&x.Name.Length<=120&&x.Description.Length<=1000&&x.Price>0&&x.Stock>=0;
static int Uid(ClaimsPrincipal u)=>int.Parse(u.FindFirstValue(ClaimTypes.NameIdentifier)!);
static bool Is(ClaimsPrincipal u,params string[] roles)=>roles.Any(u.IsInRole);
static string Hash(string password){var salt=RandomNumberGenerator.GetBytes(16);var hash=Rfc2898DeriveBytes.Pbkdf2(password,salt,200000,HashAlgorithmName.SHA256,32);return $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";}
static bool Verify(string password,string stored){var parts=stored.Split(':');if(parts.Length!=2)return false;var hash=Rfc2898DeriveBytes.Pbkdf2(password,Convert.FromBase64String(parts[0]),200000,HashAlgorithmName.SHA256,32);return CryptographicOperations.FixedTimeEquals(hash,Convert.FromBase64String(parts[1]));}
