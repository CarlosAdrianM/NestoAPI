# Roadmap: Funcionalidad Facturar Rutas

## 🔄 Cambios Importantes (Actualización 28-Oct-2025)

### ✅ Cambios Aplicados

1. **Validación MantenerJunto**
   - Se valida ANTES de intentar crear la factura (evita llamar al procedimiento `prdCrearFacturaVta` que fallaría)
   - Si `MantenerJunto = 1` y hay líneas sin albarán (Estado < 2), NO se crea factura
   - En ese caso, si tiene comentario de impresión, se imprime el ALBARÁN en lugar de la factura

2. **Impresión Condicional de Albaranes**
   - ❌ ANTES: Los albaranes FDM se imprimían SIEMPRE
   - ✅ AHORA: Los albaranes solo se imprimen si el comentario contiene "factura física" O "albarán físico"
   - Búsqueda case-insensitive y sin tildes

3. **Manejo de Errores al Crear Factura**
   - Si falla la creación de factura (por cualquier motivo) y tiene comentario de impresión:
   - Se imprime el albarán como fallback
   - Se registra el error pero el proceso continúa

### ⚠️ Pendiente de Definir

**Traspaso a Empresa "3"**
- Se debe realizar DESPUÉS de crear albarán y ANTES de crear factura
- Criterios de cuándo traspasar: **PENDIENTE DE INVESTIGACIÓN**
- Lógica de cómo traspasar: **PENDIENTE DE INVESTIGACIÓN**
- Ver sección 1.7 para detalles completos

---

