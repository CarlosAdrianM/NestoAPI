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

## Documentación por temas (`docs/claude/`)

El detalle de cada funcionalidad (reglas, endpoints, tablas fuera del EDMX, scripts, interruptores y decisiones con su fecha) NO está en
este fichero: está en `docs/claude/<tema>.md`. **Antes de tocar un área, lee su fichero** (o busca en la carpeta con Grep el número de
issue, el endpoint, la tabla o la clase): ahí están las decisiones de Carlos y los casos que las motivaron.

Al documentar algo nuevo: va al fichero de su tema (una sección `## Título (issue, fecha)`), no aquí. Aquí solo se añade su gancho a la
línea del índice. Si no encaja en ningún tema, se crea un fichero nuevo y su línea.

- **[Pedidos de venta](docs/claude/pedidos-venta.md)** (guardar, validar, autorizar, modos de servicio, picking y faltas, pago a plazos): Nota de entrega (cambios solo de cabecera) y borrar un pedido · Pedir autorización de un pedido que no pasa la validación · Recoge en tienda #571 · Versión del pedido y 409 al guardar con copia vieja #873 · Ampliar un pedido en preparación · Qué modos de servicio se ofrecen · Muestras o regalos sin stock en un pedido de TIENDA · Modo de servicio 6, «Junto con lo pendiente del cliente» · Faltas al terminar el picking · «Todo junto» con falta de pago #858 · Saldo a favor y pedidos de prepago en el picking · Pago a plazos de un pedido ya creado, con cualquier financiera #864 · Formación sin evento en la plantilla de venta
- **[Fechas de entrega del pedido](docs/claude/fechas-entrega.md)** (motor #606 de cara al cliente: promesa en riesgo, correo del pedido, dónde está lo que falta): «Dónde está lo que falta» · Color de las líneas del correo del pedido · Pie del correo · Promesa en riesgo #693
- **[Precios, ofertas y campañas](docs/claude/precios-campanas.md)** (vigencias de la tienda, N+M, campañas en bloque, descuentos autorizados, «Ofertas para ti» y su push): Pedidos de la tienda: ofertas vigentes el día del pedido · Oferta N+M que además admite el precio especial #589 · Campaña en bloque (distribuidores, filas que autorizan y criterios estrictos, solo en stock #889) · «Ofertas para ti» (ranking, push de campaña, registro en sombra)
- **[Clientes](docs/claude/clientes.md)** (alta y direcciones, origen, embudo de Odoo, sugerencias de llamadas, app de clientes (TNV)): Dirección 2 y número propio en el alta #874 · Origen del cliente #848 · Embudo de Odoo · Sugerencias de llamadas y ofertas vigentes #866 · App de clientes (TNV): lector y refresco del token
- **[Eventos, inscripciones y cupones](docs/claude/eventos-cupones.md)** (inscripciones, señal, asistencia, paso a gratuito, cupones de evento): Candidatos a invitar en sombra #850 · Teléfono de la ficha en los inscritos · Evento de pago que pasa a gratuito · Lista de asistencia #908 · La señal de un evento se cobra en Cajas · Datos de la transferencia de una inscripción #887 · Enlace de la reunión y dosier en PDF en un correo, dos días antes #895 · Cupón de evento adelantado por excepción #885 (quién lo adelantó y aviso si no asiste #910)
- **[Reparto propio (agencia 14)](docs/claude/reparto-propio.md)** (furgoneta y bultos, paradas, recogidas, notas de cobro, envíos internos, fin de ruta, día de reparto): Furgoneta bulto a bulto #772 · Manda el bulto · Envíos internos entre sedes · Fin previsto de ruta del reparto propio #881 · Día de reparto de nuestra ruta #764 · Nota de cobro #786 · Recogida solicitada por el vendedor #855 · Recogida imprevista del repartidor #859 · Parada de una nota de recogida
- **[Agencias y envíos](docs/claude/agencias-envios.md)** (seguimiento, retrasados, devueltos y reenvíos, prueba de entrega, picking de cada envío): Seguimiento automático, retrasados, reenvío que sobra y devueltos · Prueba de entrega de cualquier agencia · Reenvíos #870 · Qué picking lleva cada envío
- **[Almacén: códigos de barras, etiquetas y recorrido](docs/claude/almacen-etiquetas.md)** (código interno, muestras, impresoras de etiquetas, inmovilizados, calles del recorrido, avisos de ficha): Código de barras interno y recibir escaneando #762 (muestras #797/#884, impresora de etiquetas por almacén, lectura obligatoria en el picking) · Etiquetas de inmovilizado · Mantenimiento de calles del recorrido #642 («primero la escalera» por almacén #888) · Aviso de ficha desde Ariadna #868
- **[Inventario y stock](docs/claude/inventario-stock.md)** (inventarios, rellenado, stock de la web, altas y bajas, pérdida desconocida, almacenes ficticios): Inventario por huecos en almacenes con ubicaciones · Inventario pendiente de otro día, el picking y enviar una reposición · Rellenar y deshacer el rellenado del inventario de una tienda · Stock de la web al momento #818 · Altas y bajas de stock de las tiendas #886 · Pérdida desconocida por almacén, acumulada desde el 1 de enero #893 · Almacenes ficticios de vendedores #894
- **[Reposiciones de tienda y calendario](docs/claude/reposiciones-festivos.md)** (diferencias de reposición, excepciones del calendario, festivos y días sin reparto): Reposición del exceso de una tienda (sugerencia 1429; filtros por nombre, stock, sin ventas y «dejar N») · Diferencias de reposición «En discusión» y quién pierde #906 · Excepciones del calendario de reposiciones #867 · Festivos en tabla #779 · Años disparatados en el calendario
- **[Compras](docs/claude/compras.md)** (correos de seguimiento (#857), plazos y fechas de recepción, certeza, próxima entrada): Correos de seguimiento de compras #857 · Fechas de recepción y certeza #853 · Fechas de recepción vencidas #865 · Próxima entrada en la ficha del producto #869
- **[Aparatos: registro por producto y número de serie](docs/claude/aparatos.md)** (la ventana de Alquileres, su historia y sus descuadres): Historia del aparato y descuadres por número de serie #913 (corte 1, solo lectura; comprado sin registro y serie escrita de dos formas)
- **[Productos: la ficha](docs/claude/productos.md)** (lo que pinta la ficha de producto de Nesto): Referencia anterior y siguiente (GET api/Productos/Vecinos; la BD no tiene TRY_CONVERT)
- **[Facturación, cobros y contabilidad](docs/claude/facturacion-contabilidad.md)** (redondeo, documentos que no salen, Facturae, rectificativas, abonos, proyectos, informes de tienda): Rounding System #242 · Una factura por P.O. (ReglaPOAgrupacion) #898 · Documentos que no salen · Facturae para FACe por B2Brouter #903 · Rectificativa de una rectificativa #892 · Copiar o abonar solo unos albaranes o unas líneas #899/#863 · Abonar solo lo comprado #891 · Proyectos: imputación y cuenta de explotación #901 · Efectos del extracto (PUT api/ExtractosCliente) #644 · Facturado en tienda #719 · Alquileres · Fin de mes de Nesto viejo capado y P.O. entera o nada #898 (por pedido; también en la API, ReglaPOEntera)
- **[Plataforma: avisos, correos y ELMAH](docs/claude/plataforma-avisos.md)** (notificaciones urgentes, remitente de los correos, novedades, usuario y credenciales en ELMAH): Notificaciones urgentes #775 · Correos mandados a mano a nombre de quien los manda · Correos y WhatsApp a nombre de quien los manda #854 · Novedades por Id #495 · Novedades de una versión por la API, sin scripts de sa #890 (Scripts/PublicarNovedades.ps1) · Endpoints que no llevaban [Authorize] (PUT ParametrosUsuario y sus claves de permiso; crea la fila desde «(defecto)», incidencia 1481) · ELMAH: usuario (UserSyncHandler) y sin credenciales

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

Redondeo (`RoundingHelper`, AwayFromZero; `BaseImponible = ROUND(Bruto, 2) - ImporteDto`): ver `docs/claude/facturacion-contabilidad.md`.
Usuario de ELMAH con OWIN/JWT (`UserSyncHandler`; NO usar `TokenValidationParameters`) y ELMAH sin credenciales: ver `docs/claude/plataforma-avisos.md`.
