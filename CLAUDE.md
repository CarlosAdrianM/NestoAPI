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
- **Nota de entrega: cambios solo de cabecera (novedad 1365, 19/11/26)**: el PUT de una nota ya no da «No se puede ampliar una nota de entrega»
  si solo toca la cabecera («Recoger producto», aviso con importe, comentarios…): regla pura `PedidosVentaController.EsCambioSoloCabeceraEnNotaEntrega`
  (ni cliente, contacto, IVA ni DescuentoPP; ni líneas nuevas o que faltan; ni producto, texto, precio, cantidad, descuentos o fecha; fecha default y
  almacén vacío no cuentan). También en una nota ya procesada (líneas -2) sin nada facturado (`PuedeModificarsePedido`).
- **Borrar un pedido (Novedades 1283, Carlos 20/11/26: «nada puede desaparecer mientras haya mercancía o dinero en marcha»; sin excepciones por grupo)**:
  `DELETE api/PedidosVenta` comprueba ANTES de tocar nada LA REGLA (pura) `ReglaBorrarPedidoVenta.MotivoNoSePuedeBorrar`: líneas con picking, en albarán o
  factura o procesadas (-2), envíos en curso o más (las etiquetas pendientes se siguen borrando), prepagos (suma ≠ 0) o lo cobrado con enlaces (`LectorEnlacesPagoPedido`).
  409 `PEDIDO_NO_SE_PUEDE_BORRAR` con todos los motivos en una frase y qué hacer; si no se puede leer (`LectorBorrarPedidoVentaSql`, una consulta + enlaces), 503 y nada
  borrado. Va ANTES del 409 de las inscripciones (#732). Los borrados internos (anular recogida #855, traspaso entre empresas) no pasan por aquí.
- El Edén client (`Constantes.ClientesEspeciales.EL_EDEN`) bypasses all validations
- Orders validate through denial then acceptance validator pipeline
- Each product line validated individually with specific product ID tracking
- Material promocional (subgroup = "Muestras") always gets 100% discount

### Pedir autorización de un pedido que no pasa la validación (Novedades 1258, 20/11/26; Nesto gemelo)
- LA REGLA de quién puede «crearlo de todos modos» = quién puede AUTORIZAR el de otro: `ReglaOmitirValidacionPedido` (`Infraestructure/PedidosVenta/
  Autorizaciones/`), sacada tal cual del POST de PedidosVenta (que ya la usa): Dirección, Almacén o ParametrosUsuario `PermitirCrearPedidoConErroresValidacion`;
  Tiendas si TODAS las líneas son de su `AlmacénPedidoVta`; y el `PermisoNecesario` de la denegación (#501) si lo pide. Los parámetros se leen solo si hace
  falta (la fila «(defecto)» crea la del usuario). El PUT y `UsuarioPuedeOmitirValidacion` siguen con su copia de siempre (no se han tocado).
- `dbo.PedidosAutorizacionesSolicitadas` (fuera del EDMX, `Scripts/PedidosAutorizacionesSolicitadas.sql`; sin ella los seis endpoints dan 503): PedidoJson
  (el DTO tal como se iba a guardar; `RecogeEnTienda` se guarda como lo mandó el cliente, no calculado), Motivos (JSON {Motivos, Errores}), Estado
  Pendiente/Autorizada/Denegada/Anulada (CHECK) y, además del contrato, Huella, PermisoNecesario, Importe, NumeroLineas, Vendedor y Almacenes.
- `PedidosVentaAutorizacionesController`: `POST api/PedidosVenta/Autorizaciones` {Pedido, Comentario?} (la API VUELVE a validar con `GestorPrecios.EsPedidoValido`:
  si pasa, 400 «no necesita autorización»; Pendiente idéntica del mismo solicitante —`ReglasAutorizacionPedido.Huella`: cliente, contacto y líneas sin
  orden— en 24 h → 409 {Message, Codigo YA_SOLICITADA, Id}) → 201 {Id, Estado, Motivos, Errores, Destinatarios}; `GET …?pendientes=true&mias=false` (30 días;
  ve las suyas y las que LA REGLA le deja autorizar), `GET …/{id}` (+ Pedido; 403), `POST …/{id}/Autorizar` {Comentario?}, `POST …/{id}/Denegar` {Motivo 1..500},
  `DELETE …/{id}` (solo quien la pidió). 409 si ya no está Pendiente.
- Autorizar: bloqueo `sp_getapplock` de sesión `PedidoAutorizacion:{id}` (otro a la vez → 409), y el pedido se crea por `PedidosVentaController.PostPedidoVenta`
  (el MISMO camino, con el principal de quien autoriza) con Usuario = solicitante y `CreadoSinPasarValidacion`; `PedidosVentaController.UsuarioPermisosValidacion`
  = quien autoriza (sus parámetros, no los del solicitante). Si el alta falla, su respuesta tal cual y la solicitud sigue Pendiente. Rastro en Modificaciones
  (Tabla `PedidoAutorizado`).
- Avisos (`AvisosAutorizacionPedido`, nunca rompen): campana de Nesto NO urgente `AutorizacionPedidoPedida` {tipo, id, referencia, empresa, cliente, contacto} a los
  que pueden autorizar (`ReglasAutorizacionPedido.Destinatarios`: grupos del dominio + filas PROPIAS de ParametrosUsuario, sin «(defecto)» ni el solicitante);
  al resolver, `AutorizacionPedidoResuelta` {…, estado, pedido} al solicitante (campana) y push de NestoApp al vendedor del pedido, y `notificacionResuelta`
  (Tipo `AutorizacionPedidoPedida`, Referencia = Id) a los demás; al anular, solo `notificacionResuelta`.

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
- **Muestras: etiqueta en el cajón o la gaveta (NestoAPI#884, Carlos 24/11/26)**: la muestra sin código de fabricante NO se etiqueta
  unidad a unidad: UNA etiqueta por referencia en el cajón del mostrador (REI, ALC) o en la gaveta / el hueco (ALG), con el interno del 29.
  Leerla es la vía cómoda, NO obligatoria: `MuestrasSinCodigo.Admitido` mira la ficha (`CodBarras`, donde el interno nunca va), así que
  la muestra cuyo único código es el interno sigue «admitida» (se teclea sin aviso). Todo en `MuestrasContenedor.cs` (reglas puras
  `ReglasMuestras`, lectura en 3 consultas `RepositorioMuestrasSql`) y `MuestrasController`: (a) `POST api/Productos/Muestras/CodigoInterno?ensayo=true`
  (por defecto ensayo: cuántas y cuáles; de verdad, cada una por `AsignarInterno` → `GuardarInternoEnExclusiva`, idempotente, una que
  falla no para las demás; una fila en Modificaciones Tabla `CodigoInternoMuestras`): muestras activas sin NINGÚN GTIN válido activo
  (ficha o tabla, el interno incluido); Almacén, Compras, Dirección, Informática. (b) `POST api/Almacen/EtiquetasProducto/Muestras?ensayo=true`
  {Almacen (ALG/REI/ALC), Familia (código o nombre), SoloConStock, Productos[]}: la MISMA 3×2 y la misma impresora que la de producto,
  con «MUESTRA · Ref. X» en pequeño y el nombre en grande (sin la palabra «MUESTRA»), EAN-13 a 54 puntos (`CONTENEDOR_*`); el código, el
  de la unidad (`CodigoDeLaUnidad`: fabricante, si no el interno); en ALG una por hueco con existencias (Ubicaciones 0/3/4) por
  `OrdenRecorrido` y detrás las sin hueco; en tiendas por familia y nombre; UN trabajo, 500 como mucho; de verdad, a la que no tiene se le
  da el interno DESPUÉS de mirar la impresora. (d) `GET api/Productos/Muestras/Hoja?almacen=&familia=&soloConStock=&conFotos=true` →
  PDF (QuestPDF, `HojaMuestras.cs`): por familia, 3 por fila, foto de la tienda (`IFotosProductoAlmacen` + descarga en paralelo, la que
  no llega no sale), nombre, referencia y EAN-13 dibujado (`Ean13`, como `Code128`); 404 sin muestras. (b) y (d): los permisos de las
  etiquetas de producto (también Tiendas). (c) `PideCantidad` (aditivo, = MMP, `MuestrasSinCodigo.PideCantidad`): un bip de una muestra
  NO es una unidad, la app pide la cantidad. Va en `ProductoPorCodigoBarrasDTO` (PorCodigoBarras y los 409 de candidatos), `Inventario`
  (GET/POST/PUT, `Inventario.Partial.cs`), líneas de recorrido/picking y salida de reposición, packing, Ubicar, Recibir COMP/REPO, Enviar
  reposición y reposición puntual. No en Productos/Buscar ni en la Ficha de Ariadna. Gemelas: Nesto#650, Ariadna#108.

### App de clientes (TNV): lector y refresco del token (registros de IIS 25/11/26)
- Lector: `GetProducto(codigoBarras)` (solo ese; `BuscarPorCodigoBarras` y los flujos de almacén siguen preguntando siempre) descarta las
  fichas anuladas (Estado < 0 o null) si queda alguna viva (`ProductosController.CoincidenciasAConsiderarLector`, pura): una viva → esa, sin 409;
  varias vivas → 409 solo con ellas; todas anuladas → 409 con todas (8436538394380: 44967 anulado / 45931 vivo).
- Refresco: `api/auth/refreshToken` ya no corta a un mes de caducado el token: la ventana desde la caducidad son los mismos días que la
  absoluta desde el login (`Auth:DiasMaximosSesionTienda`, 90; `PoliticaRefrescoToken.VentanaDesdeCaducidadVencida`). 15 de 26 clientes
  echados tras un mes sin abrir la app no volvieron a entrar. Firma, absoluta y revalidación del cliente, igual; `refreshOAuthToken` no cambia.

### Etiquetas de inmovilizado (Carlos 25/11/26; gemela Nesto)
- Máquinas inmovilizadas tras el inventario (demostración, uso propio): etiqueta 3×2 «INMOVILIZADO · NO ES STOCK», «I/1823» en grande con la
  delegación, la descripción en hasta 3 líneas y un Code128 con el texto EXACTO «I/1823» (56 puntos de alto). La descripción, por LA REGLA
  `ReglaDescripcionEtiquetaInmovilizado` (sin cortar palabras): el número de serie («n/s», «s/n», «nº serie», hasta el final) sale SIEMPRE entero;
  si no cabe, se recorta el nombre con «…» y, después, la referencia. LA REGLA `CodigoInmovilizado` (`Infraestructure/Inmovilizados/`):
  `Componer` («I/» + número sin relleno) y `Leer` («I/1823», «i/1823», espacios; solo cifras; el número a secas NO). Tabla Inmovilizados (EDMX), solo lectura.
- `GET api/Inmovilizados?empresa=1&filtro=&delegacion=&soloVivos=true` (`InmovilizadosController`; cliente de la tienda 403; 500 como mucho, los más nuevos
  primero; filtro en número —«I/1823» = ese exacto— y descripción) → [{Empresa, Numero, Codigo, Descripcion, Grupo, Delegacion, Estado, EstadoDescripcion,
  FechaCreacion}]. `POST api/Inmovilizados/Etiquetas/Imprimir` {Empresa?, Numeros 1..50, Copias 1..10, Impresora?} (Administración, Dirección, Almacén,
  Tiendas, Informática; 404 si no existe ninguno) → {Impresas, Etiquetas [{Numero, Codigo, Descripcion, Delegacion, Impresora, Ok, Motivo}]}; un trabajo
  por impresora por `ImpresoraEtiquetasConAjuste` (EPL/ZPL y ajuste, como la de producto). Impresora: `ReglaImpresoraInmovilizado` (la pedida; si no, la
  fila «(defecto)» `ImpresoraEtiquetasInmovilizado.{ALG|REI|ALC}` —sin crear filas— o etiquetas2/etiquetasreina/etiquetasalcobendas; VAR u otra, la
  ImpresoraCodBarras de quien imprime).
- Leerla donde se busca un producto (`ReglasInmovilizados.Reconocer`, solo lee la BD si tiene el formato; nunca casa con el producto 1823): cuerpo
  `EsInmovilizadoDTO` {Codigo «ES_INMOVILIZADO», Message «I/1823 es un inmovilizado (…): no es stock, no se cuenta ni se prepara.» / «… tiene formato de
  inmovilizado, pero no existe.», Inmovilizado, Productos []}. 409 en GET/POST api/Inventarios (sus clientes ya leen Codigo + Message de un 409); 404 en
  GET api/Productos?codigoBarras= (su 409 es una LISTA: la app de clientes y Ariadna la deserializan) y en la ficha de Ariadna (también
  `Productos/I/{numero}/Ficha`, por la barra escapada). PorCodigoBarras y Productos/Buscar (200 con lista) no cambian: dan la lista vacía.

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
- **Entrega incompleta (NestoAPI#880 corte 1, 19/11/26)**: `EntregaIncompletaReparto.Evaluar` (pura, `FurgonetaBultos.cs`) clasifica cada bulto
  1..Bultos al entregar: Leido, EntregadoSinLeer (lo llevaba quien entrega), NoLeidoSeQueda (`EntregaRepartoDTO.SoloLeidos` = true con algún leído:
  NO se cierra, sigue en su furgoneta), NoSeCargo (sin carga vigente ni movimiento), VolvioAlAlmacen (Descargado), LoLlevaOtro. Si falta alguno y hay
  datos (algo cargado o leído; sin leer nada al cargar ni al entregar NO cuenta), el envío queda Entregado con DetalleEstado «Entregado (reparto propio):
  4 de 5 bultos. Falta RP…-5» (`PREFIJO_DETALLE`, `EsIncompleta`) + historia «Bultos» en la misma transacción; respuesta `BultosSinEntregar`,
  `BultosQueFaltan` {Codigo, Bulto, Situacion, Motivo}, `ResumenEntregaIncompleta` (también en el reintento); UN aviso tras el OK
  (`IAvisosEntregaIncompleta`: campana a Almacén + campana/push a quien hizo el pedido y su vendedor, tipo `EntregaIncompletaReparto`). El job de las
  22:00 no cierra los bultos de un envío incompleto. Ni recogidas, ni NC, ni reposiciones. `EntregaIncompleta` (+ `DetalleEntregaIncompleta`) en los
  DTO de envío: Nesto lo marca en Agencias › Tramitados («Incompleto», cabecera) y en la pestaña Entrega del pedido.
- **Reembolso entero o autorizado (Carlos 19/11/26)**: en `Entregar` con Efectivo e `ImporteCobrado` < reembolso − 0,01 € hace falta la MISMA
  autorización que para no cobrar (Autorizada o Telefonica, #773; vale para cualquier importe); sin ella, 409 `Codigo` REEMBOLSO_PARCIAL (en la raíz
  y en error.code), regla pura `ReglaReembolsoEntero`. Cobrar de más no cambia. Las notas de cobro NO (lo de menos queda a cuenta). La petición de
  autorización admite `ImporteCobrado` opcional (sin columna: va en el Motivo; si Ariadna ya lo escribe, no se repite).
- **Bultos en la prueba de entrega (19/11/26)**: `PruebaEntregaDTO`/`PruebaEntregaEnvioDTO` traen (aditivo, solo reparto propio) `Bultos`
  [{Codigo, Bulto, Situacion, Entregado, Texto}] y `ResumenBultos` («Leídos 6 de 7. Sin leer: RP…-7.»), con la misma regla de #880 sobre el envío ya
  entregado (`EntregaIncompletaReparto.EvaluarEntregado`). Sin movimientos RP del envío (antiguos, otras agencias), null. Nesto lo pinta en una línea.
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
- **Solo lo cargado y carga obligatoria (punto 6, Carlos 20/11/26)**: una parada es de un repartidor solo cuando ha leído algo suyo (RP, o el papel
  NE/NR; las NC nacen asignadas). `GET Envios?soloMios=true&soloCargados=true` = SOLO las suyas (LA REGLA `NucleoParadasReparto.Visible(…, soloCargados)`;
  sin el parámetro, las suyas + las de nadie, como siempre). `SoloCargados` (opcional) en OrdenSugerido/Estimacion (ya eran solo las suyas).
  `POST Carga/Terminar` {Fecha?} → {SinCargar[{EnvioId, CodigoDocumento, Nombre, Poblacion, Bultos, BultosSinCargar}], Total, Mensaje}: lo de «Sin cargar»
  que no lleva NADIE; campana `RepartoSinCargar` a Almacén, una vez cada 30 min por conjunto (en memoria, `MemoriaAvisosSinCargar`). Interruptor «(defecto)»
  `Reparto.CargaObligatoria` (solo «1»): Entregar un envío o nota de entrega/recogida que no lleva quien entrega → 409 `NO_CARGADA`, salvo que en
  BultosEntregados venga algo de ESA parada: se carga a su nombre (`CargarEnvio` con Reasignar, clave derivada, mismo rastro) y sigue
  (`ReglaCargaObligatoria.Decidir`, `CargaObligatoriaReparto.cs`). Ni NC ni reposiciones; un reintento con la misma clave, tampoco.

### Manda el bulto: el candado de oficina y las lecturas rechazadas (Carlos 20/11/26)
- Un bulto del reparto propio está siempre en uno de: almacén sin cargar · furgoneta de un repartidor · entregado · de vuelta en el almacén. LA REGLA
  (pura) `CandadoFurgonetaEnvio.QuienLoLleva` (`CandadoFurgonetaEnvio.cs`): algún RP del envío (o el papel NR de su nota) cuyo último movimiento es
  Cargado o Recogido → quién y desde cuándo (núcleo `EnFurgoneta`, que usan también los envíos internos). Envíos 251169 (cargado 9:42, «cerrado a mano»
  9:43) y 251227 (borrado con la etiqueta pegada).
- En la 14, desde oficina, 409 `{ error, Message, Codigo: "EN_FURGONETA", Repartidor, Desde }` («No se puede: el envío 251169 lo lleva Andre en la
  furgoneta desde las 9:42. Que lo descargue o lo entregue primero.»): DarPorCerrado, Anular, DELETE y el PUT / ModificarDatos que cambie la agencia o
  pase a 2, 4 o 5 (`CambioQueFrena`). Sin el script de la furgoneta o si no se puede leer, se sigue (ELMAH). Lo del repartidor (Entregar, Incidencia,
  Descargar, Recoger) y los jobs no pasan por aquí. «Dar por cerrado» en la 14: SIEMPRE 409 `CIERRE_NO_PERMITIDO` (las recogidas «no recogidas» y
  «anuladas» van por RecogidasParadaReparto y no cambian). Modificar (dirección) no cambia agencia ni estado: sin candado.
- DELETE api/EnviosAgencias/{id} deja SIEMPRE rastro (Modificaciones, Tabla `EnvioAgenciaBorrado`: las columnas del envío y la historia que se borra con
  él, `RastroBorradoEnvio`) en la MISMA transacción, de cualquier agencia y estado. (El borrado de un reenvío que sobra, `AnularPorEntregaAsync`, y el de
  las etiquetas pendientes al borrar un pedido no pasan por aquí.)
- `POST api/EnviosAgencias/{id}/Nota` {Texto 1..500} (Almacén, Administración, Dirección, Informática; 403 de usted): una línea en EnviosHistoria (Campo
  «Nota») sin tocar el envío. 200 {Numero, Texto, Usuario, Fecha}; 404.
- Lecturas rechazadas: `dbo.RepartoLecturasRechazadas` (`Scripts/RepartoLecturasRechazadas.sql`). Punto ÚNICO: el filtro `[ApuntarLecturasRechazadas]` de
  RepartoPropioController apunta en segundo plano (nunca rompe ni retrasa) todo 4xx de GetBulto, Cargar, Entregar, Incidencia, Recoger, Descargar y
  NoRecogida (`ReglaLecturasRechazadas`: código leído o EnvioId, Status, Codigo del 409, Mensaje 500, X-Dispositivo, X-Ariadna-Version). Sin la tabla, nada
  (ELMAH una vez). `GET api/RepartoPropio/LecturasRechazadas?dias=2&usuario=` (Almacén, Dirección, Informática; 500 como mucho; 503 sin el script).

### Envíos internos entre sedes (sugerencia 1277, Alfredo, 20/11/26)
- Lo que va de una sede a otra y NO es stock (café, folios, aparatos de demostración o de clientes): ni almacenes, ni ExtractoProducto, ni
  cliente/pedido ficticio. `dbo.RepartoEnviosInternos` (`Scripts/RepartoEnviosInternos.sql`, que además amplía la CHECK de TipoCodigo de
  RepartoBultosMovimientos con 'EI'; sin él, endpoints 503 y el reparto como antes). Bultos `EI{id}-{n}` con su historia en RepartoBultosMovimientos
  (TipoCodigo EI, EnvioId = la parada de entrega). Estado guardado Pendiente/Entregado/Anulado; EnCamino se deduce (algún bulto movido).
- LAS REGLAS (puras) `ReglasEnviosInternos` (`EnviosInternosReparto.cs`): sedes ALG/REI/ALC distintas, 1..50 bultos, descripción ≤200; paradas con EnvioId
  ≤ −1.000.000.000 (entrega −(1e9+2·id), recogida −(1e9+2·id+1); las de reposición, −Id, quedan por encima): RECOGIDA en el origen si no es Algete mientras
  quede algún bulto allí (de nadie hasta que alguien recoge uno), ENTREGA en el destino mientras esté vivo; `DondeEsta` por bulto; minutos de parada = reposición.
- Oficina (`ServicioEnviosInternos`): `POST api/RepartoPropio/EnviosInternos` {Origen, Destino, Descripcion, Bultos, Impresora?} → 201 {Id, Origen, Destino,
  Descripcion, Bultos, Estado, Codigos, EtiquetasImpresas} (etiqueta 4x6 de la plantilla del reparto propio, «ENVÍO INTERNO», en la impresora pedida o la
  ImpresoraAgencia del usuario; si no se puede, se crea igual); `POST …/{id}/ImprimirEtiquetas` {Impresora?}; `DELETE …/{id}?motivo=` (409 EN_FURGONETA
  con el cuerpo del candado; rastro `EnvioInternoAnulado`). Alta, impresión y anulación: Almacén, Dirección, Informática. `GET …/EnviosInternos?dias=7&soloVivos=true`
  (grupos del reparto) con `BultosDetalle` (Situacion SinCargar/EnFurgoneta/Entregado/Descargado y Texto).
- Reparto (`ServicioRepartoPropio.EnviosInternos.cs`): GET Envios las saca con `TipoParada` «EnvioInterno», `EsEnvioInterno`, `CodigoDocumento` EI{id},
  `FaseEnvioInterno` Recogida/Entrega, `SedeOrigen`, `SedeDestino`, `SedeParada`, `DescripcionEnvioInterno` y la dirección/coordenadas de la sede. GET Bulto/EI…:
  la de recogida si el bulto sigue en el origen, si no la de entrega. Cargar (origen Algete o bulto descargado), Recoger (origen tienda), Descargar y Entregar
  los de siempre con el código EI; Entregar exige el nombre de quien lo recibe (sin cobro, firma ni DNI), BultosEntregados opcional, «solo lo cargado»
  (`ReglaCargaObligatoria`) y entrega incompleta con el núcleo común `EntregaIncompletaReparto.EvaluarCodigos`. Sin Incidencia (400).

### Fin previsto de ruta del reparto propio (NestoAPI#881, 19/11/26)
- LA REGLA de los minutos por parada: `MinutosParadaReparto` (pura, `TiemposParadaReparto.cs`): envío y nota de entrega 15, nota de cobro 10,
  recogida 15, reposición 25; con servicio técnico, los tecleados en la nota, si no los del tipo, si no 60. Misma dirección (mismo cliente+contacto
  o mismas coordenadas a 4 decimales) = una vez (la mayor, en la primera). Colchón +15 % sobre los minutos del motor; descarga en Algete 15.
  Cierre: `HorarioCierreReparto` (L-J 17:00, V 15:00; sábado, domingo y `EsFestivoRepartoPropio` = sin cierre). Todo cambiable con
  ParametrosUsuario «(defecto)» `Reparto.MinutosParada*`, `Reparto.MinutosServicioTecnico`, `Reparto.ColchonConduccion` («15»),
  `Reparto.MinutosDescargaAlgete`, `Reparto.CierreLunesAJueves`/`CierreViernes` («17:00»), sin script (sin fila = el código; caché 5 min).
  Sustituye a los 5 min fijos: las llegadas y el «fuera de horario preferible» de OrdenSugerido (#763/#769) usan la misma regla y el colchón.
- `POST api/RepartoPropio/Estimacion` (misma petición que OrdenSugerido): evalúa el orden VIGENTE de las pendientes del repartidor, sin
  reordenar ni guardar → `EstimacionFinRutaDTO` (FinEstimado, LlegadaDestino, MinutosConduccion con colchón, MinutosParadas, MinutosDescarga, Km,
  HoraCierre, SeComparaConCierre, PasaDelCierre, MinutosDeMas, Aviso «Fin previsto 17:40 · 40 min después del cierre», Aproximada, Paradas[]
  con LlegadaEstimada y MinutosParada; además `MinutosRestantes` desde `CalculadoEl` y `HoraCierreDelDia` «17:00»: la PDA pinta ahora + MinutosRestantes). OrdenSugerido trae lo mismo en `FinRuta` (aditivo) y `MinutosParada` en cada parada. Cálculo puro
  `CalculoFinRuta`; servicio `ServicioRepartoPropio.FinRuta.cs`. Destino personalizado (casa) → solo se compara con el cierre si lleva algo para
  Algete (recogida pendiente en su ruta o NR/R «Recogido» en su furgoneta; la caja NO se mira) y entonces pasa por Algete. Sin coordenadas: centro
  del CP (`dbo.CentrosCodigosPostales`); sin OSRM: línea recta × 1,3 a 30 km/h. Nunca falla: sale `Aproximada`.
- **Servicio técnico (corte 2)**: `dbo.RepartoTiposServicioTecnico` (empieza vacía) y `NotasEntregaCabecera.TipoServicioTecnicoId`/`MinutosServicio`
  (`Scripts/Issue881_RepartoFinRutaYTiempos.sql`; sin él: tipos vacíos, POST/PUT 503, paradas sin servicio técnico). `GET/POST api/RepartoPropio/TiposServicioTecnico`
  (nombre repetido → el que hay), `GET/PUT api/NotasEntrega/{empresa}/{pedido}/ServicioTecnico` {TipoServicioTecnicoId | TipoServicioTecnicoNombre (crea el
  tipo), MinutosServicio 1..600; sin tipo = quitar; rastro `NotaServicioTecnico` en Modificaciones}. Al crear: `PedidoVentaDTO.tipoServicioTecnicoId/
  tipoServicioTecnicoNombre/minutosServicio` (solo POST; se valida antes y se guarda después) y los mismos campos en Confirmar una recogida solicitada.
  `GET Envios`: `ServicioTecnico` y `MinutosServicio` (tecleados, del tipo o 60) en las notas; entran en el fin de ruta. `ServicioServicioTecnicoReparto`.
- **Tiempos reales y modelo en sombra (corte 3)**: campos opcionales de la PDA: `FechaHoraLlegada` y `FechaHoraIr` en Entregar, Incidencia y Recoger
  (cuando cierra la reposición de una tienda); `Latitud`/`Longitud` en Cargar (al movimiento Cargado) y en Jornada/Terminar (`RepartoJornadas`, columnas
  opcionales). Tras cada OK, `dbo.RepartoParadasTiempos` (una fila por parada cerrada, idempotente por la clave; la inserción pone la parada ANTERIOR
  del repartidor ese día y su posición; `MinutosPrevistos` = la regla) y cada Estimacion/OrdenSugerido deja una foto en `dbo.RepartoEstimaciones`
  (una por repartidor cada 10 min; si no, actualiza). Nunca rompen (ELMAH). `ServicioRepartoPropio.Tiempos.cs`, `TiemposRealesReparto.cs` (ADO).
  Modelo (`RepartoPropio/ModeloTiemposParada/`, patrón ModeloPlazos): objetivo = (fin − fin anterior) − conducción en línea recta con colchón (fuera
  la primera del día y los intervalos > 120 min); segundo registro fin − llegada. Rasgos: tipo de parada, servicio técnico, bultos, cobro y forma, CP3,
  hora, día, repartidor, minutos tecleados. < 200 ejemplos → «sin datos suficientes». Job `reparto-tiempos-parada-modelo` 4:25 Madrid (cola
  «entrenamiento», nunca en horario): valida las últimas 4 semanas contra la REGLA y apunta SIEMPRE en `dbo.RepartoTiemposModeloEntrenamientos`;
  guarda `tiempos_parada.zip` en ModeloLlamadas:Carpeta si no empeora. Interruptor «(defecto)» `Reparto.TiempoParadaQuienManda` = «Regla» | «Modelo»
  (con «Modelo» el fin de ruta usa el modelo; `MinutosDe` en la respuesta). `GET api/RepartoPropio/TiemposParada/Comparacion` (Dirección/Informática).
- **Aviso de paradas para recalcular el fin de ruta (19/11/26)**: `repartoCambiado` Motivo «Parada» (grupo «Reparto», Dispositivo «NestoAPI»: sin
  excluir PDA) cuando las paradas cambian por algo que NO hace el repartidor. LA REGLA (pura) `CambiosParadasReparto.AfectaAlReparto` (parada = 14 en
  Tramitado/Incidentado): nace (alta tramitada o paso a Tramitado), deja de serlo (borrado, pendiente/en curso, cerrado, otra agencia) o cambia
  día (fecha), reembolso o bultos; NO entregar ni incidencia (el repartidor ya avisa). Se mira en `NVEntities.SaveChanges` (todo lo de EF: tramitar,
  TramitarTodos, ConfirmarTramitacion, DesdePedido/packing, Facturar rutas, notas de cobro, aplazar, anular, reembolso desfasado…) y en el PUT de
  EnviosAgencias (adjunta la entidad: compara con lo registrado); también el PUT del servicio técnico de una nota. Punto único
  `AvisoParadasReparto.Avisar` → `AgrupadorAvisosParadas`: UN aviso tras 3 s sin más (máx. 20 s), en segundo plano, nunca lanza. Las paradas de
  reposición solo las crea/cierra el repartidor (Carga/Entrega): sin aviso nuevo. Lo que Nesto escriba en EnviosAgencia por su cuenta no avisa.
- **Informe por repartidor (corte 4, 20/11/26)**: `GET api/RepartoPropio/Informe?desde=&hasta=&repartidor=&soloMio=` (`InformeRepartoController`; Almacén,
  Dirección e Informática; sin fechas, los 7 últimos días; ≤ 366 días; sin `RepartoParadasTiempos`, 503). Solo lectura en CUATRO consultas (`RepositorioInformeReparto`:
  paradas del corte 3, movimientos de bultos agregados por usuario y día, «Terminar jornada» y la PRIMERA estimación del día; las tres últimas opcionales → `Aviso`).
  LA REGLA pura `CalculoInformeReparto` (`InformeReparto.cs`): paradas hechas = entregadas + recogidas; incidencias aparte; horas = primera carga (o primer
  movimiento) → último movimiento (bultos, paradas, jornada), sin descontar la comida; horas en ruta = primera → última parada cerrada; dinero = efectivo y
  tarjeta (enlace/tarjeta) de los reembolsos + otros cobros (notas de cobro); minutos de parada reales = fin − llegada (0..120) frente a `MinutosPrevistos`.
  Totales y medias por día de la semana (por repartidor y de todos) y desglose por CP y día. Km null (corte 5). Sin PDF/Excel, ni cobros de deuda (POST Cobros: no están en estas tablas).

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

### Campaña en bloque (Black Friday 2026, 20/11/26)
- `POST api/Campanas/Bloque?ensayo=true&incluirLineas=false` (`[Authorize]`, como crear campañas; ensayo por defecto: no escribe nada) y
  `POST api/Campanas/Bloque/Excel` (siempre ensayo; hojas «Productos» con TODAS las líneas y «Resumen»). Cuerpo `PeticionCampanaBloqueDTO`
  {Nombre ≤50, FechaDesde, FechaHasta (las dos obligatorias), Empresa (solo 1), Audiencia (0/1/2, por defecto de reglas y excepciones),
  Reglas [{Familia, Descuento, Audiencia?, ProductosExcluidos, TextosExcluidos, GruposExcluidos, SubgruposExcluidos («OUT» o «COS/OUT»),
  CondicionContado?}], Excepciones [{Producto, Descuento, Audiencia?, CondicionContado?}], ProductosExcluidos, DesbloquearAplicarDto,
  CondicionContado, TextoAvisoContado}. Descuentos en TANTO POR UNO (0 < d < 1). Crea filas POR PRODUCTO de DescuentosProducto con Campana.
- LA REGLA (pura) `ReglaCampanaBloque.Calcular` (`Infraestructure/Campanas/CampanaBloque.cs`): ya estaba (fila de ESTA campaña) · NoExiste ·
  EstadoNoVivo (null o < 0) · MateriaPrima (MTP) · Muestra (MMP) · ExcluidoPorReferencia · (solo por regla, no excepción) ExcluidoPorGrupo /
  Subgrupo / Texto (sin mayúsculas ni tildes, palabra entera) · SinPvp · FichaBloqueada («Aplicar Dto» = 0 sin desbloqueo) · solape. La
  excepción manda sobre su regla. Solape = otra fila de tarifa de producto (sin cliente/proveedor, desde 1 ud.) que se solapa en fechas:
  mejor o igual (precio efectivo) → fuera `YaTieneDescuentoMejorOIgual`; peor → se crea (Carlos 20/11/26, «queda el mejor de los dos»),
  `Solape` = true; nunca se borra nada. OJO: el motor no desempata dos filas de producto de la misma CantidadMínima (FirstOrDefault de SQL).
- De verdad: UNA transacción (`RepositorioCampanaBloqueSql`, applock `CampanaBloque:{empresa}:{campaña}`): SqlBulkCopy a una temporal + un
  INSERT … NOT EXISTS por tabla (DescuentosProducto, `CampanasAplicarDto` con las fichas a desbloquear, `CampanasCondicionContado`) y UNA
  fila en Modificaciones (Tabla `CampanaBloque`). Idempotente. Tienda: si aún no ha empezado no encola nada (lo hace `vigencia-campanas` el
  día que empieza); si ya corre, una vez los productos creados con audiencia > 0. Sin las tablas que hagan falta → 503.
- Baja por nombre (`DELETE api/Campanas/PorNombre/{n}` y `…/Cerrar`): por Nº Orden en una sentencia (ya no EF fila a fila), en la misma
  transacción el desbloqueo (lo no activado se borra; lo activado se cierra ayer y el job devuelve la ficha) y el contado; rastro
  `CampanaBorrada`/`CampanaCerrada`; solo encola lo que puede estar en la tienda (`ReglaCampanaBloque.PuedeEstarEnLaTienda`: ya empezada
  y acabada hace ≤ 2 días). Script `Scripts/BlackFriday2026_CampanasBloque.sql` (GRANT INSERT/DELETE/UPDATE en las dos tablas).
- **Distribuidores: la campaña ENCIMA del suyo (Carlos 20/11/26)**: marca por DATO `dbo.CampanasAcumulanDistribuidores` (Campana, FechaDesde,
  FechaHasta; `Scripts/BlackFriday2026_CampanasAcumulanDistribuidores.sql`; caché 5 min; sin ella, nada cambia). La rellena el alta en bloque con
  `AcumulaParaDistribuidores` (misma transacción) y la quitan la baja (borra) y el cierre (FechaHasta). LA REGLA `DescuentoDistribuidorCampana`
  (pura): `1 − (1 − propio) × (1 − campaña)` a 4 decimales (30+20 → 44 %; 50+25 → 62,5 %); propio = lo que el motor da SIN la fila de tarifa del
  producto (si el cliente tiene descuento propio del producto, ese); con PRECIO ESPECIAL propio no se compone; nunca peor que hoy (máximo). Solo
  la fila de campaña de PRODUCTO. Distribuidor = LA REGLA `ClientesDistribuidores` (algún contacto con estado 6 o 67; la usan también los cheques
  regalo; caché 10 min por cliente). Motor: `GestorPrecios.ConCampanaDeDistribuidor` tras el descuento propio del producto (primero la marca, sin
  ella ni consulta); validación: `ValidadorDescuentosPermitidos.DescuentoCompuestoDistribuidor` autoriza hasta el compuesto (propio = la mayor
  de las demás filas autorizadas). La tienda no cambia (no viaja nada de esto).
- **Una fila de campaña autoriza, pero no es un techo (Carlos 25/11/26, caso Israel, cliente 206, Lisap 43165)**: en `ValidadorDescuentosPermitidos`
  LA REGLA (pura) `ReglaDescuentosQuePrevalecen.Filtrar`: la fila específica del producto prevalece sobre familia/grupo/cliente (es la forma de
  marcar un techo) SOLO si alguna aplicable por cantidad es PERMANENTE (sin `Campana`); entonces cuentan las del producto (permanentes y de campaña:
  la campaña sube el techo esos días). Si las únicas del producto son de campaña, cuentan TODAS: la campaña suma permiso y no tapa el 52 % pactado
  por familia o grupo. Vale también para el precio autorizado; el compuesto de distribuidores no cambia. El motor de precios no se toca.
  (`ValidadorOfertasYDescuentosPermitidos` tiene la lógica vieja, pero no se usa en ningún sitio.)
- **Solo autorizan los niveles del motor (Carlos 25/11/26)**: `ServicioPrecios.BuscarDescuentosPermitidos` sigue leyendo con la consulta AMPLIA
  (familia O grupo) y después LA REGLA (pura) `ReglaFilaAutorizaProducto.Casa/Filtrar` deja solo: fila de producto; global de familia (sin filtro),
  familia+grupo (grupo de la ficha) o familia+grupo+subgrupo (#467, el Outlet: contra la categoría principal o una secundaria); del cliente: grupo
  (sin familia), familia, familia+grupo, familia+filtro. Una GLOBAL de solo grupo NO autoriza («tiene que ser una fila con ese cliente, con ese grupo
  y el producto a NULL»), ni una global de familia con filtro. Antes, Schwarzkop|PEL|OUT 50 % autorizaba el 50 % en todo PEL (695 líneas 15/09-24/11).
  Interruptor ParametrosUsuario «(defecto)» `Validacion.DescuentosCriteriosEstrictos` (sin fila o «1» = estricto; «0» = la consulta amplia de antes;
  sin crear filas, caché 1 min: `InterruptorDescuentosCriteriosEstrictos`). Afecta a todos los que usan esa lectura (validador de descuentos, precio
  especial admitido de ValidadorOfertasPermitidas y de PlantillaVentas, que solo miran filas de producto).
- **Solo en stock (NestoAPI#889, Manuel 23/11/26; aviso NO estricto, como el del contado)**: `CondicionStock` en la petición (y `bool?` en regla y
  excepción) → una fila por producto en `dbo.CampanasCondicionStock` (Campana, Empresa, Producto, FechaDesde, FechaHasta; `Scripts/BlackFriday2026_CampanasCondicionStock.sql`;
  sin ella, ni aviso y el alta con la marca da 503), en la misma transacción; relanzar el alta con la marca sobre una campaña ya creada solo
  añade estas filas; la baja y el cierre por nombre también las quitan/cierran. Caché `CampanasCondicionStock` (10 min, ELMAH una vez). LA REGLA
  (pura) `AvisoCampanaSoloEnStock.Avisar` (`CondicionesCampana/`): líneas de producto -1..1 con DescuentoProducto o DescuentoLinea > 0 de un producto
  con fila vigente hoy, sumadas por producto; disponible (Carlos 24/11/26) = ExtractoProducto ALG+REI+ALC de las empresas 1 y 3 + lo que va de
  camino entre sedes (PreExtrProducto con traspaso) − LinPedidoVta -1..1 de OTROS pedidos (1 y 3) en esos almacenes, en UNA consulta
  (`LectorDisponibleCampanaStockSql`, solo si alguna línea es de la campaña). No llega → aviso `CampanaSoloEnStock` («El descuento de Black Friday
  del 45929 (NOMBRE) es solo para lo que haya en stock: hoy hay 2 de 5.») en `AvisosGrabacion` (POST), `Avisos` (PUT) y el correo. Nunca
  bloquea, quita el descuento ni rompe el guardado. Ni presupuestos ni la tienda online.
  **Pregunta antes de guardar (Carlos 24/11/26, como el contado)**: `POST api/PedidosVenta/AvisoCampanaSoloEnStock` (`PedidosVentaCondicionesCampanaController`,
  cuerpo = el `PedidoVentaDTO` que se va a guardar; solo lectura, siempre 200, nunca lanza) → {HayAviso, Campana, Mensaje (de usted, sin la pregunta final),
  Productos [{Producto, Nombre, Pedidas, Disponibles, ConDescuento, SinDescuento, DescuentoCampana, DescuentoSinCampana, PrecioSinCampana, Texto}]}.
  Al guardar, `PedidoVentaDTO.CampanaSoloEnStock` (solo de entrada, POST y PUT): null = como antes (aviso después); «QuitarDescuento» = ANTES de validar,
  LA REGLA pura `ReglaCampanaSoloEnStock.Ajustar` (`CampanaSoloEnStockPregunta.cs`): por producto, las unidades con descuento se limitan a lo disponible
  (por orden de línea; las intocables primero) y el resto va «sin campaña» en una línea NUEVA (`LineaPedidoVentaDTO.CopiaParaPartir`) o en la propia si
  no queda ninguna con descuento (Precio, DescuentoProducto, AplicarDescuento del motor y DescuentoLinea a 0); aviso `CampanaSoloEnStockAplicado` y no el de
  después; en el PUT solo líneas pendientes (-1) sin picking. «MantenerDescuento» = tal cual, sin aviso. «Sin campaña» = el motor con
  `PrecioDescuentoProducto.campanaExcluida` (las filas de tarifa del producto de esa campaña no cuentan; sin el dato, la consulta de siempre); un
  distribuidor sale con su descuento propio, sin componer. Una ficha que la campaña desbloquea (`CampanasAplicarDto` sin restaurar, vigente o activada, sin «Aplicar Dto» = 1 de antes;
  `ReglaCampanaSoloEnStock.FichaDesbloqueadaPorLaCampana`, una lectura por tanda) va «sin campaña» sin descuento (`aplicarDescuento` false). Una línea que ya va a precio «sin campaña» o peor no cuenta (lo partido no se vuelve a preguntar).

### «Ofertas para ti»: las ofertas de una campaña para cada cliente de la app y su push (25/11/26)
- Vale para CUALQUIER campaña (filas de tarifa por producto con `Campana`), no solo el Black Friday. Todo en `Infraestructure/Campanas/OfertasParaTi/`.
  LA REGLA (pura) `ReglaOfertasParaTi`: candidata = fila vigente hoy (`Vigencia`), audiencia 1 o 2 (lo que la tienda publica al profesional; el 0 no se
  anuncia), Estado ≥ 0, ni MMP ni MTP, con descuento (precio fijo → 1 − Precio/PVP); un producto en dos campañas, la de más. Por cliente: fuera lo
  pendiente (-1..1) y lo comprado desde el inicio de su campaña; orden 1) habituales que le tocan (≥ 2 días en 12 meses y ≥ 60 % del ciclo, mediana),
  2) habituales, 3) de sus 5 familias de más importe en 24 meses (descuento, popularidad), 4) lo demás (popularidad, descuento); como mucho 2 por
  familia entre los N si hay alternativas. `Motivo` LoSuelesPedir/TeToca/DeTusMarcas/TePuedeInteresar y `Texto` de tú. Stock = el lector de «solo en
  stock» (#889, pedido 0) por tramos y solo lo que hace falta.
- Modelo 1 (#671): SÍ puntúa una lista de productos para un cliente sin carrito ni vendedor, por `IPuntuadorReposicion` (`PuntuadorReposicionModelo`, el de
  la puerta del recordatorio de reposición, carrito vacío y `adicionales`). Ordena las 25 primeras de la regla con stock; sin modelo o si falla (ELMAH una
  vez por tanda), la regla. Núcleo común `ServicioOfertasParaTi` (Preparar = filas + popularidad; Calcular = compras, pendientes y familias por tandas de 200 clientes).
- `GET api/Pedidos/Cliente/Ofertas?campana=&maximo=5` (`OfertasClienteController`; cliente del claim, sin él 401; siempre 200): `Precio` por el camino de la
  app (`ProductosController.GetProductoPrecioReal`, 1 ud.) y `PrecioAntes` con `campanaExcluida` (`CalculadorPrecioSinCampanaMotor`, ficha desbloqueada
  sin descuento); si no mejora, la oferta no sale. Cargo 30 sin importes, cargo 31 con la tarifa en `Precio` (`ReglaPreciosOfertaParaTi`). Contexto de la
  campaña en caché 10 min (`CacheContextoOfertasParaTi`).
- `POST api/Campanas/Push?ensayo=true` (`CampanasPushController`, Dirección/Informática, 403 de usted; ensayo por defecto: solo lee): destinatarios = los
  del recordatorio de reposición (NestoTiendas activo con consejos, sin baja «Avisos de reposición»), SoloClientes/ExcluirClientes; sin repetir la campaña
  en `DiasSinRepetir` días mirando el BUZÓN (Datos con `"campana":"…"`, sin tabla nueva). Datos: SIEMPRE `tipo` «ofertas», `campana` e `imagenUrl` (si
  hay foto); NUNCA `productos` (Carlos 25/11/26: los push de campaña no tocan el carrito; el modo «Carrito» se quitó y un `Modo` en la petición se
  ignora). Cuerpo automático o plantilla {producto} {descuento} {n}
  {campana}; de verdad solo de 9 a 21, por `EnviarConsejoACliente` y una fila en Modificaciones (Tabla `CampanaPush`).

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
  **Por el nombre del remitente (NestoAPI#905, caso Postquam/SEUR PV26-57984, 25/11/26)**: si ningún criterio da proveedor, LA REGLA (pura)
  `ReglaNombreRemitentePaquete` (`RemitentePaquete.cs`) compara el remitente con el nombre de los proveedores con pedidos en el aire: palabra
  DISTINTIVA entera (sin forma societaria, tildes ni mayúsculas; ≥ 5 letras; nunca las genéricas, `Genericas`: COSMETIC, DISTRIBUCIONES…) y solo
  si casa UN proveedor (el falso amigo no da ninguno). Motivo `NombreRemitentePaquete`, el último (tras número, memoria, correo, dominio y nombre;
  también sirve para dar por bueno un número suelto). Un pedido suyo → inequívoco; varios → por asignar. Aplicado el envío, el remitente pasa a
  la memoria (`ReglaRecordarRemitentePaquete`, usuario «(automático)»; nunca rompe). Sin script.
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
- **Fecha línea a línea por los productos del aviso (#857h, Carlos 20/11/26; caso Weelko WK/OUT/78316 → 220931)**: `IFormatoAvisoSalida.Lineas` (vacía en la
  base; Weelko lee su «Resumen del pedido»: «[REF] descripción … 2,00», el transporte sin corchetes no cuenta). LA REGLA (pura) `ReglaLineasAvisoSalida.Casar`
  (`LineasAvisoSalida.cs`): referencia → producto por ProveedoresProducto del proveedor de cada pedido; por producto, las unidades se reparten por nº de orden
  (como mucho lo pendiente); menos que lo pendiente = PARCIAL: la fecha se pone igual (no se parte la línea) y lo dice el rastro. Un pedido casa → inequívoco
  (motivo `LineasAviso`: solo nuestro número y el PDF mandan sobre él; va por delante del dominio, el nombre y las confirmaciones); varios → gana el ÚNICO que casa el aviso entero (todas sus líneas y unidades) y, si no lo hay o hay dos, por asignar con
  los dos; ninguno → como antes. Lectura: `LeerLineasAviso` (una consulta por tanda, solo si el aviso trae líneas; ni el histórico). Al aplicar (job, a mano,
  mismo envío —los correos de la agencia heredan las líneas, `LineasAvisoCorreo.DelEnvio`—, Corregir) se vuelve a casar con ESE pedido
  (`LeerLineasAvisoDelPedido`) y `PeticionAplicacionFecha.Ordenes` limita el UPDATE, la promesa en riesgo y el rastro (`LineasAviso`, `EnvioParcial`); sin
  casar ninguna, todo el pedido. Columnas `LineasAviso`/`LineasAplicadas` (`Scripts/Issue857h_ComprasCorreosLineasAplicadas.sql`; sin ellas, la fecha a todo
  el pedido). Certeza (#853/#869): un correo con `LineasAplicadas` solo da seguimiento a las líneas de esos PRODUCTOS (`ReglaSeguimientoAgencia.Vigente(…, producto)`).
  GET …/Correos y …/PorAsignar: `LineasAviso`, `LineasCasadas`, `LineasAplicadas`. Reenvío: «Fecha de recepción del 26/11 al 24/11 en 2 líneas del pedido…: 32831 (2 uds.) y 40695 (1 ud.).»
- **Control de pendientes (NestoAPI#897, regla de Carlos 25/11/26; caso Maystar 220093/220983/220993, DHL 4313144450)**: en un proveedor con
  `Proveedores.ControlPendientes` (en `PedidoCompraEnAire`, con `FechaPrimerEnvio` de `PedidosCompraEnvios` si está la tabla) y varios candidatos de
  ÉL, sin nuestro número, PDF, líneas del aviso ni confirmación, LA REGLA (pura) `ReglaControlPendientes` (`ControlPendientesCorreo.cs`): «de pendientes»
  = alguna línea ya recibida o enviado (primer envío o [Fecha Modificación]) más de N días antes del correo (ParametrosUsuario «(defecto)»
  `Compras.DiasPedidoDePendientes`, 30 sin fila; se lee solo si hay algo que decidir y nunca crea fila): nunca recibe la fecha por esta regla. De los
  demás enviados ANTES del correo: uno → ese; varios → el MÁS ANTIGUO (el otro puede venir agrupado: no se toca); ninguno → por asignar. Un solo
  candidato con motivo `ControlPendientes` y `Explicacion` en el JSON de candidatos («MAYSTAR, S.L. tiene control de pendientes: se asigna al pedido
  en curso más antiguo, 220983; el 220093 es de pendientes y el 220993 puede venir en el mismo envío.»), que va al reenvío y al rastro (`PorQuePedido`).
  Job y Reanalizar (el histórico no). `GET …/PorAsignar`: cada candidato con `DePendientes` y `MotivoDePendientes`. Bultos y kilos: los lee SOLO la
  clase de la agencia (`IBultosKilosAgencia`, hoy DHL: «Bultos.....: 8», «Kilos.......: 123»), al vuelo del `Extracto`: `Bultos`/`Kilos` en GET
  …/Correos y …/PorAsignar y «Bultos: 8 bultos, 123 kg» en el reenvío; nunca deciden. Sin script.

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
- **Enviar una reposición (incidencia 1455, Paloma, 25/11/26)**: núcleo común `Inventarios/ErrorInventarioPendiente` (reconocer, acotar, listar, traducir;
  el picking delega en él sin cambiar nada). Terminar y la puntual (`ServicioPreparacionReposicion.ExplicandoInventarioPendiente`) cambian el texto por LA REGLA
  `ErrorInventarioPendienteReposicion` («No se puede enviar la reposición: en Alcobendas hay inventario sin contabilizar (24870 TANGA…, del 24/11, de Paloma).
  Contabilícelo o borre esa fila en Inventario y vuelva a enviarla; lo leído sigue guardado.»): las pendientes del ORIGEN (cualquier producto, las suyas
  primero), 10 como mucho; mismo código de estado (otra `NestoBusinessException` con el del original). Sin filas o sin lectura, el original. Sin traducir
  todavía: recibir una reposición, terminar una salida REPO/PICK de Ariadna, traspasos de almacenes ficticios, «No está», diferencias de reposición.

### Deshacer el rellenado del inventario de una tienda (12/11/26)
- `DELETE api/Inventarios/Rellenar?almacen=REI&fecha=2026-11-12[&empresa=1]` (`ServicioDeshacerRellenarInventario`, sin UI: a mano con el token): borra
  TODAS las filas pendientes (Estado 1, NºTraspaso NULL) de ese almacén y día, de cualquier usuario y aunque estén contadas. Solo Dirección e
  Informática (403); fecha obligatoria y almacén sin ubicaciones (400). Applock `InventarioRellenar:{empresa}:{almacen}` y transacción; nunca lo
  contabilizado ni ExtractoProducto. 200 `{ Almacen, Fecha, FilasBorradas, FilasContadas, UnidadesContadas }` (ceros si no hay nada, y sin rastro);
  rastro = UNA fila en Modificaciones (Tabla «InventarioRellenadoDeshecho», resumen en JSON con el desglose por usuario).
- **Rellenar por error (Paloma, 25/11/26: 3.032 filas en Alcobendas)**: (1) `GET api/Inventarios/Rellenar/Previa?almacen=&fecha=&empresa=` (permisos de
  rellenar; solo lectura) → {Almacen, NombreAlmacen, Fecha, FilasQueSeCrearian (MISMA consulta `SqlRellenadoInventario.SQL_STOCKS` y `FilasARellenar`;
  0 si ya hay pendientes), FilasPendientesYa, Mensaje «Va a crear 3.032 filas de inventario en Alcobendas con fecha 25/11/2026.», PuedeDeshacer,
  MotivoNoPuedeDeshacer}. (2) `POST api/Inventarios/Rellenar` exige `Confirmar` (cuerpo, o `?confirmar=`) con el código o el nombre del almacén
  (`ReglaConfirmacionRellenado`: sin mayúsculas ni tildes; vale una palabra del nombre de 4+ letras) SOLO si la petición NO trae `X-Ariadna-Version`
  (Ariadna llama al mismo endpoint y no cambia) y solo cuando se iban a crear filas (YaRellenado/YaContabilizado salen igual); si no, 400
  {Codigo «CONFIRMACION_NECESARIA», Message «Para rellenar el inventario de Alcobendas escriba el nombre del almacén.»} y nada creado. (3) El DELETE
  también lo puede quien puede rellenar si TODAS las pendientes son suyas y ninguna contada (`ReglaDeshacerRellenadoPropio`, comprobado con UPDLOCK/HOLDLOCK
  en la transacción): si no, 403 «hay filas de otro usuario (…)» o 409 «ya hay N filas contadas: pídaselo a Dirección.». Dirección e Informática, como antes.
- **Borrar desde la lista «Completo» (25/11/26)**: `GET api/Inventarios?empresa&almacen&fecha` trae en cada fila `SePuedeBorrar` (`Inventario.Partial.cs`,
  NotMapped, setter interno: un POST/PUT no lo cambia) con LA REGLA del DELETE de filas (`ReglaBorrarFilaInventario.SePuedeBorrarFila`: puede contar,
  `PuedeBorrarEsta`, pendiente, no posterior a hoy y, si es de hoy, que sobre; las comprobaciones de hoy se leen una vez y solo si hace falta). Con
  `incluirPendientesAnteriores=true` (por defecto false) añade al final las pendientes de días ANTERIORES del almacén con `PendienteDeDiaAnterior` y su Fecha.
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

### Altas y bajas de stock de las tiendas: correo mensual (NestoAPI#886, 23/11/26)
- Al enviar una reposición, lo que no está se da de baja («No está») y, si aparece, de alta (sugerencia 1419). Para controlarlo, cada tienda
  (REI, ALC) recibe una vez al mes lo dado de alta y de baja en su stock: ExtractoProducto de las empresas 1 y 3, su almacén, diarios
  `_RegInvent` (motivo «Inventario») y `_ErrRepo` («No está» al enviar la reposición N / Diferencia de la reposición N, por el Texto; si no casa,
  el texto). UNA consulta de solo lectura (NOLOCK) por almacén y periodo; LA REGLA (pura) `GestorAltasBajasTienda.Construir`: líneas por fecha y
  hora, usuario sin dominio, valor SIEMPRE a PVP (`Productos.PVP` empresa 1; nulo = 0; NUNCA coste: lo ven las tiendas), totales de altas, bajas
  y neto, y «Aparece y desaparece» (productos con alguna alta Y alguna baja, con su neto).
- `GET api/Informes/AltasBajasTienda?almacen=&desde=&hasta=` y `/Excel` (hojas «Movimientos», «Aparece y desaparece», «Resumen»): permisos y
  tienda de FacturacionTienda (`ValidarTiendaFacturacion`); sin fechas, el mes natural anterior; ≤ 366 días. `POST …/AltasBajasTienda/Correo?almacen=&desde=&hasta=`
  (Dirección, Informática, Compras; 403 de usted) y el job `tiendas-altas-bajas-mensual` (día 1 a las 8:15 de Madrid, mes anterior, REI y ALC;
  un fallo por tienda a ELMAH) van por el MISMO `EnviadorAltasBajasTienda`: a `Constantes.Correos.TIENDA_REINA`/`TIENDA_ALCOBENDAS` con CC a
  `Correos.COMPRAS`, HTML (300 líneas como mucho; el resto en el Excel adjunto). Sin movimientos, no se manda nada. Sin tabla ni script.

### Pérdida desconocida por almacén, acumulada desde el 1 de enero (NestoAPI#893 corte 1, 25/11/26)
- `GET api/Informes/PerdidaDesconocida?anio=&hasta=` y `/Excel` (hojas «Resumen», «Productos», «Diarios sin clasificar», «Notas»): SOLO Dirección e
  Informática (va a COSTE: `Productos.PrecioMedio` de hoy, como IndicadoresStock). Sin nada, este año hasta hoy; año pasado, hasta el 31/12; `hasta`
  ≤ hoy. UNA consulta agregada de solo lectura (NOLOCK, `GestorPerdidaDesconocida.SQL_PERDIDA_DESCONOCIDA`: ExtractoProducto 1+3, ALG/REI/ALC, por
  almacén, mes, producto y diario). Cuentan `_RegInvent` y `_ErrRepo` (los de #886). LA REGLA (pura) `GestorPerdidaDesconocida.Construir`: por
  producto, almacén y periodo se neta; negativo = falta, positivo = sobra; `Cuadrar` compensa la falta de un almacén con la sobra de otro del MISMO
  producto y periodo (mes, o el año en el acumulado; orden ALG, REI, ALC); lo que no cuadra = pérdida de verdad. «Aparece y desaparece» por producto.
- Los demás diarios A MANO (sin cliente, proveedor, pedido, albarán ni traspaso; fuera `_MontarKit` y `_Ficticio`) salen en `DiariosSinClasificar` y
  NO cuentan hasta que Dirección diga cuáles son pérdida y cuáles tienen causa. «En tránsito» (#906): bloque propio sin imputar a ningún almacén, hoy a
  cero (`DIARIOS_EN_TRANSITO` vacío: se rellena con el diario que escriba la resolución «pérdida en tránsito»). Sin correo ni job todavía; sin cuadre de
  referencias parecidas (44411/44412).

### Almacenes ficticios de vendedores (NestoAPI#894, 24/11/26)
- Sustituye a «Altas y bajas de almacén ficticio» de Nesto viejo: Almacenes.AlmacénFicticio = 1 (IF, AB, IM… de vendedores; PRV, AMZ, EXT, SDC,
  PAC…). Saldo = SUM(ExtractoProducto.Cantidad) empresas 1+3 por producto. Dueño = ParametrosUsuario empresa 1 `AlmacenFicticio` PROPIO (fila
  «(defecto)» vacía, `Scripts/Issue894_AlmacenesFicticios.sql`; nunca se crea al leer); «es suyo» = ese usuario o su `Vendedor` = el del que pregunta
  (claim «Vendedor» de NestoApp o su parámetro).
- LA REGLA (pura) `ReglasAlmacenesFicticios.Decidir` (`Infraestructure/AlmacenesFicticios/`): ver = Dirección, Informática, Almacén y Tiendas todos;
  cada vendedor el suyo; el jefe de equipo los de su equipo (`VendedoresEquipoString`); la tienda online, AMZ; un cliente de la tienda nada (403).
  Meter y sacar, SIEMPRE contra el almacén propio: de vendedor (tipo `Vendedor` = tiene dueño), Almacén (ALG) y Tiendas (su AlmacénPedidoVta REI/ALC,
  `GestorFacturacionTienda.NormalizarTienda`; la tienda va antes que Almacén); PRV y EXT solo Almacén; AMZ Almacén y `TiendaOnline` (ALG); los demás
  solo Almacén. Dirección e Informática consultan (no traspasan si no son de Almacén o Tiendas). Sin dueño todavía = `Otro`.
- `GET api/AlmacenesFicticios` → [{Codigo, Descripcion, Tipo, EsDeVendedor, Vendedores, EsSuyo, PuedeTraspasar, AlmacenContra, MotivoNoPuedeTraspasar,
  PermiteNegativo}]; `GET …/{almacen}/Existencias` → {Almacen, Descripcion, Productos[{Producto, Nombre, Familia, NombreFamilia, Unidades}],
  NumeroProductos, TotalUnidades} (saldo ≠ 0; sin costes ni precios; 403/404). `POST …/Traspasos?ensayo=` {AlmacenFicticio, Sentido Entregar|Recoger,
  Lineas[{Producto, Cantidad > 0, Pasillo?, Fila?, Columna?}], Comentario ≤500, Clave?} → {Ensayo, NumeroTraspaso, AlmacenFicticio, AlmacenPropio,
  Sentido, Lineas[{…, Hueco, HuecoPropuesto, PendienteDeUbicar}], Saldos, UnidadesPendientesDeUbicar, Mensaje}. 400 producto que no existe o ficticio
  (anulado solo al entregar: recoger lo anulado vale), stock del origen insuficiente (salvo `Almacenes.PermitirNegativo` del origen); 403; 404; 409 si
  el stock o el hueco cambian al guardar. Misma Clave → la misma respuesta (Modificaciones, Anterior = la clave).
- Algete (ControlUbicaciones + Productos.Ubicar): al ENTREGAR, el hueco de cada línea (el pedido o el que propone la API: FIFO con libre para la línea
  entera; lo libre = estado 0 sin pedido, albarán ni reserva, empresas 1+3) y se quita por la puerta (`SalidaDeHuecoAFicticio`, todo o nada); al
  RECOGER, «pendiente de ubicar» (`EntradaDeFicticio`, estado 2) para la PDA. Tiendas sin huecos.
- Escritura en UNA transacción: applock `TraspasoAlmacenFicticio:1`, stock releído con UPDLOCK, NºTraspaso de `NumeradorTraspasos`, por producto −N
  en el origen y +N en el destino (Texto = nombre del producto, delegación ALG y forma de venta VAR) en el diario de sistema `_Ficticio` por el NÚCLEO
  `MovimientoTraspasoStockSql` (PreExtrProducto + prdExtrProducto por `ServicioExtractoProducto`, apartando otros traspasos del diario; lo usa también
  el ajuste de #634), los huecos y UNA fila en Modificaciones (Tabla `TraspasoAlmacenFicticio`, Nuevo = el resultado + usuario y comentario). Después,
  campana de Nesto (`TraspasoAlmacenFicticio`) a los dueños que no son quien lo hace (nunca rompe). Gemelas NestoApp#292 (consulta) y Nesto#651
  (consulta y pantalla de meter/sacar).

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

### Parada de una nota de recogida: «No se ha recogido», comentario y «Anular recogida» (punto 5, Carlos 19/11/26)
- `POST api/RepartoPropio/Envios/{id}/NoRecogida` {ClaveIdempotencia, FechaHora?, Motivo (obligatorio, ≤500), Latitud?, Longitud?, FechaHoraLlegada?,
  FechaHoraIr?}: solo notas de recogida; la parada sale del reparto como Cerrado a mano (5), DetalleEstado «No recogido (reparto propio): motivo»,
  prueba Incidencia (Motivo Otro, la CHECK no cambia), sin bultos NR; 200 {EnvioId, Estado «NoRecogido»}; 409 `RECOGIDA_YA_RECOGIDA` con bultos NR en
  una furgoneta o abonada. `EntregaRepartoDTO.Comentario` (≤500, solo recogidas): historia, DetalleEstado «Recogido (reparto propio): …» y prueba
  (`PruebaEntregaDTO.Observaciones`). Con solicitud (#855): aviso a quien la pidió (`RecogidaSolicitadaNoRecogida`/`RecogidaSolicitadaComentario`).
- `POST api/RepartoPropio/Recogidas/{pedido}/Anular` {Motivo} (grupos del reparto): parada en reparto → Cerrado a mano «Recogida anulada: motivo», historia y
  Modificaciones `RecogidaAnulada` en UNA transacción (`RecogidasParadaRepartoSql.CerrarParada`, solo si sigue 1/3); la solicitud, Anulada. Las líneas de
  la nota no se tocan. Reglas puras `ReglasParadaRecogida` (`RecogidasParadaReparto.cs`).
- **«No se ha recogido» pasa a Almacén, que decide (Carlos 20/11/26)**: con `dbo.RepartoRecogidasNoRecogidas` (`Scripts/RepartoRecogidasNoRecogidas.sql`;
  sin él, NoRecogida cierra como arriba y los endpoints nuevos dan 503) la recogida NO se cierra: el envío queda Incidentado (3) con el mismo DetalleEstado y
  FechaEntrega = `FechaRepartoPropio.FECHA_PENDIENTE_DECIDIR` (31/12/2099: no sale en ningún día, ni en SinCargar; leer su papel da «pendiente de que
  Almacén decida»), fila Pendiente (escrita ANTES que el envío; la lista exige el envío Incidentado de la 14) y campana `RecogidaNoRecogida` a Almacén.
  Contrato de NoRecogida igual. `GET api/RepartoPropio/Recogidas/NoRecogidas` → [{Pedido, CodigoDocumento, EnvioId, Cliente, Contacto, Nombre, Direccion,
  Poblacion, Motivo, Repartidor, FechaHora}]. `POST …/Recogidas/{pedido}/Reprogramar` {Fecha?} → {Pedido, CodigoDocumento, EnvioId, FechaEntrega, Mensaje}:
  la MISMA nota y parada vuelven a Tramitado ese día o, sin él, LA REGLA `ReglasParadaRecogida.FechaReprogramada` (desde mañana, su día de ruta interna
  `DiaRutaInterna` o el siguiente sin `EsFestivoRepartoPropio`); sin repartidor (sus cargas vigentes reciben un Descargado RP Origen Almacen), historia,
  Modificaciones `RecogidaReprogramada` en UNA transacción y `repartoCambiado` «Parada»; fecha pasada 400; no pendiente 409. `Anular` también la cierra
  (fila Anulada). Grupos del reparto (Almacén, Administración, Dirección, Informática).

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

### Lista de asistencia: eventos online y lista sin nadie marcado (NestoAPI#908, Masterclass Rosácea 24/11/26)
- LA REGLA (pura) `ReglasListaAsistencia` (`Infraestructure/Eventos/ListaAsistenciaEvento.cs`). Evento ONLINE (`FormatosEvento.EsOnline`; el Mixto
  sí pasa lista): `POST api/Eventos/{id}/Asistencia` → 400 `LISTA_EVENTO_ONLINE` sin tocar nada; al día siguiente el job de siempre
  (`DarPorAsistidosSinLista`) da por asistidas a las pagadas (las «No podré ir», No asistió, como siempre) y su correo a la tienda dice «Evento
  online: no se pasa lista» (no «nadie ha pasado lista… pasen lista»). `EventoDTO.SePasaLista` y `TextoListaAsistencia` (solo lectura, «Evento
  online: todas las inscritas pagadas se dan por asistidas.»). Cualquier evento: guardar la lista con inscritas pagadas y NINGUNA marcada → 409
  `LISTA_SIN_ASISTENTES` (details `perderanElCupon`) salvo `AsistenciaEventoDTO.Confirmar` = true. «Muy pocas respecto a las inscritas»: sin umbral
  decidido, no se pregunta. Gemela en Nesto.

### Formación sin evento en la plantilla de venta (incidencia 1398, Pilar, Carlos 20/11/26)
- LA REGLA (pura) `ReglasFormacionPlantilla.EsFormacion` (`CursosPlantilla.cs`): grupo CUR que no es la referencia de un evento (EVENTO00123[NR]) ni la
  plantilla de los cursos (`ReglasProductoCurso.Plantilla`, 90077): 90324, 90047, MF…, IMPE…. La búsqueda de la plantilla (`BuscarProducto` y `Buscar`,
  vía `CatalogoCursosPlantilla.LineasCursos`) la saca (Estado ≥ 0) con `LineaPlantillaVenta.EsFormacion` = true y `EsCurso` = false (no se pide evento);
  los cursos de evento, como siempre (EsCurso).
- Al guardar (POST PedidosVenta): formación + productos (no presupuesto, sin cursos de evento) → `PreparacionCursos.Formacion` y
  `OrquestadorPedidoConFormacion`: los productos por el cauce de siempre y la formación, sola, por el MISMO cauce (`CopiaParaFormacion`: la cabecera
  sin prepagos, efectos ni sesión de sugerencias), que `SerieSegunLineas` pone en CV como hoy un pedido de solo formación. Respuesta: la del de
  productos con `PedidosAdicionales` [{Numero, Motivo «Formación»}]; si la formación falla, los productos se quedan y va en `AvisosCursos`. Solo
  formación: UN pedido (como antes). Sin inscripción, señal ni enlace: precio y descuento de quien la vende, con las validaciones de siempre.
  Formación en un presupuesto o con un curso de evento: el 400 de antes.

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

### Cupón de evento adelantado por excepción (NestoAPI#885, 23/11/26)
- `POST api/Cupones/{codigo}/Adelantar` {Motivo 1..500} → 200 `CuponDTO`: un cupón de Origen «Evento» que hoy está Emitido (vale desde el día
  del evento) pasa a valer desde HOY (`ValidoDesde` = hoy y Estado Activo; ni importe, ni caducidad, ni inscripción ni factura). 400 sin motivo,
  404, 409 `CUPON_NO_SE_PUEDE_ADELANTAR` si ya vale, está usado, anulado, caducado o no es de evento (`ReglaAdelantarCupon.MotivoNoAdelantable`).
  UN lote con transacción (`RepositorioCupones.SQL_ADELANTAR`, UPDATE condicionado): el cupón y la fila de Modificaciones (Tabla `CuponAdelantado`,
  JSON con código, empresa, cliente, contacto, importe, ValidoDesde anterior y nuevo, usuario y motivo). Después, campana de Nesto a Administración
  (`AvisadorCuponAdelantado`, tipo `CuponAdelantado`; nunca rompe). Sin cambios de esquema.
- LA REGLA de permisos (pura, `ReglaAdelantarCupon` en `Infraestructure/Cupones/AdelantarCupon.cs`): Dirección siempre (sin leer nada); si no,
  la fila PROPIA de ParametrosUsuario empresa 1 `Cupones.PuedeAdelantar` (sin «(defecto)», no se crea al leer): «1» = cualquier cliente,
  «Equipo» = solo si `Clientes.Vendedor` del cliente/contacto del cupón (sin contacto, el principal) es él o de su equipo (`VendedoresEquipoString`;
  su vendedor = claim «Vendedor» o parámetro «Vendedor»). Sin permiso, 403; cliente de la tienda, 403.
- `CuponDTO.SePuedeAdelantar` en GET por código y en los del cliente: solo se miran permisos si hay algún Emitido de evento (una comprobación por
  petición; con «Equipo», una lectura de vendedores por empresa). X-API-KEY o cliente: false.
- Espejo de PrestaShop (#614): si el cart_rule está Activo allí, el adelanto pone `EspejoPendienteDesde` y `HayQueSincronizar`/`SQL_PENDIENTES`
  vuelven a mandar entero (PUT con el date_from nuevo) un Activo marcado pendiente; se intenta en el acto y, si falla, el job de cada 15 min.

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

### «Dónde está lo que falta» (pedido 930942, 19/11/26)
- `GET api/PedidosVenta/{empresa}/{pedido}/DondeEstaLoQueFalta` (`DondeEstaLoQueFaltaController`, aparte de PedidosVentaController; `[Authorize]`, el
  cliente de la tienda con claim «cliente» → 403; 404 sin pedido). Por cada producto NO listo en Algete (la regla de la promesa en riesgo: todo lo
  que no es Picking ni Algete) su frase y datos (`DondeEstaLoQueFaltaDTO` → `Productos[].Texto/Situaciones[]`). Sale del motor #606
  (`CalcularPedido`, una sola verdad) y, para lo EnCamino, del seguimiento #772 por TANDA (`ServicioSeguimientoReposiciones.SeguimientosTanda`:
  cabeceras, bultos y movimientos en tres consultas para todos los traspasos; sin las cargas antiguas de las paradas). Si el seguimiento falla:
  ELMAH y la frase sin el detalle del repartidor. Todo en Algete: lista vacía y `Mensaje` con el picking. Textos puros en
  `ReglasDondeEstaLoQueFalta` («… (1 ud.): viene en la reposición 81463 de Alcobendas, recogida por Miguel el miércoles 18/11 a las 12:53; pendiente
  de recibir en Algete.»). Nesto: sección plegable en el detalle del pedido y «Dónde está» en «Pedidos con la fecha en riesgo».

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

### Pie del correo: cuando manda la fecha de entrega del pedido (pedido 931105, 19/11/26)
- Si la fecha que se da es posterior a la que daría la mercancía SOLO por la fecha de entrega de las líneas, el motor lo dice: el mismo cálculo con las
  fechas de las líneas a «hoy» (en memoria, se restauran) sale antes → `ResultadoFechaEntregaAgencia.NoAntesDeFechaEntrega` / `FechaEntregaAgenciaDTO.NoAntesDeFechaEntrega`
  y la frase única `TextosCertezaFechaEntrega.FechaEntregaDelPedido` («No sale antes del jueves 26/11/2026, que es la fecha de entrega del pedido.») en el
  Motivo, en el pie del correo y en el Mensaje de «Dónde está lo que falta». Ninguna fecha cambia. La promesa en riesgo no la usa.

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
- **La agencia ya lo tiene en camino (caso 220983, Carlos 25/11/26)**: una línea NO está «sin noticias» si su pedido tiene un envío de seguimiento
  VIVO (`CorreosDelPedidoCompra.Envios`, pura: aplicado a ese pedido, o sin aplicar ni descartar con él entre sus candidatos —en un proveedor con
  control de pendientes, solo si no es «de pendientes», #897—; último correo de los últimos `ReglaFechaVencida.DIAS_CORREO_VIVO` (7) días; sin
  llegar). Entonces, si un correo vivo trae entrega estimada futura, esa (origen `CorreoSeguimiento`); si no, origen `AgenciaEnCamino`: hoy si el job
  corre en laborable antes de la hora de corte del picking, si no el siguiente laborable, «DHL lo tiene en camino (expedición 4313144450, aviso del
  24/11): debería llegar hoy.»; NO cuenta para «hay que llamar» (ni esa vez ni en el rastro). Con un aviso de más de 7 días sin llegar, la estadística
  de siempre y el texto empieza por «DHL avisó el 14/11 (expedición …) y no ha llegado; ». Los pedidos en el aire solo se leen si hay un envío por
  asignar con varios candidatos. Sin script.
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

### Facturae para FACe por B2Brouter (NestoAPI#903, 24/11/26; gemela Nesto)
- Cinco o seis facturas al año a organismos públicos: la API genera el fichero Facturae 3.2.2 SIN firmar de una factura YA emitida y Carlos lo
  importa en b2brouter.net («importar factura emitida»), que lo firma y lo manda a FACe. Nada de certificados, firma ni envío. Solo LECTURA.
- `GET api/Facturas/Facturae?empresa=1&factura=NV26…` (`FacturaeController`; Administración, Dirección, Informática; cliente de la tienda 403) →
  `application/xml` UTF-8 «Facturae_{número}.xml»; 404 sin factura; 400 de usted con TODO lo que falta. Opcionales que GANAN sobre lo guardado:
  `oficinaContable`, `organoGestor`, `unidadTramitadora`, `organoProponente`, `suPedido`, `expediente`, `contrato`. `comprobar=true` → JSON
  `ComprobacionFacturaeDTO` (Listo, Faltas, Avisos, centros, referencias, base, impuestos, total, vencimientos con PaymentMeans e IBAN).
- Datos (`ServicioFacturae`, sin lecturas nuevas de facturación): la factura del PDF (`GestorFacturas.LeerFactura`: datos fiscales guardados, NIF,
  totales, vencimientos e IBAN de la empresa), sus líneas (`CargarCabFactura`: IVA y RE por línea), el NIF de la empresa (`CargarEmpresa`), las
  originales de una rectificativa (`ServicioFacturas.CargarFacturasRectificadas`, la de Verifactu, ahora internal) y los DIR3. Emisor = dirección
  «Empresa» del PDF; receptor = la «Fiscal»; los centros llevan la dirección fiscal del cliente. ReceiverTransactionReference de cada línea (Carlos
  25/11/26, «si tenemos PO lo ponemos; si no, ponemos el nuestro»): el parámetro `suPedido` > `CabFacturaVta.SuPedido` (#723) > el número de
  NUESTRO pedido de esa línea (`LinPedidoVta.Número`; una factura de varios pedidos lleva el suyo en cada línea) con el aviso «No consta el pedido
  del organismo: se pone nuestro número de pedido (931472) como referencia.» (ya no el de «falta la referencia»). `comprobar=true`: `SuPedido` = lo
  que sale (varios, separados por coma) y `OrigenSuPedido` «Parametro» | «Factura» | «NuestroPedido». Expediente (FileReference) y contrato solo
  por parámetro (TextMax20: más largo = falta; un P.O. de más de 20 es falta, no se cambia por el nuestro).
- LA REGLA (pura) `ConstructorModeloFacturae` + escritor `GeneradorFacturae` (`Infraestructure/Facturas/Facturae/`): cabecera por (IVA, RE) como el
  PDF y Verifactu (base = suma; cuota = ROUND(suma ImporteIVA, 2); RE = ROUND(suma RE × base, 2)); línea: TotalCost = Bruto, Discount = ImporteDto
  (con DiscountRate), el redondeo ROUND(Bruto,2) − Bruto como Charge (sube) o Discount (baja) «Redondeo a céntimos», GrossAmount = la base; cuotas de
  línea a 2 y la línea mayor de cada tipo se lleva el céntimo. Si el total no es el de la factura o los vencimientos no lo suman: falta. Número SIN serie
  (`NumeroSinSerie`) + InvoiceSeriesCode; BatchIdentifier = NIF + número + serie. Rectificativa: OR + Corrective (ReasonCode 16, CorrectionMethod 02
  «por diferencias», la «I» de Verifactu; varias originales: la primera y las demás en el texto adicional; sin vinculación: falta). Pago: TRN y CNF →
  04 con el PRIMER IBAN de la empresa (aviso si hay varios), RCB 02, EFC 01, TAR 19; otra (CHC…) → falta. Hijos sin espacio de nombres (unqualified).
- DIR3: `dbo.ClientesFacturaElectronica` (Empresa, Cliente, Contacto; OficinaContable, OrganoGestor, UnidadTramitadora, OrganoProponente NULL y
  sus nombres; fuera del EDMX, `Scripts/Issue903_ClientesFacturaElectronica.sql`). `GET/PUT api/Clientes/FacturaElectronica` (mismos grupos; PUT: los
  tres obligatorios, `ReglasDir3`: 9, letra + al menos 5 cifras; los tres vacíos = quitar; rastro en Modificaciones; sin tabla 503, el GET vacío).
  Lectura: la del contacto o, si el cliente tiene UNA sola fila, esa. Siguiente corte (si hace falta): subirlo por la API de B2Brouter.

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
- **Dirección 2 en todos los destinos (19/11/26, Carlos «adelante con todo»)**: etiquetas y envíos = `DireccionEnvioCliente` (paradas de notas
  de entrega y de cobro de la 14 —`ServicioParadasNotaEntrega.ConstruirEnvio`, `DestinoNotaEntrega.Direccion2`—, etiqueta de recogida sin parada);
  pantallas, correos y PDF = `DireccionCompletaCliente` (packing de Ariadna y en PDF —el SP no se toca: la API completa por empresa del pedido—,
  correo de pedido/presupuesto, dirección de ENTREGA de factura/albarán/pedido/nota/ticket vía `IServicioFacturas.LeerDireccion2Cliente`, inscritos,
  correo de clientes nuevos, facturación de tienda, aviso de cambio de contacto). Aditivos: `RecogidaSolicitadaDTO.Direccion2/DireccionCompleta`,
  `ComprobacionRecogidaImprevistaDTO.DireccionCompleta`, `AlquilerCabeceraDTO.Direccion2Cliente`, `ResumenPedidoVentaDTO.direccionCompleta` (+contacto).
  Listas: `DireccionCompletaCliente.RellenarPorEmpresa` (una lectura por empresa). Sin columnas o sin Dirección 2, lo de siempre.
- **Corte 2: al MODIFICAR (19/11/26)**: GET GetClienteCrear trae `TipoLugar` y sus campos descompuestos (`Direccion2Cliente.Descomponer`, la inversa
  de Componer; lo que no casa, «Otro» con el texto) + `Direccion2` (solo lectura). PUT: `Direccion2Cliente.ParaModificar` —con tipo, Componer (la
  regla del alta, se valida siempre); `QuitarDireccion2` = borrar; sin nada = NO TOCAR (NestoApp y Nesto viejos); se exige solo si la Dirección
  CAMBIA y `ExigirDireccion2Alta` = 1—. La nueva va a la sesión (`Direccion2EnSesion`) antes del SaveChanges (correo del disparador) y se escribe
  antes de publicar a Odoo (la modificación publica siempre). Nesto: «¿Qué es?» también al modificar (sin tipo elegido, ni aviso ni envío).
- **Fiscal (bloque 4a, Carlos 19/11/26)**: `DireccionCompletaCliente.ParaFiscal` = la completa si cabe ENTERA en 200 (CabFacturaVta.DireccionFiscal);
  si no, la Dirección sola. Solo en facturas que se emiten desde hoy (`ServicioFacturas` al guardar los datos fiscales); una factura emitida no
  cambia (la guardada se pinta tal cual; la antigua sin datos guardados, de la ficha SIN Dirección 2). También en pedido/albarán/proforma (fiscal
  de la ficha), PDF del 347 y mandato SEPA (DireccionDeudor). Verifactu no manda dirección (solo NIF y nombre). Rectificativas copian la de la original.
- **Tienda online y Aplázame (bloque 4b, 19/11/26)**: aditivos `DireccionCompleta` en GET api/Pedidos/Cliente/Direcciones
  (`DireccionEntregaClienteDTO`) y `direccionCompleta` en el ClienteDTO del login de la tienda (`direccion` no cambia en ninguno). Checkout de
  Aplázame (app #663 y pago a plazos #864): la calle (`street`) = `ConstructorCheckoutAplazame.Calle`, con la Dirección 2 si cabe ENTERA en 100
  (`LONGITUD_CALLE`, tope prudente: el código no tenía ninguno); si no, la Dirección sola.
- **Devueltos (seguridad, 19/11/26)**: GET Devueltos, Reenviar, Abonar y HaVuelto dan 403 a un token de cliente de la tienda
  (`ValidadorAccesoCliente.EsClienteTienda`). Empleados y vendedores, como antes.

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

### Qué modos de servicio se ofrecen: por plan de entregas (Carlos 19/11/26)
- En Algete ya no por colores: `SugeridorModoServicio` saca los hechos de cada grupo (unidades que salen AHORA, que traen las TIENDAS y sin cubrir) con
  la misma lectura (ColorStock con distintas cantidades, en memoria con el gestor precargado) y `ModosServicioPermitidos.CalcularPorPlanes` compara el
  plan de entregas de cada modo (`PlanEntregasModoServicio`: 1 una con todo; 2 lo de ahora y una por llegada; 3 espera a TODO lo de tiendas y sale con lo
  de ahora; 4 lo de ahora y una más con el resto). Una llegada de más de una unidad puede llegar partida («*»); las tiendas van en un solo suceso (sin
  desglose por tienda). Mismo plan → se ofrece uno (1 > 4 > 3 > 2), motivo «Con este pedido es lo mismo que «X»». El 3 necesita algo de tiendas, el 4
  algo ahora; #561 sigue (nada ahora ni en tiendas → 1), con un matiz (Carlos 19/11/26): si quedan VARIAS líneas sin cubrir (pueden llegar en
  fechas distintas) se ofrece también el 2 (`ModosServicioPermitidos.OfreceSegunVayaEntrandoSinNadaDisponible`); con una sola, solo el 1. Caso A (verde + entera en Reina): 3 = 1, no se ofrece. Caso B (2 uds., 1 en Reina): 3 sí.
  Una sola unidad sin cubrir: 2 = 4, solo el 4. El sugerido (regla de siempre, `Decidir`) se cambia a su equivalente ofrecido (`Ofrecido`). Tienda,
  otros almacenes, modo 6 y picking no cambian; al modificar, la comprobación tolerante de siempre (sin cambio de modo no se mira).

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

### Rectificativa de una rectificativa (NestoAPI#892, caso RV2600189, Carlos 24/11/26: se permite y se declara bien)
- Invertir una RV con destino en serie rectificativa (añadir a su propio pedido RV) = `ReglaRectificativaDeRectificativa.EsRectificarRectificativa`
  (`Infraestructure/Rectificativas/RectificativaDeRectificativa.cs`): se vinculan las líneas POSITIVAS a la línea de la RV de origen (`SeVincula`), al facturar
  en el acto (`GuardarVinculacionesRectificativa`) o en `RectificativaPendiente`; se envía como R por diferencias de esa RV (el mapeador no mira el signo).
  Con pedido nuevo sigue siendo deshacer el abono en NV (`EsDeshacerAbono`, 11/11/26). Lo de siempre (vincular las negativas) no cambia.
  Un abono no se deshace dos veces (Carlos 24/11/26): `MotivoNoSePuedeAbonar` + `ReglasAbonoCopiaFactura.MotivoYaCorregida` (todo o nada, como la de ventas)
  paran invertir una RV con alguna línea negativa ya vinculada o pendiente («La rectificativa RV2600186 ya está corregida por la RV2600189…»), vaya a RV o a NV;
  lo deshecho por el camino NV deja desde NestoAPI#907 (25/11/26) su rastro en Modificaciones (Tabla `AbonoDeshecho`, Anterior «empresa|RV», Nuevo = JSON
  con el pedido nuevo y las líneas negativas deshechas; `AbonosDeshechos.cs`, sin script) y `MotivoNoSePuedeAbonar` lo suma a las correcciones: solo
  las líneas que siguen en el pedido nuevo (borrado = ya no bloquea), con su factura o «el pedido N (sin facturar)». Si el rastro no se puede leer o
  escribir, ELMAH y como antes. Lo deshecho por NV antes del 25/11/26 no tiene rastro.
- Auto-curado del job (`ServicioFacturas.GenerarVinculacionesRectificativaDeRectificativa`, tras el LIFO de #38 si este no hace nada): RV/RC sin
  vinculaciones, sin ninguna línea negativa, todas las positivas con producto. LA REGLA (pura) `Casar`: todas casan una a una (producto, cantidad exactamente
  contraria) con UNA rectificativa anterior del mismo cliente (no una NV con negativa), sin lo ya corregido (vinculaciones o pendientes); gana la del mismo
  pedido, si no la más reciente; dos del mismo día sin la del mismo pedido → no cura. Si no cura, el correo del job dice «rectificativa en positivo» (no «devolución»).

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
- **Reenvío que sobra (novedad 1366, 19/11/26; «cerrado a mano = entregado»)**: la anulación de #699 (borra el reenvío sin tramitar, Estado < 1,
  y la decisión pasa a EntregadoPorLaAgencia) también cuando el original queda Entregado o Cerrado a mano por una persona: entrega del repartidor
  (`ServicioRepartoPropio.ResolverReenvioSobrante`), «Dar por cerrado», PUT de estado y «Borrar envío» del reenvío (si el original ya está terminado,
  anula en vez de dar 409). LA REGLA: `ReenvioEntregadoPorLaAgencia.EstaTerminado`/`PasaATerminado` (el seguimiento sigue con `PasaAEntregado`).
  Campana `ReenvioAnulado` a quien pidió el reenvío.
- **Devueltos: quién decide (Carlos 19/11/26)**: `POST {id}/Reenviar` y `{id}/Abonar` solo quien hizo el pedido, su vendedor (claim «Vendedor» o
  usuario del vendedor) y Almacén, Dirección e Informática (Administración NO): `PermisosDevueltos` (mismo núcleo que RetencionPorFalta/Decidir), 403
  de usted. «Ya está aquí» (`HaVuelto`, las dos rutas): Almacén, Dirección e Informática. El GET de Devueltos sigue abierto a empleados y vendedores.
  Los GET de EnviosAgencias con datos personales (Pendientes, PorPedido, Tramitados, Historia, Propuesta, UltimoEnvioCliente…) dan 403 a un token de
  cliente de la tienda (`ValidadorAccesoCliente.EsClienteTienda`); la app del cliente usa PedidosClienteController.
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
