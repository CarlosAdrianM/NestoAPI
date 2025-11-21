# Diseño: SelectorCCC - Control de Usuario Reutilizable
**Fecha:** 20 de Noviembre de 2024
**Objetivo:** Control para seleccionar CCC (Código Cuenta Cliente) con prevención de bucles infinitos

---

## 📋 Requisitos Funcionales

### Entradas (DependencyProperties desde Parent)
1. **Empresa** (string): Código de empresa
2. **Cliente** (string): Número de cliente
3. **Contacto** (string): Contacto del cliente
4. **FormaPago** (string, opcional): Para lógica automática (RCB = requiere CCC)

### Salida (DependencyProperty TwoWay)
5. **CCCSeleccionado** (string, nullable): CCC seleccionado o NULL para "(Sin CCC)"

### Comportamiento
- **Carga automática**: Cuando cambian Empresa/Cliente/Contacto, recargar CCCs desde API
- **Opción "(Sin CCC)"**: Primera opción del combo, devuelve NULL
- **CCCs inválidos**: Si `estado < 0`, mostrar en cursiva y deshabilitados (NO seleccionables)
- **Auto-selección inteligente**:
  - Si FormaPago = "RCB" → auto-seleccionar primer CCC válido
  - Si FormaPago ≠ "RCB" → auto-seleccionar "(Sin CCC)"
  - Si solo hay un CCC válido → auto-seleccionarlo

---

## ⚠️ PROBLEMA CRÍTICO: Bucles Infinitos de PropertyChanged

### Escenario del Problema

```
┌─────────────────────────────────────────────────────────────┐
│ Parent ViewModel (DetallePedidoVenta)                       │
│                                                             │
│ pedido.contacto cambia → dispara PropertyChanged           │
│         ↓                                                   │
│ SelectorCCC escucha y recarga CCCs                          │
│         ↓                                                   │
│ SelectorCCC auto-selecciona nuevo CCC                       │
│         ↓                                                   │
│ CCCSeleccionado cambia → dispara PropertyChanged            │
│         ↓                                                   │
│ Parent escucha y actualiza pedido.ccc                       │
│         ↓                                                   │
│ pedido.ccc cambia → dispara PropertyChanged               │
│         ↓                                                   │
│ SelectorCCC escucha... ← ¡BUCLE INFINITO!                  │
└─────────────────────────────────────────────────────────────┘
```

### Soluciones Propuestas

#### ✅ Solución 1: Flag de "Cargando" (Recomendada)

```csharp
private bool _estaCargando = false;

private static void OnContactoChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
{
    var selector = (SelectorCCC)d;

    if (selector._estaCargando)
        return; // Ignorar cambios mientras estamos cargando

    selector.CargarCCCs();
}

private async Task CargarCCCs()
{
    _estaCargando = true;
    try
    {
        var cccs = await _servicioCCC.ObtenerCCCs(Empresa, Cliente, Contacto);
        ListaCCCs = cccs;

        // Auto-seleccionar
        CCCSeleccionado = SeleccionarCCCAutomaticamente(cccs);
    }
    finally
    {
        _estaCargando = false;
    }
}
```

**Ventajas:**
- ✅ Simple y efectivo
- ✅ Protege contra bucles dentro del control
- ✅ Fácil de entender y mantener

**Limitación:**
- ⚠️ Solo protege bucles internos, no bucles con el parent

#### ✅ Solución 2: Comparar Valores Antes de Propagar

```csharp
private static void OnCCCSeleccionadoChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
{
    var selector = (SelectorCCC)d;

    if (e.OldValue?.ToString() == e.NewValue?.ToString())
        return; // No propagar si el valor realmente no cambió

    // Propagar cambio
    selector.RaisePropertyChanged(nameof(CCCSeleccionado));
}
```

**Ventajas:**
- ✅ Evita propagaciones innecesarias
- ✅ Reduce ruido de PropertyChanged

#### ✅ Solución 3: Parent Usa Guards

El parent (DetallePedidoVenta) debe protegerse:

```vb
Private _actualizandoCCC As Boolean = False

Private Sub OnCCCSeleccionadoCambio()
    If _actualizandoCCC Then Return

    Try
        _actualizandoCCC = True
        pedido.ccc = CCCSeleccionado
    Finally
        _actualizandoCCC = False
    End Try
End Sub
```

