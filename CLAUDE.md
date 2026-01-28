# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

NestoAPI is an ASP.NET Web API project built with .NET Framework 4.8 using Entity Framework 6.x for data access. The application provides a REST API for managing sales orders (pedidos de venta), customer management, invoicing, inventory, commissions, and various business operations for a distribution company.

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
                                 │
                                 ▼
┌─────────────────┐     ┌─────────────────┐
│ TiendasNueva    │────▶│  odoo-custom    │
│    Vision       │     │    -addons      │
│ (Tienda online) │     │   (Python)      │
└─────────────────┘     └─────────────────┘
```

## Solution Structure

- **NestoAPI**: Main Web API project
- **NestoAPI.Tests**: Unit tests using MSTest and FakeItEasy for mocking
- **LibreriaInformes**: Reports library project

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

### Copiar Factura / Rectificativas (Issue #85)
Sistema para copiar facturas existentes y crear rectificativas (abonos) de forma rápida.

**Componentes implementados**:
- **`GestorCopiaPedidos.cs`**: Lógica de negocio para copiar líneas de factura
- **`PedidosVentaController.CopiarFactura`**: Endpoint POST `/api/PedidosVenta/CopiarFactura`
- **`PedidosVentaController.GetClientePorFactura`**: Endpoint GET para buscar cliente por factura
- **Cliente Nesto**: `CopiarFacturaView.xaml` + `CopiarFacturaViewModel.vb` (diálogo modal)

**Flujo**:
1. Usuario pulsa "Copiar Factura" en DetallePedidoView (visible solo para grupos ALMACEN y TIENDAS)
2. Se abre diálogo con cliente y nº factura pre-rellenados (si hay línea seleccionada)
3. Opciones: invertir cantidades, añadir a pedido original, crear albarán/factura automáticamente
4. Si `CrearAlbaranYFactura=true`, se puebla `LinFacturaVtaRectificacion` para Verifactu (Issue #38)

**TRABAJO PENDIENTE - Tabla auxiliar temporal**:
Cuando el usuario crea un pedido sin marcar "Crear albarán y factura automáticamente" y posteriormente
crea la factura manualmente desde el pedido, la tabla `LinFacturaVtaRectificacion` NO se puebla porque
`ServicioFacturas.CrearFactura()` no tiene información sobre qué factura se está rectificando.

**Solución propuesta (pendiente de implementar)**:
1. Crear tabla auxiliar temporal `RectificativaPendiente`:
   ```sql
   CREATE TABLE RectificativaPendiente (
       Empresa VARCHAR(3),
       NumeroPedido INT,
       NumeroLinea INT,
       FacturaRectificada VARCHAR(20),
       FechaCreacion DATETIME,
       PRIMARY KEY (Empresa, NumeroPedido, NumeroLinea)
   )
   ```
2. `GestorCopiaPedidos` guarda metadata cuando `CrearAlbaranYFactura=false` e `InvertirCantidades=true`
3. `ServicioFacturas.CrearFactura()` consulta esta tabla al facturar y puebla `LinFacturaVtaRectificacion`
4. Limpiar registros de `RectificativaPendiente` tras facturar o si el pedido se elimina

Related files:
- `Infraestructure/Rectificativas/GestorCopiaPedidos.cs` - Lógica principal
- `Models/Rectificativas/CopiarFacturaRequestDTO.cs` - DTO de entrada
- `Models/Rectificativas/CopiarFacturaResponseDTO.cs` - DTO de respuesta
- `NestoAPI.Tests/Infrastructure/Rectificativas/GestorCopiaPedidosTests.cs` - Tests

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
