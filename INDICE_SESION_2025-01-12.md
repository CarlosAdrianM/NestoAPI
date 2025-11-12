# Índice - Sesión 12 Enero 2025: Fix Facturación Rutas

**Resumen:** Solución de 3 problemas críticos en facturación de rutas y error NotasEntrega

---

## 📚 Documentación Principal

### Resumen Ejecutivo (EMPEZAR AQUÍ)
- **`RESUMEN_SESION_2025-01-12.md`** ⭐
  - Resumen ejecutivo de la sesión
  - Estado final de todos los componentes
  - Plan de pruebas para mañana
  - Métricas y lecciones aprendidas

### Documentación Técnica Detallada
- **`SESION_FACTURACION_RUTAS_FIX_MANTENER_JUNTO_Y_NOTASENTREGA.md`** 📖
  - Descripción completa de los 3 problemas
  - Causas raíz y soluciones implementadas
  - Código modificado con explicaciones
  - Tests implementados
  - Referencias cruzadas

- **`SOLUCION_NOTASENTREGA_PRIMARY_KEY.md`** 🔍
  - Análisis profundo del error "NotaEntrega is not part of the model"
  - Proceso completo de solución en 5 fases
  - Advertencias sobre Database First vs Code First
  - Instrucciones de verificación

### Documentación de Soporte
- **`INSTRUCCIONES_ACTUALIZAR_EDMX.md`**
  - Guía paso a paso para actualizar EDMX en Visual Studio
  - Troubleshooting de problemas comunes
  - Opciones A y B según tipo de error

---

## 🗄️ Scripts SQL

### Scripts de Solución
- **`FIX_NOTAENTREGA_TABLE.sql`** ✅
  - Agrega PRIMARY KEY a tabla NotasEntrega
  - Safe: Solo agrega constraint, no modifica datos
  - Ejecutar UNA VEZ en producción

### Scripts de Verificación
- **`VERIFICAR_ANTES_DE_FIX_NOTASENTREGA.sql`** 🔍
  - EJECUTAR PRIMERO antes de aplicar FIX
  - Verifica si ya existe PRIMARY KEY
  - Detecta duplicados
  - Valida que no hay NULLs
  - Da luz verde o advierte sobre problemas

- **`LIMPIAR_DUPLICADOS_NOTASENTREGA.sql`** 🧹
  - Solo ejecutar SI VERIFICAR detectó duplicados
  - Usa transacciones para seguridad
  - Permite ROLLBACK si algo sale mal

---

## 🐍 Scripts Python

### Scripts de Automatización EDMX
- **`limpiar_edmx.py`** 🧹
  - Elimina todas las referencias de NotasEntrega del EDMX
  - Crea backup automático
  - Ejecutado: ✅ Completado

- **`renombrar_en_edmx.py`** ✏️
  - Renombra EntityType de NotasEntrega a NotaEntrega
  - Mantiene EntitySet en plural (NotasEntregas)
  - Crea backup automático
  - Ejecutado: ✅ Completado

- **`renombrar_propiedad_numero.py`** ✏️
  - Renombra propiedad NotaEntrega a Numero
  - Evita conflicto: clase no puede tener propiedad con mismo nombre
  - Mantiene mapping correcto a columna SQL
  - Crea backup automático
  - Ejecutado: ✅ Completado

---

## 💻 Scripts PowerShell

- **`forzar_regeneracion_edmx.ps1`** 🔄
  - Actualiza timestamps de archivos .tt
  - Fuerza regeneración de archivos C# desde EDMX
  - Ejecutado: ✅ Completado

- **`LIMPIAR_NOTASENTREGA_DEL_EDMX.ps1`** 🧹
  - Versión PowerShell del limpiador (no usada)
  - Alternativa a limpiar_edmx.py

---

## 🧪 Tests Unitarios

### Archivo de Tests
**`NestoAPI.Tests/Infrastructure/GestorFacturacionRutasTests.cs`**

### Tests Nuevos (Grupo 2: Líneas 200-420)

1. **`FacturarRutas_PedidoNRMMantenerJuntoQueQuedaCompleto_CreaAlbaranYFactura()`**
   - Verifica el FIX principal
   - Escenario: Después de crear albarán, todas las líneas quedan albaranadas
   - Esperado: ✅ Crea albarán Y factura (antes fallaba)

2. **`FacturarRutas_PedidoNRMMantenerJuntoQueSigueIncompleto_CreaSoloAlbaranConError()`**
   - Verifica que la validación sigue funcionando
   - Escenario: Después de crear albarán, quedan líneas pendientes
   - Esperado: ✅ Crea solo albarán, NO factura, registra error

3. **`FacturarRutas_PedidoNRMMantenerJuntoTodasLineasAlbaranadasAntes_CreaAlbaranYFactura()`**
   - Test de control
   - Escenario: Todas las líneas ya estaban albaranadas antes
   - Esperado: ✅ Crea albarán Y factura (siempre funcionó)

### Ejecutar Tests
```bash
# Todos los tests
dotnet test NestoAPI.Tests/NestoAPI.Tests.csproj

# Solo tests de facturación de rutas
dotnet test --filter "FullyQualifiedName~GestorFacturacionRutasTests"

# Solo los 3 tests nuevos
dotnet test --filter "FullyQualifiedName~GestorFacturacionRutasTests.FacturarRutas_PedidoNRMMantenerJunto"
```

---

## 📝 Código Modificado

### Backend (C#)

#### Cambio Principal
**`NestoAPI/Infraestructure/Facturas/GestorFacturacionRutas.cs`**
- **Líneas 265-271:** Recarga de líneas después de crear albarán
- **Líneas 335-368:** Comentarios actualizados en ProcesarFacturaNRM

