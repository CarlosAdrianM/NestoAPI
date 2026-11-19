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
- **Entregado cierra el envío (caso 251099, Carlos 19/11/26)**: Entregar un ENVÍO apunta Entregado a los leídos y también a los demás RP
  de ese envío que lleve encima QUIEN ENTREGA (`CierreEntregaFurgoneta`): Descripcion con « (entregado sin leer)» y UNA fila en Modificaciones
  `FurgonetaEntregaSinLeer` (también al no leer ninguno). Los que lleve otro repartidor y las paradas de reposición no cambian. Lectura
  obligatoria = como hoy, al menos uno. Job de las 22:00: antes de avisar, los RP de envíos de la 14 ya en Estado 2 que sigan en una furgoneta
  se cierran igual (Origen Almacen, usuario el repartidor, rastro por envío) y no cuentan para el aviso; si no puede mirar el estado, avisa como siempre.

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

### Excepciones del calendario de reposiciones (NestoAPI#867, 16/11/26)
- `dbo.ReposicionesCalendarioExcepciones` (fuera del EDMX, `Scripts/Issue867_ReposicionesCalendarioExcepciones.sql`; sin ella todo como antes y los
  endpoints 503): una fila por empresa + origen + destino + día de LLEGADA, Tipo `SinReposicion` (quita ese día) o `ReposicionExtra` (añade uno, con
  HoraCierre/LaborablesAntelacionCierre/HoraLlegadaHabitual). LA REGLA: `ReglaCalendarioReposiciones.ViajesDelDia` (pura); la usa
  `CalculadoraFechaReposicion` (próxima llegada, fechas de entrega, cortes del job: un día quitado ni se rellena ni avisa «fuera de plazo»). Una extra
  solo cuenta en una ruta con alguna fila activa; festivos y días sin reparto propio siguen mandando. Caché `ExcepcionesCalendarioReposiciones`
  (5 min, `Invalidar` al escribir; la calculadora la lee sola si no se le inyectan).
- `GET/POST api/Reposiciones/Calendario/Excepciones` (de hoy en adelante; POST = crear o cambiar por la clave) y `DELETE …/{id}`, con el permiso de
  PUT Calendario. 400: fecha pasada, quitar un día que el calendario no tiene (o festivo), extra en un día que sí tiene o con otra antelación; 409 si
  la hora de cierre del viaje afectado ya ha pasado. Rastro en Modificaciones. Nesto: sección «Excepciones» en la ventana del calendario.

### Inventario por huecos en almacenes con ubicaciones (incidencia 1227, 12/11/26)
- `POST api/Inventarios` en un almacén con `Almacenes.ControlUbicaciones` de un producto con `Ubicar` exige Pasillo, Fila y Columna (si no, 400
  «En {almacén} el inventario va por huecos…») y pone de teórico (`StockCalculado`) lo LIBRE de ese producto en ese hueco (Ubicaciones Estado 0,
  0 si no estaba), como el rellenado de Ariadna (`ILectorHuecoInventario`). `PUT` que quitaría el hueco a una fila que lo tenía: 400. Tiendas,
  productos que no se ubican, «Dejar a cero», rellenado de Ariadna y «No está» no cambian (no pasan por aquí).

### Años disparatados en el calendario (12/11/26)
- `CalendarioFestivos.DelAnio` de un año anterior a 2000 o de más de 5 por delante del actual (`EsAnioDisparatado`: las centinela 31/12/2099,
  2100…) → sin festivos, sin ELMAH y sin leer la tabla (los fines de semana siguen contando). El aviso del año que viene sin cargar no cambia.
  Las fotos de `fechas-entrega-fotos-sombra` guardan esas fechas como «sin fecha» (`ReglasFotosFechaEntrega.SinFecha`).

### Nota de cobro: parada de solo cobro (NestoAPI#786, 04/11/26)
- `NotasEntregaCabecera.Tipo` = «Cobro» (`TiposNotaEntrega.COBRO`; `Scripts/Issue786_NotasCobro.sql`: CHECK ampliada, columna
  `ImporteCobro` y tabla `NotasEntregaCobroEfectos` con los Nº Orden de ExtractoCliente en su orden). Pedido con NotaEntrega = 1 **sin
  líneas**: no entra en picking, packing ni Facturar rutas. No se crea ni se cambia por POST PedidosVenta / PUT Cabecera (400).
- `POST api/NotasEntrega/Cobro` (`ServicioNotasCobro`) `{ Cliente, Contacto?, Efectos?, Importe?, Fecha?, Comentario? }` → 201
  `NotaCobroDTO`; en la MISMA transacción crea la parada: envío de la agencia 14 ya tramitado, Bultos 0, Reembolso = importe (la suma de
  lo pendiente si no se da). Cualquier cliente, sea cual sea la ruta de su ficha (Carlos 13/11/26: si Almacén o Administración mandan a cobrar, es por algo); 409 si el contacto ya tiene otra en el reparto. Avisa `repartoCambiado` (Motivo «Parada»).
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
- **Un día de margen (Black Friday 2026, Carlos 18/11/26)**: para esos pedidos de la tienda (mismo usuario permitido), lo que acabó
  el día D vale todavía para un pedido del D+1 (`VigenciaPedidoTienda.DIAS_MARGEN_TIENDA` = 1; regla `VigenteParaPedidoTienda` /
  `Vigencia.EsVigente(desde, hasta, dia, diasMargenFin)`): `vigencia-campanas` la retira hacia la 1:00 y la tienda tarda un par de
  horas en actualizarse. Por eso el día del pedido cuenta también si es HOY (`ParaDia(dia, diasMargenFin)`). Solo por el FINAL: lo
  que aún no ha empezado no vale. Vendedores (Nesto, NestoApp) y pedidos que no son de la tienda: acaba el día D, como siempre.

### Oferta N+M que además admite el precio especial (Novedades 1263, 13/11/26; continuación de #589)
- La regla general NO cambia: un N+M exige cobrar a precio de ficha y sin descuento (`ReglasOfertasPermitidas.MotivoCobradoFueraDeTarifa`).
  Excepción por DATO: `OfertasPermitidas.AcumulaPrecioEspecial` = 1 (fuera del EDMX, `Scripts/Novedades1263_OfertaAcumulaPrecioEspecial.sql`;
  sin la columna, ninguna acumula) en una oferta PROPIA del producto (no vale en la de la familia). `OfertasQueAcumulanPrecioEspecial`
  (caché de 5 min) + `ReglasOfertasPermitidas.PrecioEspecialAdmitido` (pura): el suelo del precio pasa a ser el precio especial vigente
  del producto para ese cliente; ni por debajo ni con descuento. Primer caso: packs 45929/45930/45931, 6+1 del 12 al 16/11/26 (feria online).
- `GET api/PlantillaVentas/CargarPrecio` admite `cantidadOferta` (opcional): con unidades de oferta, `aplicarDescuento` = false y la oferta
  marcada, devuelve el precio especial sin descuento (`AplicarPrecioEspecialQueAcumulaConLaOferta`). Nesto y NestoApp solo mandan ese dato.
  Las sugerencias de N+M siguen exigiendo precio de ficha (no proponen el 6+1 en un producto con precio especial).

### Embudo de Odoo: bloque diario (11/11/26)
- Job `embudo-bloque-diario` TODOS los días (también fines de semana y festivos, a propósito) a las 7:00 de Madrid
  (`EmbudoBloqueDiarioJobsService`): el siguiente bloque NV-AAAA-MM-NN (NN vuelve a 01 cada mes) de 50 por `ServicioEmbudoClientes.Republicar`
  (el mismo camino que `POST api/Clientes/Sync/Republicar`: ensayo y, si llena el bloque, de verdad). Si hoy ya hay un bloque, no lanza.
  Sin tabla o si falla la consulta o el lanzamiento → no lanza, ELMAH y campana a Dirección. Menos de 50 pero alguno → lanza con
  los que haya, sin aviso (agotada la cola entran unos 45 al mes); ninguno → no lanza, solo ELMAH. Interruptor: ParametrosUsuario «(defecto)»
  `EmbudoBloqueDiario` = 1 (sin la fila, apagado) y tamaño `EmbudoTamanoBloque` (sin fila o no válido, 50; tope 100 por Office 365, se recorta y se
  registra); los crea `Scripts/EmbudoBloqueDiario_Interruptor.sql` (encendido, 50). Se ven y cambian con GET/PUT
  `api/Clientes/Embudo/Configuracion` (Dirección/Informática; Usuario2 + fila en Modificaciones). El lanzamiento a mano sigue topado en 50. Orden (manual y job, el mismo `SqlCandidatos`, 11/11/26): los más
  recientes primero (último pedido DESC; luego los que nunca compraron por FechaAlta DESC), los antiguos al final (rebotan más).
  **Solo dormidos (Carlos 17/11/26)**: entran únicamente los que nunca han comprado o con último pedido de hace más de
  `RepositorioClientesEmbudoSql.ANIOS_SIN_COMPRAR` (2) años (`@dormidoDesde`), también los de origen AMAZON/WEB. Sin ese filtro los bloques
  NV-2026-11-03..08 metieron 325 clientes vivos; Carlos los saca de `ClientesEmbudo` con `Scripts/OneShot_20261117_Embudo_SacarClientesVivos.sql`.
- **Numeración (18/11/26)**: un número ya usado no se repite aunque el bloque se borre de `ClientesEmbudo` (Odoo agrupa por el nombre, también lo archivado): el job apunta el último
  lanzado en ParametrosUsuario «(defecto)» `EmbudoUltimoBloque` (sin rastro en Modificaciones; sin fila, solo el registro) y `SiguienteBloque` nunca numera por debajo.
  `BLOQUES_QUEMADOS` = NV-2026-11-08 (el 17/11 se borraron 03-08 y el del 18/11 repitió el 03; desde el 19/11 sigue en el 09).
- Job `embudo-ajuste-ritmo` 6:45 Madrid (**APAGADO desde el 17/11/26**: solo actúa con ParametrosUsuario «(defecto)» `EmbudoAjusteRitmo` = «1»; sin fila, nada. Se apagó al pasar a solo dormidos: la recomendación de Odoo se calculó con clientes vivos; Odoo tampoco calcula. Encender con ~5 tandas de dormidos) (Odoo calcula a las 4:30 UTC; 404 = sin cálculo aún: nada, sin aviso) (`EmbudoAjusteRitmoJobsService`): GET {OdooEmbudoRitmoUrl}/nv/embudo/recomendacion con cabecera
  `X-NV-Clave` = OdooEmbudoRitmoClave (las dos en secretos.config; sin ellas, nada ni aviso). Reglas puras `ReglasRitmoEmbudo`: escalones
  50 → 75 → 100 (tope 100); «subir» un escalón si hace 7 días del último cambio (`EmbudoTamanoBloqueFecha`, también el PUT) y el dato
  (calculadoEl) tiene menos de 8, sin pasar de escalonRecomendado; «bajar» un escalón en el acto; «parar» apaga el interruptor;
  dato viejo/ilegible/403/red → nada y aviso a Dirección una vez al día (`EmbudoRitmoUltimoAvisoFallo`). Cada cambio: Modificaciones
  con motivo y cifras de Odoo + campana. La última recomendación (`EmbudoRecomendacion*`) sale en GET Configuracion. «subir» exige además el DKIM de Office 365
  publicado (`IComprobadorDkim`, DNS de selector1._domainkey.nuevavision.es; hoy NO existe): si no, no sube y avisa como mucho 1 vez/semana.
- **Orígenes AMAZON y WEB (Carlos 16/11/26)**: además de los del vendedor NV, `SqlCandidatos` añade los clientes con origen de
  `ServicioEmbudoClientes.ORIGENES_QUE_ENTRAN` (AMAZON, WEB; constante en código, EsLead = 0) sea cual sea su vendedor y su estado (algún contacto
  ≥ 0), SIEMPRE con alguna persona con correo y boletín (`EntraPorOrigen`; también con SoloConCorreo = false) y sin estar en `ClientesEmbudo`.
  Mismo orden y tope. Por SQL dinámico (sin ClientesOrigen o sin EsLead, como antes). La respuesta trae `Origenes` (cliente → origen) e `IncluidosPorOrigen`.

### Eventos: candidatos a invitar, en MODO SOMBRA (NestoAPI#850, 11/11/26)
- Solo se recogen datos (nada visible; cortes 2-4 en la issue). `dbo.EventosCandidatos` (fuera del EDMX, `Scripts/EventosCandidatosSombra.sql`):
  foto por evento + cliente NV + día con características y puntuación `ReglaCandidatosEventos` (`reglas-1`, umbral 0,30, máx. 500) más
  300 de control al azar (semilla por evento y día). El resultado NO se copia: `dbo.vEventosCandidatosResultado`. Distancia: dirección de
  `DireccionesCoordenadas` o centro del CP (`dbo.CentrosCodigosPostales`, `Scripts/CentrosCodigosPostales_GeoNames.ps1`, GeoNames CC-BY)
  a `SedesReparto`. Al crear/anunciar (Hangfire) y job `eventos-candidatos-sombra` 3:15. `GET api/Eventos/CandidatosSombra` (Dirección/Informática).

### Correos mandados a mano a nombre de quien los manda (sugerencia 1167, 11/11/26)
- Pedido, presupuesto y mandato (`GestorEnvioDocumentosCorreo`) y facturas A MANO (`GestorFacturas.EnviarFacturasACorreo`, vía
  `RemitenteDelUsuario`, que solo pone `FacturasController`): From con la MISMA dirección de la serie (Office 365 autenticado rechaza
  otra) y nombre «{Nombre} - Nueva Visión»; Reply-To y copia a su CorreoDefecto; firma con su nombre, el teléfono de la firma de la
  serie y su correo. Nombre = `Vendedores.Descripción` del vendedor (claim «Vendedor» o ParametrosUsuario «Vendedor»), si no, el usuario.
  El envío diario y la factura de Verifactu no cambian.

