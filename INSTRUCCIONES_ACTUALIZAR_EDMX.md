# Instrucciones: Actualizar EDMX para NotasEntrega

**Fecha:** 2025-01-12
**Objetivo:** Sincronizar el EDMX con la nueva PRIMARY KEY de la tabla NotasEntrega

---

## ✅ YA COMPLETADO (por Claude)

1. ✅ PRIMARY KEY agregada en SQL Server
2. ✅ Archivos auto-generados viejos eliminados:
   - `NotaEntrega.cs` (eliminado)
   - `NotasEntrega.cs` (eliminado)

---

## 🎯 PASO 3: Actualizar EDMX en Visual Studio (MANUAL)

### Opción A: Actualizar desde base de datos (RECOMENDADA)

1. **Abrir Visual Studio**
   - Abrir solución `NestoAPI.sln`

2. **Abrir el EDMX**
   - Doble clic en `Models/NestoEntities.edmx`
   - Esperar a que cargue el diseñador (puede tardar)

3. **Buscar y eliminar NotasEntrega del diseñador**
   - En el diseñador visual, buscar el cuadro "NotasEntrega"
   - Clic derecho sobre él → **"Delete"** o presionar **Delete**
   - Confirmar la eliminación

4. **Actualizar desde base de datos**
   - Clic derecho en cualquier parte vacía del diseñador
   - Seleccionar **"Update Model from Database..."**

5. **En el wizard que aparece:**

   **Pestaña "Add":**
   - Expandir **"Tables"** → **"dbo"**
   - Marcar **☑ NotasEntrega**
   - Clic en **"Finish"**

6. **IMPORTANTE: Renombrar la entidad**

   El EDMX generará la entidad con nombre `NotasEntrega` (plural), pero nuestro código usa `NotaEntrega` (singular).

   - En el diseñador, clic derecho sobre el cuadro recién agregado "NotasEntrega"
   - Seleccionar **"Properties"** (o presionar F4)
   - En la ventana de propiedades:
     - **Entity Set Name:** `NotasEntregas` (PLURAL - déjalo así)
     - **Name:** Cambiar de `NotasEntrega` a `NotaEntrega` (SINGULAR)

7. **Guardar todo**
   - Menú: **File → Save All** (Ctrl+Shift+S)
   - El EDMX regenerará automáticamente `NotaEntrega.cs`

8. **Verificar el archivo generado**
   - Abrir `Models/NotaEntrega.cs`
   - Debe tener esta estructura:

   ```csharp
   public partial class NotaEntrega  // ← SINGULAR (correcto)
   {
       [Key]
       [Column("NºOrden", Order = 0)]
       public int NºOrden { get; set; }

       [Key]
       [Column("NotaEntrega", Order = 1)]
       public int Numero { get; set; }

       // ✅ Fecha ya NO tiene [Key] (correcto)
       public System.DateTime Fecha { get; set; }
   }
   ```

9. **Rebuild Solution**
   - Menú: **Build → Rebuild Solution**
   - ✅ Debe compilar sin errores

---

### Opción B: Editar el EDMX manualmente (SI OPCIÓN A FALLA)

Si la Opción A no funciona, editar el XML del EDMX directamente:

1. **Cerrar Visual Studio**

2. **Editar NestoEntities.edmx con un editor de texto**

3. **Buscar y reemplazar en TODO el archivo:**

   ```
   Buscar:    NotasEntrega
   Reemplazar: NotaEntrega
   ```

   ⚠️ EXCEPTO en estas líneas (mantener el plural):
   ```xml
   <EntitySet Name="NotasEntregas" EntityType="NVModel.NotaEntrega" />
   ```

4. **Buscar la sección de Key de NotaEntrega:**

   Buscar esto:
   ```xml
   <EntityType Name="NotaEntrega">
     <Key>
       <PropertyRef Name="NºOrden" />
       <PropertyRef Name="NotaEntrega" />
       <PropertyRef Name="Fecha" />  <!-- ❌ ELIMINAR ESTA LÍNEA -->
     </Key>
   ```

   Cambiarlo a:
   ```xml
   <EntityType Name="NotaEntrega">
     <Key>
       <PropertyRef Name="NºOrden" />
       <PropertyRef Name="NotaEntrega" />
     </Key>
   ```

5. **Guardar y abrir Visual Studio**

6. **Rebuild Solution**

---

## 🎯 Verificación Final

Después de actualizar el EDMX:

### 1. Verificar que compila
```
Build → Rebuild Solution
```
✅ 0 Errors, 0 Warnings (pueden haber warnings, pero 0 errores)

### 2. Verificar el DbSet en NestoEntities.Context.cs
```csharp
public virtual DbSet<NotaEntrega> NotasEntregas { get; set; }
//                      ↑ Singular         ↑ Plural
```

### 3. Verificar la clase NotaEntrega.cs
- ✅ Existe `Models/NotaEntrega.cs`
- ✅ `public partial class NotaEntrega`
- ✅ Solo 2 campos con `[Key]`: NºOrden y Numero
- ✅ Fecha NO tiene `[Key]`

### 4. Verificar que NO existen archivos duplicados
- ❌ NO debe existir `Models/NotasEntrega.cs` (plural)

---

## ❓ Si sigues teniendo errores

**Error: "NotaEntrega is not part of the model"**
- Verificar que `NestoEntities.Context.cs` tiene:
  ```csharp
  public virtual DbSet<NotaEntrega> NotasEntregas { get; set; }
  ```

**Error: "Cannot convert from NotaEntrega to NotasEntrega"**
- Significa que todavía existe `NotasEntrega.cs` (plural)
- Eliminar ese archivo y rebuild

**Errores en el EDMX (Error 3002)**
- Significa que la Key en el EDMX no coincide con la de SQL Server
- Repetir Opción A o usar Opción B

---

## 📝 Archivos que deben existir después

```
✅ Models/NotaEntrega.cs (auto-generado por EDMX)
✅ Models/NotaEntrega.Partial.cs (manual, con [Table])
✅ Models/NestoEntities.edmx
✅ Models/NestoEntities.Context.cs (con DbSet<NotaEntrega> NotasEntregas)
❌ Models/NotasEntrega.cs (NO debe existir)
```

---

**Última actualización:** 2025-01-12
