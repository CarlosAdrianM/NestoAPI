# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

NestoAPI is an ASP.NET Web API project built with .NET Framework 4.8 using Entity Framework 6.x for data access. The application provides a REST API for managing sales orders (pedidos de venta), customer management, invoicing, inventory, commissions, and various business operations for a distribution company.

### Estrategia de migración progresiva: Nesto (VB.NET) → NestoAPI (C#)

Siempre que sea conveniente o necesario, se debe extraer lógica de negocio del cliente de escritorio Nesto (VB.NET) y moverla a NestoAPI (C#). Esto persigue dos objetivos:
1. **Eliminar progresivamente el legacy VB.NET**, consolidando la lógica en el backend
2. **Hacer la funcionalidad accesible a todos los clientes** (Nesto, NestoApp, TiendasNuevaVision) a través de la API, en vez de tenerla atrapada en un solo cliente

Al implementar funcionalidad nueva en NestoAPI que ya exista en Nesto, revisar si el código VB.NET correspondiente puede eliminarse y sustituirse por una llamada a la API.

## Related Repositories (Ecosistema Nesto)

El usuario Carlos tiene varios repositorios relacionados que forman el ecosistema completo:

### Nesto (Cliente de escritorio)
- **URL**: https://github.com/CarlosAdrianM/Nesto
- **Tecnologías**: C# (48%), Visual Basic .NET (46%), WPF
- **Descripción**: Aplicación de escritorio para gestión empresarial (pedidos, productos, contratos)
- **Arquitectura**: Modular con carpetas por dominio (Producto, PedidoCompra, CanalesExternos, Contratos)
- **Ruta local típica**: `C:\Users\Carlos\source\repos\Nesto`

### NestoApp (App móvil)
- **URL**: https://github.com/CarlosAdrianM/NestoApp
- **Tecnologías**: TypeScript (78%), Angular, Ionic
- **Descripción**: Aplicación móvil multiplataforma para vendedores
- **Autenticación**: Usa `/oauth/token` de NestoAPI
- **Ruta local típica**: `C:\Users\Carlos\source\repos\NestoApp`

### TiendasNuevaVision (Tienda online)
- **URL**: https://github.com/CarlosAdrianM/TiendasNuevaVision
- **Tecnologías**: (repositorio privado - verificar acceso)
- **Descripción**: Tienda online para clientes finales
- **Autenticación**: Usa `/api/auth/token` de NestoAPI
- **Ruta local típica**: `C:\Users\Carlos\source\repos\TiendasNuevaVision`

### NVIA (Gestión de vídeo-protocolos)
- **URL**: https://github.com/CarlosAdrianM/NVIA
- **Tecnologías**: C#, Blazor Server, .NET 8
- **Descripción**: Descarga transcripciones de YouTube, genera protocolos con OpenAI, inserta en tabla Videos de NestoAPI y envía push notifications de nuevos protocolos
- **Ruta local**: `C:\Users\Carlos\source\repos\ConsolaNVIA`
- **Relación con NestoAPI**: Llama a `POST /api/Notificaciones/NuevoProtocolo` para push de nuevos protocolos. Inserta directamente en las tablas Videos y VideosProductos de la BD de NestoAPI.
- **Proyectos Firebase**: Usa proyecto `NestoTiendas` (distinto de `nestoapp-c9c4c` que usa NestoApp)

### odoo-custom-addons (Integración Odoo)
- **URL**: https://github.com/CarlosAdrianM/odoo-custom-addons
- **Tecnologías**: Python (97%), TSQL
- **Descripción**: Módulos personalizados para Odoo
- **Módulos**: `helpdesk_custom_machine`, `nesto_sync` (sincronización con Nesto)
- **Ruta local típica**: `C:\Users\Carlos\source\repos\odoo-custom-addons`

### Relaciones entre repositorios
```
┌─────────────────┐     ┌─────────────────┐
│     Nesto       │────▶│    NestoAPI     │◀────┌─────────────────┐
│   (Escritorio)  │     │   (Backend)     │     │    NestoApp     │
│   WPF/VB.NET    │     │  ASP.NET 4.8    │     │  Angular/Ionic  │
└─────────────────┘     └────────┬────────┘     └─────────────────┘
                                 │ ▲
                                 ▼ │
┌─────────────────┐     ┌────────┴────────┐
│ TiendasNueva    │     │      NVIA       │
│    Vision       │     │ (Protocolos     │
│ (Tienda online) │     │  YouTube+OpenAI)│
└─────────────────┘     └─────────────────┘
```

## Solution Structure

- **NestoAPI**: Main Web API project
- **NestoAPI.Tests**: Unit tests using MSTest and FakeItEasy for mocking
- (LibreriaInformes, el proyecto del RDLC del mandato, se quitó en NestoAPI#735)

## Building and Testing

### Build
**IMPORTANT**: This project uses .NET Framework 4.8 and requires MSBuild (Visual Studio). **DO NOT** use `dotnet build` as it will fail with error MSB4019 (missing WebApplication.targets).

```bash
# CORRECT: Use MSBuild (requires Visual Studio installed)
msbuild NestoAPI.sln /t:Build /p:Configuration=Debug

# INCORRECT: Do NOT use dotnet CLI - it will fail
# dotnet build NestoAPI.sln  # ❌ This will NOT work
```

**For Claude Code users**: Since MSBuild is typically not available in the Claude Code environment, assume code changes are syntactically correct after making them. The project must be built in Visual Studio by the user.

### Running Tests
Tests can be run using the .NET CLI:
```bash
# Run all tests
dotnet test NestoAPI.Tests/NestoAPI.Tests.csproj

# Run tests with detailed output
dotnet test NestoAPI.Tests/NestoAPI.Tests.csproj --logger "console;verbosity=detailed"

# Run a specific test
dotnet test --filter "FullyQualifiedName~GestorPreciosTests"
```

## Architecture

### Data Access Layer
- **NVEntities**: Main Entity Framework DbContext generated from `NestoEntities.edmx` (Database-First approach)
- **ApplicationDbContext**: ASP.NET Identity context for authentication/authorization (connection string: "NVIdentity")
- Configuration: `LazyLoadingEnabled = false` and `ProxyCreationEnabled = false` throughout the codebase

### Business Logic Layer (Infraestructure/)
The business logic follows a Service/Manager pattern:

- **Gestores (Managers)**: Core business logic classes
  - `GestorPrecios`: Price and discount calculation engine with validation pipeline
  - `GestorPedidosVenta`: Sales order management
  - `GestorFacturas`: Invoice generation and management
  - `GestorClientes`: Customer management
  - `GestorStocks`: Inventory management
  - `GestorComisiones`: Commission calculations
  - `GestorKits`: Kit assembly management
  - `GestorUbicaciones`: Location/picking management

- **Servicios (Services)**: Orchestration layer between controllers and managers
  - Pattern: `IServicioX` interface → `ServicioX` implementation
  - Examples: `IServicioPedidosVenta`, `IServicioFacturas`, `IServicioVendedores`

### Validation Pipeline Architecture
The pricing and discount validation system uses a multi-stage validation pipeline:

1. **Price Conditions** (`ICondicionPrecioDescuento`): Basic pricing rules
   - Located in `Infraestructure/CondicionesPrecio/`
   - Examples: `OtrosAparatosNoPuedeLlevarDescuento`, `RamasonNoPuedeLlevarNingunDescuento`

2. **Denial Validators** (`IValidadorDenegacion`): Check for invalid orders
   - `ValidadorOfertasPermitidas`: Ensures only permitted offers are applied
   - `ValidadorDescuentosPermitidos`: Validates allowed discounts
   - `ValidadorOtrosAparatosSiempreSinDescuento`: Special product rules
   - Located in `Infraestructure/ValidadoresPedido/`

3. **Acceptance Validators** (`IValidadorAceptacion`): Override denials with exceptions
   - `ValidadorOfertasCombinadas`: Combined offers that bypass restrictions
   - `ValidadorMuestrasYMaterialPromocional`: Sample products (100% discount)
   - `ValidadorRegalosTiendaOnline`: Online store gifts
   - `ValidadorRegaloPorImportePedido`: Gifts based on order amount

The validation flow in `GestorPrecios.EsPedidoValido()`:
1. Run all denial validators on the order
2. For each validation failure, check acceptance validators
3. Accumulate errors that don't have valid exceptions
4. Return consolidated validation response

### Controllers Layer
- Inherit from `BaseApiController` which provides:
  - `ApplicationUserManager` and `ApplicationRoleManager` via OWIN
  - `TheModelFactory` for DTO creation
  - `GetErrorResult()` helper for Identity errors

- Key Controllers:
  - `PedidosVentaController`: Sales orders (GET, POST, PUT, DELETE)
  - `ClientesController`: Customer management
  - `FacturasController`: Invoice operations
  - `ProductosController`: Product catalog
  - `ComisionesController`: Commission calculations
  - `PickingController`: Warehouse picking operations

### Constants
All business constants are centralized in `Models/Constantes.cs`:
- `Empresas.EMPRESA_POR_DEFECTO = "1"`
- `Almacenes`: ALG (Algete), REI (Reina), ALC (Alcobendas)
- `EstadosLineaVenta`: PRESUPUESTO (-3), PENDIENTE (-1), EN_CURSO (1), ALBARAN (2), FACTURA (4)
- `Productos.SUBGRUPO_MUESTRAS`: Samples get 100% discount
- Email addresses, account codes, and other business rules

### Models and DTOs
- Entity Framework models in `Models/` (auto-generated from EDMX)
- DTOs typically named with `DTO` suffix (e.g., `PedidoVentaDTO`, `ClienteDTO`)
- Complex models organized in subdirectories:
  - `Models/Facturas/`: Invoice-related models and series implementations
  - `Models/PedidosVenta/`: Sales order DTOs and parameters
  - `Models/Picking/`: Warehouse picking models
  - `Models/RecursosHumanos/`: HR models (employees, vacations, actions)
  - `Models/Comisiones/`: Commission calculation models

## Important Patterns and Conventions

### Dependency Injection Pattern
- Constructor injection used for testing: Controllers accept `NVEntities` parameter
- Services use interfaces for testability
- `InternalsVisibleTo` attribute exposes internals to test project

### String Handling
- Database strings often padded with spaces (legacy system)
- Use `.Trim()` extensively when working with entity properties
- Client numbers, vendors, products use fixed-length string keys

### Price Calculation
The `GestorPrecios.calcularDescuentoProducto()` method:
1. Checks if discounts apply (`aplicarDescuento` flag, product family rules)
2. Calculates special prices from `DescuentosProductoes` table
3. Calculates discounts by product, family, group hierarchy
4. Applies business rules (samples = 100% discount)
5. Returns both `precioCalculado` and `descuentoCalculado`

### Error Handling
- Controllers return standard Web API responses (`Ok()`, `BadRequest()`, `NotFound()`)
- Business logic throws exceptions for invalid operations
- `RespuestaValidacion` class provides structured validation responses with:
  - `ValidacionSuperada`: Boolean success flag
  - `Motivos`: List of error messages
  - `Errores`: Detailed error list with product IDs
  - `AutorizadaDenegadaExpresamente`: Flag for explicitly denied operations

## Key Business Rules

### Sales Order Validation
- El Edén client (`Constantes.ClientesEspeciales.EL_EDEN`) bypasses all validations
- Orders validate through denial then acceptance validator pipeline
- Each product line validated individually with specific product ID tracking
- Material promocional (subgroup = "Muestras") always gets 100% discount

### Commission System
Recent refactoring consolidated commission calculations into `GestorComisiones`:
- Handles annual commissions by vendor
- Commission details and monthly summaries
- Tramo (tier) based calculations

### Inventory and Picking
- Multi-warehouse system (Algete, Reina, Alcobendas)
- Kit assembly tracked through `GestorKits`
- Picking system with location management (`GestorUbicaciones`)
- Stock tracking across warehouses via `GestorStocks`

### Código de barras interno y recibir escaneando (NestoAPI#762, 30/10/26)
- Todo producto sin código de barras recibe uno **interno**: EAN-13 del rango 29 (`CodigoBarrasInterno`): `290` + referencia a 9
  cifras + control (17404 → 2900000174047) o `299` + contador para referencias no numéricas o con choque. Va en
  `ProductosCodigosBarras` (Origen `Almacen`) y **nunca es el principal** (la ficha `Productos.CodBarras` la manda PrestaShop como GTIN).
  Se da en exclusiva (`sp_getapplock` en `RepositorioCodigosBarras.GuardarInternoEnExclusiva`). En la BD nadie usaba el 29.
- Etiqueta 3×2 EAN-13: `POST api/Almacen/EtiquetasProducto/Imprimir`. Desde NestoAPI#791 es la ÚNICA etiqueta de producto
  (`Formato` se acepta y se ignora; ya no hay Code128 con la referencia). Lleva el EAN-13 de la unidad (Cantidad 1) o, si no
  tiene, el interno, que se le da al imprimir (`CodigoBarras`, `CodigoInterno`, `CodigoAsignado` en la respuesta).
  Medidas en `GeneradorEtiquetasProducto.EAN_*`; prueba con `Scripts/Issue762_EtiquetaPrueba.ps1`.
- Las REPO se reciben solo escaneando (`RecepcionDTO.SoloEscaneando`); lo que llega sin etiqueta, unidad a unidad con
  `POST api/Almacen/Recepciones/{tipo}/{documento}/SinEtiqueta` (avisa al origen y a Compras; la unidad la cuenta la app).
  Ariadna#92: si el producto tiene código de envase (ficha o ProductosCodigosBarras activo, sin contar los internos 29), `NoLleva`
  → 400 «léalo del envase» y `NoSeLee`/`Otro` avisan SOLO a Compras (`IAvisadorSinEtiqueta.AvisarACompras`).
- Limpieza: `GET api/Productos/RepuestosSinCodigoBarras?meses=3` (o `Scripts/Issue762_ProductosRepuestosSinCodigo.sql`).
- **Muestras sin código (NestoAPI#797, 03/11/26)**: las muestras (subgrupo MMP) sin código NO se etiquetan. Toda línea con `SinCodigo`
  (recorrido, packing, Ubicar, Recibir COMP/REPO, y Enviar reposición) lleva `SinCodigoAdmitido` = sin código Y MMP (regla única
  `MuestrasSinCodigo.Admitido`; cada consulta trae `Productos.SubGrupo`): se teclea, sin aviso. `SinEtiqueta` de una línea admitida
  cuenta y no llama a `IAvisadorSinEtiqueta`; `RepuestosSinCodigoBarras` y su script excluyen MMP. Imprimir etiqueta de una muestra sigue valiendo.

### Furgoneta bulto a bulto (NestoAPI#772, 31/10/26)
- Cada bulto del reparto propio (`RP{envío}-{N}`, `NR{pedido}-{N}`, `R{traspaso}-{N}`) tiene su historia en `RepartoBultosMovimientos`
  (`Scripts/Issue772_FurgonetaBultos.sql`): Cargado, Recogido, Entregado, Descargado (Origen Repartidor/Almacen). «Lo que lleva encima»
  = último movimiento (por FechaHora del aparato e Id) Cargado o Recogido suyo (`NucleoFurgoneta`). `Cargar` sigue escribiendo en
  `EnviosAgenciaCargaBultos`/`RepartoParadasReposicionBultos` y además su Cargado con la MISMA clave (las NR no: el bulto no existe hasta recogerlo).
- `GET api/RepartoPropio/Furgoneta`, `POST Recoger`, `POST Descargar`, `POST Jornada/Terminar` (`ServicioRepartoPropio.Furgoneta.cs`);
  `BultosEntregados` en Entregar (opcional; sin leer, salen los de la parada que llevaba; obligatorio con `RepartoEscaneoEntregaObligatorio`="1" en «(defecto)»).
- Recoger el ÚLTIMO bulto R de una reposición de tienda → `MarcarRecogida` (como `POST Reposiciones/Recogida`) y `haCambiado` al grupo
  `Reposiciones:{tienda}` del AlmacenHub (Nesto, Enviar reposición). `GET Reposiciones/Puntual/Estado` trae BultosRecogidos/BultosTotal/RepartidorRecogida.
- Red de seguridad (`RedSeguridadFurgoneta`): recibir una REPO y el abono de una NR cierran lo que siga en una furgoneta. Job
  `furgoneta-sin-descargar` L-V 22:00 (campana a Almacén, Dirección y el repartidor). `GET api/Reposiciones/{traspaso}/Seguimiento`.
- **Sin etiqueta RP para recogidas y cobros (NestoAPI#847, 11/11/26)**: `ImprimirEtiqueta` y la reimpresión de `Tramitar` (la de Nesto)
  dan 409 con el motivo si el envío de la 14 es la parada de una nota de Recogida o de Cobro (`EtiquetaRepartoPropioDeNota`); sus
  bultos son NR{pedido}-{N}. Reimprimir la RP de una recogida dejó un bulto fantasma en la furgoneta. Si no se sabe el tipo, se imprime.
- **Packing entre dos PDA (incidencia 1169, Ariadna)**: F8 en la lista no vale para una entrega que ha terminado OTRA PDA y solo está
  resaltada (se reimprimía el envío del compañero y la propia no salía); vale al entrar en ella o elegirla con las flechas.
- **Incidencias 1189/1190 (11/11/26)**: cargar la parada de una RECOGIDA (o de una NC) ya no apunta Cargado RP (no hay bulto; la carga en
  `EnviosAgenciaCargaBultos` sigue: repartidor, soloMios, SinCargar igual). Una incidencia de un envío sin bultos en ninguna furgoneta
  apunta su Cargado (salió sin leer). Descargar admite el papel `NE{pedido}` (todos los RP de su envío en furgoneta) y con una NR cierra
  el RP sin bulto de su recogida. `SinLeer` = true en Descargar: quita de SU furgoneta un bulto de GET Furgoneta (409 `EN_OTRA_FURGONETA`
  si es de otro), rastro en Modificaciones (`FurgonetaDescargaSinLeer`) y campana a Almacén. La respuesta trae `Descargados`.

### Notificaciones urgentes (NestoAPI#775, 31/10/26)
- La urgencia va **por tipo**, en código: lista blanca `NotificacionesUrgentes.TIPOS` (`Infraestructure/Notificaciones/NotificacionesUrgentes.cs`).
  Hoy solo `AutorizacionReembolsoReparto` (#773). Al guardarse en el buzón de Nesto, además de `hayNotificacionesNuevas` se manda
  `hayNotificacionUrgente` (AvisosHub) y Nesto abre una ventana roja con pitido. Datos["referencia"] identifica lo que hay que contestar.
- Al contestarse: `notificacionResuelta` {Tipo, Referencia, Respondio, Resultado} a todos los destinatarios → se cierra en todos los Nesto.
- `GET api/Notificaciones/Urgentes`: las urgentes del buzón del usuario (24 h) que siguen pendientes (cada tipo con su
  `IComprobadorUrgentesPendientes`). Nesto lo pide al arrancar y en cada (re)conexión; sin sondeo.
- Autorización de reembolso: el primer aviso va SOLO a Administración; a los 2 min (`BackgroundJob.Schedule`,
  `EscaladoAutorizacionesReembolsoJobs`) si sigue Pendiente va también a Dirección (menos los que ya son de Administración) y vuelve a sonar
  en Administración. La revisión de las telefónicas sigue yendo a las dos. `POST …/Envios/{id}/Autorizacion` devuelve `Conectados`
  (Administración con Nesto abierto, `UsuariosConectadosNesto.Conectados`) y `ConectadosNombres`.

### Día de reparto de nuestra ruta: código postal y excepción por contacto (NestoAPI#764, #799)
- Las rutas propias (`RutasPropiasConfiguradas`: AT, 16…) tienen día de reparto: el del CP de entrega en `CodigosPostalesDiaReparto`
  (`DiasRepartoCodigosPostales`) o, si el contacto tiene excepción, el de `ClientesDiaReparto` (`DiasRepartoClientes`, #799,
  `Scripts/Issue799_ClientesDiaReparto.sql`). Ambas fuera del EDMX, caché de 10 min, sin tabla = sin días.
- LA REGLA: `DiasRepartoCodigosPostales.DiaRutaInterna(ruta, cp, empresa, cliente, contacto)` (la excepción manda). Úsala siempre que
  se conozca el cliente (fecha de entrega, FechaEntregaAgencia, fechas futuras, aviso de cierre, picking, sugerencia de ruta).
- `GET/PUT api/Clientes/DiaReparto` (PUT: Dirección/Almacén/Administración; Ruta null quita): invalida la caché y recalcula las líneas
  pendientes sin picking del contacto por ruta propia al día nuevo de ESA semana (`RepositorioDiasRepartoClientes.NuevaFechaEntrega`).

### Festivos en tabla (NestoAPI#779, 04/11/26)
- `GestorFestivos.EsFestivo` (remesas, fechas de entrega, picking, `JobsFestivos`, avisos…) lee los festivos del año de
  `CalendarioFestivos.DelAnio`: tabla `dbo.Festivos` (Fecha, Tipo, Descripcion, Usuario, FechaModificacion; fuera del EDMX;
  `Scripts/Issue779_Festivos.sql`), caché de 10 min, `Invalidar` al escribir. Tipos = enum `TipoFestivo`; las reglas por delegación no cambian.
- Por qué «Algete» NO cuenta para ALG (Carlos 04/11/26): en Algete unos empleados cogen las fiestas de Algete y otros las de Madrid, pero el
  almacén abre; Algete solo cierra en nacionales y autonómicas. Reina cierra en las locales de Madrid y Alcobendas en las de Alcobendas.
- Respaldo: sin tabla → la lista del código (`FiestasDelCodigo`, sin ELMAH); tabla sin filas del año → ELMAH una vez por año y la
  lista del código de ese año (o nada). La lista del código se quita cuando la tabla esté en producción.
- `GET api/Festivos?anio=`, `POST api/Festivos`, `DELETE api/Festivos/{fecha}/{tipo}` (escribir: Administración/Dirección/Informática).
  Job `festivos-sin-cargar` (día 1 de cada mes; en diciembre mira el año siguiente) avisa a Dirección (campana + correo).
- **Día sin reparto propio (NestoAPI#841, 09/11/26)**: tipo `RepartoPropio` (no sale la furgoneta). NO es festivo de ningún almacén
  (`EsFestivo(fecha, ALG/REI/ALC)` lo ignora); `GestorFestivos.EsDiaSinRepartoPropio` y LA REGLA del reparto propio
  `GestorFestivos.EsFestivoRepartoPropio` (= `EsFestivo(fecha, GestorFestivos.REPARTO_PROPIO)`: festivo de Algete o sin reparto).
  Frena AT/16 (fecha de entrega, picking de rutas internas, RP, paradas), las llegadas de reposición (`CalculadoraFechaReposicion`) y el
  aviso de las 22:00 de la furgoneta. Lo dan de alta también Almacén (solo ese tipo; no hay grupo «Logística» en el dominio, Carlos 09/11/26). Requiere `Scripts/Issue841_FestivosRepartoPropio.sql` (CHECK).

### Nota de cobro: parada de solo cobro (NestoAPI#786, 04/11/26)
- `NotasEntregaCabecera.Tipo` = «Cobro» (`TiposNotaEntrega.COBRO`; `Scripts/Issue786_NotasCobro.sql`: CHECK ampliada, columna
  `ImporteCobro` y tabla `NotasEntregaCobroEfectos` con los Nº Orden de ExtractoCliente en su orden). Pedido con NotaEntrega = 1 **sin
  líneas**: no entra en picking, packing ni Facturar rutas. No se crea ni se cambia por POST PedidosVenta / PUT Cabecera (400).
- `POST api/NotasEntrega/Cobro` (`ServicioNotasCobro`) `{ Cliente, Contacto?, Efectos?, Importe?, Fecha?, Comentario? }` → 201
  `NotaCobroDTO`; en la MISMA transacción crea la parada: envío de la agencia 14 ya tramitado, Bultos 0, Reembolso = importe (la suma de
  lo pendiente si no se da). Solo rutas propias; 409 si el contacto ya tiene otra en el reparto. Avisa `repartoCambiado` (Motivo «Parada»).
- En `GET api/RepartoPropio/Envios`: `TipoParada` «NotaCobro», `EsNotaCobro`, `CodigoDocumento` NC{pedido}, `EfectosCobro` (pendiente de
  ahora) y `EmpresaCobro` de los efectos (uno de la 3 → sin tarjeta). No va en la furgoneta, ni en SinCargar, ni se leen bultos al entregar.
- Entregar: efectivo → `ServicioCobrosReparto.Cobrar` (#766) con sus efectos y la MISMA clave que la entrega (idempotente; antes de tocar
  el envío), asiento y caja en la prueba; tarjeta → el enlace del envío lleva los efectos (`LiquidacionNotaCobro`: suman justo el importe,
  si no, a cuenta); sin cobro → autorización (#773). Firma: `TextoConformidadEntrega.Cobro` («he pagado X €»). Gemelas: Ariadna (PDA) y Nesto (pantalla).
- **Repartidor asignado (Carlos 04/11/26)**: `Repartidor` obligatorio en el POST (400 si no es de `GET api/RepartoPropio/Repartidores`:
  sin grupo de seguridad de reparto, son los que han movido o cargado bultos de la furgoneta en 90 días + el que lo pide,
  `ServicioRepartidores`). Se guarda en `NotasEntregaCabecera.Repartidor` (`Scripts/Issue786b_NotaCobroRepartidor.sql`; sin él, 400 al
  crear). En `GET Envios` la NC lleva ese `Repartidor` (no se carga: `CompletarCarga` no lo borra) y con soloMios solo le sale a él;
  las NC de antes (sin repartidor) siguen saliendo a todos. Ariadna no filtra: lo hace la API.

### Stock de la web al momento (NestoAPI#818, 05/11/26)
- La fuente de verdad es `ExtractoProducto`: el disparador `trgExtractoProducto_SyncTienda` (AFTER INSERT/UPDATE/DELETE,
  `Scripts/Issue818_ExtractoProducto_EncolarSyncTienda.sql`) encola en `Nesto_sync` cada producto (y sus kits) cuya sentencia
  no sume cero por producto y almacén (empresas 1 y 3; ALG, REI, ALC; ficha de la empresa 1 con Estado >= 0; sin repetir pendientes).
  El +1/-1 de la carpeta o de un cambio de ubicación suma cero y no encola (Carlos 05/11/26). Nunca tumba el apunte (TRY/CATCH sin ROLLBACK).
- No hace falta encolar a mano al recibir, inventariar o regularizar, ni pasar `sincronizar-stocks-nocturno` a horario: queda
  de red (LinPedidoVta por fuera de la API, DescuentosProducto, encolados perdidos). Tras encolar, `@@IDENTITY` es el Id de Nesto_sync.

### Origen del cliente: campaña (lead) o canal (NestoAPI#848, 09/11/26)
- `dbo.OrigenesCliente.EsLead` (DEFAULT 1): 1 = campaña (META-HARMONIA-0926…: hay que llamarlo), 0 = canal (WEB, AMAZON, TIENDA-REINA,
  TIENDA-ALCOBENDAS, CURSOS: solo un dato para medir). `Scripts/Issue848_OrigenesCanal.sql` ANTES de la API (la API lee la columna).
- Los de canal NO son leads: `SQL_LEADS` filtra `o.EsLead = 1` y `ReglasLeadsCampana.PendienteDeLlamar` los descarta (job
  `leads-sin-llamar`, sugerencias de contacto); `POST api/Clientes/Origenes/ImportarLeads` con uno de canal → 400. A Odoo
  (`nesto_sync`) el origen solo viaja desde el importador de leads, así que los de canal no llegan.
- Propuesto por usuario (dato, no código): ParametrosUsuario «OrigenClientePorDefecto» (empresa 1; fila del usuario o «(defecto)»,
  vacía = sin propuesta; no se crea fila al leer). `GET api/Clientes/Origenes` marca `PropuestoParaElUsuario`; Nesto lo preselecciona en el alta.

### Prueba de entrega de cualquier agencia (10/11/26)
- `GET api/EnviosAgencias/{id}/PruebaEntrega` (`PruebasEntregaController`, permisos de `PermisosRepartoPropio.PuedeVerDatosPersonales`):
  siempre 200 con `PruebaEntregaEnvioDTO` (Disponible false + Mensaje si no hay: agencia sin prueba, sin entregar, la agencia
  falla); 403 sin permiso, 404 si no existe. Documento en base64 + TipoDocumento, o UrlDocumento. Nunca se guarda copia: se pide
  a la agencia en cada consulta. `GET api/RepartoPropio/Envios/{id}/PruebaEntrega` y `…/Firma` siguen igual.
- `ServicioPruebasEntrega` resuelve el proveedor por la agencia en el registro de perfiles SIN puerta y convierte cualquier fallo
  en mensaje (ELMAH). Proveedores: `PruebaEntregaRepartoPropio` (la de Ariadna + firma PNG), `PruebaEntregaCTT` (`cls/pods/{código}`
  con hash MD5 de código + centro + CP destino; receptor del evento 2100 del seguimiento), `PruebaEntregaGls` (GetExpCli: pod,
  NombreEntrega, DniEntrega y la imagen de la digitalización «POD», que hoy es un JPEG y se trae desde el servidor).
- **Agencia nueva** = una clase `IProveedorPruebaEntrega` + `IPerfilConPruebaEntrega` en su perfil (`PerfilesAgencias.cs`). Ni el
  endpoint ni Nesto cambian (Nesto: `MostradorPruebaEntrega` en ControlesUsuario, en Agencias › Tramitados y en el detalle del pedido).

### Pedidos de la tienda: ofertas vigentes el día del pedido (feria online 12-16/11/26)
- `GestorPrecios.EsPedidoValido` valida un pedido de la tienda online (`VigenciaPedidoTienda.EsPedidoTiendaOnline`: «TOTAL PEDIDO:»
  en comentarios + todas las líneas WEB; la misma regla usa `ValidadorRegalosTiendaOnline`) con la vigencia de DescuentosProducto,
  OfertasPermitidas, combinadas, escalonadas y regalos por importe del día `pedido.fecha` (en el PUT, `cabPedidoVta.Fecha`), si es
  anterior a hoy y de hace como mucho `DIAS_MAXIMOS_ATRAS` (15). Cómo: `ServicioPrecios.ParaDia(dia)` (instancia, sin estáticos);
  el motor de precios y las sugerencias siguen con hoy. Regalos por importe con día: por día inclusive.

### Correos mandados a mano a nombre de quien los manda (sugerencia 1167, 11/11/26)
- Pedido, presupuesto y mandato (`GestorEnvioDocumentosCorreo`) y facturas A MANO (`GestorFacturas.EnviarFacturasACorreo`, vía
  `RemitenteDelUsuario`, que solo pone `FacturasController`): From con la MISMA dirección de la serie (Office 365 autenticado rechaza
  otra) y nombre «{Nombre} - Nueva Visión»; Reply-To y copia a su CorreoDefecto; firma con su nombre, el teléfono de la firma de la
  serie y su correo. Nombre = `Vendedores.Descripción` del vendedor (claim «Vendedor» o ParametrosUsuario «Vendedor»), si no, el usuario.
  El envío diario y la factura de Verifactu no cambian.

### Agency and Shipping
- Multiple shipping agencies supported
- Special handling for Glovo (`GestorAgenciasGlovo`)
- Shipping cost tracking through specific accounts

## Testing Notes

- Tests use FakeItEasy for mocking
- `InternalsVisibleTo` exposes internal classes to test assembly
- Test pattern: `[TestClass]` with `[TestMethod]` attributes
- Recent test additions: `GestorPreciosTests` for validation pipeline
- Controllers can be instantiated with mock `NVEntities` for testing

## Configuration

- Web API configuration in `App_Start/WebApiConfig.cs`:
  - JSON-only responses (XML removed)
  - Reference loop handling enabled
  - CORS commented out but available

- Connection strings in `Web.config`:
  - Main database connection (Entity Framework)
  - "NVIdentity" for ASP.NET Identity

### Rounding System (Issues #242/#243)
The application uses `RoundingHelper` for all decimal rounding operations:
- **Location**: `Infraestructure/RoundingHelper.cs`
- **Default mode**: `AwayFromZero` (commercial rounding, compliant with Spanish legislation)
- **Rollback**: Set `RoundingHelper.UsarAwayFromZero = false` to revert to VB6-style `ToEven` rounding
- **SQL Server**: Uses `ROUND()` which is already AwayFromZero - no changes needed
- **Auto-fix preventivo**: `ServicioFacturas.CrearFactura()` recalcula las líneas del pedido ANTES de llamar al stored procedure de facturación. Esto previene errores de descuadre causados por diferencias de redondeo entre C# y el SP `prdCrearFacturaVta`. El fix se loguea a ELMAH para diagnóstico.
- **Restricción BD (CK_LinPedidoVta_5)**: `([bruto]=[precio]*[cantidad] OR [tipolinea]<>(1))` - Esta restricción impide modificar Bruto sin modificar también Precio. Por eso el auto-fix NO puede cambiar Bruto.
- **CLAVE PARA EL ASIENTO CONTABLE (02/12/25)**: El SP `prdCrearFacturaVta` construye el asiento usando:
  - HABER Ventas (700): `SUM(ROUND(Bruto, 2))`
  - DEBE Descuentos (665): `SUM(ROUND(Bruto * Dto, 2))`
  - La diferencia (Ventas - Descuentos) debe coincidir con `SUM(BaseImponible)`
- **Cálculo correcto de líneas (coherente con el asiento contable)**:
  - `Bruto = Cantidad * Precio` - **NO se puede redondear** por restricción `CK_LinPedidoVta_5`
  - `ImporteDto = ROUND(Bruto * SumaDescuentos, 2)` - Se redondea el descuento ANTES de restar
  - `BaseImponible = ROUND(Bruto, 2) - ImporteDto` - **USAR ROUND(Bruto, 2)** para que cuadre el asiento
  - `ImporteIVA = BaseImponible * PorcentajeIVA / 100` - Sin redondear (precisión completa)
  - `ImporteRE = BaseImponible * PorcentajeRE` - Sin redondear (precisión completa)
  - `Total = BaseImponible + ImporteIVA + ImporteRE` - Sin redondear (precisión completa)
- **Por qué ROUND(Bruto, 2) en BaseImponible**: Si Bruto tiene más de 2 decimales (ej: 67.4325 de CanalesExternos), la diferencia con ROUND(Bruto,2) (ej: 67.43) se acumula en múltiples líneas y descuadra el asiento. El SP usa ROUND(Bruto,2) para Ventas, así que BaseImponible debe calcularse igual.

Related files:
- `RoundingHelper.cs` - Rounding logic with configurable mode
- `ValidadorDescuentoPP.cs` - Validates early payment discount calculations
- `ServicioFacturas.cs` - Auto-fix preventivo en `CrearFactura()` y `RecalcularLineasPedido()`
- `GestorFacturacionRutas.cs` - Logs detailed diagnostics on mismatch errors
- `GestorPedidosVenta.cs` - `CalcularImportesLinea()` calcula Bruto, ImporteDto, BaseImponible con redondeo correcto
- `RoundingHelperTests.cs` - Tests documentando el comportamiento de redondeo y cálculo de líneas

### ELMAH User Sync (05/12/25)
ELMAH no mostraba el usuario en los errores porque OWIN/JWT no sincroniza automáticamente el usuario con `HttpContext.Current.User`.

**Solución implementada**:
1. **`UserSyncHandler.cs`**: DelegatingHandler que copia `request.GetRequestContext().Principal` a `HttpContext.Current.User`
2. **`WebApiConfig.cs`**: Registra el handler con `config.MessageHandlers.Add(new UserSyncHandler())`

**IMPORTANTE - NO usar TokenValidationParameters (09/12/25)**:
El commit c4cbbd1 añadió `TokenValidationParameters` a `JwtBearerAuthenticationOptions`, pero esto causaba
que OWIN ignorara `AllowedAudiences` e `IssuerSecurityKeyProviders`, rechazando todos los tokens JWT.
Ver `StartupJwtConfigurationTests.cs` para detalles del bug y su documentación.

**Flujos de autenticación soportados**:
| Aplicación | Endpoint | Usuario en ELMAH |
|------------|----------|------------------|
| NestoApp (vendedores) | `/oauth/token` | `UserName` de Identity |
| Nesto (empleados) | `/api/auth/windows-token` | `DOMINIO\Usuario` |
| TiendasNuevaVision (clientes) | `/api/auth/token` | `email` |

Related files:
- `Infraestructure/UserSyncHandler.cs` - DelegatingHandler para sincronizar usuario
- `App_Start/WebApiConfig.cs` - Registro del handler
- `StartupJwtConfigurationTests.cs` - Documentación del bug de TokenValidationParameters

## Recent Changes

Based on git status, recent work includes:
- Copiar Factura / Rectificativas (Issue #85) - Create rectificativas from existing invoices
- Rounding system refactoring (Issues #242/#243) - Changed to AwayFromZero with rollback capability
- Refactoring of validator system (`ValidadorOfertasYDescuentosPermitidos` split into separate validators)
- New offer management system (`GestorOfertasPedido`)
- Commission system refactoring (`GestorComisiones`)
- HR festivos (holidays) management (`GestorFestivos`)
- Video product management (`VideosProductos`)
- Prestashop login integration for video module