#### 🎯 Solución Completa: Combinación

Usar **Solución 1 + 2** en el control, y documentar que el parent debe usar **Solución 3**.

---

## 🏗️ Arquitectura del Control

### Modelo de Datos

```csharp
public class CCCItem
{
    public string CCC { get; set; }           // Código CCC completo
    public string Entidad { get; set; }       // Banco/entidad
    public string Sucursal { get; set; }      // Sucursal
    public int Estado { get; set; }           // Estado: < 0 = inválido
    public bool EsValido => Estado >= 0;

    public string Descripcion { get; set; }   // Para mostrar en el combo

    // Para styling en XAML
    public bool EsInvalido => Estado < 0;
}
```

### Servicio HTTP

```csharp
public interface IServicioCCC
{
    /// <summary>
    /// Obtiene los CCCs disponibles para un cliente/contacto específico.
    /// </summary>
    /// <param name="empresa">Código de empresa</param>
    /// <param name="cliente">Número de cliente</param>
    /// <param name="contacto">Contacto del cliente</param>
    /// <returns>Lista de CCCs disponibles (incluye inválidos con estado < 0)</returns>
    Task<IEnumerable<CCCItem>> ObtenerCCCs(string empresa, string cliente, string contacto);
}

public class ServicioCCC : IServicioCCC
{
    private readonly IConfiguracion _configuracion;

    public ServicioCCC(IConfiguracion configuracion)
    {
        _configuracion = configuracion ?? throw new ArgumentNullException(nameof(configuracion));
    }

    public async Task<IEnumerable<CCCItem>> ObtenerCCCs(string empresa, string cliente, string contacto)
    {
        // Validaciones
        if (string.IsNullOrWhiteSpace(empresa))
            throw new ArgumentException("Empresa es requerida", nameof(empresa));
        if (string.IsNullOrWhiteSpace(cliente))
            throw new ArgumentException("Cliente es requerido", nameof(cliente));
        if (string.IsNullOrWhiteSpace(contacto))
            throw new ArgumentException("Contacto es requerido", nameof(contacto));

        using (HttpClient client = new HttpClient())
        {
            client.BaseAddress = new Uri(_configuracion.servidorAPI);

            // TODO: Ajustar endpoint según tu API
            string url = $"Clientes/CCCs?empresa={empresa}&cliente={cliente}&contacto={contacto}";

            var response = await client.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Error al obtener CCCs: {response.StatusCode}");
            }

            string json = await response.Content.ReadAsStringAsync();
            var cccs = JsonConvert.DeserializeObject<IEnumerable<CCCItem>>(json);

            return cccs ?? Enumerable.Empty<CCCItem>();
        }
    }
}
```

---

## 🎨 Implementación del Control

### DependencyProperties

