# Solución: Error "NotaEntrega is not part of the model"

**Fecha:** 2025-01-12
**Problema:** Entity Framework lanza error al intentar insertar en `db.NotasEntregas`

---

## Diagnóstico

### Causa Raíz

La tabla `NotasEntrega` en SQL Server **NO tiene PRIMARY KEY definida**.

### Evidencia

En el archivo EDMX (`NestoEntities.edmx`) aparece este warning:

```xml
<!--Errores durante la generación:
advertencia 6002: La tabla o vista 'NV.dbo.NotasEntrega' no tiene definida ninguna
clave principal. Se ha inferido la clave y la definición se creado como una vista
o tabla de solo lectura.-->
```

Entity Framework infirió una clave primaria **incorrecta**:

```xml
<Key>
  <PropertyRef Name="NºOrden" />
  <PropertyRef Name="NotaEntrega" />
  <PropertyRef Name="Fecha" />  <!-- ❌ Fecha NO debería ser clave -->
</Key>
```

### Consecuencias

1. **Entity Framework considera la tabla como "read-only"** (solo lectura)
2. Los intentos de `db.NotasEntregas.Add()` fallan
3. El error puede manifestarse de diferentes formas:
   - "NotaEntrega is not part of the model"
   - "Cannot insert into read-only table"
   - SaveChanges falla silenciosamente

---

## Solución

### Paso 1: Agregar PRIMARY KEY en SQL Server

Ejecutar el script `FIX_NOTAENTREGA_TABLE.sql`:

```sql
USE NV
GO

ALTER TABLE [dbo].[NotasEntrega]
ADD CONSTRAINT PK_NotasEntrega PRIMARY KEY CLUSTERED
(
    [NºOrden] ASC,
    [NotaEntrega] ASC
)
GO
```

### Paso 2: Actualizar el EDMX en Visual Studio

1. Abrir `NestoEntities.edmx` en Visual Studio
2. Clic derecho en el diseñador → **"Update Model from Database"**
3. En la pestaña **"Refresh"**:
   - Expandir **"Tables"**
   - Marcar **"NotasEntrega"**
   - Clic en **"Finish"**
4. Guardar el EDMX

### Paso 3: Verificar la corrección

Después de actualizar el EDMX, el EntityType debe tener **solo 2 campos en la clave**:

```xml
<EntityType Name="NotasEntrega">
  <Key>
    <PropertyRef Name="NºOrden" />
    <PropertyRef Name="NotaEntrega" />
    <!-- ✅ Fecha ya NO está en la Key -->
  </Key>
  <Property Name="NºOrden" Type="Int32" Nullable="false" />
  <Property Name="NotaEntrega" Type="Int32" Nullable="false" />
  <Property Name="Fecha" Type="DateTime" Nullable="false" Precision="3" />
</EntityType>
```

### Paso 4: Limpiar archivos auto-generados (opcional)

El archivo `NotaEntrega.cs` actualmente tiene Data Annotations incorrectas:

```csharp
[Key]
[Column("NºOrden", Order = 0)]
public int NºOrden { get; set; }

[Key]
[Column("NotaEntrega", Order = 1)]
public int Numero { get; set; }

// ❌ Fecha NO debería tener [Key]
public System.DateTime Fecha { get; set; }
```

Después de regenerar el EDMX, este archivo debería actualizarse automáticamente.

### Paso 5: Recompilar

```bash
# En Visual Studio
Build → Rebuild Solution
```

---

## Verificación

Después de aplicar la solución, verificar que:

1. ✅ El script SQL ejecutó correctamente
2. ✅ El EDMX se actualizó sin warnings
3. ✅ El archivo `NotaEntrega.cs` tiene solo 2 campos en `[Key]`
4. ✅ La aplicación compila sin errores
5. ✅ `db.NotasEntregas.Add()` funciona correctamente

---

## Problema Similar con ExtractoRuta

Si encuentras el mismo problema con la tabla `ExtractoRuta`, aplicar la misma solución:

```sql
ALTER TABLE [dbo].[ExtractoRuta]
ADD CONSTRAINT PK_ExtractoRuta PRIMARY KEY CLUSTERED
(
    [Nº_Orden] ASC
)
GO
```

Luego actualizar el EDMX como se describe arriba.

---

## Notas Técnicas

### ¿Por qué EF infiere Fecha como clave?

Cuando una tabla NO tiene PRIMARY KEY, Entity Framework intenta inferir una clave basándose en:
1. Campos NOT NULL
2. Campos que parecen únicos
3. El orden de las columnas

Si la combinación `(NºOrden, NotaEntrega)` tiene duplicados en los datos de prueba, EF puede agregar `Fecha` para hacer la clave única.

### Database First vs. Data Annotations

En este proyecto se usa **Database First** (EDMX). Las Data Annotations en `NotaEntrega.cs` pueden causar conflictos.

**Regla:** En Database First, NO usar `[Table]`, `[Key]`, `[Column]` manualmente. Dejar que el EDMX genere todo.

Si necesitas configuración adicional, usar **Fluent API** en `OnModelCreating` (pero el EDMX puede sobreescribirlo).

---

## Referencias

- [Entity Framework 6 - Working with Keys](https://docs.microsoft.com/en-us/ef/ef6/modeling/designer/advanced/defining-query)
- [Entity Framework Warning 6002](https://docs.microsoft.com/en-us/ef/ef6/modeling/designer/advanced/edmx/msl-spec#mapping-warnings)

---

**Última actualización:** 2025-01-12