### Recoge en tienda (NestoAPI#571, cortes 1 y 2, 11/11/26)
- Sin columna: la ruta es la verdad. `TiendasRecogida.EsRutaDeRecogida` (AM, ALC, REI). `PedidoVentaDTO.RecogeEnTienda` (bool?): al leer,
  siempre calculado de la ruta; al escribir, null = no tocar (Nesto y apps viejas), y el POST/PUT lo traducen a ruta con las reglas puras
  de `Infraestructure/PedidosVenta/RecogeEnTienda.cs` (ALG→AM, ALC→ALC, REI→REI; false en ALC/REI → 400; el PUT compara con la ruta
  GUARDADA; bloqueo = `CambioAlmacenPedido.Motivo`; al desmarcar vuelve la ruta de la ficha, `RutaQueVale.Leer`).
- `GestorPortes.CalcularPortes`: ruta de recogida (o `PedidoPortesInput.RecogeEnTienda` = true, la plantilla de NestoApp) → ni portes NI
  comisión contra reembolso. Las demás rutas sin portes siguen llevando comisión. Pendientes: picking de recogidas de ALG e impresión en AM.

### Novedades por Id (NestoAPI#495, 11/11/26)
- `GET api/Novedades?ambitos=NestoApp,NestoAPI&desdeId=n` (`ambitos` manda sobre `ambito`; sigue el filtro por perfiles). Parámetros
  `UltimaNovedadVistaApp`/`UltimaNovedadVista` por ParametrosUsuario (`Scripts/Issue495_UltimaNovedadVista.sql`). Una sugerencia implementada
  conserva su Id antiguo: con `desdeId` no sale. NestoApp ya lo usa (2.28.0); el lado Nesto, pendiente.

### Correos y WhatsApp a nombre de quien los manda: flecos (NestoAPI#854, NestoApp#272, 11/11/26)
- `ServicioCorreoElectronico.EnviarCorreoSMTP` apunta en ELMAH el correo que el SMTP rechaza tras el reintento (asunto, From, To, motivo).
- `ProveedorDocumentosCorreo.NombreVendedorDelUsuario` (claim «Vendedor» o parámetro «Vendedor»; null si no se conoce, SIN caer al usuario):
  firma del WhatsApp (`GestorEnlacesDescarga.Firma`, `EnlaceDescargaDTO.Remitente`). `FacturasController` pasa ya el claim al remitente.

### Correos de seguimiento de compras: fase 1 en sombra y fase 3 (NestoAPI#857, 12/11/26)
- Job `compras-correos-seguimiento` (cada 30 min, L-V 8:00-18:30 Madrid, `Infraestructure/PedidosCompra/CorreosSeguimiento/`): lee por
  Microsoft Graph (HttpClient, client credentials, SOLO lectura: nunca marca ni mueve) la bandeja de entrada del buzón de Compras con delta
  (`ComprasCorreosSeguimientoEstado`; sin enlace, los últimos 7 días; 410 = empezar de nuevo). Claves en secretos.config `ComprasBuzonTenant`,
  `ComprasBuzonClientId`, `ComprasBuzonSecreto`, `ComprasBuzon`: sin ellas no hace nada ni avisa. Sin `Scripts/Issue857_ComprasCorreosSeguimiento.sql`,
  tampoco (ELMAH una vez). 401/429/red: ELMAH y sigue desde la misma página en la siguiente pasada; el delta solo avanza si todo se guardó.
- Guarda en `ComprasCorreosSeguimiento` (idempotente por MensajeId) SOLO los correos con seguimiento o de un remitente marcado
  (`AgenciasEntradaRemitentes`): agencia, primer código/URL (JSON con todos), extracto de 500, nada de cuerpo ni adjuntos. Agencias:
  `IAgenciaEntrada` (núcleo `AgenciaEntradaPorPatrones` + una clase de datos por agencia en `AgenciasEntradaRegistradas`, `HorasHastaEntrega`
  24/48) + `AgenciaEntradaGenerica` (descubre: agencia NULL). Enlaces desenvueltos sin peticiones (safelinks, Proofpoint v2, google.com/url, redirect=…).
  Lo que capta el genérico (sin remitente marcado) lleva la agencia que el texto NOMBRE tras una etiqueta («Transportista:», «Enviado por:», «Agencia:»,
  «Carrier:», «Shipped via»…; `AgenciaPorNombreEnTexto`, `NombresEnTexto` de cada agencia, 40 caracteres de esa línea, gana el nombre más largo); el nombre
  suelto no cuenta. `AnalisisCorreoSeguimiento.AgenciaPorNombreEnTexto` no se guarda (sin columna). MBE (Mail Boxes Etc) es agencia registrada.
- Asignación en sombra (`AsignadorPedidosCompra`, pura): número de pedido nuestro > dirección de un contacto > dominio (web/contactos; nunca
  gratuito, nuestro o de agencia) > nombre del proveedor; con proveedor, TODOS sus pedidos en el aire (-1/1 Enviado) son candidatos. El job
  rellena PedidoReal/FechaAlbaranReal con el primer candidato albaraneado desde el día del correo (para medir). No toca compras (fase 3).
- `GET/POST/DELETE api/Compras/AgenciasEntrada/Remitentes`, `GET …/Candidatos?dias=30`, `GET …/Correos?dias=7` (Compras/Dirección/Informática).
- **Histórico** (`ServicioHistoricoCorreosCompras`): `POST …/Historico?meses=12[&ahora=true]` (Dirección/Informática; 1-24; 202 encolado en Hangfire,
  409 si hay otra viva, 503 sin claves o tablas) y `GET …/Historico` (progreso). Lee la bandeja por fechas (`LeerPagina`, sin delta) con el MISMO
  núcleo (`GuardarCorreos`), asignando contra los pedidos en el aire EL DÍA del correo (`EnAireEl`) y midiendo por lotes por Id. Progreso en
  `ComprasCorreosSeguimientoEstado` fila `historico|{buzón}` (JSON; reanuda por cursor de fecha); 429 → Retry-After (tope 5 min); 5 fallos → Fallo + ELMAH una vez.
  L-V 8-20 Madrid no empieza: se programa a las 21:00 (salvo `ahora`).
- **Adjuntos** (`Scripts/Issue857b_ComprasCorreosHistoricoYAdjuntos.sql`: `NumAdjuntos`, `Adjuntos` JSON de 10, sin los incrustados): una petición a Graph
  solo por correo GUARDADO con `hasAttachments`; sin las columnas (caché 10 min) se guarda como antes y `…/Correos` los da a null.
- **Entrega estimada y salidas nuestras** (`EntregaEstimadaCorreo.cs`, `Scripts/Issue857c_ComprasCorreosEntregaEstimada.sql`, 5 columnas opcionales con
  el mismo trato que los adjuntos): `ExtractorEntregaEstimada` (puro) saca día y tramo tras una etiqueta o frase («Entrega estimada», «te lo entregaremos,
  como máximo, el…», «will be delivered on…»; una fecha suelta no cuenta; fuera de [día del correo, +60] nada). `ReglaDisponibleCompra`: manda el FINAL del tramo
  (Carlos 12/11/26): antes del corte del picking en laborable de Algete, ese día; si no, el siguiente laborable. `EnvioNuestroDeSalida` («tu pedido … de NUEVA
  VISION», «remitente: NUEVA VISION»…; nombrarnos como destinatario no cuenta): se guarda con `EsSalidaNuestra` y SIN proveedor, candidatos ni disponible.