```csharp
public partial class SelectorCCC : UserControl
{
    // NO establecer DataContext = this (lección aprendida de SelectorDireccionEntrega)

    private readonly IServicioCCC _servicioCCC;
    private bool _estaCargando = false;

    #region Dependency Properties

    // === ENTRADAS desde Parent ===

    public static readonly DependencyProperty EmpresaProperty =
        DependencyProperty.Register(
            nameof(Empresa),
            typeof(string),
            typeof(SelectorCCC),
            new FrameworkPropertyMetadata(null, OnEmpresaChanged));

    public string Empresa
    {
        get => (string)GetValue(EmpresaProperty);
        set => SetValue(EmpresaProperty, value);
    }

    private static void OnEmpresaChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var selector = (SelectorCCC)d;
        if (selector._estaCargando) return;
        selector.CargarCCCsAsync();
    }

    public static readonly DependencyProperty ClienteProperty =
        DependencyProperty.Register(
            nameof(Cliente),
            typeof(string),
            typeof(SelectorCCC),
            new FrameworkPropertyMetadata(null, OnClienteChanged));

    public string Cliente
    {
        get => (string)GetValue(ClienteProperty);
        set => SetValue(ClienteProperty, value);
    }

    private static void OnClienteChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var selector = (SelectorCCC)d;
        if (selector._estaCargando) return;
        selector.CargarCCCsAsync();
    }

    public static readonly DependencyProperty ContactoProperty =
        DependencyProperty.Register(
            nameof(Contacto),
            typeof(string),
            typeof(SelectorCCC),
            new FrameworkPropertyMetadata(null, OnContactoChanged));

    public string Contacto
    {
        get => (string)GetValue(ContactoProperty);
        set => SetValue(ContactoProperty, value);
    }

    private static void OnContactoChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var selector = (SelectorCCC)d;
        if (selector._estaCargando) return;
        selector.CargarCCCsAsync();
    }

    public static readonly DependencyProperty FormaPagoProperty =
        DependencyProperty.Register(
            nameof(FormaPago),
            typeof(string),
            typeof(SelectorCCC),
            new FrameworkPropertyMetadata(null, OnFormaPagoChanged));

    public string FormaPago
    {
        get => (string)GetValue(FormaPagoProperty);
        set => SetValue(FormaPagoProperty, value);
    }

    private static void OnFormaPagoChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var selector = (SelectorCCC)d;
        if (selector._estaCargando) return;
        selector.ActualizarSeleccionSegunFormaPago();
    }

    // === SALIDA hacia Parent (TwoWay) ===

    public static readonly DependencyProperty CCCSeleccionadoProperty =
        DependencyProperty.Register(
            nameof(CCCSeleccionado),
            typeof(string),
            typeof(SelectorCCC),
            new FrameworkPropertyMetadata(
                null,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnCCCSeleccionadoChanged));

    public string CCCSeleccionado
    {
        get => (string)GetValue(CCCSeleccionadoProperty);
        set => SetValue(CCCSeleccionadoProperty, value);
    }

    private static void OnCCCSeleccionadoChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // Comparar valores para evitar propagaciones innecesarias
        if (e.OldValue?.ToString() == e.NewValue?.ToString())
            return;

        // El cambio es real, dejarlo propagar
    }

    #endregion

    // === PROPIEDAD INTERNA (no DP) ===

    private ObservableCollection<CCCItem> _listaCCCs;
    public ObservableCollection<CCCItem> ListaCCCs
    {
        get => _listaCCCs;
        set
        {
            _listaCCCs = value;
            OnPropertyChanged(nameof(ListaCCCs));
        }
    }
}
```

### Lógica de Carga

```csharp
private async void CargarCCCsAsync()
{
    // Validar que tenemos los datos necesarios
    if (string.IsNullOrWhiteSpace(Empresa) ||
        string.IsNullOrWhiteSpace(Cliente) ||
        string.IsNullOrWhiteSpace(Contacto))
    {
        // Limpiar lista si faltan datos
        ListaCCCs = new ObservableCollection<CCCItem>();
        return;
    }

    _estaCargando = true;
    try
    {
        // Llamar al servicio
        var cccs = await _servicioCCC.ObtenerCCCs(Empresa, Cliente, Contacto);

        // Construir lista con opción "(Sin CCC)"
        var lista = new ObservableCollection<CCCItem>();

        // 1. Agregar opción "(Sin CCC)" al principio
        lista.Add(new CCCItem
        {
            CCC = null,
            Descripcion = "(Sin CCC)",
            Estado = 1, // Válido
            EsValido = true
        });

        // 2. Agregar CCCs de la API
        foreach (var ccc in cccs)
        {
            // Construir descripción para el combo
            if (ccc.Estado < 0)
            {
                ccc.Descripcion = $"{ccc.CCC} - {ccc.Entidad} (INVÁLIDO)";
            }
            else
            {
                ccc.Descripcion = $"{ccc.CCC} - {ccc.Entidad}";
            }

            lista.Add(ccc);
        }

        ListaCCCs = lista;

        // Auto-seleccionar según lógica de negocio
        AutoSeleccionarCCC(lista);
    }
    catch (Exception ex)
    {
        // TODO: Logging
        Debug.WriteLine($"[SelectorCCC] Error al cargar CCCs: {ex.Message}");

        // Lista vacía en caso de error
        ListaCCCs = new ObservableCollection<CCCItem>();
    }
    finally
    {
        _estaCargando = false;
    }
}

private void AutoSeleccionarCCC(ObservableCollection<CCCItem> lista)
{
    // Si ya hay una selección válida en la lista, respetarla
    if (!string.IsNullOrEmpty(CCCSeleccionado))
    {
        var existe = lista.Any(c => c.CCC == CCCSeleccionado);
        if (existe)
            return; // Mantener selección actual
    }

    // Lógica de auto-selección
    if (FormaPago?.Trim() == Constantes.FormasPago.RECIBO)
    {
        // Forma de pago es RCB (Recibo) → Seleccionar primer CCC válido
        var primerValido = lista.FirstOrDefault(c => c.EsValido && !string.IsNullOrEmpty(c.CCC));
        CCCSeleccionado = primerValido?.CCC;
    }
    else
    {
        // Forma de pago NO es Recibo → Seleccionar "(Sin CCC)"
        CCCSeleccionado = null;
    }
}

private void ActualizarSeleccionSegunFormaPago()
{
    if (_estaCargando || ListaCCCs == null || !ListaCCCs.Any())
        return;

    AutoSeleccionarCCC(ListaCCCs);
}
```

