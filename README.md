# Mercado ISW — Nueva interfaz minimalista

Mini aplicación web académica realizada en **ASP.NET Core 10, Entity Framework Core y SQL Server**, con interfaz HTML/CSS/JS sin frameworks adicionales.

## Inicio rápido en Windows / Visual Studio 2022

1. Abre Docker Desktop (Linux Containers).
2. En esta carpeta copia `.env.example` a `.env` y establece una contraseña fuerte en `DB_PASSWORD`.
3. Ejecuta `docker compose up --build -d`.
4. Abre **http://localhost:8080**.
5. Comprueba los contenedores: `docker compose ps`. Para errores: `docker compose logs --tail=80 app`.

**No hace falta instalar SQL Server manualmente.** La app crea sus tablas y datos de prueba en el primer arranque.

## Cuentas de demostración

| Rol | Correo | Contraseña |
| --- | --- | --- |
| Administrador | `admin@demo.local` | `Admin123!` |
| Vendedor | `vendedor@demo.local` | `Vendedor123!` |
| Cliente | `cliente@demo.local` | `Cliente123!` |
| Segundo cliente | `cliente2@demo.local` | `Cliente123!` |

Estas credenciales son **solo para laboratorio local**. No publiques la app con ellas.

## Pantallas incluidas

- **Productos:** lista, búsqueda, filtro de existencias/precio, detalle, comentarios y agregar al carrito.
- **Login:** autentica al usuario mediante cookie y muestra las cuentas locales de prueba.
- **Carrito:** agregar, quitar, cambiar cantidades, calcular total y confirmar un pedido sin pagos reales.
- **Pedidos:** ver los pedidos propios, o todos los pedidos para el administrador.
- **Mis productos:** el vendedor solo visualiza y administra sus propios productos; alta, edición y eliminación.
- **Gestión de productos / Administración:** el administrador puede administrar el catálogo completo y ver estadísticas agregadas.

La aplicación no incluye pasarela de pago ni servicio de entregas; no se requieren para este ejercicio.

## Seguridad y alcance académico

- `LAB_MODE=false` es el ajuste predeterminado y recomendado para el uso normal.
- `LAB_MODE=true` **solo** se permite en `Development`, sobre Docker aislado y **nunca** en un servidor público. Activa deliberadamente algunas debilidades de laboratorio (SQLi, XSS almacenado y control de acceso); es responsabilidad del equipo documentar y corregir los hallazgos.
- **Fase 1 pendiente:** completar las cinco PoC exigidas, incluyendo JWT, los diagramas STRIDE/DFD y los CVSS.
- **Fase 2 pendiente:** Redis, Argon2id/bcrypt requerido, TOTP, JWT RS256, refresh tokens, gestión de secretos y SAST/SCA/DAST completo. El CI incluido es una base, no un pipeline de seguridad completo ni validado.
- El formulario de comentarios en modo laboratorio permite contenido sin escape deliberadamente. No introducir datos reales.
- El diseño usa ilustraciones tipográficas locales en vez de imágenes externas, para no depender de recursos o licencias.

## Git

Ejecuta `git init`, `git add .` y `git commit -m "feat: crear mini tienda ISW con tres roles"` si aún no has inicializado el repositorio. Nunca subas `.env`.

## Nota sobre actualizaciones

Si ya ejecutaste una versión anterior, puedes copiar tu `.env` existente a esta carpeta y ejecutar `docker compose up --build -d`. Si conservas la misma configuración de Compose y volumen, se reutilizarán los datos. No uses `docker compose down -v` salvo que quieras borrar la base de datos.

---

## Documento original del proyecto

# Mercado ISW — Proyecto académico UNAPEC

Mini tienda en ASP.NET Core (.NET 10), EF Core, SQL Server y Docker. **No desplegar el modo laboratorio en redes públicas.**

## Ejecución rápida

1. Instalar Docker Desktop (modo Linux containers).
2. `cp .env.example .env` (PowerShell: `Copy-Item .env.example .env`).
3. Editar `.env` con un `DB_PASSWORD` robusto (letras, números y símbolos).
4. `docker compose up --build -d`
5. Abrir http://localhost:8080

Para detener: `docker compose down`. Para reiniciar desde cero borrando datos: `docker compose down -v`.

También se puede abrir `IswStore.csproj` en Visual Studio compatible con .NET 10; en ese caso hay que configurar una conexión SQL Server válida en `ConnectionStrings__Default`.

### Cuentas solo para desarrollo

| Rol | Correo | Contraseña |
| --- | --- | --- |
| Admin | admin@demo.local | Admin123! |
| Vendedor | vendedor@demo.local | Vendedor123! |
| Cliente A | cliente@demo.local | Cliente123! |
| Cliente B | cliente2@demo.local | Cliente123! |

No reutilizar estas cuentas/contraseñas para despliegues reales. Las contraseñas se guardan como PBKDF2 con sal, no en texto plano.

