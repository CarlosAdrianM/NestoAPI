# Estado: Refactorización SelectorDireccionEntrega
**Fecha:** 20 de Noviembre de 2024

---

## 📋 Contexto

El control `SelectorDireccionEntrega` se usa en **múltiples partes de la aplicación** y es **delicado de refactorizar**. Para hacerlo de forma segura, se planificó una refactorización en **5 fases** con tests.

**Ubicación:**
- Control: `Nesto/ControlesUsuario/SelectorDireccionEntrega/`
- Tests: `Nesto/ControlesUsuario.Tests/SelectorDireccionEntregaTests.cs`

---

## ✅ Trabajo Completado: FASES 1 y 2

### FASE 1: Tests de Caracterización (Documentales)

**Archivo:** `SelectorDireccionEntregaTests.cs`

Se escribieron **tests de caracterización** que documentan el comportamiento actual del control:

#### Tests Completados:

1. **Dependency Properties** (3 tests)
   - `AlCambiarEmpresa_LlamaCargarDatosDirectamente()` ✅
   - `AlCambiarCliente_UsaDebouncing()` ✅
   - `AlCambiarTotalPedido_LlamaCargarDatos()` ✅

2. **Sincronización Seleccionada ↔ DireccionCompleta** (2 tests)
   - `AlCambiarDireccionCompleta_ActualizaSeleccionada()` ✅
   - `AlCambiarSeleccionada_TrimmeaElValor()` ✅

3. **Event Subscriptions** (2 tests documentales)
   - `AlCargarse_SeSuscribeAClienteCreadoEvent()` ✅ (documental)
   - `AlDescargarse_SeDesuscribeDeClienteCreadoEvent()` ✅ (documental)

4. **ColeccionFiltrable** (2 tests)
   - `AlCrearse_InicializaColeccionFiltrable()` ✅
   - `AlSeleccionarElemento_ActualizaDireccionCompleta()` ✅ (documental)

5. **Configuración y Validaciones** (2 tests documentales)
   - `CargarDatos_RequiereConfiguracionEmpresaYCliente()` ✅ (documental)
   - `ConstructorSinParametros_PermiteInstanciacionParaXaml()` ✅

6. **Debouncing** (1 test documental)
   - `DebounceTimer_TieneDelay100Milisegundos()` ✅ (documental)

7. **Dirección Por Defecto** (2 tests documentales)
   - `AlCargarDatos_SeleccionaDireccionPorDefectoSiNoHaySeleccion()` ✅ (documental)
   - `AlCargarDatos_RespetaSeleccionExistente()` ✅ (documental)

**Total: 14 tests de caracterización**

### FASE 2: Entender el Comportamiento Actual

Se documentaron los comportamientos clave del control:

- **Carga de direcciones**: Cuando cambian Cliente/Empresa
- **Auto-selección**: Dirección por defecto (`esDireccionPorDefecto`)
- **Sincronización**: Entre `Seleccionada` (string contacto) y `DireccionCompleta` (objeto completo)
- **Debouncing**: DispatcherTimer de 100ms para cambios de Cliente
- **Eventos**: Suscripción a `ClienteCreadoEvent` y `ClienteModificadoEvent`

---

## 🚧 Problema Actual: Falta FASE 3 (Dependency Injection)

### Por Qué los Tests No Son Reales

Los tests actuales son mayormente **documentales** (solo `Assert.IsTrue(true, "comentario")`) porque **el control NO es testeable** en su forma actual.

### Causa Raíz: Service Locator Anti-Pattern

**Archivo:** `SelectorDireccionEntrega.xaml.cs` (líneas 35-65)

El constructor sin parámetros usa `ContainerLocator.Container.Resolve<>()`:

```csharp
public SelectorDireccionEntrega()
{
    InitializeComponent();
    // ...

    try
    {
        // ❌ SERVICE LOCATOR: Dificulta testing
        regionManager = ContainerLocator.Container.Resolve<IRegionManager>();
        eventAggregator = ContainerLocator.Container.Resolve<IEventAggregator>();
        _configuracion = ContainerLocator.Container.Resolve<IConfiguracion>();

        // ...
    }
    catch
    {
        // Se usa solo para poder testar controles que incluyan un SelectorDireccionEntrega
    }
}
```

### Problema Adicional: HttpClient Directo en cargarDatos()

**Archivo:** `SelectorDireccionEntrega.xaml.cs` (líneas 356-399)

El método `cargarDatos()` crea directamente un `HttpClient`:

