# Configuración Verifacti

## API Keys

### Sandbox (Pruebas)
```
vf_test_qESaoRRR7Oq2MJgyTeVcc07WoeDcogPb4vpVA9lYYFo=
```

### Producción
```
(pendiente de obtener)
```

## Endpoints

- **Base URL**: https://api.verifacti.com/
- **Crear factura**: POST /verifactu/create
- **Modificar factura**: PUT /verifactu/modify
- **Anular factura**: POST /verifactu/cancel
- **Consultar estado**: GET /verifactu/status/{uuid}
- **Bulk (hasta 50)**: POST /verifactu/create_bulk

## Autenticación

Header: `Authorization: Bearer {API_KEY}`

## Notas importantes

- Sandbox retiene datos 90 días
- Máximo 12 líneas por factura → Agrupar por tipo de IVA
- Formato fecha: DD-MM-YYYY
- La fecha de expedición debe ser la fecha actual

---

## DISEÑO APROBADO (27/11/2025)

### Series de factura - Configuración final

| Serie | Tramita Verifactu | Tipo | Descripción | Serie Rectificativa |
|-------|-------------------|------|-------------|---------------------|
| **NV** | ✅ SÍ | F1 | Venta de productos | RV |
| **CV** | ✅ SÍ | F1 | Servicios de formación | RC |
| **RV** | ✅ SÍ | R1/R3/R4 | Rectificativas de NV | - |
| **RC** | ✅ SÍ | R1/R3/R4 | Rectificativas de CV | - |
| **GB** | ❌ NO | - | Empresa 3, interno | - |
| **EV** | ❌ ELIMINAR | - | → Pasa a NV | - |
| **UL** | ❌ ELIMINAR | - | → Pasa a NV | - |
| **VC** | ❌ ELIMINAR | - | → Pasa a NV | - |
| **DV** | ❌ ELIMINAR | - | → Rectificativas R3 van a RV | - |

### Tipos de Rectificativa (campo TipoRectificativa)

| Valor | Significado | Uso | Default |
|-------|-------------|-----|---------|
| **R1** | Art. 80.1, 80.2, 80.6 LIVA | Devolución de productos | ✅ SÍ |
| **R3** | Art. 80.4 LIVA | Deuda incobrable (>1 año) | NO |
| **R4** | Resto | Error en factura | NO |

---

## CALENDARIO APROBADO

### SEMANA 1: 27 nov - 4 dic
- 27/11 (Jue): ✅ Análisis + API key sandbox
- 28/11 (Vie): 🟡 BUFFER
- 01/12 (Lun): Implementar ISerieFacturaVerifactu + diccionario series
- 02/12 (Mar): Crear series RV y RC + SQL campos CabPedidoVta
- 03/12 (Mié): SQL campos CabFacturaVta + tabla LinFacturaVtaRectificacion
- 04/12 (Jue): Actualizar EDMX + modificar ServicioFacturas (persistir datos cliente)
- 05/12 (Vie): 🟡 BUFFER

### SEMANA 2: 9-11 dic
- 09/12 (Mar): Crear ServicioVerifacti (cliente HTTP + DTOs)
- 10/12 (Mié): Implementar envío factura F1 a sandbox + guardar respuesta
- 11/12 (Jue): Modificar RDLC para incluir QR + probar impresión
- 12/12 (Vie): 🟡 BUFFER

### SEMANA 3: 15-18 dic
- 15/12 (Lun): Implementar envío rectificativas (R1/R3/R4) a Verifacti
- 16/12 (Mar): Lógica búsqueda facturas originales por producto/cantidad
- 17/12 (Mié): Guardar vinculaciones en LinFacturaVtaRectificacion
- 18/12 (Jue): UI en Nesto WPF: botón crear rectificativa completa
- 19/12 (Vie): 🟡 BUFFER

### SEMANA 4: 22-23 dic
- 22/12 (Lun): Eliminar series EV, UL, VC, DV + testing integral
- 23/12 (Mar): Corrección de bugs críticos
- 24-25/12: 🎄 FESTIVOS

### SEMANA 5: 26-30 dic
- 26/12 (Jue): Despliegue a producción (sandbox Verifacti)
- 27/12 (Vie): 🟡 BUFFER
- 29/12 (Lun): Cambiar a API producción Verifacti
- 30/12 (Mar): Monitorización + correcciones finales
- 31/12 (Mié): ❌ No laborable

---

## CAMBIOS EN BASE DE DATOS

### CabPedidoVta (campo nuevo)
```sql
ALTER TABLE CabPedidoVta ADD TipoRectificativa CHAR(2) NULL;
-- Valores: R1, R3, R4 (NULL = no es rectificativa)
```

### CabFacturaVta (campos nuevos)
```sql
ALTER TABLE CabFacturaVta ADD
    NombreFiscal NVARCHAR(100) NULL,
    CifNif VARCHAR(20) NULL,
    DireccionFiscal NVARCHAR(200) NULL,
    CodPostalFiscal VARCHAR(10) NULL,
    PoblacionFiscal NVARCHAR(100) NULL,
    ProvinciaFiscal VARCHAR(50) NULL,
    VerifactuUUID VARCHAR(50) NULL,
    VerifactuHuella VARCHAR(100) NULL,
    VerifactuQR NVARCHAR(MAX) NULL,
    VerifactuURL VARCHAR(500) NULL,
    VerifactuEstado VARCHAR(50) NULL,
    TipoRectificativa CHAR(2) NULL;
```

### LinFacturaVtaRectificacion (tabla nueva)
```sql
CREATE TABLE LinFacturaVtaRectificacion (
    Empresa CHAR(1) NOT NULL,
    NumeroFactura VARCHAR(15) NOT NULL,
    NumeroLinea INT NOT NULL,
    FacturaOriginalNumero VARCHAR(15) NOT NULL,
    FacturaOriginalLinea INT NOT NULL,
    CantidadRectificada DECIMAL(18,4) NOT NULL,
    CONSTRAINT PK_LinFacturaVtaRectificacion
        PRIMARY KEY (Empresa, NumeroFactura, NumeroLinea,
                     FacturaOriginalNumero, FacturaOriginalLinea)
);
```