```csharp
// Línea 270: El cambio crítico
await db.Entry(pedido).Collection(p => p.LinPedidoVtas).LoadAsync();
```

#### EDMX
**`NestoAPI/Models/NestoEntities.edmx`**
- EntityType NotaEntrega corregido
- PRIMARY KEY correcta: (NºOrden, Numero)
- Propiedad "Numero" mapea a columna "NotaEntrega"

#### Archivos Generados
**`NestoAPI/Models/NotaEntrega.cs`** (auto-generado)
```csharp
public partial class NotaEntrega
{
    public int NºOrden { get; set; }
    public int Numero { get; set; }  // Mapea a "NotaEntrega" en SQL
    public DateTime Fecha { get; set; }
}
```

### Frontend (VB.NET/XAML)

#### Vista
**`Nesto/Modulos/PedidoVenta/PedidoVenta/Views/ErroresFacturacionRutasPopup.xaml`**
- Línea 6: `MinWidth="800" MinHeight="400"` (antes: tamaño fijo)
- Líneas 47-48: ScrollBars automáticos
- Líneas 50-58: Menú contextual con 3 opciones de copiado

#### Code-behind
**`Nesto/Modulos/PedidoVenta/PedidoVenta/Views/ErroresFacturacionRutasPopup.xaml.vb`**
- Líneas 37-90: 3 nuevos event handlers
  - `CopiarErrorCompleto_Click()`
  - `CopiarSoloMensaje_Click()`
  - `CopiarNumeroPedido_Click()`

---

## 📋 Checklist de Producción

### Antes de Desplegar

- [ ] **SQL:** Ejecutar `VERIFICAR_ANTES_DE_FIX_NOTASENTREGA.sql`
- [ ] **SQL:** Si OK, ejecutar `FIX_NOTAENTREGA_TABLE.sql`
- [ ] **SQL:** Verificar que PRIMARY KEY se creó correctamente
- [ ] **VS:** Compilar solución sin errores
- [ ] **VS:** Ejecutar tests unitarios (todos en verde)
- [ ] **Git:** Commit de cambios
- [ ] **Git:** Push a repositorio

### Al Desplegar

- [ ] **IIS:** Detener aplicación
- [ ] **Files:** Backup de DLLs actuales
- [ ] **Files:** Copiar nuevos binarios
- [ ] **IIS:** Iniciar aplicación
- [ ] **Test:** Crear pedido de prueba
- [ ] **Test:** Facturar ruta de prueba
- [ ] **Monitor:** Revisar logs en tiempo real

### Verificación Post-Deploy

- [ ] **Funcional:** Pedido con MantenerJunto se factura correctamente
- [ ] **Funcional:** Nota de entrega se crea sin errores
- [ ] **UI:** Ventana de errores se redimensiona
- [ ] **UI:** Menú contextual funciona
- [ ] **Logs:** No hay errores nuevos en Event Log
- [ ] **Performance:** Tiempos de respuesta normales

---

## 🆘 Troubleshooting

### Error: "NotaEntrega is not part of the model"
1. Verificar que PRIMARY KEY existe en SQL
2. Verificar que EDMX tiene EntityType NotaEntrega
3. Verificar que existe NotaEntrega.cs (no NotasEntrega.cs)
4. Rebuild Solution

### Error: "Los nombres de los miembros no pueden ser iguales que su tipo"
1. Verificar que la propiedad se llama `Numero`, no `NotaEntrega`
2. Ejecutar `renombrar_propiedad_numero.py` si es necesario
3. Clic derecho en `NestoEntities.tt` → Run Custom Tool
4. Rebuild Solution

### Error: Pedidos con MantenerJunto no se facturan
1. Verificar que el código tiene la recarga de líneas (línea 270)
2. Revisar logs: Debe aparecer "Recargando líneas del pedido..."
3. Ejecutar tests: `FacturarRutas_PedidoNRMMantenerJuntoQueQuedaCompleto`

### Ventana de errores no se abre
1. Verificar que proyecto VB está compilado
2. Verificar que no hay errores de XAML
3. Revisar Output → Debug para excepciones

---

## 📞 Soporte

### Información de Debug

**Logs relevantes:**
- Visual Studio → Output → Debug
- Buscar: "Recargando líneas del pedido"
- Buscar: "ERROR en nota de entrega"
- Buscar: "Procesando pedido"

**Tests de diagnóstico:**
```bash
# Verificar que el fix está aplicado
dotnet test --filter "FacturarRutas_PedidoNRMMantenerJuntoQueQuedaCompleto"

# Verificar estructura de NotaEntrega
# Debe tener propiedad "Numero", no "NotaEntrega"
```

### Contactos
- **Documentación completa:** Ver archivos .md en este directorio
- **Tests:** `NestoAPI.Tests/Infrastructure/GestorFacturacionRutasTests.cs`
- **Código:** `NestoAPI/Infraestructure/Facturas/GestorFacturacionRutas.cs:265-271`

---

## 📈 Métricas de Calidad

- **Cobertura de tests:** +3 tests unitarios nuevos
- **Documentación:** 1000+ líneas de documentación técnica
- **Scripts de automatización:** 4 scripts Python + 2 PowerShell
- **Backups automáticos:** Todos los scripts crean backups
- **Verificación pre-deploy:** Script SQL de verificación
- **Rollback:** Posible mediante backups del EDMX

---

**Última actualización:** 2025-01-12 17:40
**Autor:** Claude (Anthropic)
**Estado:** ✅ Listo para producción (pendiente pruebas funcionales)
