# Guía de Pruebas - Mañana 13 Enero 2025

**Objetivo:** Verificar que los 3 fixes funcionan correctamente en entorno real

---

## 🚀 Preparación Rápida

### Antes de empezar
1. ✅ Solución compila sin errores
2. ✅ PRIMARY KEY agregada en SQL (tabla NotasEntrega)
3. ✅ Tests unitarios pasan (todos en verde)
4. ⏳ **PENDIENTE:** Pruebas funcionales en entorno real

---

## 🧪 Plan de Pruebas

### Prueba 1: Pedido con MantenerJunto ⭐ (CRÍTICO)

**Objetivo:** Verificar que ahora SÍ se factura después de crear albarán

**Pasos:**
1. Crear pedido NRM (Normal) con estos datos:
   - Cliente: Cualquiera
   - Periodo facturación: **NRM**
   - **MantenerJunto: ✅ SÍ (marcar checkbox)**
   - Ruta: Cualquier ruta propia (ej: "AM" - Almacén para pruebas)

2. Agregar 2 líneas al pedido:
   - **Línea 1:** Producto con stock disponible (se albaranará)
   - **Línea 2:** Producto con stock disponible (se albaranará)
   - Ambas con **Visto Bueno = ✅**

3. Guardar pedido y asignar picking a ambas líneas

4. Facturar ruta (botón "Facturar Rutas")

5. **Verificar:**
   ```
   ✅ Se crea albarán
   ✅ Se crea factura (ANTES FALLABA - debe funcionar ahora)
   ❌ NO aparece en ventana de errores
   ```

6. **Revisar logs en Visual Studio:**
   - Output → Debug
   - Buscar: "Recargando líneas del pedido desde BD..."
   - Buscar: "Líneas recargadas. Estados actuales: ..."
   - Verificar que aparecen los estados actualizados

**Resultado esperado:**
- ✅ Albarán creado
- ✅ Factura creada
- ✅ Logs muestran recarga de líneas

**Si falla:**
- Revisar que el código tiene la línea 270: `await db.Entry(pedido).Collection(p => p.LinPedidoVtas).LoadAsync();`
- Ejecutar test: `FacturarRutas_PedidoNRMMantenerJuntoQueQuedaCompleto`

---

### Prueba 2: Pedido con MantenerJunto (caso negativo)

**Objetivo:** Verificar que la validación sigue funcionando

**Pasos:**
1. Crear pedido NRM con:
   - **MantenerJunto: ✅ SÍ**
   - Ruta: Cualquiera

2. Agregar 2 líneas:
   - **Línea 1:** Con picking asignado (se albaranará)
   - **Línea 2:** SIN picking (NO se albaranará)
   - Ambas con Visto Bueno

3. Facturar ruta

4. **Verificar:**
   ```
   ✅ Se crea albarán (solo de línea 1)
   ❌ NO se crea factura (correcto)
   ✅ Aparece en ventana de errores
   ✅ Error dice: "MantenerJunto=1 y hay 1 línea(s) sin albarán"
   ```

**Resultado esperado:**
- ✅ Albarán creado
- ❌ Factura NO creada (correcto)
- ✅ Error registrado en ventana

---

### Prueba 3: Nota de Entrega ⭐ (CRÍTICO)

**Objetivo:** Verificar que se puede crear sin error "NotaEntrega is not part of the model"

**Pasos:**
1. Crear pedido con:
   - **NotaEntrega: ✅ SÍ (marcar checkbox)**
   - Periodo: Cualquiera
   - Cliente: Cualquiera
   - Ruta: Cualquiera

2. Agregar líneas con visto bueno y picking

3. Facturar ruta

4. **Verificar:**
   ```
   ✅ Se crea nota de entrega (ANTES FALLABA)
   ❌ NO aparece error en ventana de errores
   ❌ NO hay error en logs de Visual Studio
   ```

**Resultado esperado:**
- ✅ Nota de entrega creada
- ✅ Sin errores

**Si falla:**
- Verificar PRIMARY KEY en SQL: `SELECT * FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS WHERE TABLE_NAME = 'NotasEntrega'`
- Verificar que existe archivo `NotaEntrega.cs` (no `NotasEntrega.cs`)
- Verificar que la clase tiene propiedad `Numero`, no `NotaEntrega`

---

### Prueba 4: Ventana de errores - UX

**Objetivo:** Verificar mejoras de interfaz

**Pasos:**
1. Crear varios pedidos con errores (ej: sin visto bueno)

2. Facturar ruta → Aparecerá ventana de errores