## 📋 Índice
1. [Cambios Importantes](#-cambios-importantes-actualización-28-oct-2025)
2. [Análisis Previo](#análisis-previo)
3. [Fase 1: Backend (API)](#fase-1-backend-api)
   - 1.1 [Modelos y DTOs](#-11-modelos-y-dtos-tdd---1-2-horas)
   - 1.2 [Repositorio/Servicio de Consulta](#-12-repositorioservicio-de-consulta-tdd---2-3-horas)
   - 1.3 [Gestor de Facturación](#️-13-gestor-de-facturación-de-rutas-tdd---4-6-horas)
   - 1.4 [Controller API](#-14-controller-api-tdd---1-2-horas)
   - 1.5 [Servicio de Impresión](#️-15-servicio-de-impresión-tdd---2-3-horas)
   - 1.6 [Constantes](#-16-agregar-a-constantes-5-10-min)
   - 1.7 [⚠️ Traspaso a Empresa "3" - PENDIENTE](#️-17-traspaso-a-empresa-3---pendiente-de-definir)
4. [Fase 2: Frontend (WPF)](#fase-2-frontend-wpf)
5. [Fase 3: Integración y Testing E2E](#fase-3-integración-y-testing-e2e)
6. [Fase 4: Mejoras y Refinamiento](#fase-4-mejoras-y-refinamiento)
7. [Consideraciones Técnicas](#consideraciones-técnicas)
8. [Checklist General](#checklist-general)

---

## Análisis Previo

### 🔍 Investigación Inicial (30-45 min)

**Tareas:**
- [ ] Buscar y analizar `PickingPopupView.xaml` y `PickingPopupViewModel.cs` como referencia
- [ ] Localizar `DetallePedidoView.xaml` y analizar estructura de botones actual
- [ ] Buscar referencias a `configuracion.UsuarioEnGrupo()` para entender el patrón
- [ ] Localizar `AgenciasViewModel` y analizar implementación de impresión con Pdfium
- [ ] Buscar ejemplos de apertura de pedido con doble clic (Comisiones, CanalesExternos)
- [ ] Identificar el servicio/gestor actual de albaranes y facturas en la API
- [ ] Verificar si existe `GestorAlbaranes` y `GestorFacturas` o similar
- [ ] Analizar estructura de `CabPedidoVta` y `LinPedidoVta` en el modelo
- [ ] **IMPORTANTE:** Investigar lógica de traspaso de pedidos a empresa "3" (cuándo y cómo)
- [ ] Localizar procedimiento almacenado `prdCrearFacturaVta` y analizar su lógica
- [ ] Verificar comportamiento del flag `MantenerJunto` en pedidos

**Archivos a revisar:**
```
Nesto/Views/PickingPopupView.xaml
Nesto/ViewModels/PickingPopupViewModel.cs
Nesto/Views/DetallePedidoView.xaml
Nesto/ViewModels/DetallePedidoViewModel.cs
Nesto/ViewModels/AgenciasViewModel.cs
Nesto/ViewModels/ComisionesViewModel.cs (para abrir pedido)
NestoAPI/Models/Constantes.cs (verificar GruposSeguridad, rutas, etc.)
NestoAPI/Controllers/AlbaranesController.cs (si existe)
NestoAPI/Controllers/FacturasController.cs (si existe)
```

**Constantes a verificar:**
```csharp
Constantes.GruposSeguridad.DIRECCION
Constantes.GruposSeguridad.ALMACEN
Constantes.EstadosLineaVenta.EN_CURSO (valor = 1)
Constantes.Rutas.* (verificar si existen "16", "AT", "FW", "00")
Constantes.PeriodoFacturacion.* (verificar "NRM", "FDM")
```

---

## Fase 1: Backend (API)

### 📦 1.1: Modelos y DTOs (TDD - 1-2 horas)

#### 1.1.1: Crear DTOs de Request/Response

**Tests a crear primero:**
- [ ] `FacturarRutasRequestDTO_DebeValidarTipoRutaCorrectamente()`
- [ ] `FacturarRutasResponseDTO_DebeContenerListadoErrores()`
- [ ] `PedidoConErrorDTO_DebeContenerInformacionCompleta()`

**Código a implementar después:**
```csharp
// NestoAPI/Models/Facturas/FacturarRutasRequestDTO.cs
public class FacturarRutasRequestDTO
{
    public TipoRutaFacturacion TipoRuta { get; set; }
    public DateTime? FechaEntregaDesde { get; set; } // Por defecto: DateTime.Today
}

public enum TipoRutaFacturacion
{
    RutaPropia,      // "16", "AT"
    RutasAgencias    // "FW", "00"
}

// NestoAPI/Models/Facturas/FacturarRutasResponseDTO.cs
public class FacturarRutasResponseDTO
{
    public int PedidosProcesados { get; set; }
    public int AlbaranesCreados { get; set; }
    public int FacturasCreadas { get; set; }
    public int FacturasImpresas { get; set; }
    public int AlbaranesImpresos { get; set; }
    public List<PedidoConErrorDTO> PedidosConErrores { get; set; }
    public TimeSpan TiempoTotal { get; set; }
}

// NestoAPI/Models/Facturas/PedidoConErrorDTO.cs
public class PedidoConErrorDTO
{
    public string Empresa { get; set; }
    public int NumeroPedido { get; set; }
    public string Cliente { get; set; }
    public string Contacto { get; set; }
    public string NombreCliente { get; set; }
    public string Ruta { get; set; }
    public string PeriodoFacturacion { get; set; }
    public string TipoError { get; set; } // "Albarán", "Factura", "Impresión"
    public string MensajeError { get; set; }
    public DateTime FechaEntrega { get; set; }
    public decimal Total { get; set; }
}
```

**Ubicación:** `NestoAPI/Models/Facturas/`

---

### 📊 1.2: Repositorio/Servicio de Consulta (TDD - 2-3 horas)

#### 1.2.1: Crear `IServicioPedidosParaFacturacion`

**Tests a crear primero:**
- [ ] `ObtenerPedidosRutaPropia_DebeRetornarSoloPedidosRuta16YAT()`
- [ ] `ObtenerPedidosRutaPropia_DebeExcluirPedidosSinLineasEnCurso()`
- [ ] `ObtenerPedidosRutaPropia_DebeExcluirPedidosSinPicking()`
- [ ] `ObtenerPedidosRutaPropia_DebeExcluirPedidosSinVistoBueno()`
- [ ] `ObtenerPedidosRutaPropia_DebeExcluirPedidosConFechaEntregaPasada()`
- [ ] `ObtenerPedidosRutasAgencias_DebeRetornarSoloPedidosRutaFWY00()`
- [ ] `ObtenerPedidosParaFacturar_DebeAplicarTodosFiltrosCorrectamente()`

**Código a implementar después:**
```csharp
// NestoAPI/Infraestructure/IPedidos/IServicioPedidosParaFacturacion.cs
public interface IServicioPedidosParaFacturacion
{
    Task<List<CabPedidoVta>> ObtenerPedidosParaFacturar(
        TipoRutaFacturacion tipoRuta,
        DateTime fechaEntregaDesde);
}

// NestoAPI/Infraestructure/Pedidos/ServicioPedidosParaFacturacion.cs
public class ServicioPedidosParaFacturacion : IServicioPedidosParaFacturacion
{
    private readonly NVEntities db;

    public async Task<List<CabPedidoVta>> ObtenerPedidosParaFacturar(
        TipoRutaFacturacion tipoRuta,
        DateTime fechaEntregaDesde)
    {
        // Determinar rutas según tipo
        List<string> rutas = tipoRuta == TipoRutaFacturacion.RutaPropia
            ? new List<string> { "16", "AT" }
            : new List<string> { "FW", "00" };

        // Query con todos los filtros
        var pedidos = await db.CabPedidoVtas
            .Include(p => p.LinPedidoVtas)
            .Where(p => rutas.Contains(p.Ruta))
            .Where(p => p.LinPedidoVtas.Any(l =>
                l.Estado == Constantes.EstadosLineaVenta.EN_CURSO &&
                l.Picking != null &&
                l.Picking != 0))
            .Where(p => p.FechaEntrega >= fechaEntregaDesde)
            .Where(p => p.VistoBueno == true)
            .ToListAsync();

        return pedidos;
    }
}
```

**Ubicación:** `NestoAPI/Infraestructure/Pedidos/`

---

### ⚙️ 1.3: Gestor de Facturación de Rutas (TDD - 4-6 horas)

#### 1.3.1: Crear `GestorFacturacionRutas`

**Tests a crear primero (orden de implementación):**

**Grupo 1: Detección de comentarios de impresión**
- [ ] `DebeImprimirDocumento_FacturaFisica_RetornaTrue()`
- [ ] `DebeImprimirDocumento_AlbaranFisico_RetornaTrue()`
- [ ] `DebeImprimirDocumento_ComentarioConTextoAdicional_RetornaTrue()`
- [ ] `DebeImprimirDocumento_CaseInsensitive_RetornaTrue()`
- [ ] `DebeImprimirDocumento_SinTildes_RetornaTrue()`
- [ ] `DebeImprimirDocumento_SinComentario_RetornaFalse()`

**Grupo 2: Creación de albaranes**
- [ ] `CrearAlbaranDePedido_PedidoValido_CreaAlbaranCorrectamente()`
- [ ] `CrearAlbaranDePedido_PedidoSinLineas_LanzaExcepcion()`
- [ ] `CrearAlbaranDePedido_ErrorEnBD_CapturaMensajeError()`

**Grupo 3: Validación MantenerJunto**
- [ ] `PuedeFacturarPedido_MantenerJuntoConLineasSinAlbaran_RetornaFalse()`
- [ ] `PuedeFacturarPedido_MantenerJuntoTodasConAlbaran_RetornaTrue()`
- [ ] `PuedeFacturarPedido_NoMantenerJunto_RetornaTrue()`
- [ ] `PuedeFacturarPedido_MantenerJuntoSinLineas_RetornaTrue()`

**Grupo 4: Traspaso a empresa "3"**
- [ ] `DebeTraspasarAEmpresa3_TODO_DefinirCriterios()` ⚠️ **PENDIENTE DE DEFINIR**
- [ ] `TraspasarPedidoAEmpresa3_TODO_ImplementarLogica()` ⚠️ **PENDIENTE DE DEFINIR**

**Grupo 5: Creación de facturas**
- [ ] `CrearFacturaDePedido_AlbaranValido_CreaFacturaCorrectamente()`
- [ ] `CrearFacturaDePedido_SinAlbaran_LanzaExcepcion()`
- [ ] `CrearFacturaDePedido_ErrorEnBD_CapturaMensajeError()`

**Grupo 6: Impresión**
- [ ] `ImprimirFactura_FacturaExiste_GeneraPdfCorrectamente()`
- [ ] `ImprimirAlbaran_AlbaranExiste_GeneraPdfCorrectamente()`
- [ ] `ImprimirFactura_ErrorPdfium_CapturaMensajeError()`

**Grupo 7: Proceso completo por pedido**
- [ ] `ProcesarPedidoNRM_ConFacturaFisica_CreaAlbaranFacturaEImprime()`
- [ ] `ProcesarPedidoNRM_SinFacturaFisica_CreaAlbaranFacturaNoImprime()`
- [ ] `ProcesarPedidoNRM_MantenerJuntoConLineasPendientes_ImprimeAlbaran()` ⭐ **NUEVO**
- [ ] `ProcesarPedidoFDM_ConComentarioImpresion_ImprimeAlbaran()` ⭐ **ACTUALIZADO**
- [ ] `ProcesarPedidoFDM_SinComentarioImpresion_NoImprimeAlbaran()` ⭐ **NUEVO**
- [ ] `ProcesarPedido_ErrorEnAlbaran_ContinuaYRegistraError()`
- [ ] `ProcesarPedido_ErrorEnFactura_ContinuaYRegistraError()`

**Grupo 8: Proceso masivo**
- [ ] `FacturarRutas_ListaVacia_RetornaCerosProcesados()`
- [ ] `FacturarRutas_TodosExitosos_RetornaContadoresCorrectos()`
- [ ] `FacturarRutas_AlgunosConErrores_ContinuaConElResto()`
- [ ] `FacturarRutas_AlgunosConErrores_RetornaListadoErrores()`

**Código a implementar después:**
```csharp
// NestoAPI/Infraestructure/Facturas/GestorFacturacionRutas.cs
public class GestorFacturacionRutas
{
    private readonly NVEntities db;
    private readonly IServicioAlbaranes servicioAlbaranes;
    private readonly IServicioFacturas servicioFacturas;
    private readonly IServicioImpresion servicioImpresion;

    public GestorFacturacionRutas(
        NVEntities db,
        IServicioAlbaranes servicioAlbaranes,
        IServicioFacturas servicioFacturas,
        IServicioImpresion servicioImpresion)
    {
        this.db = db;
        this.servicioAlbaranes = servicioAlbaranes;
        this.servicioFacturas = servicioFacturas;
        this.servicioImpresion = servicioImpresion;
    }

    public async Task<FacturarRutasResponseDTO> FacturarRutas(
        List<CabPedidoVta> pedidos)
    {
        var response = new FacturarRutasResponseDTO
        {
            PedidosConErrores = new List<PedidoConErrorDTO>()
        };

        var stopwatch = Stopwatch.StartNew();

        foreach (var pedido in pedidos)
        {
            try
            {
                await ProcesarPedido(pedido, response);
                response.PedidosProcesados++;
            }
            catch (Exception ex)
            {
                RegistrarError(pedido, "Proceso general", ex.Message, response);
            }
        }

        stopwatch.Stop();
        response.TiempoTotal = stopwatch.Elapsed;

        return response;
    }

    private async Task ProcesarPedido(
        CabPedidoVta pedido,
        FacturarRutasResponseDTO response)
    {
        // 1. Crear albarán
        string numeroAlbaran = null;
        try
        {
            numeroAlbaran = await servicioAlbaranes.CrearAlbaranDePedido(
                pedido.Empresa,
                pedido.Número);
            response.AlbaranesCreados++;
        }
        catch (Exception ex)
        {
            RegistrarError(pedido, "Albarán", ex.Message, response);
            return; // No continuar si falla el albarán
        }

        // 2. ⚠️ TODO: Verificar si hay que traspasar a empresa "3"
        // PENDIENTE: Definir criterios de cuándo traspasar
        // PENDIENTE: Implementar lógica de traspaso
        // await TraspasarAEmpresa3SiCorresponde(pedido);

        // 3. Si es NRM, crear factura (si es posible)
        if (pedido.PeriodoFacturacion?.Trim() == "NRM")
        {
            await ProcesarFacturaNRM(pedido, numeroAlbaran, response);
        }
        // 4. Si es FDM, imprimir albarán si tiene comentario
        else if (pedido.PeriodoFacturacion?.Trim() == "FDM")
        {
            await ImprimirAlbaranSiCorresponde(pedido, numeroAlbaran, response);
        }
    }

    private async Task ProcesarFacturaNRM(
        CabPedidoVta pedido,
        string numeroAlbaran,
        FacturarRutasResponseDTO response)
    {
        // Validar ANTES si se puede facturar (MantenerJunto)
        // Esto evita llamar al procedimiento prdCrearFacturaVta que fallaría
        if (!PuedeFacturarPedido(pedido))
        {
            // No se puede facturar porque MantenerJunto = 1 y hay líneas sin albarán
            // En este caso, si tiene comentario de impresión, imprimir el ALBARÁN
            if (DebeImprimirDocumento(pedido.Comentarios))
            {
                try
                {
                    await servicioImpresion.ImprimirAlbaran(
                        pedido.Empresa,
                        numeroAlbaran);
                    response.AlbaranesImpresos++;
                }
                catch (Exception ex)
                {
                    RegistrarError(pedido, "Impresión Albarán", ex.Message, response);
                }
            }
            return; // No intentar crear factura
        }

        // Crear factura
        string numeroFactura = null;
        try
        {
            numeroFactura = await servicioFacturas.CrearFacturaDeAlbaran(
                pedido.Empresa,
                numeroAlbaran);
            response.FacturasCreadas++;
        }
        catch (Exception ex)
        {
            RegistrarError(pedido, "Factura", ex.Message, response);

            // Si falla la factura pero tiene comentario, imprimir albarán
            if (DebeImprimirDocumento(pedido.Comentarios))
            {
                try
                {
                    await servicioImpresion.ImprimirAlbaran(
                        pedido.Empresa,
                        numeroAlbaran);
                    response.AlbaranesImpresos++;
                }
                catch (Exception exImpresion)
                {
                    RegistrarError(pedido, "Impresión Albarán", exImpresion.Message, response);
                }
            }
            return;
        }

        // Imprimir factura si contiene comentario de impresión
        if (DebeImprimirDocumento(pedido.Comentarios))
        {
            try
            {
                await servicioImpresion.ImprimirFactura(
                    pedido.Empresa,
                    numeroFactura);
                response.FacturasImpresas++;
            }
            catch (Exception ex)
            {
                RegistrarError(pedido, "Impresión Factura", ex.Message, response);
            }
        }
    }

    /// <summary>
    /// Verifica si el pedido puede ser facturado.
    /// Reproduce la lógica del procedimiento prdCrearFacturaVta:
    /// Si MantenerJunto = 1 y existen líneas con Estado < 2 (sin albarán), no se puede facturar.
    /// </summary>
    private bool PuedeFacturarPedido(CabPedidoVta pedido)
    {
        // Si no tiene MantenerJunto, siempre se puede facturar
        if (!pedido.MantenerJunto)
            return true;

        // Si MantenerJunto = 1, verificar si todas las líneas tienen albarán (Estado >= 2)
        bool tieneLineasSinAlbaran = pedido.LinPedidoVtas
            .Any(l => l.Estado < Constantes.EstadosLineaVenta.ALBARAN);

        return !tieneLineasSinAlbaran;
    }

    private async Task ImprimirAlbaranSiCorresponde(
        CabPedidoVta pedido,
        string numeroAlbaran,
        FacturarRutasResponseDTO response)
    {
        // Solo imprimir si tiene comentario de impresión
        if (!DebeImprimirDocumento(pedido.Comentarios))
            return;

        try
        {
            await servicioImpresion.ImprimirAlbaran(
                pedido.Empresa,
                numeroAlbaran);
            response.AlbaranesImpresos++;
        }
        catch (Exception ex)
        {
            RegistrarError(pedido, "Impresión Albarán", ex.Message, response);
        }
    }

    /// <summary>
    /// Determina si se debe imprimir un documento (factura o albarán) físicamente.
    /// Busca en los comentarios del pedido:
    /// - "Factura física" (case insensitive, sin tildes)
    /// - "Albarán físico" (case insensitive, sin tildes)
    /// </summary>
    private bool DebeImprimirDocumento(string comentarios)
    {
        if (string.IsNullOrWhiteSpace(comentarios))
            return false;

        // Normalizar: quitar tildes y convertir a minúsculas
        string comentariosNormalizados = RemoverTildes(comentarios.ToLower());

        // Buscar "factura fisica" o "albaran fisico"
        string facturaFisica = RemoverTildes("factura fisica".ToLower());
        string albaranFisico = RemoverTildes("albaran fisico".ToLower());

        return comentariosNormalizados.Contains(facturaFisica) ||
               comentariosNormalizados.Contains(albaranFisico);
    }

    private string RemoverTildes(string texto)
    {
        var normalized = texto.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();

        foreach (char c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private void RegistrarError(
        CabPedidoVta pedido,
        string tipoError,
        string mensajeError,
        FacturarRutasResponseDTO response)
    {
        var cliente = db.Clientes.FirstOrDefault(c =>
            c.Empresa == pedido.Empresa &&
            c.Nº_Cliente == pedido.Cliente &&
            c.Contacto == pedido.Contacto);

        response.PedidosConErrores.Add(new PedidoConErrorDTO
        {
            Empresa = pedido.Empresa,
            NumeroPedido = pedido.Número,
            Cliente = pedido.Cliente,
            Contacto = pedido.Contacto,
            NombreCliente = cliente?.Nombre ?? "Desconocido",
            Ruta = pedido.Ruta,
            PeriodoFacturacion = pedido.PeriodoFacturacion,
            TipoError = tipoError,
            MensajeError = mensajeError,
            FechaEntrega = pedido.FechaEntrega ?? DateTime.Today,
            Total = pedido.LinPedidoVtas?.Sum(l => l.Importe) ?? 0
        });
    }
}
```

**Ubicación:** `NestoAPI/Infraestructure/Facturas/`

**Dependencias a verificar/crear:**
- `IServicioAlbaranes` (puede ya existir)
- `IServicioFacturas` (probablemente existe como `GestorFacturas`)
- `IServicioImpresion` (nuevo, para abstraer impresión con Pdfium)

---

### 🎯 1.4: Controller API (TDD - 1-2 horas)

#### 1.4.1: Crear `FacturacionRutasController`

**Tests a crear primero:**
- [ ] `FacturarRutas_UsuarioNoAutorizado_Retorna403()`
- [ ] `FacturarRutas_TipoRutaInvalido_Retorna400()`
- [ ] `FacturarRutas_RequestValido_Retorna200ConResponse()`
- [ ] `FacturarRutas_SinPedidos_RetornaResponseVacio()`

**Código a implementar después:**
```csharp
// NestoAPI/Controllers/FacturacionRutasController.cs
[Authorize]
[RoutePrefix("api/FacturacionRutas")]
public class FacturacionRutasController : ApiController
{
    private readonly NVEntities db;
    private readonly IServicioPedidosParaFacturacion servicioPedidos;

    public FacturacionRutasController()
    {
        db = new NVEntities();
        servicioPedidos = new ServicioPedidosParaFacturacion(db);
    }

    // Constructor para testing
    internal FacturacionRutasController(
        NVEntities db,
        IServicioPedidosParaFacturacion servicioPedidos)
    {
        this.db = db;
        this.servicioPedidos = servicioPedidos;
    }

    /// <summary>
    /// Factura pedidos por rutas (propia o agencias) listos para facturar
    /// </summary>
    /// <param name="request">Parámetros de facturación</param>
    /// <returns>Resultado con contadores y errores</returns>
    [HttpPost]
    [Route("Facturar")]
    public async Task<IHttpActionResult> FacturarRutas(
        [FromBody] FacturarRutasRequestDTO request)
    {
        if (request == null)
            return BadRequest("Request no puede ser null");

        // Validar autorización (Almacén o Dirección)
        var user = User as ClaimsPrincipal;
        var grupos = user?.Claims
            .Where(c => c.Type == ClaimTypes.Role)
            .Select(c => c.Value)
            .ToList() ?? new List<string>();

        if (!grupos.Contains(Constantes.GruposSeguridad.ALMACEN) &&
            !grupos.Contains(Constantes.GruposSeguridad.DIRECCION))
        {
            return StatusCode(HttpStatusCode.Forbidden);
        }

        try
        {
            // 1. Obtener pedidos
            var fechaDesde = request.FechaEntregaDesde ?? DateTime.Today;
            var pedidos = await servicioPedidos.ObtenerPedidosParaFacturar(
                request.TipoRuta,
                fechaDesde);

            if (!pedidos.Any())
            {
                return Ok(new FacturarRutasResponseDTO
                {
                    PedidosConErrores = new List<PedidoConErrorDTO>()
                });
            }

            // 2. Procesar facturación
            var servicioAlbaranes = new ServicioAlbaranes(db);
            var servicioFacturas = new ServicioFacturas(db);
            var servicioImpresion = new ServicioImpresion();

            var gestor = new GestorFacturacionRutas(
                db,
                servicioAlbaranes,
                servicioFacturas,
                servicioImpresion
            );

            var response = await gestor.FacturarRutas(pedidos);

            return Ok(response);
        }
        catch (Exception ex)
        {
            return InternalServerError(ex);
        }
    }
}
```

**Ubicación:** `NestoAPI/Controllers/`

---

### 🖨️ 1.5: Servicio de Impresión (TDD - 2-3 horas)

#### 1.5.1: Crear `IServicioImpresion`

**Tests a crear primero:**
- [ ] `ImprimirFactura_FacturaExiste_GeneraPdfEnRutaCorrecta()`
- [ ] `ImprimirFactura_FacturaNoExiste_LanzaExcepcion()`
- [ ] `ImprimirAlbaran_AlbaranExiste_GeneraPdfEnRutaCorrecta()`
- [ ] `ImprimirFactura_ErrorReportServer_LanzaExcepcionConMensajeClaro()`

**Código a implementar después:**
```csharp
// NestoAPI/Infraestructure/Impresion/IServicioImpresion.cs
public interface IServicioImpresion
{
    Task ImprimirFactura(string empresa, string numeroFactura);
    Task ImprimirAlbaran(string empresa, string numeroAlbaran);
}

// NestoAPI/Infraestructure/Impresion/ServicioImpresion.cs
public class ServicioImpresion : IServicioImpresion
{
    private readonly IServicioReportServer servicioReportServer;

    public ServicioImpresion(IServicioReportServer servicioReportServer = null)
    {
        this.servicioReportServer = servicioReportServer
            ?? new ServicioReportServer();
    }

    public async Task ImprimirFactura(string empresa, string numeroFactura)
    {
        try
        {
            // Generar PDF de la factura usando Report Server
            var parametros = new Dictionary<string, string>
            {
                { "empresa", empresa },
                { "numeroFactura", numeroFactura }
            };

            string rutaPdf = await servicioReportServer.GenerarPdf(
                "/Facturas/FacturaVenta",
                parametros,
                $"Factura_{empresa}_{numeroFactura}.pdf"
            );

            // TODO: Enviar a impresora o cola de impresión
            // Por ahora solo generamos el PDF en la ruta configurada
        }
        catch (Exception ex)
        {
            throw new Exception(
                $"Error al imprimir factura {numeroFactura}: {ex.Message}",
                ex);
        }
    }

    public async Task ImprimirAlbaran(string empresa, string numeroAlbaran)
    {
        try
        {
            var parametros = new Dictionary<string, string>
            {
                { "empresa", empresa },
                { "numeroAlbaran", numeroAlbaran }
            };

            string rutaPdf = await servicioReportServer.GenerarPdf(
                "/Albaranes/AlbaranVenta",
                parametros,
                $"Albaran_{empresa}_{numeroAlbaran}.pdf"
            );

            // TODO: Enviar a impresora
        }
        catch (Exception ex)
        {
            throw new Exception(
                $"Error al imprimir albarán {numeroAlbaran}: {ex.Message}",
                ex);
        }
    }
}
```

**Ubicación:** `NestoAPI/Infraestructure/Impresion/`

**NOTA:** Verificar si ya existe integración con Report Server / SSRS. Si no existe, crear abstracción.

---

### 📝 1.6: Agregar a Constantes (5-10 min)

```csharp
// NestoAPI/Models/Constantes.cs

public static class Rutas
{
    public const string RUTA_PROPIA_16 = "16";
    public const string RUTA_PROPIA_AT = "AT";
    public const string RUTA_AGENCIA_FW = "FW";
    public const string RUTA_AGENCIA_00 = "00";

    public static List<string> RutasPropia => new List<string> { RUTA_PROPIA_16, RUTA_PROPIA_AT };
    public static List<string> RutasAgencias => new List<string> { RUTA_AGENCIA_FW, RUTA_AGENCIA_00 };
}

public static class PeriodoFacturacion
{
    public const string NORMAL = "NRM";
    public const string FIN_DE_MES = "FDM";
}

public static class EstadosLineaVenta
{
    // ... (constantes existentes)
    public const short EN_CURSO = 1;
    public const short ALBARAN = 2;
    public const short FACTURA = 4;
}
```

**NOTA:** Verificar que `EstadosLineaVenta.ALBARAN` existe con valor 2, necesario para la validación de `MantenerJunto`.

---

### ⚠️ 1.7: Traspaso a Empresa "3" - PENDIENTE DE DEFINIR

**Estado:** 🔴 **PENDIENTE - Requiere análisis y definición**

**Ubicación en el flujo:**
- **DESPUÉS** de crear el albarán
- **ANTES** de crear la factura

**Tareas pendientes:**

#### Análisis (1-2 horas)
- [ ] **Investigar cuándo hay que traspasar un pedido a empresa "3"**
  - ¿Qué criterios determinan el traspaso?
  - ¿Tipo de cliente?
  - ¿Producto?
  - ¿Ruta?
  - ¿Forma de pago?
  - ¿Otros factores?

- [ ] **Investigar cómo se hace actualmente el traspaso**
  - ¿Existe un servicio/método ya implementado?
  - ¿Procedimiento almacenado?
  - ¿API endpoint?
  - ¿Qué datos se copian/mueven?
  - ¿Qué pasa con el pedido original?

- [ ] **Buscar referencias en el código existente**
  ```bash
  # Buscar en NestoAPI
  grep -r "empresa.*3" --include="*.cs"
  grep -r "traspa" --include="*.cs"
  grep -r "Traspas" --include="*.cs"
  ```

#### Implementación (pendiente de análisis)
- [ ] Tests para `DebeTraspasarAEmpresa3(pedido)`
- [ ] Tests para `TraspasarPedidoAEmpresa3(pedido)`
- [ ] Implementar lógica de traspaso
- [ ] Integrar en `GestorFacturacionRutas.ProcesarPedido()`
- [ ] Manejo de errores si falla el traspaso

**Código placeholder en GestorFacturacionRutas:**
```csharp
// 2. ⚠️ TODO: Verificar si hay que traspasar a empresa "3"
// PENDIENTE: Definir criterios de cuándo traspasar
// PENDIENTE: Implementar lógica de traspaso
// if (DebeTraspasarAEmpresa3(pedido))
// {
//     try
//     {
//         await TraspasarPedidoAEmpresa3(pedido);
//     }
//     catch (Exception ex)
//     {
//         RegistrarError(pedido, "Traspaso Empresa 3", ex.Message, response);
//         return; // ¿Continuar o abortar si falla?
//     }
// }
```

**Preguntas a responder:**
1. ¿El traspaso es obligatorio para todos los pedidos de ciertas rutas?
2. ¿Qué pasa si falla el traspaso? ¿Se aborta todo el proceso?
3. ¿El pedido se "mueve" a empresa 3 o se "copia"?
4. ¿Hay que actualizar referencias después del traspaso?
5. ¿El albarán creado se mantiene en empresa original o también se traspasa?

**Prioridad:** ⚠️ ALTA - Debe definirse antes de pasar a producción

---

## Fase 2: Frontend (WPF)

### 🎨 2.1: Popup View y ViewModel (2-3 horas)

#### 2.1.1: Crear `FacturarRutasPopupView.xaml`

**Tareas:**
- [ ] Copiar estructura de `PickingPopupView.xaml` como base
- [ ] Diseñar layout con dos RadioButtons: "Ruta propia" y "Rutas de agencias"
- [ ] Agregar botón "Facturar" (Command)
- [ ] Agregar botón "Cancelar"
- [ ] Agregar ProgressBar para mostrar progreso
- [ ] Agregar TextBlock para mostrar mensajes de estado

**Estructura propuesta:**
```xml
<Window x:Class="Nesto.Views.FacturarRutasPopupView"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Facturar Rutas"
        Width="400" Height="250"
        WindowStartupLocation="CenterOwner"
        ResizeMode="NoResize">

    <Grid Margin="20">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <!-- Título -->
        <TextBlock Grid.Row="0"
                   Text="Seleccione el tipo de ruta a facturar:"
                   FontSize="14" FontWeight="SemiBold"
                   Margin="0,0,0,15"/>

        <!-- Opciones -->
        <StackPanel Grid.Row="1" Margin="0,0,0,20">
            <RadioButton Content="Ruta propia (16, AT)"
                         IsChecked="{Binding EsRutaPropia}"
                         Margin="0,0,0,10"
                         FontSize="13"/>
            <RadioButton Content="Rutas de agencias (FW, 00)"
                         IsChecked="{Binding EsRutasAgencias}"
                         FontSize="13"/>
        </StackPanel>

        <!-- Progress Bar -->
        <ProgressBar Grid.Row="2"
                     Height="20"
                     Margin="0,0,0,10"
                     Visibility="{Binding EstaProcesando, Converter={StaticResource BoolToVisibilityConverter}}"
                     IsIndeterminate="True"/>

        <!-- Mensaje de estado -->
        <TextBlock Grid.Row="3"
                   Text="{Binding MensajeEstado}"
                   TextWrapping="Wrap"
                   FontSize="12"
                   Foreground="{Binding ColorMensaje}"
                   VerticalAlignment="Top"/>

        <!-- Botones -->
        <StackPanel Grid.Row="4"
                    Orientation="Horizontal"
                    HorizontalAlignment="Right"
                    Margin="0,15,0,0">
            <Button Content="Facturar"
                    Width="100"
                    Height="30"
                    Margin="0,0,10,0"
                    Command="{Binding FacturarCommand}"
                    IsEnabled="{Binding EstaProcesando, Converter={StaticResource InverseBoolConverter}}"/>
            <Button Content="Cancelar"
                    Width="100"
                    Height="30"
                    Command="{Binding CancelarCommand}"/>
        </StackPanel>
    </Grid>
</Window>
```

**Ubicación:** `Nesto/Views/FacturarRutasPopupView.xaml`

---

#### 2.1.2: Crear `FacturarRutasPopupViewModel.cs`

**Tareas:**
- [ ] Implementar propiedades: `EsRutaPropia`, `EsRutasAgencias`, `EstaProcesando`, `MensajeEstado`, `ColorMensaje`
- [ ] Implementar `FacturarCommand` (async)
- [ ] Implementar `CancelarCommand`
- [ ] Integrar con `IServicioFacturacionRutas` (cliente API)
- [ ] Manejar resultado con errores
- [ ] Si hay errores, abrir ventana de listado de errores

**Código propuesto:**
```csharp
// Nesto/ViewModels/FacturarRutasPopupViewModel.cs
public class FacturarRutasPopupViewModel : ViewModelBase
{
    private readonly IServicioFacturacionRutas servicioFacturacion;
    private readonly IServicioDialogos servicioDialogos;

    public FacturarRutasPopupViewModel(
        IServicioFacturacionRutas servicioFacturacion,
        IServicioDialogos servicioDialogos)
    {
        this.servicioFacturacion = servicioFacturacion;
        this.servicioDialogos = servicioDialogos;

        FacturarCommand = new RelayCommand(
            async () => await FacturarRutas(),
            () => !EstaProcesando);
        CancelarCommand = new RelayCommand(Cancelar);

        // Por defecto: Ruta propia
        EsRutaPropia = true;
    }

    #region Propiedades

    private bool _esRutaPropia;
    public bool EsRutaPropia
    {
        get => _esRutaPropia;
        set
        {
            if (SetProperty(ref _esRutaPropia, value))
            {
                if (value) EsRutasAgencias = false;
            }
        }
    }

    private bool _esRutasAgencias;
    public bool EsRutasAgencias
    {
        get => _esRutasAgencias;
        set
        {
            if (SetProperty(ref _esRutasAgencias, value))
            {
                if (value) EsRutaPropia = false;
            }
        }
    }

    private bool _estaProcesando;
    public bool EstaProcesando
    {
        get => _estaProcesando;
        set => SetProperty(ref _estaProcesando, value);
    }

    private string _mensajeEstado;
    public string MensajeEstado
    {
        get => _mensajeEstado;
        set => SetProperty(ref _mensajeEstado, value);
    }

    private Brush _colorMensaje = Brushes.Black;
    public Brush ColorMensaje
    {
        get => _colorMensaje;
        set => SetProperty(ref _colorMensaje, value);
    }

    #endregion

    #region Commands

    public ICommand FacturarCommand { get; }
    public ICommand CancelarCommand { get; }

    private async Task FacturarRutas()
    {
        EstaProcesando = true;
        MensajeEstado = "Obteniendo pedidos...";
        ColorMensaje = Brushes.Blue;

        try
        {
            var tipoRuta = EsRutaPropia
                ? TipoRutaFacturacion.RutaPropia
                : TipoRutaFacturacion.RutasAgencias;

            var request = new FacturarRutasRequestDTO
            {
                TipoRuta = tipoRuta,
                FechaEntregaDesde = DateTime.Today
            };

            MensajeEstado = "Facturando pedidos...";
            var response = await servicioFacturacion.FacturarRutas(request);

            // Mostrar resultado
            MostrarResumen(response);

            // Si hay errores, mostrar ventana de errores
            if (response.PedidosConErrores != null && response.PedidosConErrores.Any())
            {
                await MostrarVentanaErrores(response.PedidosConErrores);
            }
            else
            {
                // Cerrar popup si todo fue exitoso
                await Task.Delay(2000); // Mostrar mensaje 2 segundos
                Cancelar();
            }
        }
        catch (Exception ex)
        {
            MensajeEstado = $"Error: {ex.Message}";
            ColorMensaje = Brushes.Red;
        }
        finally
        {
            EstaProcesando = false;
        }
    }

    private void MostrarResumen(FacturarRutasResponseDTO response)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"✓ Procesados: {response.PedidosProcesados}");
        sb.AppendLine($"✓ Albaranes: {response.AlbaranesCreados}");
        sb.AppendLine($"✓ Facturas: {response.FacturasCreadas}");

        if (response.FacturasImpresas > 0)
            sb.AppendLine($"✓ Facturas impresas: {response.FacturasImpresas}");
        if (response.AlbaranesImpresos > 0)
            sb.AppendLine($"✓ Albaranes impresos: {response.AlbaranesImpresos}");

        if (response.PedidosConErrores?.Any() == true)
        {
            sb.AppendLine($"⚠ Errores: {response.PedidosConErrores.Count}");
            ColorMensaje = Brushes.Orange;
        }
        else
        {
            ColorMensaje = Brushes.Green;
        }

        sb.AppendLine($"Tiempo: {response.TiempoTotal.TotalSeconds:F1}s");

        MensajeEstado = sb.ToString();
    }

    private async Task MostrarVentanaErrores(List<PedidoConErrorDTO> errores)
    {
        var viewModel = new ErroresFacturacionViewModel(errores);
        var view = new ErroresFacturacionView
        {
            DataContext = viewModel
        };

        view.ShowDialog();
    }

    private void Cancelar()
    {
        // Cerrar ventana
        var window = Application.Current.Windows
            .OfType<FacturarRutasPopupView>()
            .FirstOrDefault();
        window?.Close();
    }

    #endregion
}
```

**Ubicación:** `Nesto/ViewModels/FacturarRutasPopupViewModel.cs`

---

### 📋 2.2: Ventana de Errores (1-2 horas)

#### 2.2.1: Crear `ErroresFacturacionView.xaml`

**Tareas:**
- [ ] DataGrid con columnas: Pedido, Cliente, Ruta, Tipo Error, Mensaje
- [ ] Doble clic en fila abre el pedido
- [ ] Botón "Cerrar"
- [ ] Botón "Exportar CSV" (opcional)

**Estructura propuesta:**
```xml
<Window x:Class="Nesto.Views.ErroresFacturacionView"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Errores en Facturación de Rutas"
        Width="900" Height="500"
        WindowStartupLocation="CenterOwner">

    <Grid Margin="10">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <!-- Título -->
        <TextBlock Grid.Row="0"
                   Text="{Binding TituloVentana}"
                   FontSize="16" FontWeight="Bold"
                   Margin="0,0,0,10"/>

        <!-- DataGrid de errores -->
        <DataGrid Grid.Row="1"
                  ItemsSource="{Binding Errores}"
                  SelectedItem="{Binding ErrorSeleccionado}"
                  AutoGenerateColumns="False"
                  IsReadOnly="True"
                  SelectionMode="Single"
                  CanUserSortColumns="True">

            <DataGrid.InputBindings>
                <MouseBinding Gesture="LeftDoubleClick"
                              Command="{Binding AbrirPedidoCommand}"/>
            </DataGrid.InputBindings>

            <DataGrid.Columns>
                <DataGridTextColumn Header="Pedido"
                                    Binding="{Binding NumeroPedido}"
                                    Width="80"/>
                <DataGridTextColumn Header="Cliente"
                                    Binding="{Binding Cliente}"
                                    Width="80"/>
                <DataGridTextColumn Header="Nombre"
                                    Binding="{Binding NombreCliente}"
                                    Width="200"/>
                <DataGridTextColumn Header="Ruta"
                                    Binding="{Binding Ruta}"
                                    Width="60"/>
                <DataGridTextColumn Header="Tipo Error"
                                    Binding="{Binding TipoError}"
                                    Width="100"/>
                <DataGridTextColumn Header="Mensaje"
                                    Binding="{Binding MensajeError}"
                                    Width="*"/>
                <DataGridTextColumn Header="Total"
                                    Binding="{Binding Total, StringFormat=C}"
                                    Width="100"/>
            </DataGrid.Columns>
        </DataGrid>

        <!-- Botones -->
        <StackPanel Grid.Row="2"
                    Orientation="Horizontal"
                    HorizontalAlignment="Right"
                    Margin="0,10,0,0">
            <Button Content="Exportar CSV"
                    Width="120"
                    Height="30"
                    Margin="0,0,10,0"
                    Command="{Binding ExportarCommand}"/>
            <Button Content="Cerrar"
                    Width="100"
                    Height="30"
                    Command="{Binding CerrarCommand}"/>
        </StackPanel>
    </Grid>
</Window>
```

**Ubicación:** `Nesto/Views/ErroresFacturacionView.xaml`

---

#### 2.2.2: Crear `ErroresFacturacionViewModel.cs`

**Código propuesto:**
```csharp
// Nesto/ViewModels/ErroresFacturacionViewModel.cs
public class ErroresFacturacionViewModel : ViewModelBase
{
    private readonly IServicioNavegacion servicioNavegacion;

    public ErroresFacturacionViewModel(List<PedidoConErrorDTO> errores)
    {
        Errores = new ObservableCollection<PedidoConErrorDTO>(errores);
        servicioNavegacion = ServiceLocator.Current.GetInstance<IServicioNavegacion>();

        AbrirPedidoCommand = new RelayCommand(
            AbrirPedido,
            () => ErrorSeleccionado != null);
        ExportarCommand = new RelayCommand(ExportarCSV);
        CerrarCommand = new RelayCommand(Cerrar);
    }

    public ObservableCollection<PedidoConErrorDTO> Errores { get; }

    public string TituloVentana =>
        $"Errores en Facturación ({Errores.Count} pedidos con errores)";

    private PedidoConErrorDTO _errorSeleccionado;
    public PedidoConErrorDTO ErrorSeleccionado
    {
        get => _errorSeleccionado;
        set => SetProperty(ref _errorSeleccionado, value);
    }

    public ICommand AbrirPedidoCommand { get; }
    public ICommand ExportarCommand { get; }
    public ICommand CerrarCommand { get; }

    private void AbrirPedido()
    {
        if (ErrorSeleccionado == null) return;

        // Buscar cómo se abre un pedido en otros ViewModels
        // Ej: ComisionesViewModel, CanalesExternosViewModel
        servicioNavegacion.AbrirDetallePedido(
            ErrorSeleccionado.Empresa,
            ErrorSeleccionado.NumeroPedido);
    }

    private void ExportarCSV()
    {
        // TODO: Implementar exportación a CSV
        var saveDialog = new SaveFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv",
            FileName = $"ErroresFacturacion_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
        };

        if (saveDialog.ShowDialog() == true)
        {
            // Generar CSV
        }
    }

    private void Cerrar()
    {
        var window = Application.Current.Windows
            .OfType<ErroresFacturacionView>()
            .FirstOrDefault();
        window?.Close();
    }
}
```

**Ubicación:** `Nesto/ViewModels/ErroresFacturacionViewModel.cs`

---

### 🔘 2.3: Botón en DetallePedidoView (1 hora)

#### 2.3.1: Agregar botón "Facturar Rutas"

**Tareas:**
- [ ] Analizar layout actual de botones en `DetallePedidoView.xaml`
- [ ] Decidir ubicación óptima (ribbon, toolbar, panel inferior)
- [ ] Agregar botón con comando y CanExecute
- [ ] Aplicar estilo consistente con otros botones

**Opciones de ubicación:**

**Opción A: Ribbon (si existe)**
```xml
<!-- En el Ribbon, crear nuevo grupo "Facturación" -->
<RibbonGroup Header="Facturación">
    <RibbonButton Label="Facturar Rutas"
                  LargeImageSource="/Images/facturar_rutas.png"
                  Command="{Binding AbrirFacturarRutasCommand}"/>
</RibbonGroup>
```

**Opción B: ToolBar horizontal**
```xml
<ToolBar>
    <!-- Botones existentes... -->
    <Separator/>
    <Button Content="Facturar Rutas"
            Command="{Binding AbrirFacturarRutasCommand}"
            ToolTip="Facturar pedidos de rutas (Almacén/Dirección)">
        <Button.Style>
            <!-- Estilo consistente -->
        </Button.Style>
    </Button>
</ToolBar>
```

**Opción C: Panel de acciones (agrupado)**
```xml
<!-- Crear un Expander o GroupBox para operaciones masivas -->
<GroupBox Header="Operaciones Masivas">
    <StackPanel Orientation="Horizontal">
        <Button Content="Picking Masivo" Command="{Binding PickingCommand}"/>
        <Button Content="Facturar Rutas" Command="{Binding AbrirFacturarRutasCommand}"/>
        <!-- Otros botones masivos... -->
    </StackPanel>
</GroupBox>
```

**Ubicación:** `Nesto/Views/DetallePedidoView.xaml`

---

#### 2.3.2: Agregar comando en `DetallePedidoViewModel.cs`

**Tareas:**
- [ ] Agregar propiedad `AbrirFacturarRutasCommand`
- [ ] Implementar CanExecute: usuario en Almacén o Dirección
- [ ] Implementar Execute: abrir `FacturarRutasPopupView`

**Código propuesto:**
```csharp
// Nesto/ViewModels/DetallePedidoViewModel.cs (agregar)

public ICommand AbrirFacturarRutasCommand { get; private set; }

// En el constructor:
AbrirFacturarRutasCommand = new RelayCommand(
    AbrirFacturarRutas,
    PuedeFacturarRutas
);

private bool PuedeFacturarRutas()
{
    return configuracion.UsuarioEnGrupo(Constantes.GruposSeguridad.DIRECCION) ||
           configuracion.UsuarioEnGrupo(Constantes.GruposSeguridad.ALMACEN);
}

private void AbrirFacturarRutas()
{
    var servicioFacturacion = ServiceLocator.Current
        .GetInstance<IServicioFacturacionRutas>();
    var servicioDialogos = ServiceLocator.Current
        .GetInstance<IServicioDialogos>();

    var viewModel = new FacturarRutasPopupViewModel(
        servicioFacturacion,
        servicioDialogos);

    var view = new FacturarRutasPopupView
    {
        DataContext = viewModel,
        Owner = Application.Current.MainWindow
    };

    view.ShowDialog();
}
```

**Ubicación:** `Nesto/ViewModels/DetallePedidoViewModel.cs`

---

### 🌐 2.4: Cliente API (1-2 horas)

#### 2.4.1: Crear `IServicioFacturacionRutas`

**Tareas:**
- [ ] Crear interfaz con método `FacturarRutas`
- [ ] Implementar cliente HTTP que llama al endpoint de la API
- [ ] Manejar errores de conexión
- [ ] Registrar en IoC container

**Código propuesto:**
```csharp
// Nesto/Services/IServicioFacturacionRutas.cs
public interface IServicioFacturacionRutas
{
    Task<FacturarRutasResponseDTO> FacturarRutas(
        FacturarRutasRequestDTO request);
}

// Nesto/Services/ServicioFacturacionRutas.cs
public class ServicioFacturacionRutas : IServicioFacturacionRutas
{
    private readonly HttpClient httpClient;
    private readonly IConfiguracion configuracion;

    public ServicioFacturacionRutas(
        HttpClient httpClient,
        IConfiguracion configuracion)
    {
        this.httpClient = httpClient;
        this.configuracion = configuracion;
    }

    public async Task<FacturarRutasResponseDTO> FacturarRutas(
        FacturarRutasRequestDTO request)
    {
        try
        {
            var url = $"{configuracion.UrlApi}/api/FacturacionRutas/Facturar";

            var json = JsonConvert.SerializeObject(request);
            var content = new StringContent(
                json,
                Encoding.UTF8,
                "application/json");

            var response = await httpClient.PostAsync(url, content);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            var result = JsonConvert.DeserializeObject<FacturarRutasResponseDTO>(
                responseJson);

            return result;
        }
        catch (HttpRequestException ex)
        {
            throw new Exception(
                $"Error al conectar con la API: {ex.Message}",
                ex);
        }
        catch (Exception ex)
        {
            throw new Exception(
                $"Error al facturar rutas: {ex.Message}",
                ex);
        }
    }
}
```

**Ubicación:** `Nesto/Services/`

**Registro IoC (en App.xaml.cs o módulo Ninject):**
```csharp
container.Bind<IServicioFacturacionRutas>()
    .To<ServicioFacturacionRutas>()
    .InSingletonScope();
```

---

## Fase 3: Integración y Testing E2E

### 🧪 3.1: Tests de Integración API (2-3 horas)

**Tests a crear:**
- [ ] `IntegrationTest_FacturarRutasPropia_PedidoCompleto()`
- [ ] `IntegrationTest_FacturarRutasAgencias_PedidoFDM()`
- [ ] `IntegrationTest_FacturarRutas_ConErrores_RetornaListado()`
- [ ] `IntegrationTest_FacturarRutas_ConFacturaFisica_ImprimePdf()`

**Configurar:**
- Base de datos de test
- Datos de prueba (pedidos en estado correcto)
- Cleanup después de cada test

---

### 🖼️ 3.2: Tests UI (WPF) (2-3 horas)

**Tests a crear (con FakeItEasy):**
- [ ] `FacturarRutasPopupViewModel_FacturarExitoso_MuestraMensajeVerde()`
- [ ] `FacturarRutasPopupViewModel_ConErrores_AbreVentanaErrores()`
- [ ] `ErroresFacturacionViewModel_DobleClick_AbrePedido()`
- [ ] `DetallePedidoViewModel_UsuarioNoAutorizado_BotonDeshabilitado()`

---

### 🎯 3.3: Testing Manual (1-2 horas)

**Checklist de testing manual:**
- [ ] Botón aparece solo para usuarios Almacén/Dirección
- [ ] Popup se abre correctamente
- [ ] Opciones de ruta funcionan (radiobuttons)
- [ ] Proceso de facturación funciona para ruta propia
- [ ] Proceso de facturación funciona para rutas agencias
- [ ] Detecta correctamente "FACTURA FÍSICA" (con y sin tildes)
- [ ] Impresión funciona
- [ ] Ventana de errores muestra listado correcto
- [ ] Doble clic abre pedido correcto
- [ ] Manejo de errores es robusto

---

## Fase 4: Mejoras y Refinamiento

### 🚀 4.1: Optimizaciones (1-2 horas)

**Tareas:**
- [ ] Paralelizar creación de albaranes/facturas (Task.WhenAll con límite)
- [ ] Agregar logs detallados (Serilog o similar)
- [ ] Implementar retry policy para impresión fallida
- [ ] Cache de rutas y configuraciones

---

### 📊 4.2: Reporting (1 hora)

**Tareas:**
- [ ] Agregar opción de generar reporte de facturación
- [ ] Email automático a Almacén con resumen
- [ ] Dashboard con estadísticas (opcional)

---

### 🎨 4.3: UX Improvements (1 hora)

**Tareas:**
- [ ] Agregar preview de pedidos antes de facturar
- [ ] Mostrar contador en tiempo real durante proceso
- [ ] Animaciones en progreso
- [ ] Notificaciones toast al finalizar

---

## Consideraciones Técnicas

### 🔒 Seguridad

- [ ] Validar permisos en API (además de frontend)
- [ ] Auditoría de facturaciones masivas
- [ ] Rate limiting en endpoint

### 🗄️ Performance

- [ ] Índices en base de datos para queries de rutas
- [ ] Paginación si hay muchos pedidos (>100)
- [ ] Timeout configurables para operaciones largas

### 🐛 Manejo de Errores

- [ ] Rollback de transacciones si falla creación de factura
- [ ] No continuar si falla albarán (correcto según diseño)
- [ ] Logs detallados en API para debugging

### 📝 Documentación

- [ ] Swagger documentation para endpoint
- [ ] Comentarios XML en métodos públicos
- [ ] README con proceso de facturación

---

## Checklist General

### Backend (API)
- [ ] DTOs creados y testeados
- [ ] Servicio de consulta pedidos implementado
- [ ] Gestor de facturación implementado
- [ ] Controller con autenticación
- [ ] Servicio de impresión implementado
- [ ] Constantes agregadas
- [ ] Tests unitarios pasando (verde)
- [ ] Tests de integración pasando

### Frontend (WPF)
- [ ] Popup view creado
- [ ] Popup ViewModel implementado
- [ ] Ventana de errores creada
- [ ] ViewModel de errores implementado
- [ ] Botón agregado en DetallePedidoView
- [ ] Comando en DetallePedidoViewModel
- [ ] Cliente API implementado
- [ ] IoC configurado
- [ ] Tests UI pasando

### Integración
- [ ] API y Frontend comunicándose correctamente
- [ ] Impresión funcionando end-to-end
- [ ] Manejo de errores completo
- [ ] Testing manual completado

### Documentación
- [ ] Código comentado
- [ ] README actualizado
- [ ] Swagger documentation

---

## Estimación de Tiempo Total

| Fase | Tiempo Estimado |
|------|----------------|
| Análisis Previo | 0.5 - 1 hora |
| Backend (API) | 10 - 16 horas |
| Frontend (WPF) | 6 - 9 horas |
| Integración y Testing | 5 - 8 horas |
| Mejoras y Refinamiento | 3 - 4 horas |
| **TOTAL** | **24.5 - 38 horas** |

**Recomendación:** Abordar en sprints de 3-4 horas, completando cada fase antes de pasar a la siguiente.

---

## Orden Recomendado de Implementación

1. **Sesión 1 (3-4h):** Análisis + DTOs + Constantes + Tests básicos
2. **Sesión 2 (3-4h):** Servicio de consulta pedidos + Tests
3. **Sesión 3 (4-5h):** Gestor de facturación (parte 1: albaranes)
4. **Sesión 4 (4-5h):** Gestor de facturación (parte 2: facturas e impresión)
5. **Sesión 5 (3-4h):** Controller API + Tests de integración
6. **Sesión 6 (3-4h):** Frontend: Popup View y ViewModel
7. **Sesión 7 (3-4h):** Frontend: Ventana errores + Botón en DetallePedido
8. **Sesión 8 (2-3h):** Testing E2E + Ajustes finales

---

## 📊 Diagrama de Flujo Actualizado

### Flujo de Facturación por Pedido

```
┌─────────────────────────────────────────────────────────────────┐
│                    INICIO: Procesar Pedido                       │
└────────────────────────────┬────────────────────────────────────┘
                             │
                             ▼
                    ┌────────────────────┐
                    │ 1. Crear Albarán   │
                    └─────────┬──────────┘
                              │
                        ┌─────┴─────┐
                        │ ¿Éxito?   │
                        └─────┬─────┘
                   No ◄────┤     ├────► Sí
                       │             │
                       ▼             ▼
              ┌──────────────┐  ┌────────────────────────────┐
              │ Registrar    │  │ 2. ⚠️ Traspaso Empresa "3" │
              │ Error        │  │    (PENDIENTE DEFINIR)     │
              │ RETORNAR     │  └─────────┬──────────────────┘
              └──────────────┘            │
                                          ▼
                                  ┌───────────────┐
                                  │ ¿PeriodoFact? │
                                  └───────┬───────┘
                                          │
                    ┌─────────────────────┼─────────────────────┐
                    │ NRM                 │                 FDM │
                    ▼                     ▼                     ▼
        ┌──────────────────────┐  ┌──────────────┐  ┌─────────────────────┐
        │ 3a. Validar          │  │  (Otro)      │  │ 3b. ¿Debe Imprimir? │
        │ MantenerJunto        │  │  No hacer    │  │  Comentario?        │
        └──────┬───────────────┘  │  nada        │  └──────────┬──────────┘
               │                  └──────────────┘             │
         ┌─────┴─────┐                              ┌──────────┴──────────┐
         │ ¿Puede    │                              │ Sí                No│
         │ Facturar? │                              ▼                     ▼
         └─────┬─────┘                    ┌──────────────────┐  ┌────────────┐
               │                          │ Imprimir Albarán │  │ FIN        │
         ┌─────┴─────┐                    └──────────────────┘  └────────────┘
         │ Sí      No│
         ▼           ▼
   ┌──────────┐  ┌──────────────────┐
   │ Crear    │  │ ¿Debe Imprimir?  │
   │ Factura  │  │ Comentario?      │
   └────┬─────┘  └────────┬─────────┘
        │                 │
   ┌────┴────┐      ┌─────┴─────┐
   │ ¿Éxito? │      │ Sí      No│
   └────┬────┘      ▼           ▼
        │     ┌──────────────┐  └─► FIN
        │     │ Imprimir     │
   ┌────┴────┐│ Albarán      │
   │ Sí    No││ (fallback)   │
   ▼         ▼└──────────────┘
┌──────┐  ┌──────────────────┐
│¿Debe │  │ ¿Debe Imprimir?  │
│Impri │  │ Comentario?      │
│mir?  │  └────────┬─────────┘
└──┬───┘           │
   │         ┌─────┴─────┐
   │         │ Sí      No│
   ▼         ▼           ▼
┌──────┐  ┌──────────┐  └─► FIN
│Impri │  │ Imprimir │
│mir   │  │ Albarán  │
│Factu │  │(fallback)│
│ra    │  └──────────┘
└──────┘
```

### Condiciones de Impresión

| Documento | Se Imprime Si... |
|-----------|------------------|
| **Factura** | PeriodoFacturacion = NRM **Y** Comentario contiene "factura física" o "albarán físico" **Y** Factura creada exitosamente |
| **Albarán (NRM)** | PeriodoFacturacion = NRM **Y** (MantenerJunto impide facturar **O** Error al crear factura) **Y** Comentario contiene palabras clave |
| **Albarán (FDM)** | PeriodoFacturacion = FDM **Y** Comentario contiene "factura física" o "albarán físico" |

### Palabras Clave de Impresión (case-insensitive, sin tildes)

- "factura física" o "factura fisica"
- "albarán físico" o "albaran fisico"

---

## Notas Finales

- **TDD:** Escribir tests PRIMERO, luego código
- **Commits frecuentes:** Después de cada test que pasa
- **Code review:** Revisar antes de merge a main
- **Backup:** Base de datos de producción antes de facturaciones masivas
- **⚠️ Importante:** Definir lógica de traspaso a empresa "3" antes de producción

---

**Fecha de creación:** 28 de octubre de 2025
**Última actualización:** 28 de octubre de 2025
**Autor:** Carlos (con asistencia de Claude Code)
**Versión:** 2.0 (Actualizada con validación MantenerJunto y traspaso empresa 3)