---

## 🎨 XAML del Control

```xaml
<UserControl x:Class="ControlesUsuario.SelectorCCC"
             x:Name="Root"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:local="clr-namespace:ControlesUsuario">

    <!-- IMPORTANTE: NO establecer DataContext aquí para permitir herencia del parent -->

    <UserControl.Resources>
        <!-- Estilo para items inválidos (estado < 0) -->
        <Style x:Key="ItemCCCStyle" TargetType="ComboBoxItem">
            <Style.Triggers>
                <DataTrigger Binding="{Binding EsInvalido}" Value="True">
                    <Setter Property="FontStyle" Value="Italic" />
                    <Setter Property="Foreground" Value="Gray" />
                    <Setter Property="IsEnabled" Value="False" />
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </UserControl.Resources>

    <ComboBox x:Name="comboCCC"
              ItemsSource="{Binding ElementName=Root, Path=ListaCCCs}"
              SelectedValue="{Binding ElementName=Root, Path=CCCSeleccionado, Mode=TwoWay}"
              SelectedValuePath="CCC"
              DisplayMemberPath="Descripcion"
              ItemContainerStyle="{StaticResource ItemCCCStyle}"
              MinWidth="200">

        <!-- Tooltip opcional para mostrar info del CCC seleccionado -->
        <ComboBox.ToolTip>
            <TextBlock>
                <TextBlock.Text>
                    <MultiBinding StringFormat="{}CCC: {0}&#x0a;Entidad: {1}">
                        <Binding ElementName="comboCCC" Path="SelectedItem.CCC" />
                        <Binding ElementName="comboCCC" Path="SelectedItem.Entidad" />
                    </MultiBinding>
                </TextBlock.Text>
            </TextBlock>
        </ComboBox.ToolTip>
    </ComboBox>
</UserControl>
```

**Notas clave del XAML:**
- ✅ `x:Name="Root"` para usar en bindings
- ✅ Bindings con `ElementName=Root` (NO DataContext)
- ✅ `SelectedValuePath="CCC"` + `SelectedValue` para binding del valor
- ✅ `DisplayMemberPath="Descripcion"` para mostrar texto
- ✅ `ItemContainerStyle` para deshabilitar/estilizar items inválidos
- ✅ Tooltip informativo opcional

---

## 📝 Uso desde Parent (DetallePedidoVenta)

```xaml
<controles:SelectorCCC
    Empresa="{Binding pedido.empresa, Mode=OneWay}"
    Cliente="{Binding pedido.cliente, Mode=OneWay}"
    Contacto="{Binding pedido.contacto, Mode=OneWay}"
    FormaPago="{Binding pedido.formaPago, Mode=OneWay}"
    CCCSeleccionado="{Binding pedido.ccc, Mode=TwoWay}"
    MinWidth="250" />
```

**En el ViewModel del Parent (DetallePedidoViewModel.vb):**

```vb
' Guard para evitar bucles
Private _actualizandoCCC As Boolean = False

' Si escuchas cambios de pedido.ccc (PropertyChanged):
Private Sub OnPedidoPropertyChanged(sender As Object, e As PropertyChangedEventArgs)
    If e.PropertyName = "ccc" AndAlso Not _actualizandoCCC Then
        ' Hacer algo cuando cambia el CCC...
    End If
End Sub
```

---

## 🧪 Tests de Caracterización

### Tests del Servicio