## Fase 1: laboratorio de pentesting (SOLO local)

Cambiar `LAB_MODE=true` en `.env` y reiniciar con `docker compose up -d --build --force-recreate app`. Existe una *simulación aislada de cuatro errores de diseño* para pruebas en el entorno propio:

- **SQL Injection:** concatenación insegura en `GET /api/products?q=...`.
- **XSS almacenado:** comentarios HTML sin escape en el cliente y cookie de sesión legible por JavaScript.
- **IDOR/BOLA:** no se comprueba pertenencia en `/api/cart/{userId}` ni rol en `/api/admin`.
- **Autenticación débil:** sin limitación de intentos y revelación de cuentas existentes.

**JWT débil / `alg: none` no está implementado.** La sesión usa cookies, así que este requisito del curso sigue pendiente; se necesita introducir JWT en una rama de laboratorio separada y después sustituirlo por JWT RS256 seguro en la defensa. No se debe declarar una PoC de JWT si no existe.

Mantener laboratorio expuesto solo a `127.0.0.1`. No realizar pruebas contra terceros. No usar datos reales.

## Fase 2: estado real

**Incluido:** consultas LINQ parametrizadas fuera del laboratorio, escape de comentarios, CSP básica, autorización por rol y propiedad, bloqueo temporal de login tras cinco fallos, hash PBKDF2, `.gitignore`, Docker multi-stage con usuario no root, pipeline inicial de build + Trivy base.

**Pendiente para rúbrica completa:** JWT RS256 de 15 minutos, refresh rotativo en BD, 2FA TOTP, bcrypt 12 o Argon2id (PBKDF2 actual no cumple el algoritmo solicitado), rate limiting Redis, gestor de secretos/vault, CSRF antiforgery, cookies Secure bajo HTTPS real, políticas ABAC más completas, migraciones EF, suite de tests, revisión de concurrencia/stock y autorización granular, SAST Semgrep/SonarQube con 2 High demostrados y corregidos, SCA real de paquetes, ZAP DAST automatizado, Trivy de la imagen construida y evidencias ASVS L2. El archivo CI es inicial, **no** un pipeline final completo.

## Estructura

- `Models.cs`: entidades y DbContext.
- `Program.cs`: endpoints API, auth y modo local de laboratorio.
- `wwwroot/`: frontend HTML/CSS/JS responsive.
- `Dockerfile`, `docker-compose.yml`: despliegue local.
- `.github/workflows/security.yml`: pipeline inicial.
- `docs/`: modelado de amenazas, mapa de pruebas y pendientes.

## Diseño de laboratorio

`LAB_MODE` se bloquea fuera de `Development`, y Compose publica solo en localhost. Aun así, no es apropiado usar el modo vulnerable para navegación habitual ni como servicio abierto. La rama de defensa debe conservar el modo inseguro deshabilitado y debe usar pruebas automatizadas para evidenciar las correcciones.

## Nota de verificación

El proyecto fue generado como código fuente. No se ha podido compilar ni ejecutar Docker en el entorno de generación; validar con `docker compose up --build` antes de realizar la entrega académica.

## Imágenes de productos (versión actualizada)

El **administrador** puede crear, modificar y reemplazar imágenes de todos los productos. Un **vendedor** solo puede hacerlo con productos que le pertenecen; el **cliente** no tiene permiso. Esta regla se valida en el servidor, no solo en la interfaz.

- En **Gestionar productos** o **Mis productos**, selecciona **Nuevo producto** o **Editar**.
- El campo **Imagen del producto** permite JPG, PNG o WebP de hasta **2 MB**, muestra vista previa y es opcional.
- Al editar sin elegir un archivo nuevo, la imagen anterior se conserva.
- Si no hay imagen, el catálogo muestra «Sin imagen», en vez de asignar una imagen aleatoria.
- Las imágenes se almacenan como datos binarios en la tabla `Products` (`ImageData` y `ImageContentType`), persistidos en el volumen de SQL Server de Docker. **No hace falta crear ni montar una carpeta uploads.**
- Para actualizar una base existente, el arranque agrega las dos columnas cuando faltan; esto es una adaptación académica simple, no sustituye migraciones formales.

No uses `docker compose down -v` porque elimina los datos de la base. Si utilizas una carpeta nueva con un nombre de proyecto Docker distinto, el volumen anterior puede no reutilizarse automáticamente. Haz una copia de seguridad antes de mover datos existentes.


## Ajustes de interfaz (29 octubre 2026)
- Vista previa de imágenes: la CSP autoriza `blob:` exclusivamente para `img-src`, para mostrar imágenes locales seleccionadas antes del guardado. No se habilita `blob:` para scripts.
- Administración: separación explícita entre tarjetas de resumen, área de acciones y botones; versión adaptable a pantallas pequeñas.
- Sigue siendo un proyecto de laboratorio. No expongas servicios vulnerables a Internet.
