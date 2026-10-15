/*
    NestoAPI#613 (cupones, corte 3 de #610): tabla Cupones y producto de la línea de descuento.

    Ejecutar en SSMS contra NV como sa (CREATE TABLE: el login nuevavision no puede). Idempotente: se puede lanzar dos
    veces. Se puede lanzar ANTES o DESPUÉS de publicar la API: la tabla NO está en el EDMX (SQL directo) y, sin ella, la API
    no emite ni deja usar cupones. Después de Issue612_FacturacionCursosEventos.sql (no depende de él, pero va en ese orden).

    1. Cupones: uno por inscripción pagada (o manual, de Dirección con motivo).
       - Codigo: legible y único (EV-2610-7K3Q: EV de evento o MN de manual, año y mes, cuatro caracteres sin 0/O ni 1/I).
       - Importe: IVA incluido (= la señal). En el pedido es una línea −1 del producto CUPONEVENTO a Importe / 1,21 al 21 %.
       - ValidoDesde = día del evento; Caducidad = 6 meses después.
       - Estado: Emitido (antes del evento) → Activo → Usado (en un pedido; vuelve a Activo si se quita la línea o se borra el
         pedido antes de servirlo); Anulado (no asistió: lista del corte 1) o Caducado (job inscripciones-eventos, 7:55).
       - UsadoEnPedido/EmpresaUso/FechaUso/CanalUso (forma de venta de la línea) y EmpresaFacturaUso/FacturaUso al facturarlo.
       - FechaAvisoCorreo/CorreoAviso: el correo al cliente (interruptor Cupones:CorreoCliente, apagado).
       Una inscripción, un cupón: lo controla la API con un INSERT ... WHERE NOT EXISTS con bloqueo (sin índice filtrado).
    2. Producto CUPONEVENTO: copia de TiCKET (familia Bonificac, grupo COS, ficticio, G21) en las empresas 1 y 3, como el
       CHEQUE50_OCT26 del cheque regalo (#593). Ficticio: no va a la tienda online ni a la plantilla y no cuenta como venta.
       OJO comisiones: igual que el cheque, al ser grupo COS / familia Bonificac resta de lo vendido (decisión pendiente de
       Carlos; se cambia en la ficha del producto, sin programar).

    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID('dbo.Cupones') IS NULL
BEGIN
    CREATE TABLE dbo.Cupones (
        Id int IDENTITY(1, 1) NOT NULL,
        Codigo varchar(20) NOT NULL,
        Empresa char(3) NOT NULL,
        Cliente char(10) NOT NULL,                      -- código de cliente: lo puede usar cualquier contacto del cliente
        Contacto char(3) NULL,                          -- el de la inscripción (informativo)
        Importe decimal(10, 2) NOT NULL,                -- IVA incluido
        Origen varchar(10) NOT NULL,                    -- Evento o Manual
        EventoInscripcionId int NULL,
        EventoId int NULL,
        EmpresaFacturaOrigen char(3) NULL,              -- la factura (exenta) del curso
        FacturaOrigen char(10) NULL,
        Motivo nvarchar(500) NULL,                      -- obligatorio en los manuales
        FechaEmision datetime NOT NULL CONSTRAINT DF_Cupones_FechaEmision DEFAULT (GETDATE()),
        ValidoDesde date NOT NULL,
        Caducidad date NOT NULL,
        Estado varchar(10) NOT NULL CONSTRAINT DF_Cupones_Estado DEFAULT ('Emitido'),
        EmpresaUso char(3) NULL,
        UsadoEnPedido int NULL,
        EmpresaFacturaUso char(3) NULL,
        FacturaUso char(10) NULL,
        FechaUso datetime NULL,
        CanalUso varchar(20) NULL,
        MotivoAnulacion nvarchar(200) NULL,
        FechaAvisoCorreo datetime NULL,
        CorreoAviso nvarchar(200) NULL,
        Usuario varchar(50) NOT NULL CONSTRAINT DF_Cupones_Usuario DEFAULT (SUSER_SNAME()),
        FechaModificacion datetime NOT NULL CONSTRAINT DF_Cupones_Fecha DEFAULT (GETDATE()),
        CONSTRAINT PK_Cupones PRIMARY KEY (Id),
        CONSTRAINT UQ_Cupones_Codigo UNIQUE (Codigo),
        CONSTRAINT FK_Cupones_Inscripciones FOREIGN KEY (EventoInscripcionId) REFERENCES dbo.EventosInscripciones (Id),
        CONSTRAINT FK_Cupones_Eventos FOREIGN KEY (EventoId) REFERENCES dbo.Eventos (Id),
        CONSTRAINT CK_Cupones_Estado CHECK (Estado IN ('Emitido', 'Activo', 'Usado', 'Anulado', 'Caducado')),
        CONSTRAINT CK_Cupones_Origen CHECK (Origen IN ('Evento', 'Manual')),
        CONSTRAINT CK_Cupones_Importe CHECK (Importe > 0),
        CONSTRAINT CK_Cupones_Fechas CHECK (ValidoDesde <= Caducidad),
        CONSTRAINT CK_Cupones_MotivoManual CHECK (Origen <> 'Manual' OR LEN(ISNULL(Motivo, '')) > 0),
        CONSTRAINT CK_Cupones_Uso CHECK (Estado <> 'Usado' OR UsadoEnPedido IS NOT NULL)
    );
    CREATE INDEX IX_Cupones_Cliente ON dbo.Cupones (Empresa, Cliente, Estado);
    CREATE INDEX IX_Cupones_Pedido ON dbo.Cupones (UsadoEnPedido);
    CREATE INDEX IX_Cupones_Inscripcion ON dbo.Cupones (EventoInscripcionId);
    CREATE INDEX IX_Cupones_Estado ON dbo.Cupones (Estado, ValidoDesde, Caducidad);
END
GO

-- BD de negocio: la API corre con la cuenta de máquina de RDS2016
GRANT SELECT, INSERT, UPDATE ON dbo.Cupones TO [NUEVAVISION\RDS2016$];
GO

------------------------------------------------------------------------------------------------
-- Producto de la línea del cupón: CUPONEVENTO, copia de TiCKET (empresas 1 y 3)
------------------------------------------------------------------------------------------------
INSERT INTO dbo.Productos (Empresa, [Número], Nombre, Grupo, PVP, [IVA Soportado], [IVA Repercutido], Comentarios, Estado,
       CodBarras, [Aplicar Dto], SubGrupo, [Tamaño], UnidadMedida, Familia, Foto, PrecioMedio, Ficticio, FechaInicio, FechaFinal,
       NumeroSesiones, MateriaPrima, UnidadesPorEtiqueta, VariasOpciones, Ubicar, ProductoAnterior, GestionStockManual,
       RoturaStockProveedor, ProductosPorCaja, CantidadMinimaPedido, Revisado, ComentariosFactura, NecesitaNumSerie, ExclusivoProfesional)
SELECT e.Empresa, 'CUPONEVENTO', N'CUPÓN DESCUENTO EVENTO', t.Grupo, 0, t.[IVA Soportado], 'G21',
       N'NestoAPI#613: cupón de evento (línea negativa del descuento, una por pedido).', t.Estado,
       NULL, 0, t.SubGrupo, t.[Tamaño], t.UnidadMedida, t.Familia, NULL, 0, 1, NULL, NULL,
       t.NumeroSesiones, t.MateriaPrima, t.UnidadesPorEtiqueta, t.VariasOpciones, t.Ubicar, NULL, t.GestionStockManual,
       t.RoturaStockProveedor, t.ProductosPorCaja, t.CantidadMinimaPedido, t.Revisado, NULL, t.NecesitaNumSerie, t.ExclusivoProfesional
FROM dbo.Productos t
     CROSS JOIN (SELECT '1' AS Empresa UNION ALL SELECT '3') e
WHERE t.Empresa = '1' AND t.[Número] = 'TiCKET'
      AND NOT EXISTS (SELECT 1 FROM dbo.Productos x WHERE x.Empresa = e.Empresa AND x.[Número] = 'CUPONEVENTO');

SELECT @@ROWCOUNT AS ProductosCreadosAhora;
GO

-- Comprobación
SELECT 'Cupones' AS Tabla, COUNT(*) AS Filas FROM dbo.Cupones;
SELECT RTRIM(Empresa) Empresa, RTRIM([Número]) Producto, Nombre, PVP, Ficticio, Familia, Grupo, [IVA Repercutido]
FROM dbo.Productos WHERE [Número] = 'CUPONEVENTO';
GO