- **FASE 3: ya NO es solo sombra** (`Scripts/Issue857d_ComprasCorreosAplicarFecha.sql`; sin él, todo como antes y los endpoints nuevos 503). El job pone
  `LinPedidoCmp.FechaRecepción` (+Usuario «Correo de seguimiento», [Fecha Modificación]) de TODAS las líneas en el aire del pedido si el envío es
  inequívoco (un solo candidato), el correo es de los últimos 3 días y ParametrosUsuario «(defecto)» `Compras.CorreosSeguimientoAplicar` = «1» (si no,
  no escribe). Fecha = DÍA DE LLEGADA (`ReglaFechaRecepcionPorCorreo`: la entrega más tardía del envío o recibido + 24/48 h; nunca antes de hoy), no el
  disponible. Pisa lo que hubiera; misma fecha → ni UPDATE ni rastro. Rastro: Modificaciones Tabla `CompraFechaPorCorreo`; marca en el correo
  (EmpresaAplicada, PedidoAplicado, FechaRecepcionAplicada, AplicadoEl, AplicadoPor). El histórico NUNCA aplica. Anota la promesa en riesgo (#693) si retrasa.
- **Mismo envío** (`MismoEnvio`, puro, sin columna): correos que comparten un código (Codigo + Seguimientos) con < 10 días; se deciden juntos al final
  de la tanda (caso UPS ×3 + MBE con el proveedor). Heredan los candidatos (motivo `MismoEnvio`); uno aplicado fija el pedido de los demás. Remitente del
  paquete (`ExtractorRemitentePaquete`, columna `RemitentePaquete`) y su memoria `ComprasRemitentesPaquete` (motivo `RemitentePaquete`, tras el número).
  Por asignar: UNA campana `CompraSeguimientoPorAsignar` a Compras por envío (`AvisarPendientes`, 20 min después de su último correo).
- `GET …/PorAsignar?dias=7` (por envío; candidatos con FechaEnvio, LineasPendientes/YaRecibidas y `Sugerido` = el enviado más reciente antes del primer
  correo, `ReglaPedidoSugerido`), `POST …/Correos/{id}/Asignar[?reasignar=true]` {Empresa, Pedido, RecordarRemitente} (también con el interruptor
  apagado; se propaga al envío; guarda `EligioSugerido`), `POST …/Correos/{id}/Descartar` (se propaga), `GET/DELETE …/RemitentesPaquete[/{id}]`,
  `POST …/Correos/Reanalizar?dias=7` (Dirección/Informática, 1-30: relee por fechas, rehace análisis y enlaces de lo no aplicado; no aplica ni avisa).
- **Avisos de salida del proveedor (#857e, Carlos 18/11/26; caso DDUUEETT 220923)**: no hay «correos de proveedor» aparte: el remitente marcado
  (`AgenciasEntradaRemitentes`, `Scripts/Issue857e_ComprasCorreosRemitentesPdf.sql`; sin él, como antes) puede ir SIN agencia (proveedor), con
  `FiltroTexto` («albarán de entrega|delivery note»: asunto o cuerpo, sin mayúsculas ni tildes, palabras enteras; si no casa, el correo es de un
  remitente sin marcar) y `LeerPdf`. Con `LeerPdf` (job y Reanalizar; el histórico NO) se bajan por Graph hasta 3 PDF de 5 MB y se leen EN MEMORIA
  (`ILectorTextoPdf` → PdfPig 0.1.16, 10 páginas; nada a disco ni a BD). `ExtractorAvisoSalida` (puro): pedido = número del texto que es un pedido
  en el aire de SU proveedor (etiquetado «REF/Pedido/PO…» primero; uno solo → inequívoco, motivo `PdfAdjunto`; sin proveedor, solo etiquetado),
  agencia por `AgenciaPorNombreEnTexto` («Compañía de transporte: SCHENKER») y entrega por `ExtractorEntregaEstimada`. Se guarda y aplica por el
  camino de siempre (fase 3, interruptor, 3 días, rastro, promesa en riesgo, reenvío con «Aviso de salida del proveedor: pedido N leído del PDF»).
  Columnas `AvisoProveedor`, `PedidoEnAdjunto`, `AdjuntoLeido` (SinPdf/Leido/SinTexto/Ilegible/Grande/NoDescargado). Certeza: un aviso del
  proveedor SIN código ni enlace (`CorreoAplicadoPedidoCompra.SinSeguimientoAgencia`) no es seguimiento de agencia → Aproximada, no CasiSegura.
- **Boletines y remitentes ignorados (#857e, falso positivo 18/11/26: boletín de comunicaciones@perfectbeauty.es por `track.…/click.php?L=…&J=…`)**:
  el genérico no cuenta los redirectores de clic (`RedirectorMailing`: dominios de envío masivo —sendgrid, list-manage, mailjet, acumbamail,
  brevo, hubspot…—, tramos click.php/click/ls/click/track/click… o parámetros L y J). Un boletín (`BoletinComercial`: «darse de baja»,
  «unsubscribe», «ver en el navegador»… o buzón comunicaciones@/newsletter@/news@/marketing@…) no se guarda por la vía genérica (solo si una agencia
  registrada saca su seguimiento o es de un remitente marcado). Marca `Ignorar` del remitente (columna del mismo Issue857e): sus correos ni se
  guardan, ni se reenvían, ni avisan. `POST …/Remitentes` con `Ignorar`; `POST …/Correos/{id}/Descartar?ignorarRemitente=true` deja la dirección
  exacta como ignorada (rastro `CompraCorreoRemitenteIgnorado` en Modificaciones; respuesta con `RemitenteIgnorado`). Nesto: botón pendiente.
- **Dos niveles (#857f, Carlos 19/11/26)**: NIVEL 1 = el FORMATO del aviso del proveedor (su ERP; `IFormatoAvisoSalida` en `FormatosAvisoSalida.cs`,
  núcleo `FormatoAvisoSalidaBase`; hoy `FormatoAvisoSalidaDduueett` y el genérico de respaldo): agencia, número de envío, pedido y su entrega. Formato
  nuevo = una clase + una línea en `FormatosAvisoSalida.Registrados`. NIVEL 2 = la AGENCIA (`IAgenciaEntrada`): además de reconocer y sacar el código,
  `EsCorreoPropio`, `LeerEntrega` (día, tramo y `EstadoEnvioAgencia` EnCamino/EnReparto/Reprogramada/Entregado de SUS correos; por defecto el extractor
  común; propias SEUR —«Entrega prevista Jueves 19 de Noviembre», «Se entregará HOY de 13:45 a 14:45»— y UPS) y `UrlSeguimiento` (UPS y DB Schenker/DSV).
  TODO lo de cada agencia vive en su clase (Carlos 19/11/26: nada de «si es Schenker…» fuera). Un correo de la propia agencia SIN código (SEUR, enlaces
  opacos) se guarda si dice entrega o estado; SEUR toma como código la referencia del remitente («Tu envío PV26-56989 de Postquam»).
  Precedencia (`ReglaFechaRecepcionPorCorreo`, por bulto): entregado > lo que dice la agencia (correo o consulta, `OrigenEntrega` Agencia/Consulta) >
  el aviso del proveedor (Proveedor; también filas de antes sin origen) > recibido + 24/48 h; nunca antes de hoy. Un correo «Entregado» no aplica
  fecha (desenlace `Entregado`). Consulta (marco, sin ninguna agencia hoy): `IConsultaEntregaAgencia` (se implementa SOLO en la clase de la agencia) +
  job `compras-consulta-entrega-agencias` L-V 7:30 Madrid (`ServicioConsultaEntregaAgencias`: aplicados con pedido en el aire, `ConsultaEntregaSegura`
  8 s y nunca lanza, `Corregir` con `PeticionAplicacionFecha.Corregir`), interruptor `Compras.CorreosSeguimientoConsultarAgencia` (solo «1»; escribir exige
  además el de aplicar). `Scripts/Issue857f_ComprasCorreosEstadoEnvio.sql`: `EstadoEnvio`, `OrigenEntrega`, `EntregaConsultadaEl` (opcionales, mismo trato).
  Reanalizar ahora también INSERTA lo que antes no se guardaba (`Insertados`). La DSV pública da 429: PROHIBIDO intentar leerla.
- **Nacex, MBE, Weelko y Schenker (#857g, Carlos 19/11/26)**: el analizador prueba PRIMERO las agencias que mandan el correo (`EsCorreoPropio`).
  `IAgenciaEntrada` suma `PlazoEnLaborables` y `LeerDatosEnvio` (`DatosEnvioAgencia`: remitente del paquete, referencias del remitente, mensajero
  real, salida nuestra); `SeguimientoCorreo.Agencia` = la agencia de ESE código si no es la del correo (su enlace lo compone ella). Nacex lee
  «Tu entrega está programada para el dd/MM/yyyy antes de las HH:mm» (día + Hasta, EnCamino; los horarios NACEX.shop no cuentan); código = el
  albarán; sin `UrlSeguimiento`. MBE = INTERMEDIARIO: por cada «Envío: N» del resumen, remitente (nombre hasta la forma societaria), «Ref.:»,
  «Courier:» y sus «Tracking:» (marcados con su agencia) + «Tracking MBE:»; los envíos nuestros del resumen no cuentan (todos nuestros = salida).
  Así queda en el mismo envío que los avisos de UPS, que dan la fecha. Weelko (`FormatoAvisoSalidaWeelko`, nivel 1): solo su CONFIRMACIÓN de pedido
  (`IFormatoAvisoSalida.Confirmacion`): no se guarda como correo ni pone fecha; va a `dbo.ComprasConfirmacionesProveedor`
  (`Scripts/Issue857g_ComprasConfirmacionesProveedor.sql`, opcional) con nuestros candidatos (`ReglaPedidoConfirmado`: la referencia, el único o el
  único enviado 48 h antes). Un correo con «Ref.: SO253360…» se asigna a esos candidatos (`ReglaPedidoProveedor`, motivo `PedidoProveedor`; no
  pisa número ni PDF). DB Schenker: 72 h LABORABLES (`PlazoAgenciaEntrada`, festivos de Algete); las demás, horas naturales como antes.

### Inventario pendiente de otro día y el picking (incidencias 1227 y 1057, 12/11/26)
- `DELETE api/Inventarios/Filas/{n}`: una fila PENDIENTE de un día ANTERIOR a hoy se borra siempre (`ReglaBorrarFilaInventario.EsDeDiaAnterior`,
  sin mirar si sobra; mismos permisos, applocks y rastro); las de hoy, solo las que sobran; las de días posteriores y las contabilizadas, nunca.
  `Marcar` pone `SePuedeBorrar` en las de `OtraFecha` anteriores a hoy (no en las de hoy al comprobar otro día). El aviso `FilasBorradas` saca las
  borradas ese día aunque la fila fuera de otro, con «(fila del dd/MM/yyyy)». Nesto y Ariadna pintan el botón solo por `SePuedeBorrar`.
- **Dos filas de días distintos se bloqueaban entre sí (novedad 1364, ALC, 19/11/26)**: la fila de ayer salía sin «Borrar» porque la usuaria
  de tienda no tenía «InventarioBorrarFilas». Ahora su PROPIA fila pendiente de un día anterior la borra quien puede contar en el almacén
  (`ReglaBorrarFilaInventario.PuedeBorrarEsta`/`EsDelUsuario`; Marcar y DELETE). Y «Contabilizar también la del dd/MM»: GET Comprobaciones y POST
  Contabilizar con `incluirDiasAnteriores` cuentan las pendientes de días ANTERIORES a la fecha como del día (se comprueban con él, entran en el
  recuento, `Dias` por usuario; `IncluyeDiasAnteriores`, `DiasAnterioresIncluidos`). El SP sigue siendo uno por usuario (ya contabilizaba todas
  las suyas: por eso OtraFecha bloquea). OtraFecha trae `SePuedeIncluir` (todas anteriores); con una de un día posterior, elegir esa fecha.
- `PUT api/Inventarios/{id}` cambiando Fecha (o Almacén, Empresa, Número) daba 500: son parte de la clave en el EDMX y EF las pone en el WHERE
  del UPDATE con el valor nuevo (0 filas → DbUpdateConcurrencyException). Ahora 400 (`ReglaClaveFilaInventario`); lo que no viene, misma fecha
  con otra hora o relleno se toman de la guardada; una concurrencia de verdad, 409.
- Picking cortado por los disparadores del inventario («Se esta realizando el inventario del producto[1]»): `GestorPicking.EnExclusiva` lo
  traduce (`ErrorInventarioPendientePicking`, pura) a un mensaje con las filas pendientes (fuera de REI) de los productos del picking (o todas si
  no casa ninguna; 10 y «… y N más»). Es un `Exception` (INTERNAL_ERROR, sin «[CÓDIGO]» en Nesto) con el original dentro; si la consulta falla, el original.

### Deshacer el rellenado del inventario de una tienda (12/11/26)
- `DELETE api/Inventarios/Rellenar?almacen=REI&fecha=2026-11-12[&empresa=1]` (`ServicioDeshacerRellenarInventario`, sin UI: a mano con el token): borra
  TODAS las filas pendientes (Estado 1, NºTraspaso NULL) de ese almacén y día, de cualquier usuario y aunque estén contadas. Solo Dirección e
  Informática (403); fecha obligatoria y almacén sin ubicaciones (400). Applock `InventarioRellenar:{empresa}:{almacen}` y transacción; nunca lo
  contabilizado ni ExtractoProducto. 200 `{ Almacen, Fecha, FilasBorradas, FilasContadas, UnidadesContadas }` (ceros si no hay nada, y sin rastro);
  rastro = UNA fila en Modificaciones (Tabla «InventarioRellenadoDeshecho», resumen en JSON con el desglose por usuario).

### Alquileres: equipo y nombre del cliente en la relación (Novedades 1181, 12/11/26)
- `GET api/Alquileres/Cabeceras` y `…/Cabeceras/PorCliente` traen además (solo lectura, `AlquilerCabeceraDTO`) `ProductoNombre` (ficha de
  Productos; si no, la copia `CabAlquileres.NombreProducto`) y `ClienteNombre` (contacto del alquiler o, si falta o no casa, el principal), con
  una consulta por tabla (sin N+1). `NombreCliente` sigue siendo solo el del contacto exacto (etiqueta). El guardado los ignora y Nesto no los manda.

### Faltas al terminar el picking: aviso y muestras fuera (Novedades 1228, 12/11/26)
- `OrigenSalidaPicking.Terminar`: una falta de una MUESTRA (LinPedidoVta.SubGrupo MMP, no nota de entrega, interruptor
  `GestorMuestrasSinStock.CLAVE`) se QUITA del pedido (`ITransaccionSalida.QuitarDelPedido`: total = línea borrada; parcial = se queda
  con lo cogido y lo que falta no queda pendiente) con rastro `EscritorMuestrasSinStock.ModificacionFaltaPicking`, en la misma
  transacción; solo si del pedido sale algo más que muestras (si no, pendiente como siempre). Lo demás queda pendiente como hoy.
- Ya guardado, `IAvisadorFaltasPicking` (en segundo plano, nunca rompe el Terminar): por pedido, texto puro `AvisoFaltasPicking`
  (tipo `PickingConFaltas`, Datos pedido → NestoApp lo abre sin cambios) al buzón de quien hizo el pedido + su push de NestoApp, y push al
  vendedor si es otro (reglas de #688); a Compras (`TIPO_PICKING_FALTA`) lo pendiente sin compra que no es muestra. Ariadna: sin cambios
  (`ResultadoTerminarSalidaDTO.FaltasPorAvisar` es JsonIgnore).
- **«No está» = hueco a cero (C1, Carlos 12/11/26)**: `POST api/Almacen/Recogidas/{tipo}/{n}/NoEsta` {Producto, Hueco, Cantidad}
  (`ServicioNoEstaSalida`, núcleo común + `IOrigenNoEsta` por tipo: PICK y REPO, los dos únicos tipos de salida). En UNA transacción: applocks `InventarioRellenar:`,
  la reserva propia del hueco vuelve a libre, fila de Inventarios (usuario técnico «Ariadna (No está)», contado 0) y
  prdContabilizarInventario por su único call site (solo esas filas); cuadre comprobado (sin pendientes, hueco sin libre, extracto = lo
  libre) o se deshace. Luego reserva en el siguiente hueco (`HuecoPropuesto`) o, sin más, falta definitiva YA (`QuitarFaltasDeLosPedidos`,
  con A y B). `Regularizado` false = no se ha tocado nada (tienda, se está inventariando, interruptor…) y la app da la falta como
  siempre; 409 si falla. `?ensayo=true` (Admin/Dirección). Ariadna aún no lo llama.
- **REPO y ENCENDIDO (NestoAPI#858, Carlos 12/11/26)**: el origen solo pone (a) la reserva propia y cómo se suelta, (b) cómo se reserva
  en el siguiente hueco y (c) la falta definitiva; lo demás es núcleo. REPO (`OrigenNoEstaReposicion`): (a) estado 4 del traspaso
  (NºTraspasoRepo; las -4 de Nesto viejo no) → libre en su hueco por la MISMA escritura que el picking; (b) estado 4 con su NºOrdenRepo
  como CambiarHuecoReservaReposicion (`SQL_RESERVA_DE_LA_LIBRE_PARA_EL_TRASPASO`), sin «pendiente de ubicar»; (c) menos en la salida y
  en la entrada de PreExtrProducto SIN tocar Ubicaciones (`TransaccionSalidaSql.QuitarDelTraspaso`: el inventario ya la quitó del stock).
  Si era lo último por sacar del traspaso, `Regularizado` false (el Terminar no encontraría la salida). Interruptor en ParametrosUsuario
  empresa 1 «(defecto)» `Salidas.NoEstaRegulariza`: sin fila o «1» = encendido; «0»/false/no/off/apagado = apagado al momento
  (`Scripts/Issue858_NoEsta_Interruptor.sql`, opcional; el appSettings ya no se lee). Cada uno regularizado: rastro + línea «[No está] …» en ELMAH.

### Facturado en tienda: acumulado del año y comparativo (NestoAPI#719, gemela de Nesto#559, 12/11/26)
- `GET api/Informes/FacturacionTienda` (y `/Excel`) con `tipo=Semana|Acumulado|Comparativo` (vacío = Semana, lo de siempre),
  `hasta` (sin él, hoy), `anios` (Comparativo: 2 por defecto, 1-10) y `almacen` (alias de `tienda`); mismos permisos y tienda que la semana.
- Acumulado/Comparativo → `FacturacionTiendaAnualDTO` (meses de enero al de `hasta` + `Total`; cada mes con `Anteriores` y variación € y %
  del año de `hasta` respecto a cada año). Una consulta agregada por año y mes (`SQL_FACTURACION_TIENDA_ANUAL`), del 1 de enero al mismo
  día y mes de cada año. Excel: hoja «Acumulado» y, en el comparativo, también «Comparativo». El correo (POST …/Correo) sigue siendo de la semana.

### Mantenimiento de calles del recorrido (NestoAPI#642, corte 4 de #639, para Nesto#528, 12/11/26)
- `GET api/Almacen/Recorrido?almacen=ALG&empresa=1` → `RecorridoAlmacenDTO`: calles (Orden, Sentido A/D, pasillos con Posicion, Nombre y
  Huecos), `PasillosSinCalle` (con huecos en uso o con nombre) y `Tramos` (pasillo+columna+nivel AMano/Escalera de los huecos en uso, con la
  regla única `OrdenRecorrido`). Sin las tablas de `Scripts/Issue641_RecorridoCalles.sql`: `TablasCreadas` false y el orden de siempre.
- `PUT …/Recorrido?almacen=&empresa=&ensayo=` sustituye TODO el recorrido del almacén en una transacción (`RepositorioRecorridoSql.Guardar`):
  el orden de la lista es el de las calles (Orden 10, 20…), el de cada lista de pasillos su Posicion; `FilaAMano` null = no tocar. Permiso: el
  de Etiquetas de hueco (escribir en api/Almacen: Almacén o Dirección). 400 validación, 403, 503 sin tablas. Sin caché: el lector es Scoped.

### Recogida solicitada por el vendedor (NestoAPI#855, 12/11/26; gemelas Nesto#635 y NestoApp#259)
- `dbo.RecogidasSolicitadas` (fuera del EDMX, `Scripts/Issue855_RecogidasSolicitadas.sql`; sin él, 503 y el job no hace nada). Estado
  guardado Solicitada/Confirmada/Rechazada/Anulada; al leer se calcula EnReparto (parada de la 14 tramitada/incidentada) y Recogida
  (abono hecho, parada entregada o bultos NR recogidos). `QuienRecoge`: Reparto (solo ruta propia, `RutasRepartoPropio` + interruptor;
  400 «pídala como la recojo yo») o Vendedor (cualquier contacto).
- `POST/GET api/NotasEntrega/Recogidas/Solicitudes` (vendedor de NestoApp solo sus clientes, `PermisosRecogidasSolicitadas`; empleados),
  `GET …/Solicitudes/{id}`, `GET …/Pendientes`, `POST …/{id}/Confirmar` y `…/{id}/Rechazar` (los cuatro grupos de notas), `DELETE …/{id}?motivo=`.
- Confirmar crea la nota de recogida como #757 (pedido NotaEntrega = 1, líneas de texto de 50, NotasEntregaCabecera Recogida) en UNA
  transacción con la solicitud (`RepositorioRecogidasSolicitadas.Confirmar`). Con Vendedor las líneas nacen con fecha 31/12/2099: no
  entran en picking ni Facturar rutas (no hay parada) y `POST Recogidas/Abono` (la lectura del NR en Algete) no le pide la parada
  recogida (`ServicioAbonosRecogida.EsRecogidaDelVendedor`). Ese es el cierre en los dos casos; Descargar de Ariadna no vale (409 NO_EN_FURGONETA).
- Anular: quien la pidió (o los grupos de notas) mientras no esté en marcha (Solicitada, o Confirmada sin picking, envío ni abono: se borra
  la nota como Borrar pedido); en marcha, solo Almacén o Administración: se marca y la nota se queda (`AvisoAnulacion`, campana a Almacén).
- Avisos: campana a Almacén (`RecogidaSolicitada`), push NestoApp + campana Nesto a quien la pidió (`RecogidaSolicitadaResuelta`,
  `RecogidaSolicitadaAnulada`), `haCambiado` al grupo `RecogidasSolicitadas` del AlmacenHub. Job `recogidas-solicitadas-sin-respuesta`
  (L-V cada hora 8-20): un día laborable de Algete sin contestar → `RecogidaSolicitadaSinRespuesta` a Almacén y a quien la pidió, una vez.
- Al abonarla (`POST Recogidas/Abono`, ya guardado el abono; `ServicioRecogidasSolicitadas.AlAbonar`, nunca lo rompe): con Vendedor, su
  nota se procesa como nota de entrega (`ServicioNotasEntrega.ProcesarNotaRecogidaDelVendedor`: líneas a -2 con fecha de hoy, SIN
  ExtractoRuta; `RepositorioRecogidasSolicitadas.CerrarNotaDelVendedor`, una transacción, idempotente: si falla, ELMAH y se reintenta en
  la siguiente lectura del NR). Con abono nuevo, `RecogidaSolicitadaRecibida` (push + campana, Datos + `estado` «Recogida») a quien la
  pidió. Las notas de recogida sin solicitud y `ProcesarNotaEntrega` (Facturar rutas) no cambian.

### Recogida imprevista del repartidor (NestoAPI#859, 12/11/26; gemela Ariadna 29d1390)
- `POST api/RepartoPropio/RecogidasImprevistas/Comprobar` {EnvioParada? | Empresa?, Cliente, Contacto} y `POST api/RepartoPropio/RecogidasImprevistas`
  {Clave (Guid), FechaHora, …, Bultos 1-99, Texto ≤500, FotoBase64?, AceptaRetornoVivo} (`ServicioRecogidasImprevistas`, permisos de reparto).
  Retorno vivo = envío de otra agencia (no la 14) del contacto con Retorno ≠ 0, Tramitado o Incidentado, de los últimos 60 días, tras refrescar su
  seguimiento (4 s por envío; si no contesta, el estado guardado y se dice). Con retorno vivo sin aceptar: 409 `{Message, Codigo: "RETORNO_VIVO", RetornosVivos}`.
- Crear, en UNA transacción (`RepositorioRecogidasSolicitadas.CrearImprevista`, mismo núcleo `CrearNota` que Confirmar de #855): la nota NR con
  líneas a 31/12/2099 (sin picking ni parada), fila «Confirmada» con Origen «Repartidor», Clave y AvisoUrgente (`Scripts/Issue859_RecogidasImprevistas.sql`;
  sin él, 503) y los «Recogido» NR{p}-{n} en la furgoneta de quien llama. Misma Clave → la misma respuesta. Se cierra como la del vendedor al leer
  el NR (o NR-n) en Algete (`EsRecogidaDelVendedor` incluye Origen «Repartidor»). Foto opcional al blob de bultos (`recogidas-imprevistas/…`).
- Avisos: campana `RecogidaImprevista` a Almacén, haCambiado a Reparto y RecogidasSolicitadas; con retorno aceptado, urgente
  `RecogidaConRetornoEnCamino` (referencia = el envío; suena mientras siga vivo) y línea en EnviosHistoria. Nunca se toca la agencia.

### «Todo junto» con falta de pago: pedido retenido por falta (NestoAPI#858 punto 4, 12/11/26)
- En `OrigenSalidaPicking.QuitarFaltasDeLosPedidos` (Terminar y falta definitiva del «No está»), dentro de su transacción
  (`ReglasRetencionTodoJunto.RetenerSiHaceFalta`, solo si la tx implementa `IRetencionTodoJuntoSalida`, como `TransaccionSalidaSql`): una falta
  que queda pendiente (no muestra ni quitada) de una línea con base > 0 y no ficticia, de un pedido en modo efectivo 1 del que sale algo más →
  fila «Pendiente» en `dbo.PedidosRetenidosPorFalta` (`Scripts/Issue858_PedidosRetenidosPorFalta.sql`; sin él, nada y ELMAH una vez).
  Plazo: `Picking.TodoJuntoPlazoHoras` (2) horas laborables L-V 8-18 sin festivos de Algete. Interruptor `Picking.TodoJuntoRetener` (apagado con «0»).
- Bloqueo con «Esperando al vendedor: no empaquetar…»: `GuardiaPedidoRetenido.Motivo` de producción (albarán, Facturar rutas, etiqueta del
  packing → 409) y cerrar caja/foto de bulto (`GuardarBulto`, 409). El packing marca `RetenidoPorFalta`, `AvisoRetencion`, `PlazoRetencion`.
- `GET api/PedidosVenta/RetenidosPorFalta`, `POST api/PedidosVenta/{empresa}/{pedido}/RetencionPorFalta/Decidir` {Decision ServirLoQueHay|Esperar}
  (quien hizo el pedido, su vendedor o Almacén/Administración/Dirección/Informática). «Esperar» solo con el picking terminado: las reservas del
  pedido pasan a «pendiente de ubicar» (el mozo las ubica con la PDA) y Picking = 0. Job `pedidos-retenidos-por-falta` (L-V cada 15 min 8-18):
  vencidas → Esperar + aviso `PedidoRetenidoPorFaltaEsperar`. El aviso `PickingConFaltas` lleva Datos `retenido`="true" y `plazo`.

### Teléfono de la ficha en los inscritos (fleco de la sugerencia 1362, 19/11/26)
- `InscripcionEventoDTO.Telefono` (solo lectura): `Clientes.Teléfono` del cliente/contacto de la inscripción, de la misma consulta por tanda que
  la dirección (`Completar`). La hoja «Imprimir lista» de Nesto usa el de la persona que asiste y, si no, este.

### Evento de pago que pasa a gratuito (Novedades 1233, 12/11/26)
- `PUT api/Eventos/{id}` que deja las dos señales a 0: las Reservadas sin dinero ni documentos pasan a Inscrita / Gratuita (FechaLimitePago
  y FormaPago a NULL, rastro en Modificaciones) y sus enlaces de pago pendientes (cadena de reintentos incluida) se anulan con el UPDATE de
  `AnulacionEnlacesPago`, todo en la transacción del evento (si un enlace ya no está pendiente: 409 y nada cambia). Las Caducadas igual solo si
  caben TODAS en las plazas libres (no las «No podré ir» ni las de un cliente con otra viva). Reglas puras: `ReglasPasoAGratuitoEvento`;
  transición exclusiva `ReglasInscripcionEvento.PuedePasarPorPasoAGratuito` (no está en `PuedePasar`).
- Con alguna pagada, facturada, con lista pasada, cobro, señal, factura del curso, cupón o enlace cobrado/compartido con otro evento: 400 como
  antes (todo o nada). Nunca se deshacen facturas ni cupones. La respuesta trae `InscripcionesConvertidasAGratuitas`, `InscripcionesRecuperadas`,
  `CaducadasSinRecuperar`, `EnlacesPagoAnulados` y `MensajePasoAGratuito`.
- Un cobro tardío del enlace de una Inscrita no la paga ni factura: ELMAH + aviso de pago fuera de plazo a Administración. Los jobs de aviso,
  liberación y caducidad se saltan los eventos que hoy son gratuitos. `CrearEvento` con productos requeridos y sin importes nace 0/0.

### La señal de un evento se cobra en Cajas (caso del cliente 33895, Carlos 19/11/26)
- `GET api/Eventos/Inscripciones/SinPagar?cliente=&empresa=1`: reservas (Reservada/Caducada, de pago, evento de hoy en adelante) con `Importe`
  (la señal), `Documento` INS{id}, `Cobrable`/`Motivo` (`ReglasCobroSenalCaja.MotivoNoCobrable`: señal de #591, propuesta de #615 sin confirmar,
  enlace cobrado, pedido de cursos #691, o `IConfirmadorSenalEvento.MotivoNoFacturaSolo`: interruptor apagado / no automatizable) y `CobroYaApuntado`.
- `POST api/Eventos/Inscripciones/{id}/CobroCaja` {FormaPago EFC|TAR|TRN, Importe, CuentaCobro?, Fecha?, Diario?, Delegacion?, FormaVenta?,
  ConceptoAdicional?} (`ServicioInscripcionesEventos.CobrarSenalEnCaja`, `Infraestructure/Eventos/CobroSenalCaja.cs`): importe ≠ señal → 400 (entera o
  nada); tiendas efectivo/tarjeta, Administración/Dirección también transferencia (`PermisosEventos.PuedeCobrarSenalEnCaja`). En UNA transacción
  (`RepositorioCobroSenalCaja.Registrar`, applock por inscripción, estado releído): cobro a cuenta «INS{id}» = los apuntes de un cobro a cuenta de Cajas
  (`ConstructorApuntesCobroSenalCaja`, prdContabilizar por el núcleo de siempre), anula el enlace pendiente y deja la inscripción Pagada; si ya hay cobro
  INS{id} no lo repite. Después, `TrasPagar` (el corte 2 de siempre): pedido + factura exenta, `LiquidadorSenalCurso` liquida el cobro INS{id} contra el
  efecto (LIQUIDADA) y cupón. Relanzar = terminar sin cobrar otra vez. Rastro en Modificaciones (Tabla `CobroSenalCaja`).
- «Marcar pagada» (y inscribir ya pagada): solo Administración/Dirección/Informática (`PuedeMarcarPagadaAMano`, 403 de usted). Facturar a mano y la
  asistencia siguen con `PuedeMarcarPagada`. POST/PUT PedidosVenta con la referencia del curso de un evento vivo (sin EventoId; en el PUT, líneas nuevas):
  aviso `CursoVendidoAMano` (POST en `AvisosGrabacion`), se guarda igual. Nesto: Cajas › Cobros enseña las reservas; Marcar pagada oculto a las tiendas.

### Fechas de recepción y certeza (NestoAPI#853, 12/11/26)
- Plazo real por proveedor en `dbo.ProveedoresPlazosEntrega` (fuera del EDMX, `Scripts/Issue853_PlazosProveedoresYEnvios.sql`): mediana y P80
  de días (CabPedidoCmp.[Fecha Modificación] → FechaAlbarán, líneas TipoLínea '1', 24 meses, empresas 1+3, negativos fuera), cadencia de
  pedidos del último año y fila genérica '*'. Cálculo puro `CalculoPlazosProveedores`; job `proveedores-plazos-entrega` 3:40 Madrid (cola
  «indicadores»); `GET api/Compras/PlazosProveedores` (Compras/Dirección/Informática), `POST …/Recalcular` (Dirección/Informática, 202).
  Caché de 10 min (`CachePlazosProveedores`). Proveedor habitual de un producto = ProveedoresProducto con menor Orden; si no, el de su última compra.
- Al ENVIAR un pedido de compra (PUT api/PedidosCompra con PathPedido): con ParametrosUsuario «(defecto)» `Compras.FechaRecepcionPorPlazoReal`
  = «1» y plazos, `ReglaFechaRecepcionEstimada` (hoy + P80 propio con ≥5 recepciones, nunca menos que DíasEnServir; si no, DíasEnServir; si
  no, P80 genérico); si no, hoy + DíasEnServir. Nunca rompe el envío. Siempre (con tabla) se apunta en `dbo.PedidosCompraEnvios` (primer envío;
  `FechaModelo` reservada para ML.NET) y `dbo.vPedidosCompraEnviosError` mide el error.
- Certeza en el motor #606 (aditivo en `PartidaFechaEntregaAgenciaDTO`): `Certeza` (Segura, CasiSegura, Aproximada, Orientativa),
  `ConSeguimientoAgencia`/`AgenciaSeguimiento` (correo de #857 aplicado, no descartado, con FechaRecepcionAplicada = la FechaRecepción de
  ahora), `FechaOrientativa` (solo sin fecha: NO entra en ninguna fecha, prometida, promesa en riesgo ni simulación), `PedidoProveedorOrientativo`
  y `FechaPrevistaProveedorOrientativo` (pedido enviado con prevista a >15 días: la regla de los 15 días NO cambia). Orientativa de lo no pedido:
  `ReglaFechaOrientativaSinPedir` (hoy + hasta el próximo pedido según cadencia + P80). Lectura por tanda en `ILectorCertezaFechaEntrega`.
- Textos: UNA pieza pura `TextosCertezaFechaEntrega` para el motivo del motor, el correo (etiqueta «[Prácticamente segura]», «[Aproximada]»,
  «[Orientativa]» al principio de cada bloque) y la promesa en riesgo. Fotos: proveedor con seguimiento → caso `EnTransito`.
- **Modelo ML.NET en sombra** (`PlazosEntrega/ModeloPlazos/`): LightGBM de regresión de log(1 + días hasta el PRIMER albarán), 5 años, un
  ejemplo por pedido recibido (0-120 días). Rasgos puros sin fuga (`ConstructorRasgosPlazos`: solo pedidos enviados ANTES de ese día y plazos
  con albarán ANTES): proveedor (<10 pedidos → «(otros)»), día/mes, líneas, log importe, DíasEnServir, mediana/P80/último de sus 10 últimos
  plazos, días desde su pedido anterior y pedidos en el aire. Prudente = media en log + P80 de los residuos (no hay cuantil público en 4.0.2).
  Job `plazos-proveedores-modelo` 4:10 Madrid (cola «entrenamiento»): valida con los últimos 3 meses (margen calibrado con los 3 anteriores) contra
  la regla «como si» (`ReglaPlazosComoSi`), apunta SIEMPRE las métricas en `dbo.PlazosProveedoresModeloEntrenamientos` y solo cambia el modelo si
  no empeora (`PuertaCalidadPlazos`: % corto +1 punto, error +0,25 días). Modelo en `ModeloLlamadas:Carpeta` (`plazos_proveedores.zip`/`.json`
  con la historia reciente por proveedor). Al enviar rellena `FechaModelo` (3 s como mucho; si falla, NULL). ParametrosUsuario «(defecto)»
  `Compras.FechaRecepcionQuienManda` = «Regla» | «Modelo» (con el interruptor de plazo real a «1»: la del modelo, nunca < hoy + DíasEnServir;
  Origen `Modelo`/`ModeloDiasEnServir`). `GET api/Compras/PlazosProveedores/Comparacion?meses=6` (último entrenamiento + lo real con veredicto,
  ≥50 recibidos) y `POST …/Modelo/Entrenar[?ahora=true]` (Dirección/Informática; L-V 8-20 → a las 21:00).
- `AsignadorPedidosCompra` (#857): un número cuenta como pedido nuestro solo si es token propio (no pegado a letras/guiones) Y lleva etiqueta
  de pedido/referencia delante (30 caracteres) o su proveedor se reconoce por otro criterio.
- **Medir las orientativas de lo no pedido (NestoAPI#861, 13/11/26)**: la regla no cambia. `dbo.FechasOrientativasSinPedir` (fuera del EDMX,
  `Scripts/Issue861_FechasOrientativasSinPedir.sql`, requiere el de #853): una fila por día + empresa + producto con venta pendiente cuya partida del
  motor queda SIN fecha y con orientativa de lo no pedido (`ReglasFechasOrientativas.EsOrientativaSinPedir`: SinStock, sin pedido lejano) y sus
  ingredientes (proveedor habitual, último pedido, cadencia, días desde/hasta, plazo propio/genérico, llegada de la regla y la salida que ve el usuario,
  `YaTocabaPedir`, `PrimeraDelEpisodio` = sin foto el laborable anterior). Job `fechas-orientativas-sin-pedir` 3:50 Madrid (cola «indicadores»): FOTO
  con el mismo motor que `fechas-entrega-fotos-sombra` (sin avisos, nunca en caliente) y RESOLVER por lotes de Id con una consulta: primer pedido
  al MISMO proveedor (empresas 1+3) con el producto enviado desde el día de la foto (`PedidosCompraEnvios.FechaEnvio` o `[Fecha Modificación]`) y
  su primer albarán; errores real − previsto; sin llegar a los 120 días, `Caducada`. Sin tabla, nada (ELMAH una vez). `dbo.vFechasOrientativasError`
  (primeras resueltas por proveedor; Grupo Todas/YaTocaba/NoTocaba). `GET api/Compras/PlazosProveedores/Orientativas?meses=6` (Compras/Dirección/
  Informática; lo mismo en C# con los meses y los totales) y `POST …/Orientativas/Procesar` (Dirección/Informática, 202); 503 sin el script.
- **Packs montables (NestoAPI#606, 18/11/26, caso 44376)**: un producto TITULAR de un kit (tabla Kits, sin materias primas MTP) sin stock propio
  suficiente en Algete se MONTA con sus componentes (origen `Montaje`, después de libres y de desmontar kits y antes que las tiendas). Lo monta almacén a
  mano (`POST api/Productos/MontarKit`, Diario _MontarKit) con el informe de kits de antes del picking (`prdInformeKitsQueSePuedenMontar`, parte «m»): el
  picking NO monta (sin stock del pack no lo reserva). Mismo supuesto que el origen `Kit` (desmontar). LA REGLA (pura, `BolsaMontajeAlgete` en
  `MontajeKitsAlgete.cs`): componente libre = stock Algete − pendientes de otros en Algete − lo que pide suelto el propio pedido − lo que necesitan los packs
  que otros pedidos deben por encima del stock de ese pack; se montan los que permite el componente más escaso → Segura, sale como lo libre. Lo que no se
  puede montar: cada componente que falta se reparte con las reglas de cualquier producto (`ComponentesMontaje`, un solo nivel, sin lo libre de Algete) y el
  pack sale el día del PEOR (`Montaje` con `ComponenteQueFalta`/`NombreComponente`/`OrigenComponente` y la certeza de ese componente) o, si alguno no tiene
  fecha, `SinStock` nombrándolo («para montarlo en Algete falta el componente 40010 …»; su orientativa no se mide en #861). Lectura por tanda:
  `SQL_KITS_MONTAJE` (una consulta) y los componentes entran en la lectura general; `SQL_ESTADOS` trae el nombre. Llega a todo lo que usa el motor
  (prometida, correo, promesa en riesgo, fotos: un pack que se monta ya «sale del stock»; el que espera, se mide como su componente). El modo de servicio
  sugerido (`SugeridorModoServicio`) no lo usa aún; el aviso de Nesto antes del picking solo pregunta por los kits que DESMONTAR.

### Color de las líneas del correo del pedido: el mismo motor que el pie (pedido 925901, 18/11/26)
- El correo pintaba cada línea con `GestorStocks.ColorStock` (stock − TODOS los pendientes) y el pie con el motor #606: un pedido a 31/12/2099
  ponía en rojo una línea que el pie daba «con stock en Algete». LA REGLA (pura): `ReglaColorLineaCorreo` (`Infraestructure/PedidosVenta/`): de
  las partidas del producto en `FechaEntregaAgencia.Desglose`, la PEOR: Picking/Algete → verde; Kit/Montaje sin componente que falte → rosa (no
  cuenta como «tiene que venir»: ni reserva ni parte el pedido); Tienda/EnCamino (o pack que espera un componente de tienda) → rosa «tiene que
  venir»; Proveedor/SinStock (o pack que espera eso) → rojo. `GestorPresupuestos.StockDeLinea` + `ResumenStock`: de ahí salen `faltaStockDeAlgo`
  (#507, «¡¡¡ ATENCIÓN !!!») y `tieneQueVenirAlgunProducto`. Reservas a tiendas y su copia (`CalcularReservasDesdeMotor`, `ObtenerAlmacenesConReservas`):
  lo que el motor trae de cada tienda (Tienda + Unidades).
- Respaldo `ColorStock` (como siempre) para lo que el motor no calcula: presupuestos, líneas que no son de Algete, servidas (≥ 2) o sin cantidad,
  producto que no está en el desglose, origen desconocido, o el motor falló (FechaEntregaAgencia null). El «antes» del «✓ LIBERAR» y su copia a
  tiendas siguen con la cuenta de siempre (no hay motor del pedido anterior). Sugeridor de modo de servicio y portes no cambian.

### Pago a plazos de un pedido ya creado, con cualquier financiera (NestoAPI#864, 13/11/26; continuación de #663)
- Un empleado (Nesto) o un vendedor (NestoApp) pasa el pedido y el CLIENTE termina la financiación de ese pedido en la app (push) o con
  un enlace. TODO NEUTRO (Carlos 13/11/26): el núcleo `ServicioPagoAPlazos` (`Infraestructure/Pagos/PagoAPlazos/`: pedido, prepago, estados,
  canal, avisos, plazos) solo habla con `IProveedorPagoAPlazos` (condiciones y límites, crear la solicitud, URL de pago, interpretar y
  contestar la notificación, consultar/capturar, cancelar, reembolsar). Aplázame = `Pagos/Aplazame/ProveedorPagoAPlazosAplazame` (sobre
  `IClienteApiAplazame`). **Otra financiera** = una clase con la interfaz + una línea en `RegistroProveedoresPagoAPlazos.PorDefecto` + su
  código en el parámetro `PagoAPlazosProveedor`. Lo de la app de #663 (api/Pedidos/Cliente/…/Aplazame, PagosTPV, job `aplazame-capturas`) NO cambia.
- `dbo.SolicitudesPagoAPlazos` (fuera del EDMX, `Scripts/Issue864_PagoAPlazos.sql`; sin él los endpoints dan 503 y el job no hace nada).
  Estados: Pendiente → Aprobado → Capturado; Rechazado, Cancelado, Caducado. Referencia ante el proveedor `PAP-{Id}`. Parámetros «(defecto)»:
  `PagoAPlazosPedidos` (Apagado/Pruebas/Activo; interruptor PROPIO, no mira `AplazameApp`), `PagoAPlazosPedidosClientesPrueba`,
  `PagoAPlazosProveedor` (Aplazame), `PagoAPlazosDiasAviso` (3), `PagoAPlazosDiasValidezSolicitud` (7). Límites y plazo de captura: del proveedor.
- `GET api/PedidosVenta/{empresa}/{pedido}/PagoAPlazos/Disponible` (siempre 200: Disponible, Motivo, Proveedor, NombreProveedor, Importe,
  límites, DiasPlazoCaptura, ClienteTieneApp, Canal sugerido, Aviso), `POST …/PagoAPlazos` {Canal? App|Enlace, Correo?} (409 no disponible o
  App sin app; 400 canal/correo; 502 el proveedor falla; UrlPago si es Enlace o si la push no sale), `GET …/PagoAPlazos/Estado` (SinSolicitud…),
  `DELETE …/PagoAPlazos?motivo=`. Permisos (`PermisosPagoAPlazos`): como los enlaces de pago, menos los clientes de la tienda (403) y el
  vendedor de NestoApp solo con pedidos de sus clientes. App de clientes: `GET api/Pedidos/Cliente/{pedido}/PagoAPlazos` (UrlPago del WebView).
- Disponibilidad (`ReglasPagoAPlazos.MotivoNoDisponible`, pura): interruptor, proveedor operativo, ni nota de entrega ni presupuesto ni
  facturado, sin otra financiación concedida, pendiente (líneas -1/1/2 − prepagos − enlaces cobrados) > 0 y dentro de los límites, y todas las
  líneas sin servir con Fecha_Entrega ≤ hoy + plazo de captura (NO mira stock). `ClienteTieneApp` = dispositivo activo de NestoTiendas.
- Notificación: `POST api/PagoAPlazos/Notificacion/{proveedor}` (anónimo; la valida el proveedor). Aprobación = UPDATE condicional
  Pendiente→Aprobado con el importe EXACTO + Prepago (cuenta y prefijo del proveedor; Aplázame: los de #663) con `Prepagos.FechaCobro` (#860)
  = día de la aprobación, idempotente por el concepto; si el pedido ya debe menos de lo pedido, no se confirma. NO toca la forma de pago.
- Job `pago-a-plazos-pedidos` (L-S cada hora 8:35-20:35): todo en albarán → captura lo servido (nunca más de lo aprobado, preguntando antes
  lo ya capturado); a `PagoAPlazosDiasAviso` del límite sin salir → aviso (una vez); plazo vencido → cancela en el proveedor y QUITA el
  prepago (con parte servida: captura lo servido y ajusta el prepago); pedido borrado (también en `DELETE api/PedidosVenta`) → cancela;
  pendiente sin terminar a los 7 días → Caducado.
- Avisos (`TextosAvisosPagoAPlazos`): al cliente push `PagoAPlazosPendiente` (de tú; Datos tipo, empresa, pedido, idSolicitud, cliente,
  estado, proveedor, importe); a quien hizo el pedido, a quien pidió la financiación y a su vendedor (campana + push NestoApp)
  `PagoAPlazosAprobado`, `PagoAPlazosRechazado`, `PagoAPlazosPlazoCerca` (+fechaLimite, dias) y `PagoAPlazosPerdido`.
  El resultado también al CLIENTE (13/11/26; mismos Datos y dispositivos que el pendiente, sin app nada, TNV#99 los abre igual):
  `PagoAPlazosAprobadoCliente` (con el prepago recién creado, como el del vendedor) y `PagoAPlazosRechazadoCliente` (al pasar a Rechazado).

### Fechas de recepción vencidas: se recalculan (NestoAPI#865, 16/11/26)
- En el job del correo de proveedores vencidos (L-V 8:00, `ProveedoresVencidosJobsService`), ANTES del correo, `ServicioFechasVencidas`
  (`Infraestructure/PedidosCompra/FechasVencidas.cs`) pone fecha nueva a cada línea en el aire (LinPedidoCmp -1/1, Enviado) con FechaRecepción < hoy.
  Regla pura `ReglaFechaVencida`: correo de seguimiento #857 del pedido (envío aplicado, o inequívoco sin descartar ni llegado:
  `CorreosDelPedidoCompra`) con `ReglaFechaRecepcionPorCorreo.DiaDeLlegada` ≥ hoy → ese día; si no («sin noticias»), de las recepciones del
  proveedor (mismo origen que #853, 24 meses, 1+3, `LeerRecepcionesDeProveedores`) que tardaron MÁS de los días que lleva esperando
  (desde CabPedidoCmp.[Fecha Modificación]), el P80 desde ese día; con < 5 casos, hoy + P80 genérico ('*'); sin nada, no se toca. Lo
  estimado nunca antes del siguiente laborable de Algete.
- Escritura (`GuardarFechaRecalculada`, una transacción por línea, solo si sigue en el aire y con la fecha leída): Usuario «Fecha recalculada
  (vencida)», [Fecha Modificación], Modificaciones Tabla `CompraFechaRecalculada` (JSON con Clave «empresa/pedido/orden», antes/después, origen y
  cifras) y promesa en riesgo (#693) por el núcleo común `CambiosCompraPromesaRiesgo.AnotarCambioFecha` (el mismo de la fase 3 de #857).
  Se recalcula cada vez; desde el 2.º vencimiento (filas del rastro de esa línea en 365 días) el correo marca «Hay que llamar al proveedor».
  Correo: columna «Nueva fecha» con `TextosFechaVencida` (de usted). Interruptor ParametrosUsuario «(defecto)» `Compras.RecalcularFechasVencidas`
  (sin fila o «1» encendido; «0»/false/no/off/apagado → el correo de siempre). Script `Scripts/Issue865_ComprasFechasVencidasYReenvio.sql`.
- Corte 2: en «Clientes esperando», lo que trae una reposición de tienda en camino (PreExtrProducto con NºTraspaso, neto por traspaso y
  producto > 0, en el almacén de destino: lo mismo que el «en camino» del motor) sale aparte con su llegada (siguiente día de reparto
  propio, `GestorFestivos.EsFestivoRepartoPropio`) y NO cuenta como falta (`EsperaClientesProducto.CubiertoPorReposicion`; `Faltan` lo descuenta).
- Corte 3 (TEMPORAL): el job `compras-correos-seguimiento` reenvía a compras@ cada correo que acaba de GUARDAR (`ReenviadorCorreosSeguimiento`,
  `CorreosSeguimiento/ReenvioCorreosSeguimiento.cs`) por el SMTP de siempre: asunto «[Nesto: correo de seguimiento] …», cabecera con lo hecho
  (`AlGuardar` devuelve el desenlace por MensajeId: fecha antes → después y pedido, misma fecha, por asignar, salida nuestra, descartado, ya
  recibido, aplicar apagado, antiguo…; textos `TextosReenvioCorreoSeguimiento`) y debajo el cuerpo que Graph dio en esa pasada (no se guarda;
  Graph sigue solo lectura). Idempotente sin columna (solo lo recién insertado; el INSERT es único por MensajeId). El histórico y Reanalizar no
  reenvían. El analizador ignora los correos con esa marca en el asunto (por si vuelven al buzón). Fallo → ELMAH una vez al día, nunca rompe
  la pasada. Interruptor `Compras.CorreosSeguimientoReenviar`: SOLO «1» = encendido (sin fila, apagado; el script lo crea a «1»).

### Próxima entrada en la ficha del producto (NestoAPI#869, 16/11/26)
- `GET api/Productos/ProximaEntrada?producto=&almacen=ALG&empresa=1` (`ProximaEntradaProductoController`, solo lectura, `[Authorize]`; 404 si no
  existe): (1) la línea en el aire (LinPedidoCmp -1/1 Enviado, 1+3) de FechaRecepción más temprana, Aproximada o CasiSegura por `ReglaSeguimientoAgencia`
  (correo #857 vigente), con `Recalculada` (Usuario de #865) y `PrevistaPasada`; (2) sin pedido, Orientativa = `ReglaFechaOrientativaSinPedir` del
  proveedor habitual + `FechaSiSePidieraHoy` (`ReglaFechaRecepcionEstimada` sin DíasEnServir); (3) `SinDatos` con `Motivo`. `Texto` hecho
  (`TextosProximaEntrada`, calificativos de `TextosCertezaFechaEntrega`). Lee con `LectorCertezaFechaEntregaSql`. Aparte de GET api/Productos (que va por Nesto_sync).

### Sugerencias de llamadas y ofertas vigentes (NestoAPI#866, 16/11/26)
- Siempre hay ofertas (bimestrales, de marca, campañas; Carlos 16/11/26): impulso SIEMPRE encendido salvo ParametrosUsuario empresa 1
  «(defecto)» `Rapports.ImpulsoOfertas` = 0/false/no/off/apagado (sin fila = encendido). Regla pura `ReglasImpulsoOfertasContacto`, después del
  modelo como el del cheque. Oferta = fila global de DescuentosProducto desde 1 unidad que «parece campaña» (fechas, audiencia o Campana, como
  `api/Campanas?soloCampanas`) o N+M de OfertasPermitidas con fechas, vigente con la regla única `Vigencia`, o SIN fechas y con `FechaModificación`
  de los últimos 90 días (`ReglasImpulsoOfertasContacto.DIAS_NM_SIN_FECHAS`, `CuentaOfertaNm`, 18/11/26: las bimestrales suelen cargarse sin fechas;
  su fecha de entrada, `FechaEntrada`, es esa FechaModificación y no tienen fin); de cada producto, la de más descuento
  (precio especial = 1 − precio/PVP; N+M = M/(N+M)). Caché 10 min; compras de la cartera en UNA consulta por vendedor (`ImpulsoOfertasContactoSql`,
  misma asignación que la cartera). Producto habitual = ≥ 2 días con compra en 12 meses; ciclo = mediana entre compras; puntuación: le toca > descuento > peso.
- Suben como mucho 5 al día detrás del cheque (Origen «Ofertas», prefijo «Compra el »), solo si su mejor producto ya le toca y no se habló con él
  desde que entró la oferta (en 14 días), ni intento < 2 días ni pedido < 6. Motivo: «Compra el 45929 (NOMBRE) cada 45 días y lleva 64 sin comprarlo; hoy
  tiene un 20 % de descuento (hasta el 30/11). No hablamos con él desde el 02/10.» («solo hasta» si caduca en ≤ 7 días, «oferta nueva» si entró hace ≤ 7).
- Motivo explicado (corte 2): las de Origen Probabilidad llevan detrás «Sobre todo porque …» con las 2 causas de más contribución positiva del LightGBM
  (`ExplicacionModeloContacto`, CalculateFeatureContribution de ML.NET 4.0.2; one-hot sumado por rasgo, plantilla por rasgo). Solo modelo general; si falla, sin ella.
- Datos (corte 3, `Scripts/Issue866_SugerenciasContactoOfertas.sql`, columnas opcionales fuera del EDMX): Origen, ProductosOferta (JSON de los 3 mejores en
  TODAS las sugerencias con alguno: grupo de control), y job `sugerencias-contacto-resultado` 3:25 Madrid: PedidoEn7Dias, PedidoConOferta, ImportePedido7Dias
  de las de hace 8-60 días (un UPDATE). Para decidir si se añaden rasgos de ofertas al modelo.

### Aviso de ficha desde Ariadna: foto del mozo (NestoAPI#868, 16/11/26)
- `POST api/Almacen/AvisosFicha` admite `FotoMozoBase64` (opcional, JPEG, tope 4 MB; `FotoMozoAvisoFicha.Leer`, 400 si no vale): se sube UNA vez al blob de
  bultos (`avisos-ficha/{producto}-{guid}.jpg`) y su ruta va en `AvisosFichaProducto.FotoMozo` (`Scripts/Issue868_AvisosFichaFotoMozo.sql`; sin la columna o
  sin blob, no se guarda pero la foto va igual en el correo). Correo: «La foto de la tienda que ha visto» (cid foto-ficha) y «Lo que tiene el mozo en la
  mano» (cid foto-mozo). Varios informantes: la última foto (sin foto nueva se queda la guardada; el correo solo lleva la del envío).

### Reenvíos: solo sale lo que ha vuelto (NestoAPI#870, 16/11/26)
- El reenvío de «Reenviar» (#699) nace en Pendientes (corte 1). La caja del original «ha vuelto» = fila en `dbo.EnviosAgenciaHaVuelto`
  (fuera del EDMX, `Scripts/Issue870_EnviosAgenciaHaVuelto.sql`; Envio, Fecha, Usuario, Como Lectura/YaEstaAqui/AlImprimir, Codigo) + línea
  `HaVuelto` en EnviosHistoria (`Infraestructure/Agencias/EnviosHaVuelto.cs`). Idempotente; se marca aunque no tenga reenvío; solo lo que ha salido (Estado >= 1).
- `POST api/EnviosAgencias/{id}/HaVuelto` (Nesto, «Ya está aquí» en Devueltos) y `POST api/EnviosAgencias/HaVuelto` {Codigo} (Ariadna, Entradas › F6
  «Ha vuelto un paquete»: CodigoBarras exacto o con 1-4 cifras de bulto al final, o el número de envío tecleado). 404/409 (NO_HA_SALIDO,
  VARIOS_ENVIOS)/503 sin el script. Con reenvío esperando: campana `EnvioHaVuelto` + push NestoApp a quien lo pidió (no si lo marca él ni forzado).
- LA GUARDA (`ServicioEnviosHaVuelto.EsperandoCajaAsync`): reenvío (Decision Reenviar, < tramitado) sin vuelta del original → 409 `ESPERANDO_CAJA`
  en PUT (pendiente→en curso/tramitado), Tramitar, ImprimirEtiqueta, EtiquetaZpl y ConfirmarTramitacion; `?yaEstaAqui=true` fuerza y marca
  (AlImprimir). TramitarTodos (GLS) lo salta (`Resultado` EsperandoCaja). La propuesta de envío no reutiliza ese pendiente. Sin las tablas o si
  falla la comprobación: no actúa (ELMAH). EntregadoPorLaAgencia sigue igual. Devueltos trae HaVuelto/FechaHaVuelto/UsuarioHaVuelto/ComoHaVuelto/ReenvioEsperandoCaja.

### Prepago y saldo a favor (Novedades 1302, 17/11/26)
- Un pago es de UN documento (Carlos 17/11/26): cada saldo a favor (ImportePdte < 0) se clasifica por los números de su Concepto (y, solo
  como pedido, su Nº Documento) contra los pedidos y facturas DEL MISMO cliente (`ClasificadorSaldoAFavor`, puro; el rellenador lee esos
  números en una consulta por cosa). Reservado a un pedido = solo cubre ese pedido, aunque no salga o ya esté facturado; reservado a una
  factura = no cubre ningún pedido; sin documento del cliente («curso WOW», 987654 de otro cliente, fechas) = saldo LIBRE.
- Disponible de un pedido PRE = sus Prepagos + lo reservado a él + su parte del saldo libre (± `DESCUADRE_PERMITIDO`). Libre = sin documento −
  deuda vencida (#815 igual) − lo que se llevan los otros PRE del cliente: los de pickings anteriores sin facturar (max(0, importe en picking −
  sus prepagos − lo reservado a ellos), `IRellenadorPrepagosService.PedidosPrepagoEnPicking`, una consulta) y, en la misma pasada, los de
  número menor que salen. Regla pura `ReglaSaldoAFavorPrepago`; acumulador `RepartoSaldoAFavorPrepago` (GestorPicking lo prepara y decide por
  número; el que no sale suelta su parte del LIBRE). Antes cada pedido veía el saldo entero (930831 y 930835 del 42004, picking 100105).

### Versión del pedido y 409 al guardar con copia vieja (NestoAPI#873, gemela Nesto#642, 17/11/26)
- Caso 929555: un PUT con una copia de antes del prepago («Prepagos: []») lo borró. `PedidoVentaDTO.Version` (opaca): LA REGLA pura
  `VersionPedidoVenta.Calcular` sobre UNA consulta (`LectorVersionPedidoSql`, sin NOLOCK): RowVersion de la cabecera, nº y MAX(RowVersion)
  de las líneas, y nº/suma de Id/CHECKSUM de prepagos y efectos (cambia con cualquier guardado, lo haga quien lo haga). La rellenan
  GET pedido (leída ANTES que el pedido: de más, nunca de menos), POST y la respuesta del PUT (`RespuestaModificacionPedidoDTO.Version`,
  ahora casi siempre 200). `GET api/PedidosVenta/{empresa}/{numero}/Version` → { Version, Usuario, Fecha } (sin leer el pedido).
- PUT con Version distinta → 409 `PEDIDO_CAMBIADO` { error: { code, message, details: { Version, Usuario, Fecha } }, Message } ANTES de tocar
  nada («El pedido 929555 lo ha modificado Aida a las 15:14 desde que usted lo abrió. Recárguelo y vuelva a hacer los cambios.»; quién y
  cuándo = lo más reciente de cabecera/líneas/prepagos/efectos). Sin Version (NestoApp, TNV, Nesto viejo, CambiarCliente, UnirPedidos), como antes.
- Prepagos (`AplicarPrepagosAlModificar`): los que no trae el PUT SOLO se quitan con la Version comprobada (`PuedeQuitarPrepagosQueNoVienen`);
  sin ella se conservan, aviso `PrepagoConservado` (respuesta y correo) y ELMAH. Añadir/actualizar por concepto, como siempre.
- `?devolverPedido=true` (opcional, por tamaño) en PUT y POST: la respuesta trae además el pedido tal como ha quedado (el del GET, con
  los Nº de orden de las líneas nuevas) y su Version, leídos de forma coherente (`LeerPedidoConVersionCoherente`: versión, pedido,
  versión; si cambia mientras, repite 3 veces; si no, null). PUT → `RespuestaModificacionPedidoDTO.Pedido`; POST → `PedidoGuardado` del
  cuerpo. Nulos no viajan. Nesto lo adopta como foto para «Recargar conservando mis cambios» (Nesto#642); NestoApp#285.
- SignalR: `pedidoCambiado` {Empresa, Pedido, Usuario, Fecha, Version} al grupo `Pedido:{empresa}:{numero}` del **AlmacenHub** (donde Nesto ya
  entra/sale con Entrar/Salir), a todos (Nesto ignora el suyo por la Version), solo si alguien lo tiene abierto, en segundo plano
  (`AvisoPedidoCambiado`, nunca lanza): PUT, POST, cobro de la app (`ServicioPagos`) y pago a plazos (`RepositorioPagoAPlazos`).

### Ampliar un pedido en preparación (caso 931007, 18/11/26)
- Añadir líneas de PRODUCTO (id 0, cantidad > 0, sin evento, cheque ni cupón) a un pedido EN PREPARACIÓN que sale hoy ya no pasa en silencio.
  LA REGLA (pura): `AmpliacionPedidoEnPreparacion.Evaluar`; «en preparación» = la de #533 (`CambioModoConPicking.TienePickingVivo` /
  `TieneAlbaranDeHoy`). No salta en presupuestos, notas, ni si hoy no sale (`GuardiaPedidoRetenido`: retenido, aplazado, por falta): ahí se guarda y
  campana normal a Almacén. Las de texto nuevas van con los productos; las de cuenta contable nuevas se quedan. Sustituye a `ComprobarSiSePuedenInsertarLineas` (no saltaba tras el corte).
- PUT (`ServicioAmpliacionPedidoEnPreparacion.Comprobar`, antes de tocar nada): `PedidoVentaDTO.AmpliacionEnPreparacion` null → 409 `PEDIDO_EN_PREPARACION`
  (details Pickings, Envio, AgenciaYaLoTiene, Opciones, Productos); «PedidoNuevo» → en UNA transacción el original sin lo añadido (el mismo PUT) y un
  pedido nuevo por el POST de siempre (`Separar`, núcleo común; sin prepago: lo dice el aviso), `RespuestaModificacionPedidoDTO.PedidoNuevo` (y en
  UnirPedidos `PedidoNuevoAmpliacion`; la elección solo viaja en una ampliación nueva, la plantilla); «PedirAlmacen» → solo si la agencia no lo tiene
  (`YaLoTieneLaAgencia`, en la 14 = cargado en la furgoneta) ni hay etiqueta en curso con reembolso: se guarda y se pregunta a Almacén.
- PedirAlmacen: `dbo.AmpliacionesPedidoEnPreparacion` (fuera del EDMX, `Scripts/AmpliacionesPedidoEnPreparacion.sql`; sin ella, 503 y PedidoNuevo igual).
  Urgente #775 (tipo `AmpliacionPedidoEnPreparacion`, acción «pregunta», referencia = Id) a los Nesto de Almacén. `POST api/PedidosVenta/AmpliacionesEnPreparacion/{id}/Respuesta`
  {Entra} (Almacén/Dirección/Informática): «Sí» saca el picking del pedido (`GestorPicking.SacarPicking`; si no coge lo añadido, cuenta como «No»), «No» o
  60 min sin respuesta (`AmpliacionesPedidoJobs.Vencer`, Hangfire) lo pasan a un pedido nuevo (`PasoAmpliacionAPedidoNuevo`: si ya tiene picking, no se
  toca y se avisa). `notificacionResuelta` + campana/push a quien lo pidió. `GET …/AmpliacionesEnPreparacion[?pendientes=false]`, `GET …/{id}`.
- Correo: lo añadido sin picking en un pedido con picking sale como «Añadido después de sacar el picking (pendiente de que Almacén diga…)» o, en
  «Todo junto», «(sale aparte; puede salir el…)» (`GestorPresupuestos.PartidasAnadidasTrasElPicking`).

### Dirección 2 y número propio en el alta (NestoAPI#874 corte 1, Nesto#643, 17/11/26)
- `Clientes.Dirección` sigue siendo calle y número (50). Lo que necesita el repartidor va en `Clientes.Direccion2` (nvarchar(100)) +
  `TipoLugar` (Piso/Local/Casa/Nave/Otro, CHECK `CK_Clientes_TipoLugar`), fuera del EDMX (`Scripts/Issue874_ClientesDireccion2.sql`;
  `Direccion2ClienteSql`, sin columnas: no guarda ni lee y ELMAH una vez).
- LA REGLA (pura): `Direccion2Cliente.Componer` / `ParaDireccion(clienteCrear, exigir)`: Piso = piso y puerta («1º D»); Local = nº opcional
  + RÓTULO obligatorio sin valor por defecto («LOCAL 6 · RÓTULO: EL EDÉN»); Casa («CASA»); Nave = nº («NAVE 4»); Otro = texto. Mayúsculas.
  En el alta (`ClienteCrear.TipoLugar`, `Piso`, `Puerta`, `NumeroLocal`, `Rotulo`, `NumeroNave`, `OtroTexto`): con dirección y sin tipo → 400
  solo con ParametrosUsuario «(defecto)» `ExigirDireccion2Alta` = «1» (nace a «0» hasta NestoApp#286); con tipo se valida siempre.
  Pensada para que la use también la modificación cuando CAMBIE la dirección (Carlos 17/11/26; pendiente, corte 2).
- Número propio: si Places da la calle sin número, `ComprobarDatosGenerales(..., numero)` lo comprueba con Google (geocodificación calle +
  número + CP, `IServicioDireccionesGoogle.Geocodificar`; `NumeroPropioDireccion.Confirmado`: mismo portal y mismo CP) y lo devuelve en
  `DireccionFormateada`; «S/N» no se pregunta. En «Número» SOLO va el portal (cifras + letra/BIS opcionales, o S/N): «Nº 99», «99 local 6»,
  texto → 400 siempre. Válido que Google no encuentra (Carlos 17/11/26): no bloquea; la respuesta trae `NumeroSinConfirmarPorGoogle` +
  `AvisoNumero` (la pregunta) y el alta (`ClienteCrear.Numero`, que se vuelve a comprobar) solo sigue con `NumeroConfirmadoSinGoogle`
  (si no, 400 con la pregunta); entonces apunta una fila en Modificaciones (Tabla `ClienteNumeroSinGoogle`). Google caído en el alta → sigue.
- Envío: `DireccionEnvioCliente.Componer` (pura): Dirección + «, » + Dirección 2 si cabe en 80; si no, delante de las Observaciones (80) sin
  quitar nada; si tampoco, aviso. Núcleo: `GestorPedidoParaAgencia` (GET ParaAgencia de Nesto, propuesta del servidor, DesdePedido y packing;
  el aviso sale en `PropuestaEnvioDTO.AvisoDireccion2` y en los avisos de DesdePedido) + etiqueta pendiente y ActualizarDireccionEtiqueta.
- **A Odoo (corte 3, 18/11/26), SOLO de ida**: el mensaje de clientes (`ClienteSyncMessage`, uno por contacto) lleva `Direccion2` y `TipoLugar` al
  lado de `Direccion`, ausentes (WhenWritingNull) si el contacto no tiene, si están en blanco o sin el script; nunca "". Se leen en UNA consulta
  por cliente (`Direccion2ClienteSql.LeerDeClientes`, vía `IServicioGestorClientes.LeerDireccion2Clientes`; el gestor guarda las filas del último
  cliente para sus contactos seguidos y la publicación suelta las vuelve a leer). Cuándo viajan: en cualquier publicación del cliente; el alta
  escribe la Dirección 2 ANTES de publicar, así que su mensaje ya la lleva. `trgClientesUpd` NO encola por Direccion2/TipoLugar (hoy solo las
  escribe el alta). De Odoo a Nesto se ignoran: ni `ClienteChangeDetector` ni `BarreraClientesExternos` las miran.
- **En los correos del disparador (19/11/26, caso 42154/0)**: «Cambio de Vendedor» y «Cambio de Dirección» pintan «Dirección, Dirección 2»
  (`Scripts/Issue874_trgClientesUpd_Direccion2.sql`, vuelta atrás `…_ROLLBACK.sql`). En el alta la columna aún es NULL en el INSERT: antes de
  guardar la ficha, `Clientes.Direccion2EnSesion.Poner` (vía `GestorClientes.PonerDireccion2EnSesion`) deja la Dirección 2 en el contexto de la
  sesión (`sp_set_session_context`, clave `Direccion2Alta:{empresa}|{cliente}|{contacto}`) con la conexión abierta a mano (misma sesión que el
  INSERT; en el alta con entrega, la de la transacción) y la borra al acabar; el disparador usa esa clave antes que `i.Direccion2`. Nunca
  rompe el alta (ELMAH una vez). Para la modificación (corte 2) vale el mismo mecanismo: poner la nueva antes del SaveChanges.
- **Dirección de entrega distinta en la misma alta (19/11/26, Carlos)**: `ClienteCrear.DireccionEntrega` (`DireccionEntregaCrear`, opcional; sin
  él, el alta de siempre): Direccion, DireccionVerificada, Numero, NumeroConfirmadoSinGoogle, TipoLugar + sus campos, CodigoPostal, Poblacion,
  Provincia, Ruta, Telefono, ComentariosRuta, ComentariosPicking. Solo en un cliente NUEVO con dirección fiscal (`AltaConDireccionEntrega.MotivoNoAdmitida`:
  con EsContacto, sin dirección, sin CP → 400). La segunda dirección pasa por el MISMO núcleo que la fiscal (`GestorClientes.ValidarDireccionAlta`:
  #499, «¿Qué es?» y número propio) y su 400 empieza por «Dirección de entrega: ». El contacto de entrega es un alta de contacto de las de siempre
  (`AltaConDireccionEntrega.ContactoDeEntrega` + `PrepararClienteCrear` con el contacto siguiente reservado): hereda NIF, nombre, estado, forma y
  plazos, IBAN (CCC), vendedores, días de servir y las MISMAS personas de contacto (comunes); no los Comentarios generales ni el origen. En UNA
  transacción (`AbrirTransaccionAlta`; lo que lee por otra conexión se lee antes del primer guardado): principal, entrega y `ContactoDefecto` del
  principal = el de entrega (ContactoCobro/Bonificacion siguen en el principal). Se publican los dos a Odoo (primero el principal), cada uno con su
  Dirección 2. Respuesta: la ficha del principal + `ContactoEntrega` (solo si se creó). Un CP nuevo compartido por las dos se crea una vez.
  Nesto: casilla «La entrega es en otra dirección» en «Datos generales» (`BloqueDireccionAlta`, el mismo bloque que la fiscal). NestoApp: issue gemela.
- **CP del alta solo de Google (19/11/26)**: en Nesto, al CREAR, el código postal no se teclea nunca salvo que Google no lo dé (novedad 482); en la
  modificación sigue como estaba. La API NO comprueba en el POST que el CP case con la dirección de Google (solo la pantalla; NestoApp sin cambios).
- **Dirección completa visible (NestoAPI#879, novedad 1320, 19/11/26)**: LA REGLA (pura) `DireccionCompletaCliente.Componer` = Dirección + «, » +
  Dirección 2 (sin ella, la Dirección; SIN truncar: es para pantalla; la de la etiqueta sigue siendo `DireccionEnvioCliente`). Campo aditivo
  `direccionCompleta` (solo lectura, el PUT lo ignora; `direccion` no cambia) en `ClienteDTO` de GET api/Clientes (las tres búsquedas y el cliente
  por empresa/cliente/contacto) y en `DireccionesEntregaClienteDTO` de GET PlantillaVentas/DireccionesEntrega. Las listas leen la Dirección 2 en
  UNA consulta por empresa (`Direccion2ClienteSql.LeerDeClientes`, IN por lotes de 500); sin columnas o si falla, = Dirección. Nesto la pinta en
  el selector de clientes (y su selector de entrega), la cabecera del detalle del pedido y la lista de la ficha (`DireccionParaMostrar`).
- **Entrega por defecto de un cliente que ya existe (NestoAPI#879 corte 3)**: facturación = ficha con ClientePrincipal; entrega por defecto =
  `ContactoDefecto` de la principal (`ReglaContactoDefecto`, pura). `GET api/Clientes/ContactoDefecto?empresa=&cliente=` → { Empresa, Cliente,
  ContactoFacturacion, ContactoDefecto } (404 sin cliente). `PUT api/Clientes/ContactoDefecto` { Empresa, Cliente, Contacto } (`[Authorize]`, sin
  grupo, como PUT api/Clientes): contacto del cliente y activo (400 de usted si no; 404 sin cliente), escribe ContactoDefecto (+Usuario) en las
  principales ACTIVAS y una fila en Modificaciones (Tabla `ClienteContactoDefecto`) en una transacción; si ya lo era, 200 con `Cambiado` false y
  nada escrito. No cambia la principal. Nesto: pestaña «Direcciones» de la ficha (ClienteComercial). OJO: hoy Nesto NO propone ese contacto al
  elegir cliente para un pedido (el selector pide el principal); cambiarlo está pendiente de decisión.

### Auditoría de seguridad: efectos del extracto (Nesto#644, hallazgos 8 y 2, 17/11/26)
- `PUT api/ExtractosCliente` (solo lo llama Nesto, ficha de cliente, con el token de Windows): `[Authorize]` + Administración/Dirección/Informática
  (`ExtractosClienteController.PuedeModificarEfecto`; 403 de usted). Escribe solo lo que cambia (textos sin relleno ni mayúsculas, null = vacío;
  vencimiento sin FechaVto = Fecha); sin cambios, 400 (antes 500). `IniciarPago` (`POST api/Pagos` y los cobros internos) rechaza con 400 un efecto que
  no existe o es de otro cliente (`ServicioPagos.MotivoEfectosDeOtroCliente`; no mira empresa ni contacto del cobro: 1/3 y varios contactos valen) y
  pone a cada efecto el contacto de su apunte.

### Qué picking lleva cada envío (incidencia 1328, 18/11/26)
- `dbo.EnviosAgenciaPickings` (Envio, Picking, FechaAlta, Usuario; PK Envio+Picking; fuera del EDMX, `Scripts/Issue1328_EnviosAgenciaPickings.sql`;
  sin ella, todo como antes y ELMAH una vez). Un único punto: `RepositorioPickingsEnvio` (`Infraestructure/Agencias/PickingsEnvio.cs`, nunca lanza).
  Se apunta (unión, nunca se quita) con los pickings de las líneas del pedido en curso o en albarán (`ReglasPickingsEnvio.PickingsDeLasLineas`) al
  crear en curso por POST api/EnviosAgencias, en DesdePedido (nuevo, pendiente reutilizado, ampliación; también el packing de Ariadna) y en el PUT
  de un envío aún en el almacén que queda en curso o más (`ApuntarAlModificar`). El reenvío (#699) copia los del original; el retorno de
  GestionEnTransito lleva `SIN_MERCANCIA` (0). La etiqueta pendiente (estado -1), las paradas de notas y las notas de cobro no apuntan (sin dato).
- LA REGLA (`AplazamientoPedidoConPicking.YaLoTieneLaAgencia(envios, pickings, …)`, `EnvioDelPedido.PuedeLlevar`): la agencia «ya tiene» las
  líneas que cambian de fecha si un envío del pedido tramitado o más (no devuelto ni reparto propio por entregar) PUEDE llevarlas: con dato, si
  lleva alguno de sus pickings; sin dato, si no está terminado (entregado 2 o cerrado a mano 5: lo terminado no puede contener lo que sigue en el
  almacén). Lo mismo filtra las etiquetas a anular y los envíos del reparto propio a los que se mueve el día. La usan el PUT del pedido (por línea)
  y AvisarLogisticaAplazamiento. Caso 930358: el 250999 entregado (picking 100095) ya no bloquea aplazar el 100114.

### Promesa en riesgo: frase corta, causa original y buena noticia (NestoAPI#693, Carlos 18/11/26)
- `TextosPromesaRiesgo.Motivo` (campana, pantalla de Nesto, que pinta `Motivo` tal cual, y correo): QUÉ FALTA, CUÁNDO LLEGA y CUÁNDO SALE, nada
  de lo que está en Algete. Agrupa misma causa y fecha («Faltan 45949 JABON CARBON ACTIVO y 5 productos más (50 uds.): llegan hoy del proveedor.
  Sale el jueves 19/11 (aproximado). Va todo junto.»); nombre a 30 letras por palabra; fechas SIEMPRE «el jueves 19/11» (nunca «hoy» ni
  «mañana»: la frase se guarda y se lee días después; año solo si es otro);
  certeza en una palabra (`TextosCertezaFechaEntrega.CalificativoCorto`: Segura y CasiSegura → seguro, Aproximada, Orientativa). Ya no lleva la
  causa del evento («Reina mandó 1 de 3…»: queda en `Evento`) ni «todo está listo en Algete, pero no salió…».
- Regla (`EvaluadorPromesaRiesgo.MereceAviso`): se avisa en el acto lo que tiene culpables (no listo en Algete) o fecha de entrega posterior.
  Registrado y ya sin culpables → `YaLlego`: «Faltaba X; llegó el martes 17/11. Sale el martes 17/11.», se resuelve con «Ha llegado lo que
  faltaba», campana UNA vez solo si se avisó la mala, y en el correo resumen en una línea solo si la mala salió en un correo (`LeerYaSalenSinCorreo`, 7 días).
- Todo en Algete y va tarde (sin registrar, o tras la buena noticia) → fila «en observación» (`EnObservacion` = 1, `TodoListoDesde`): sin
  aviso, fuera del listado y del correo; sigue siendo candidato. Si sale, se resuelve sin ruido. Si termina entero el primer día laborable de
  Algete posterior (`ObservacionVencida`, fines de semana y festivos) y sigue sin salir → `SigueSinSalir`: pasa a aviso normal («Está todo en
  Algete desde el lunes 16/11 y sigue sin salir. Es de prepago: compruebe que está pagado. Revise el pedido.»; causas baratas: prepago PRE,
  retenido por falta #858, fecha de entrega posterior, ruta propia; si no, «Revise el pedido (retenido, pendiente de prepago, fecha de
  entrega…).»). Una vez: que su fecha se corra un día cada día no es «empeorar»; si aparece un culpable, riesgo normal.
- Columnas opcionales `CausaOriginal` (JSON de lo que faltaba; se suma, nunca se pierde), `TodoListoDesde` y `EnObservacion`
  (`Scripts/PromesaRiesgo_CausaOriginal.sql`; la API las usa solo si están las tres; caché + ELMAH una vez). Sin ellas: textos nuevos, buena
  noticia «Ya está todo en Algete: sale en el próximo picking.» y sin aviso de los que lo tienen todo en Algete (como antes).
- **Lo que ya viene de camino va antes que lo libre de las tiendas (pedido 930942, 19/11/26)**: el repartidor del motor #606 daba lo que
  falta a lo LIBRE de una tienda antes que a la reposición ya terminada que viene a Algete (Alcobendas mandó la unidad en la 81463 y el aviso
  dijo «tiene que venir de Reina… lunes 23/11»). Ahora `RepartidorStockFechaEntregaAgencia.TomarEnCaminoAAlgete` (puro) coge primero lo de
  camino (`DatosSombraModoServicio.EnCaminoAAlgete`, `SQL_EN_CAMINO_A_ALGETE`: PreExtrProducto neto por empresa+traspaso+producto > 0 con la
  entrada en ALG y la tienda de su Delegación), descontando lo que ya deben en Algete los demás pedidos (la reposición más antigua es suya).
  La partida `EnCamino` lleva `TraspasoEnCamino`/`TiendaEnCamino` (NO `Tienda`: no se reserva nada en ella) y los textos dicen «viene de
  camino en la reposición 81463 de Alcobendas» (`CalculadoraFechaEntregaAgencia.DeCamino`). Correo resumen: solo va lo recalculado sin
  error en `RevisarAntesDelCorreo` (`PedidosRevisados`) o lo detectado después de empezar (`ReglaCorreoPromesaRiesgo.PuedeIr`); lo demás
  espera al siguiente correo, nunca sale el porqué de hace horas.

### Modo de servicio 6, «Junto con lo pendiente del cliente» (sugerencia 1340, 18/11/26; Nesto gemelo)
- `ModosServicio.JUNTO_CON_LO_PENDIENTE` = 6: un «todo junto» (ServirJunto = 1, `EsTodoJunto`/`EsEntregaUnica` true, facturación como el 1) que
  además no sale hasta que pueda salir lo pendiente del MISMO empresa+cliente+contacto. `Efectivo(6, true)` = 6; ServirJunto desmarcado → 2.
  `EsValido` sigue siendo 1..4 (parámetro ModoServicioPorDefecto); `EsElegible` = 1..4 + 6. Nunca se sugiere.
- LA REGLA (`Infraestructure/PedidosVenta/JuntoConPendiente/ReglaJuntoConPendiente`): cuenta lo que tenga alguna línea de producto en ALG,
  -1/1, con cantidad y SIN picking (también 31/12/2099: las notas nacen así). No cuentan el propio, presupuestos, cursos (van en la tienda),
  notas de cobro/recogida (sin producto), retenidos (5) ni otros 6 (dos 6 no se esperan). Lectura en UNA consulta (`RepositorioJuntoConPendiente`).
- Script `Scripts/Sugerencia1340_ModoJuntoConLoPendiente.sql`: amplía CK_CabPedidoVta_ModoServicio a 1..6 (lección #698) y crea
  `PedidosJuntoConPendienteAvisos`. Sin él (`ModoAdmitido`, caché 5 min): no se ofrece, elegirlo → 400, el picking y el job no hacen nada.
- Se OFRECE (ModoServicioSugerido y OpcionesModos, copia de la sugerencia, la caché no se toca) solo si hay algo con lo que juntarse o el pedido ya
  está en 6, nunca en tienda; `ModoServicioPermitidoDTO.Explicacion` («Saldrá junto con la nota de entrega 929701.») y `PedidosConLosQueSeJunta`.
  POST/PUT: ENTRAR en el 6 sin nada pendiente o sin script → 400 `MODO_SERVICIO_NO_PERMITIDO` (alternativa 1); quien devuelve el 6 no se rechaza.
- Picking (`Models/Picking/JuntoConPendientePicking`): el rellenador arrastra los 6 del mismo contacto que un candidato y lo pendiente de los 6
  (picking de un pedido, de un cliente o de rutas). El 6 se decide DESPUÉS de los demás: sale si todo lo suyo está en la pasada y sale
  (`MotivoNoSalePicking.EsperaPendienteDelCliente` si no); mismo número de picking → un envío en el packing. Sin nada pendiente, como el 1.
- Portes (decisión 7): al grabar, la base de lo pendiente se suma y si lo pendiente ya lleva portes el 6 no los añade; en el picking el grupo
  que sale junto suma lo que se factura (`ImporteEntregaJunto`) y paga portes UNA vez (`PortesYaEnElGrupo`).
- Fecha (#606): el motor calcula el 6 como 1 y cada pendiente con su modo; sale el día del PEOR (`ReglaFechaJuntoConPendiente`,
  `FechaEntregaAgenciaDTO.JuntoConPendiente`): «Sale el lunes 30/11/2026, junto con la nota de entrega 929701, que espera el … (45238).» Va al pie
  del correo y a la promesa en riesgo (culpable propio; `SQL_CANDIDATOS` incluye los 6 cuyo contacto tiene pendiente el producto).
- Job `pedidos-junto-con-pendiente-esperando` (L-V 9:30): 15 días en 6 (desde PedidosModoServicioOrigen #704; si no, la fecha del pedido) y
  aún esperando → campana/push a quien lo hizo y push a su vendedor, una vez (`PedidosJuntoConPendienteAvisos`).

### Agency and Shipping
- Multiple shipping agencies supported
- Special handling for Glovo (`GestorAgenciasGlovo`)
- Shipping cost tracking through specific accounts
- **Seguimiento automático y Retrasados (sugerencia 1329, 18/11/26)**: LA REGLA `ReglaSeguimientoAutomaticoAgencia`: una agencia tiene
  seguimiento automático si la consulta el job de seguimiento (`IFabricaAgenciasRemotas.AgenciasConSeguimiento`: perfil con
  `IPerfilConSeguimiento` + puerta de activas, sin cuarentena ni sombra; con perfil de seguimiento: GLS 1, Innovatrans 12 y CTT 13) o si su perfil declara
  `IPerfilConEstadosPropios` (el reparto propio 14: el repartidor cierra los envíos desde Ariadna). `GET api/EnviosAgencias/Retrasados`
  solo lista las de esa regla: Canteras (11), la GLS de la espejo (5), Glovo, Sending, CEX… no salen (se quedaban 60 días en Tramitado).
  Ninguna agencia escrita a mano: la que tenga perfil con seguimiento entra sola.
- **Devueltos: `Resuelto` (sugerencia 1329)**: `EnvioDevueltoDTO.Resuelto` (solo lectura, `ReglaDevueltoResuelto`) = la caja ha vuelto
  (#870) + decisión tomada + (si es Reenviar) el reenvío ya tramitado; y siempre los `EntregadoPorLaAgencia` (Carlos 18/11/26: si la agencia acabó entregando, ya no se espera caja). La API devuelve todos; Nesto oculta los resueltos por defecto
  (casilla «Ver también los resueltos») y tiene una caja de lectura que llama a `POST api/EnviosAgencias/HaVuelto` {Codigo}.
- **Devueltos por pedido (sugerencia 1356, 19/11/26)**: `GET api/EnviosAgencias/Devueltos?empresa=&pedido=` filtra en la consulta (misma
  ventana de 60 días); sin `pedido`, como siempre. Lo usa la pestaña «Entrega» del pedido en Nesto. Reenviar/Abonar siguen con solo `[Authorize]`.

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