```csharp
private async Task cargarDatos()
{
    // ...

    // ❌ NO TESTEABLE: Crea HttpClient directamente
    using (HttpClient client = new HttpClient())
    {
        client.BaseAddress = new Uri(Configuracion.servidorAPI);

        string urlConsulta = "PlantillaVentas/DireccionesEntrega?empresa=" + Empresa +
                             "&clienteDirecciones=" + Cliente;
        response = await client.GetAsync(urlConsulta);

        if (response.IsSuccessStatusCode)
        {
            string resultado = await response.Content.ReadAsStringAsync();
            listaDireccionesEntrega.ListaOriginal =
                new ObservableCollection<IFiltrableItem>(
                    JsonConvert.DeserializeObject<ObservableCollection<DireccionesEntregaCliente>>(resultado)
                );

            // Lógica de auto-selección
            // ...
        }
    }
}
```

**Por qué NO es testeable:**
1. ❌ No se puede mockear `HttpClient` fácilmente
2. ❌ Tests requieren API real corriendo (lentos, frágiles)
3. ❌ No se pueden simular errores HTTP
4. ❌ No se pueden testear casos edge sin datos reales

---

## 🎯 Solución Propuesta: FASE 3 - Refactorizar para DI Pura

### Estrategia de Refactorización

#### Paso 3.1: Crear Servicio de Direcciones

**NUEVO archivo:** `Nesto/ControlesUsuario/Services/IServicioDireccionesEntrega.cs`

```csharp
using ControlesUsuario.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ControlesUsuario.Services
{
    public interface IServicioDireccionesEntrega
    {
        /// <summary>
        /// Obtiene las direcciones de entrega para un cliente.
        /// </summary>
        /// <param name="empresa">Empresa del cliente</param>
        /// <param name="cliente">Número de cliente</param>
        /// <param name="totalPedido">Total del pedido (opcional)</param>
        /// <returns>Lista de direcciones de entrega</returns>
        Task<IEnumerable<DireccionesEntregaCliente>> ObtenerDireccionesEntrega(
            string empresa,
            string cliente,
            decimal? totalPedido = null
        );
    }
}
```

**NUEVO archivo:** `Nesto/ControlesUsuario/Services/ServicioDireccionesEntrega.cs`

```csharp
using ControlesUsuario.Models;
using Nesto.Infrastructure.Contracts;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace ControlesUsuario.Services
{
    public class ServicioDireccionesEntrega : IServicioDireccionesEntrega
    {
        private readonly IConfiguracion _configuracion;

        public ServicioDireccionesEntrega(IConfiguracion configuracion)
        {
            _configuracion = configuracion ?? throw new ArgumentNullException(nameof(configuracion));
        }

        public async Task<IEnumerable<DireccionesEntregaCliente>> ObtenerDireccionesEntrega(
            string empresa,
            string cliente,
            decimal? totalPedido = null)
        {
            if (string.IsNullOrWhiteSpace(empresa))
                throw new ArgumentException("Empresa es requerida", nameof(empresa));

            if (string.IsNullOrWhiteSpace(cliente))
                throw new ArgumentException("Cliente es requerido", nameof(cliente));

            using (HttpClient client = new HttpClient())
            {
                client.BaseAddress = new Uri(_configuracion.servidorAPI);

                string urlConsulta = $"PlantillaVentas/DireccionesEntrega?empresa={empresa}&clienteDirecciones={cliente}";

                if (totalPedido.HasValue && totalPedido.Value != 0)
                {
                    urlConsulta += $"&totalPedido={totalPedido.Value.ToString(CultureInfo.GetCultureInfo("en-US"))}";
                }

                var response = await client.GetAsync(urlConsulta);

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"Error al obtener direcciones de entrega: {response.StatusCode}");
                }

                string resultado = await response.Content.ReadAsStringAsync();
                var direcciones = JsonConvert.DeserializeObject<IEnumerable<DireccionesEntregaCliente>>(resultado);

                return direcciones ?? Enumerable.Empty<DireccionesEntregaCliente>();
            }
        }
    }
}
```

#### Paso 3.2: Refactorizar SelectorDireccionEntrega

**Modificar:** `SelectorDireccionEntrega.xaml.cs`

**Cambios en el constructor:**

