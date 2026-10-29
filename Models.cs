using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace IswStore;
public class User { public int Id {get;set;} [MaxLength(100)] public string Name {get;set;}=""; [MaxLength(200)] public string Email {get;set;}=""; public string PasswordHash {get;set;}=""; public string Role {get;set;}="Cliente"; }
public class Product { public int Id {get;set;} [MaxLength(120)] public string Name {get;set;}=""; [MaxLength(1000)] public string Description {get;set;}=""; public decimal Price {get;set;} public int Stock {get;set;} public int SellerId {get;set;} public byte[]? ImageData {get;set;} [MaxLength(30)] public string? ImageContentType {get;set;} }
public class CartItem { public int Id {get;set;} public int UserId {get;set;} public int ProductId {get;set;} public int Quantity {get;set;}=1; }
public class Review { public int Id {get;set;} public int ProductId {get;set;} public int UserId {get;set;} [MaxLength(2000)] public string Text {get;set;}=""; }
public class Order { public int Id {get;set;} public int UserId {get;set;} public DateTime CreatedAt {get;set;}=DateTime.UtcNow; public decimal Total {get;set;} }
public class StoreDb(DbContextOptions<StoreDb> options):DbContext(options) {
 public DbSet<User> Users => Set<User>(); public DbSet<Product> Products => Set<Product>(); public DbSet<CartItem> CartItems => Set<CartItem>(); public DbSet<Review> Reviews => Set<Review>(); public DbSet<Order> Orders => Set<Order>();
 protected override void OnModelCreating(ModelBuilder model) { model.Entity<User>().HasIndex(x=>x.Email).IsUnique(); model.Entity<Product>().Property(x=>x.Price).HasPrecision(12,2); model.Entity<Order>().Property(x=>x.Total).HasPrecision(12,2); model.Entity<CartItem>().HasIndex(x=>new{x.UserId,x.ProductId}).IsUnique(); }
}
public record LoginRequest(string Email,string Password);
public record ProductInput(string Name,string Description,decimal Price,int Stock);
public record CartInput(int ProductId,int Quantity);
public record ReviewInput(int ProductId,string Text);