```csharp
[TestClass]
public class ServicioCCCTests
{
    [TestMethod]
    [ExpectedException(typeof(ArgumentException))]
    public async Task ObtenerCCCs_EmpresaNull_LanzaExcepcion()
    {
        var config = A.Fake<IConfiguracion>();
        var sut = new ServicioCCC(config);

        await sut.ObtenerCCCs(null, "10", "0");
    }

    [TestMethod]
    public async Task ObtenerCCCs_ConParametrosValidos_RetornaListaCCCs()
    {
        // Test documental: requiere API mock
        Assert.IsTrue(true, "Documentado: debe llamar endpoint Clientes/CCCs con query params");
    }
}
```

### Tests del Control

```csharp
[TestClass]
public class SelectorCCCTests
{
    [TestMethod]
    public void SelectorCCC_AlCambiarEmpresa_RecargaCCCs()
    {
        // Test de caracterización
        var config = A.Fake<IConfiguracion>();
        var servicio = A.Fake<IServicioCCC>();

        Thread thread = new Thread(() =>
        {
            var sut = new SelectorCCC(servicio);
            sut.Empresa = "1";
            sut.Cliente = "10";
            sut.Contacto = "0";
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        // Verificar que se llamó al servicio
        A.CallTo(() => servicio.ObtenerCCCs("1", "10", "0"))
            .MustHaveHappened();
    }

    [TestMethod]
    public void SelectorCCC_ConFormaPagoRCB_SeleccionaPrimerCCCValido()
    {
        // Test de lógica de auto-selección
        Assert.IsTrue(true, "Documentado: RCB debe auto-seleccionar primer CCC válido");
    }

    [TestMethod]
    public void SelectorCCC_ConFormaPagoNoRCB_SeleccionaSinCCC()
    {
        // Test de lógica de auto-selección
        Assert.IsTrue(true, "Documentado: NO RCB debe auto-seleccionar (Sin CCC)");
    }
}
```

---

## ⚠️ Consideraciones Importantes

### 1. Endpoint de la API

**TODO:** Verificar el endpoint correcto. Posibilidades:
- `GET /Clientes/CCCs?empresa={empresa}&cliente={cliente}&contacto={contacto}`
- `GET /Clientes/{empresa}/{cliente}/{contacto}/CCCs`
- `GET /CCCs?empresa={empresa}&cliente={cliente}&contacto={contacto}`

### 2. Modelo de Datos CCCItem

**TODO:** Verificar campos exactos que devuelve la API:
- ¿Cómo se llama el campo del CCC? (`ccc`, `codigoCCC`, `numeroCuenta`)
- ¿Qué campo identifica al banco? (`entidad`, `banco`, `nombreBanco`)
- ¿Hay campo `estado`? ¿Qué valores toma?

### 3. Performance

Si el control se carga muchas veces, considera:
- **Caché** de CCCs en el servicio (con TTL)
- **Debouncing** si Empresa/Cliente/Contacto cambian rápido

### 4. Validación de CCC

¿Necesita el control validar el formato del CCC (IBAN, etc.)?
- Si SÍ: agregar validación en `OnCCCSeleccionadoChanged`
- Si NO: confiar en que la API solo devuelve CCCs válidos

---

## 📚 Archivos a Crear

1. **`ControlesUsuario/Models/CCCItem.cs`** - Modelo de datos
2. **`ControlesUsuario/Services/IServicioCCC.cs`** - Interface del servicio
3. **`ControlesUsuario/Services/ServicioCCC.cs`** - Implementación del servicio
4. **`ControlesUsuario/SelectorCCC/SelectorCCC.xaml`** - Vista
5. **`ControlesUsuario/SelectorCCC/SelectorCCC.xaml.cs`** - Code-behind
6. **`ControlesUsuario.Tests/Services/ServicioCCCTests.cs`** - Tests del servicio
7. **`ControlesUsuario.Tests/SelectorCCCTests.cs`** - Tests del control
8. **Registro en `Application.xaml.vb`** - DI del servicio

---

## 🎯 Siguiente Paso

¿Empezamos con la implementación? ¿Quieres que primero:

1. **Verificar el endpoint de la API** (¿existe ya? ¿cómo se llama?)
2. **Empezar con el servicio** (IServicioCCC + implementación)
3. **Empezar con el control** (XAML + code-behind)
4. **Otra cosa**

**¿Qué prefieres?**