```csharp
using ControlesUsuario.Services; // ✨ NUEVO

public partial class SelectorDireccionEntrega : UserControl, INotifyPropertyChanged
{
    private readonly IRegionManager regionManager;
    private readonly IEventAggregator eventAggregator;
    private readonly IConfiguracion _configuracion;
    private readonly IServicioDireccionesEntrega _servicioDirecciones; // ✨ NUEVO
    private DispatcherTimer timer;

    // ❌ DEPRECAR: Constructor sin parámetros (solo para XAML legacy)
    public SelectorDireccionEntrega()
    {
        InitializeComponent();
        GridPrincipal.DataContext = this;

        listaDireccionesEntrega = new();
        listaDireccionesEntrega.TieneDatosIniciales = true;
        listaDireccionesEntrega.VaciarAlSeleccionar = false;
        listaDireccionesEntrega.SeleccionarPrimerElemento = false;

        try
        {
            regionManager = ContainerLocator.Container.Resolve<IRegionManager>();
            eventAggregator = ContainerLocator.Container.Resolve<IEventAggregator>();
            _configuracion = ContainerLocator.Container.Resolve<IConfiguracion>();
            _servicioDirecciones = ContainerLocator.Container.Resolve<IServicioDireccionesEntrega>(); // ✨ NUEVO

            ConfigurarEventHandlers();
        }
        catch
        {
            // Se usa solo para poder testar controles que incluyan un SelectorDireccionEntrega
        }
    }

    // ✅ CONSTRUCTOR PRINCIPAL (para DI y tests)
    public SelectorDireccionEntrega(
        IRegionManager regionManager,
        IEventAggregator eventAggregator,
        IConfiguracion configuracion,
        IServicioDireccionesEntrega servicioDirecciones) // ✨ NUEVO parámetro
    {
        InitializeComponent();
        GridPrincipal.DataContext = this;

        listaDireccionesEntrega = new();
        listaDireccionesEntrega.TieneDatosIniciales = true;
        listaDireccionesEntrega.VaciarAlSeleccionar = false;

        this.regionManager = regionManager;
        this.eventAggregator = eventAggregator;
        this._configuracion = configuracion;
        this._servicioDirecciones = servicioDirecciones; // ✨ NUEVO

        ConfigurarEventHandlers();
    }

    // ✨ NUEVO: Método para evitar duplicar código
    private void ConfigurarEventHandlers()
    {
        listaDireccionesEntrega.ElementoSeleccionadoChanged += (sender, args) =>
        {
            if (listaDireccionesEntrega is not null &&
                listaDireccionesEntrega.ElementoSeleccionado is not null &&
                DireccionCompleta != listaDireccionesEntrega.ElementoSeleccionado)
            {
                this.SetValue(DireccionCompletaProperty,
                    listaDireccionesEntrega.ElementoSeleccionado as DireccionesEntregaCliente);
            }
        };
    }
}
```

**Refactorizar cargarDatos():**

```csharp
private async Task cargarDatos()
{
    // Validaciones
    if (_servicioDirecciones == null)
    {
        // Modo degradado: no hay servicio inyectado
        return;
    }

    if (Empresa == null || Cliente == null)
    {
        return;
    }

    try
    {
        // ✨ USAR SERVICIO en lugar de HttpClient directo
        var direcciones = await _servicioDirecciones.ObtenerDireccionesEntrega(
            Empresa,
            Cliente,
            TotalPedido != 0 ? TotalPedido : (decimal?)null
        );

        // Actualizar lista
        listaDireccionesEntrega.ListaOriginal =
            new ObservableCollection<IFiltrableItem>(direcciones);

        // Lógica de auto-selección (sin cambios)
        if (DireccionCompleta == null && Seleccionada != null)
        {
            DireccionCompleta = (DireccionesEntregaCliente)listaDireccionesEntrega.Lista
                .SingleOrDefault(l => (l as DireccionesEntregaCliente).contacto == Seleccionada);
        }

        if (DireccionCompleta == null && Seleccionada == null)
        {
            DireccionCompleta = (DireccionesEntregaCliente)listaDireccionesEntrega.Lista
                .SingleOrDefault(l => (l as DireccionesEntregaCliente).esDireccionPorDefecto);
        }
    }
    catch (Exception ex)
    {
        throw new Exception($"No se pudieron leer las direcciones de entrega: {ex.Message}", ex);
    }
}
```

#### Paso 3.3: Registrar Servicio en el Container

**Modificar:** `Nesto/Bootstrapper.cs` (o donde se registren los servicios)

```csharp
using ControlesUsuario.Services;

protected override void RegisterTypes(IContainerRegistry containerRegistry)
{
    // ... otros registros ...

    // ✨ NUEVO: Registrar servicio de direcciones
    containerRegistry.RegisterSingleton<IServicioDireccionesEntrega, ServicioDireccionesEntrega>();
}
```