3. **Verificar:**
   ```
   ✅ Ventana se puede redimensionar
   ✅ Al maximizar, el DataGrid se ajusta y ocupa toda la ventana
   ✅ Se puede ver el mensaje de error completo
   ```

4. **Verificar menú contextual:**
   - Clic derecho sobre un error
   - Debe aparecer menú con 3 opciones:
     - "Copiar error completo"
     - "Copiar solo mensaje"
     - "Copiar número de pedido"

5. Seleccionar "Copiar error completo"

6. Pegar en Notepad (Ctrl+V)

7. **Verificar:**
   ```
   ✅ Se copió el error con formato:
   Pedido: 12345
   Cliente: 1001 (Nombre Cliente)
   Ruta: AM
   Periodo: NRM
   Fecha Entrega: 12/01/2025
   Total: 150,00 €
   Tipo de Error: Visto Bueno
   Mensaje: El pedido tiene líneas sin visto bueno...
   ```

**Resultado esperado:**
- ✅ Ventana redimensionable
- ✅ DataGrid se ajusta
- ✅ Menú contextual funciona
- ✅ Copiar al portapapeles funciona

---

## 📊 Checklist de Verificación

Al terminar las pruebas, marcar:

### Funcionalidad Principal
- [ ] ✅ Pedido con MantenerJunto se factura después de crear albarán
- [ ] ✅ Validación de MantenerJunto sigue funcionando (caso negativo)
- [ ] ✅ Notas de entrega se crean sin error
- [ ] ✅ Logs muestran recarga de líneas del pedido

### UX - Ventana de Errores
- [ ] ✅ Ventana es redimensionable
- [ ] ✅ DataGrid se ajusta al tamaño
- [ ] ✅ Menú contextual aparece
- [ ] ✅ "Copiar error completo" funciona
- [ ] ✅ "Copiar solo mensaje" funciona
- [ ] ✅ "Copiar número de pedido" funciona

### Performance
- [ ] ✅ Facturación no es más lenta que antes
- [ ] ✅ No hay nuevos errores en logs
- [ ] ✅ No hay excepciones no manejadas

---

## 🐛 Si encuentras bugs

### Reportar:
1. **Qué:** Descripción del problema
2. **Cuándo:** En qué paso de las pruebas ocurrió
3. **Logs:** Copiar logs de Visual Studio → Output → Debug
4. **Error:** Si aparece ventana de error, copiar el mensaje completo
5. **Datos:** Número de pedido, cliente, ruta

### Documentar en:
- Crear archivo: `BUGS_ENCONTRADOS_2025-01-13.md`
- Incluir toda la información anterior
- Agregar capturas de pantalla si es posible

---

## 🎉 Si todo funciona

### Celebrar 🎊
1. Marcar todos los checkboxes de arriba
2. Crear archivo: `PRUEBAS_EXITOSAS_2025-01-13.md`
3. Documentar:
   - Hora de inicio y fin de pruebas
   - Pedidos de prueba usados (números)
   - Capturas de pantalla de resultados
   - Cualquier observación

### Siguiente paso
- ✅ Marcar como "Listo para producción"
- ✅ Hacer commit final
- ✅ Push a repositorio
- ✅ Desplegar en producción (siguiendo checklist de `INDICE_SESION_2025-01-12.md`)

---

## 📞 Ayuda Rápida

### Archivos de referencia:
- **Resumen ejecutivo:** `RESUMEN_SESION_2025-01-12.md`
- **Índice completo:** `INDICE_SESION_2025-01-12.md`
- **Detalle técnico:** `SESION_FACTURACION_RUTAS_FIX_MANTENER_JUNTO_Y_NOTASENTREGA.md`
- **Problema NotasEntrega:** `SOLUCION_NOTASENTREGA_PRIMARY_KEY.md`

### Tests unitarios:
```bash
dotnet test --filter "GestorFacturacionRutasTests"
```

### Logs:
- Visual Studio → Output → Debug
- Buscar: "Recargando líneas", "ERROR", "CRÍTICO"

---

## ⏱️ Tiempo estimado

- Preparación: 5 minutos
- Prueba 1 (MantenerJunto): 10 minutos
- Prueba 2 (MantenerJunto negativo): 5 minutos
- Prueba 3 (Nota de Entrega): 5 minutos
- Prueba 4 (UX Ventana): 5 minutos
- Documentación: 10 minutos

**Total:** ~40 minutos

---

**Buena suerte con las pruebas! 🚀**

**Fecha:** 13 Enero 2025
**Preparado:** 12 Enero 2025 17:45
