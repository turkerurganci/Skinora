---
name: module-test-assembly-initializer
description: Yeni modül integration-test assembly'sinde EF model-cache race'ini önlemek için TestAssemblyModuleInitializer ([ModuleInitializer]) ekle
metadata:
  type: project
---

EF Core `AppDbContext` modelini **process-statik** cache'ler (model connection-string'e bağlı değil — ilk build'de tüm process için sabitlenir). Modül entity-config'leri `AppDbContext.RegisterModuleAssembly(assembly)` ile kaydedilir. Modül kayıtları per-class `static` ctor'larda **farklı alt kümelerle** yapılırsa ve xUnit sınıfları paralel koşarsa, eksik-kümeli bir sınıf ilk `AppDbContext`'i açtığında model o entity'ler olmadan cache'lenir → diğer sınıflar `System.InvalidOperationException : Cannot create a DbSet for 'X' because this type is not included in the model for the context.` ile **sıralamaya bağlı flaky** kırılır (örn. Platform kaydedilmezse `AuditLog`/`SystemSetting`).

**Çözüm:** Her modül test assembly'sine bir `TestAssemblyModuleInitializer` ekle — `[ModuleInitializer]` metodu assembly yüklenince (herhangi bir test sınıfından + model build'den önce) çalışır ve assembly'nin **tüm** testlerinin ihtiyaç duyduğu modülleri kaydeder. Böylece model her zaman tam kümeyle kurulur; per-class static ctor'lar (idempotent) tek başına yarışı kapatmaz.

**Bu pateni içeren assembly'ler:** `Skinora.API.Tests`, `Skinora.Auth.Tests`, `Skinora.Fraud.Tests` (sonuncusu T101 K11 oturumu, PR #156 — CI run 27090686509'da bu race'le düşmüştü). **Yeni bir modül test assembly'si eklerken, veya mevcut birine başka-modül entity'si kullanan test eklerken bu initializer'ı ekle/güncelle.**