---

## 🧪 FASE 4: Tests Reales con Mocks (PENDIENTE)

Una vez completada la FASE 3, se podrán escribir tests reales:

### Test 4.1: Cargar direcciones con mock

```csharp
[TestMethod]
public async Task CargarDatos_ConEmpresaYCliente_CargaDireccionesDesdeServicio()
{
    // Arrange
    var configuracion = A.Fake<IConfiguracion>();
    var eventAggregator = A.Fake<IEventAggregator>();
    var regionManager = A.Fake<IRegionManager>();
    var servicioMock = A.Fake<IServicioDireccionesEntrega>();

    var direccionesEsperadas = new List<DireccionesEntregaCliente>
    {
        new DireccionesEntregaCliente { contacto = "0", nombre = "Dirección 1" },
        new DireccionesEntregaCliente { contacto = "5", nombre = "Dirección 2" }
    };

    A.CallTo(() => servicioMock.ObtenerDireccionesEntrega("1", "10", null))
        .Returns(Task.FromResult<IEnumerable<DireccionesEntregaCliente>>(direccionesEsperadas));

    var sut = new SelectorDireccionEntrega(regionManager, eventAggregator, configuracion, servicioMock);

    // Act
    sut.Empresa = "1";
    sut.Cliente = "10";
    await Task.Delay(150); // Esperar debouncing + carga

    // Assert
    A.CallTo(() => servicioMock.ObtenerDireccionesEntrega("1", "10", null))
        .MustHaveHappened();
    Assert.AreEqual(2, sut.listaDireccionesEntrega.ListaOriginal.Count);
}
```

### Test 4.2: Auto-selección de dirección por defecto

```csharp
[TestMethod]
public async Task CargarDatos_SinSeleccionPrevia_SeleccionaDireccionPorDefecto()
{
    // Arrange
    var servicioMock = A.Fake<IServicioDireccionesEntrega>();

    var direcciones = new List<DireccionesEntregaCliente>
    {
        new DireccionesEntregaCliente
        {
            contacto = "0",
            nombre = "Principal",
            esDireccionPorDefecto = true
        },
        new DireccionesEntregaCliente
        {
            contacto = "5",
            nombre = "Secundaria",
            esDireccionPorDefecto = false
        }
    };

    A.CallTo(() => servicioMock.ObtenerDireccionesEntrega(A<string>._, A<string>._, A<decimal?>._))
        .Returns(Task.FromResult<IEnumerable<DireccionesEntregaCliente>>(direcciones));

    var sut = new SelectorDireccionEntrega(
        A.Fake<IRegionManager>(),
        A.Fake<IEventAggregator>(),
        A.Fake<IConfiguracion>(),
        servicioMock
    );

    // Act
    sut.Empresa = "1";
    sut.Cliente = "10";
    await Task.Delay(150);

    // Assert
    Assert.IsNotNull(sut.DireccionCompleta);
    Assert.AreEqual("0", sut.DireccionCompleta.contacto);
    Assert.AreEqual("Principal", sut.DireccionCompleta.nombre);
}
```

### Test 4.3: Manejo de errores HTTP

```csharp
[TestMethod]
[ExpectedException(typeof(Exception))]
public async Task CargarDatos_CuandoServicioFalla_LanzaExcepcion()
{
    // Arrange
    var servicioMock = A.Fake<IServicioDireccionesEntrega>();

    A.CallTo(() => servicioMock.ObtenerDireccionesEntrega(A<string>._, A<string>._, A<decimal?>._))
        .Throws(new Exception("Error al obtener direcciones de entrega: 500"));

    var sut = new SelectorDireccionEntrega(
        A.Fake<IRegionManager>(),
        A.Fake<IEventAggregator>(),
        A.Fake<IConfiguracion>(),
        servicioMock
    );

    // Act
    sut.Empresa = "1";
    sut.Cliente = "10";
    await Task.Delay(150); // Debería lanzar excepción

    // Assert: ExpectedException
}
```

---

## 🏗️ FASE 5: Refactorizar Control (PENDIENTE)

Una vez que el control sea testeable (FASE 4 completa), se pueden hacer refactorizaciones adicionales con confianza:

### Posibles Mejoras:

