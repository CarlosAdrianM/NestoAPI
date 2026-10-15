/*
    NestoAPI#612 (cupones, corte 2 de #610): la señal confirmada crea el pedido del curso, lo factura exento, liquida la
    señal y reserva la plaza, sola.

    Ejecutar en SSMS contra NV como sa (CREATE TABLE: el login nuevavision no puede). Idempotente: se puede lanzar dos
    veces. Se puede lanzar ANTES o DESPUÉS de publicar la API: la tabla NO está en el EDMX (SQL directo) y todo va detrás
    del interruptor Eventos:FacturarCursoAlCobrar del Web.config, que nace APAGADO.

    EventosInscripcionesCurso: lo hecho al facturar el curso de cada inscripción, para seguir desde donde se quedó si algo
    falla (prdCrearAlbaránVta y prdCrearFacturaVta abren su propia transacción y la factura sale a Verifactu: no cabe todo
    en una transacción). Una fila por inscripción.
      - Pedido / EmpresaPedido: el pedido del curso (se apunta en la misma transacción que se crea el pedido).
      - Factura / EmpresaFactura: la factura exenta (CV). Nunca se repite.
      - EstadoLiquidacion: Pendiente, Liquidada (señal o cobro del enlace liquidado contra el efecto de la factura) o
        SinCobro (efectivo, datáfono o transferencia marcada a mano: la factura queda pendiente de cobro).
      - Estado: EnCurso, Facturada, Error (lo relanza el job inscripciones-eventos cada mañana, hasta 5 intentos, o
        Administración con POST api/Eventos/Inscripciones/{id}/Facturar) o NoAutomatizable (importe distinto de la
        señal, evento sin referencia de curso…: aviso a Administración).
      - ProcesandoDesde: candado para que el webhook y el botón no lo hagan dos veces a la vez (caduca a los 15 min).

    ANTES DE ENCENDER EL INTERRUPTOR (no lo hace este script, lo decide Carlos):
      prdCrearFacturaVta comprueba prdFechaEsVálida (Ventas) y prdContabilizar (Contabilidad) con el @Usuario que se le
      pasa. Lo que hace la API sola (el cobro del enlace de pago) firma como «SenalesEventos» (Web.config
      Eventos:UsuarioAutomatico) y ese usuario no tiene fechas en FechasPermitidas: sin ellas, la factura falla con
      «Fecha no permitida» (se queda en Error y se reintenta). Ejemplo, a ajustar:
        -- INSERT INTO dbo.FechasPermitidas (Empresa, Usuario, FechaInicialVta, FechaFinalVta, FechaInicialCnt, FechaFinalCnt)
        -- SELECT e.Empresa, 'SenalesEventos', '20261001', '20270630', '20261001', '20270630'
        -- FROM (SELECT '1' AS Empresa UNION ALL SELECT '3') e
        -- WHERE NOT EXISTS (SELECT 1 FROM dbo.FechasPermitidas f WHERE f.Empresa = e.Empresa AND f.Usuario = 'SenalesEventos');
      Lo que marca una persona (pago a mano, señal de #591) firma con su usuario, que ya tiene sus fechas.

    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID('dbo.EventosInscripcionesCurso') IS NULL
BEGIN
    CREATE TABLE dbo.EventosInscripcionesCurso (
        InscripcionId int NOT NULL,
        EmpresaPedido char(3) NOT NULL,
        Pedido int NULL,
        EmpresaFactura char(3) NULL,
        Factura char(10) NULL,
        EstadoLiquidacion varchar(20) NULL,
        NumOrdenCobro int NULL,                         -- ExtractoCliente.[Nº Orden] del cobro (señal o enlace)
        NumOrdenEfecto int NULL,                        -- ExtractoCliente.[Nº Orden] del efecto de la factura
        Estado varchar(20) NOT NULL CONSTRAINT DF_EventosInscripcionesCurso_Estado DEFAULT ('EnCurso'),
        Mensaje nvarchar(1000) NULL,
        Intentos int NOT NULL CONSTRAINT DF_EventosInscripcionesCurso_Intentos DEFAULT (0),
        ProcesandoDesde datetime NULL,
        Usuario varchar(50) NOT NULL CONSTRAINT DF_EventosInscripcionesCurso_Usuario DEFAULT (SUSER_SNAME()),
        FechaCreacion datetime NOT NULL CONSTRAINT DF_EventosInscripcionesCurso_FechaCreacion DEFAULT (GETDATE()),
        FechaModificacion datetime NOT NULL CONSTRAINT DF_EventosInscripcionesCurso_Fecha DEFAULT (GETDATE()),
        CONSTRAINT PK_EventosInscripcionesCurso PRIMARY KEY (InscripcionId),
        CONSTRAINT FK_EventosInscripcionesCurso_Inscripciones FOREIGN KEY (InscripcionId) REFERENCES dbo.EventosInscripciones (Id),
        CONSTRAINT CK_EventosInscripcionesCurso_Estado CHECK (Estado IN ('EnCurso', 'Facturada', 'Error', 'NoAutomatizable')),
        CONSTRAINT CK_EventosInscripcionesCurso_Liquidacion CHECK (EstadoLiquidacion IS NULL OR EstadoLiquidacion IN ('Pendiente', 'Liquidada', 'SinCobro'))
    );
    CREATE INDEX IX_EventosInscripcionesCurso_Estado ON dbo.EventosInscripcionesCurso (Estado, Intentos);
    CREATE INDEX IX_EventosInscripcionesCurso_Pedido ON dbo.EventosInscripcionesCurso (Pedido);
END
GO

-- BD de negocio: la API corre con la cuenta de máquina de RDS2016
GRANT SELECT, INSERT, UPDATE ON dbo.EventosInscripcionesCurso TO [NUEVAVISION\RDS2016$];
GO

-- Comprobación
SELECT 'EventosInscripcionesCurso' AS Tabla, COUNT(*) AS Filas FROM dbo.EventosInscripcionesCurso;
SELECT Empresa, Usuario, FechaInicialVta, FechaFinalVta, FechaInicialCnt, FechaFinalCnt
FROM dbo.FechasPermitidas WHERE Usuario = 'SenalesEventos';
GO
