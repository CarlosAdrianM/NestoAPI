/*
    NestoAPI#901 (Carlos 25/11/26, primer corte): PROYECTOS. Imputar ingresos y gastos ya contabilizados a cada proyecto (una
    convocatoria de un curso del SEPE —IMPE0210-2026-1—, una feria, un evento o una campaña) y sacar su cuenta de explotación.

    La contabilidad NO se toca: ni la tabla Contabilidad ni los procedimientos que contabilizan. Se imputa DESPUÉS de contabilizar
    (las facturas de compra se contabilizan hoy desde Nesto viejo), por IMPORTE, y cada imputación guarda una FOTO del apunte.

    Crea:
    - dbo.Proyectos: código único por empresa (≤ 20, mayúsculas), nombre, tipo (CursoSEPE, Evento, Feria, Campana, Otro), fechas
      (FechaHasta NULL = abierto sin fin), estado (Abierto/Cerrado: cerrado no admite imputaciones nuevas ni cambios), producto del
      curso y evento de Nesto opcionales (sin FK dura), presupuesto de ingresos y de gastos informativo, comentarios y auditoría.
    - dbo.ProyectosImputaciones: apunte (Empresa + Nº Orden de Contabilidad) → proyecto, con IMPORTE SIEMPRE POSITIVO (la parte del
      apunte). Si suma o resta lo dice el apunte: 6xx gasto, 7xx ingreso; al lado contrario del natural (abono) resta. La foto del
      apunte (cuenta, fecha, concepto, documento, asiento, debe, haber, tercero) para no depender de Contabilidad en los listados.
      Un apunte puede repartirse entre varios proyectos; la suma no pasa del apunte (lo controla la API, con un bloqueo por apunte).

    Puede ir ANTES o DESPUÉS de publicar la API: sin las tablas, los endpoints de api/Proyectos dan 503 y no pasa nada más.
    Lanzar como sa en NV. Idempotente: se puede relanzar. GRANT a [NUEVAVISION\RDS2016$] (como sus hermanas); Modificaciones ya la
    escribe la API (Tablas «Proyecto» y «ProyectoImputacion»).
*/
SET NOCOUNT ON;
USE NV;
GO
IF OBJECT_ID(N'dbo.Proyectos', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Proyectos (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Proyectos PRIMARY KEY CLUSTERED,
        Empresa char(3) NOT NULL,
        Codigo nvarchar(20) NOT NULL,
        Nombre nvarchar(100) NOT NULL,
        Tipo nvarchar(20) NOT NULL CONSTRAINT CK_Proyectos_Tipo CHECK (Tipo IN (N'CursoSEPE', N'Evento', N'Feria', N'Campana', N'Otro')),
        FechaDesde date NOT NULL,
        FechaHasta date NULL,
        Estado nvarchar(10) NOT NULL CONSTRAINT DF_Proyectos_Estado DEFAULT (N'Abierto')
            CONSTRAINT CK_Proyectos_Estado CHECK (Estado IN (N'Abierto', N'Cerrado')),
        Producto nvarchar(15) NULL,             -- la referencia del curso (IMPE0210), opcional
        EventoId int NULL,                      -- evento de Nesto, opcional, sin FK dura
        PresupuestoIngresos decimal(18,2) NULL, -- informativos
        PresupuestoGastos decimal(18,2) NULL,
        Comentarios nvarchar(1000) NULL,
        Usuario nvarchar(50) NOT NULL,
        FechaAlta datetime NOT NULL CONSTRAINT DF_Proyectos_FechaAlta DEFAULT (GETDATE()),
        UsuarioModificacion nvarchar(50) NULL,
        FechaModificacion datetime NULL,
        CONSTRAINT CK_Proyectos_Fechas CHECK (FechaHasta IS NULL OR FechaHasta >= FechaDesde),
        CONSTRAINT CK_Proyectos_Presupuesto CHECK (ISNULL(PresupuestoIngresos, 0) >= 0 AND ISNULL(PresupuestoGastos, 0) >= 0)
    );
    CREATE UNIQUE INDEX UX_Proyectos_Codigo ON dbo.Proyectos (Empresa, Codigo);
    CREATE INDEX IX_Proyectos_Estado ON dbo.Proyectos (Empresa, Estado) INCLUDE (Codigo, Producto);
END
GO
IF OBJECT_ID(N'dbo.Proyectos', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.ProyectosImputaciones', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProyectosImputaciones (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ProyectosImputaciones PRIMARY KEY CLUSTERED,
        ProyectoId int NOT NULL CONSTRAINT FK_ProyectosImputaciones_Proyectos REFERENCES dbo.Proyectos (Id),
        Empresa char(3) NOT NULL,
        NumeroOrdenApunte int NOT NULL,         -- Contabilidad.[Nº Orden]
        Importe decimal(18,2) NOT NULL CONSTRAINT CK_ProyectosImputaciones_Importe CHECK (Importe > 0),
        -- Foto del apunte al imputar
        Cuenta nvarchar(10) NOT NULL,
        NombreCuenta nvarchar(75) NULL,
        FechaApunte datetime NOT NULL,
        Concepto nvarchar(50) NULL,
        Documento nvarchar(10) NULL,
        Asiento int NULL,
        Debe money NOT NULL,
        Haber money NOT NULL,
        Tercero nvarchar(10) NULL,              -- proveedor o cliente del asiento, si lo hay
        TipoTercero nvarchar(10) NULL CONSTRAINT CK_ProyectosImputaciones_TipoTercero CHECK (TipoTercero IS NULL OR TipoTercero IN (N'Proveedor', N'Cliente')),
        NombreTercero nvarchar(50) NULL,
        Comentario nvarchar(200) NULL,
        Usuario nvarchar(50) NOT NULL,
        Fecha datetime NOT NULL CONSTRAINT DF_ProyectosImputaciones_Fecha DEFAULT (GETDATE())
    );
    CREATE UNIQUE INDEX UX_ProyectosImputaciones_ProyectoApunte ON dbo.ProyectosImputaciones (ProyectoId, Empresa, NumeroOrdenApunte);
    CREATE INDEX IX_ProyectosImputaciones_Apunte ON dbo.ProyectosImputaciones (Empresa, NumeroOrdenApunte) INCLUDE (ProyectoId, Importe);
END
GO
IF OBJECT_ID(N'dbo.Proyectos', N'U') IS NOT NULL
    GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.Proyectos TO [NUEVAVISION\RDS2016$];
IF OBJECT_ID(N'dbo.ProyectosImputaciones', N'U') IS NOT NULL
    GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.ProyectosImputaciones TO [NUEVAVISION\RDS2016$];
GO
-- Comprobación
SELECT CASE WHEN OBJECT_ID(N'dbo.Proyectos', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.ProyectosImputaciones', N'U') IS NOT NULL
            THEN N'OK: NestoAPI#901 (tablas Proyectos y ProyectosImputaciones)'
            ELSE N'FALTA algo' END AS Resultado;
GO