1. **Extraer ViewModel**: Separar lógica de negocio de la UI
2. **Eliminar debouncing manual**: Usar reactive extensions (Rx)
3. **Mejorar manejo de errores**: Notificar al usuario de errores HTTP
4. **Optimizar auto-selección**: Simplificar lógica con LINQ
5. **Añadir logging**: Para debugging de comportamiento
6. **Property validation**: Validar Empresa/Cliente antes de cargar

---

## 📊 Resumen de Fases

| Fase | Descripción | Estado |
|------|-------------|--------|
| **FASE 1** | Escribir tests de caracterización (documentales) | ✅ **COMPLETADO** (14 tests) |
| **FASE 2** | Documentar comportamiento actual del control | ✅ **COMPLETADO** |
| **FASE 3** | Refactorizar para DI pura (extraer servicio HTTP) | ⏳ **PENDIENTE** |
| **FASE 4** | Escribir tests reales con mocks | ⏳ **PENDIENTE** (bloqueada por FASE 3) |
| **FASE 5** | Refactorizar control (mejoras adicionales) | ⏳ **PENDIENTE** (bloqueada por FASE 4) |

---

## 🎯 Próximos Pasos

### Paso Inmediato: Empezar FASE 3

1. **Crear servicio de direcciones**:
   - [ ] Crear `IServicioDireccionesEntrega.cs`
   - [ ] Crear `ServicioDireccionesEntrega.cs`
   - [ ] Agregar tests unitarios para el servicio

2. **Refactorizar control**:
   - [ ] Modificar constructores para aceptar `IServicioDireccionesEntrega`
   - [ ] Extraer método `ConfigurarEventHandlers()`
   - [ ] Refactorizar `cargarDatos()` para usar servicio

3. **Registrar en DI**:
   - [ ] Registrar servicio en Bootstrapper

4. **Verificar no hay regresiones**:
   - [ ] Compilar solución
   - [ ] Ejecutar tests existentes
   - [ ] Prueba manual en DetallePedidoVenta
   - [ ] Prueba manual en PlantillaVenta

---

## ⚠️ Riesgos y Precauciones

### Riesgo 1: Constructor Sin Parámetros Deja de Funcionar

**Problema**: XAML todavía usa constructor sin parámetros en algunos lugares.

**Mitigación**:
- Mantener constructor sin parámetros funcionando (usa Service Locator)
- Registrar servicio en el container para que `Resolve<>()` funcione
- Documentar que el constructor con DI es el preferido

### Riesgo 2: Cambio Rompe Otros Controles

**Problema**: Otros controles pueden depender del comportamiento actual.

**Mitigación**:
- Tests de caracterización protegen contra regresiones
- Hacer cambios incrementales (servicio primero, luego control)
- Probar manualmente en todos los formularios que usan el control

### Riesgo 3: Performance de Tests

**Problema**: Tests con mocks pueden ser más lentos que tests documentales.

**Mitigación**:
- Usar `[TestCategory]` para separar tests rápidos de lentos
- Ejecutar tests en paralelo donde sea posible
- Mantener tests unitarios del servicio separados de tests del control

---

## 📚 Referencias

### Archivos Clave

**Control:**
- `Nesto/ControlesUsuario/SelectorDireccionEntrega/SelectorDireccionEntrega.xaml.cs`
- `Nesto/ControlesUsuario/SelectorDireccionEntrega/SelectorDireccionEntrega.xaml`
- `Nesto/ControlesUsuario/SelectorDireccionEntrega/SelectorDireccionEntregaModel.cs`

**Tests:**
- `Nesto/ControlesUsuario.Tests/SelectorDireccionEntregaTests.cs`

**Usos del Control:**
- `Nesto/Modulos/PedidoVenta/PedidoVenta/Views/DetallePedidoView.xaml` (línea 174)
- `Nesto/Modulos/PlantillaVenta/Views/PlantillaVentaView.xaml` (buscar SelectorDireccionEntrega)
- Otros formularios (buscar referencias)

### Patrones Aplicados

- **Tests de Caracterización**: Documentar comportamiento antes de refactorizar
- **Dependency Injection**: Inyectar dependencias en lugar de crearlas
- **Service Pattern**: Extraer lógica HTTP a servicio reutilizable
- **Test-Driven Refactoring**: Red-Green-Refactor con seguridad

---

**Autor:** Claude Code (Anthropic)
**Fecha:** 20 de Noviembre de 2024
**Estado:** 📋 Documento de estado - FASES 1-2 completadas, FASE 3 pendiente
**Contexto:** Refactorización en curso de SelectorDireccionEntrega para hacerlo testeable
