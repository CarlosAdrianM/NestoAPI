/*
    NestoAPI#857f (19/11/26): la fecha de los correos de seguimiento en dos niveles (el aviso del proveedor y la agencia).

    Añade a dbo.ComprasCorreosSeguimiento (la de Scripts/Issue857_ComprasCorreosSeguimiento.sql):
      - EstadoEnvio nvarchar(20) NULL: el estado del envío según la PROPIA agencia (su correo o su consulta): EnCamino, EnReparto,
        Reprogramada o Entregado. NULL = no lo dice (o correo de antes de este script). Un «Entregado» no pone fecha (la recepción la
        marca el albarán), pero queda apuntado.
      - OrigenEntrega nvarchar(20) NULL: de dónde sale la entrega estimada del correo: Agencia (su correo), Proveedor (su aviso, cuerpo o
        PDF) o Consulta (se le ha preguntado a la agencia). Manda la agencia sobre el proveedor; sin ninguna, recibido + 24/48 h.
      - EntregaConsultadaEl datetime NULL: cuándo se preguntó por última vez a la agencia (job «compras-consulta-entrega-agencias»).

    Crea en ParametrosUsuario (empresa '1', usuario '(defecto)') el interruptor 'Compras.CorreosSeguimientoConsultarAgencia' a '0'
    (APAGADO) solo si no existe. '1' = el job de las 7:30 (L-V) vuelve a preguntar a las agencias que saben (hoy NINGUNA: el marco está
    listo para cuando la clase de una agencia sepa consultar, p. ej. la API oficial de DSV para Schenker). Sin fila u otro valor, apagado.
    Escribir en compras exige además 'Compras.CorreosSeguimientoAplicar' = '1'.

    ORDEN: después de Issue857, Issue857c y Issue857d (si no existe la tabla, no hace nada y lo dice). Puede ir ANTES o DESPUÉS de publicar
    la API: sin las tres columnas la API guarda como hasta ahora (sin estado ni origen) y el job de consulta no hace nada; lo vuelve a
    comprobar cada 10 minutos, sin reiniciar. Tras lanzarlo, POST api/Compras/AgenciasEntrada/Correos/Reanalizar?dias=7 para repasar los
    correos de estos días (inserta los avisos de SEUR que antes no se guardaban; no aplica, ni reenvía, ni avisa).

    Lanzar como sa en NV. Idempotente: se puede relanzar. Cada columna nueva en su propio lote. Los permisos ya los tiene la tabla
    (GRANT SELECT, INSERT, UPDATE a [NUEVAVISION\RDS2016$] en Issue857): no hace falta ninguno nuevo.
*/
SET NOCOUNT ON;
USE NV;
GO
IF OBJECT_ID(N'dbo.ComprasCorreosSeguimiento', N'U') IS NULL
    RAISERROR(N'No existe dbo.ComprasCorreosSeguimiento: lance antes Scripts/Issue857_ComprasCorreosSeguimiento.sql. No se ha cambiado nada.', 16, 1);
GO
IF OBJECT_ID(N'dbo.ComprasCorreosSeguimiento', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.ComprasCorreosSeguimiento', N'EstadoEnvio') IS NULL
    ALTER TABLE dbo.ComprasCorreosSeguimiento ADD EstadoEnvio nvarchar(20) NULL;
GO
IF OBJECT_ID(N'dbo.ComprasCorreosSeguimiento', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.ComprasCorreosSeguimiento', N'OrigenEntrega') IS NULL
    ALTER TABLE dbo.ComprasCorreosSeguimiento ADD OrigenEntrega nvarchar(20) NULL;
GO
IF OBJECT_ID(N'dbo.ComprasCorreosSeguimiento', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.ComprasCorreosSeguimiento', N'EntregaConsultadaEl') IS NULL
    ALTER TABLE dbo.ComprasCorreosSeguimiento ADD EntregaConsultadaEl datetime NULL;
GO
IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'Compras.CorreosSeguimientoConsultarAgencia' AND Usuario = '(defecto)')
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'Compras.CorreosSeguimientoConsultarAgencia', '(defecto)', '0', 'Compras857f', GETDATE());
GO
-- Comprobación (la misma que hace la API)
SELECT CASE WHEN COL_LENGTH(N'dbo.ComprasCorreosSeguimiento', N'EstadoEnvio') IS NOT NULL
             AND COL_LENGTH(N'dbo.ComprasCorreosSeguimiento', N'OrigenEntrega') IS NOT NULL
             AND COL_LENGTH(N'dbo.ComprasCorreosSeguimiento', N'EntregaConsultadaEl') IS NOT NULL
            THEN N'OK: columnas EstadoEnvio, OrigenEntrega y EntregaConsultadaEl en dbo.ComprasCorreosSeguimiento'
            ELSE N'FALTA: no están las tres columnas (¿existe la tabla?)' END AS Resultado;
GO
SELECT RTRIM(Clave) AS Clave, RTRIM(Usuario) AS Usuario, RTRIM(Valor) AS Valor FROM ParametrosUsuario
WHERE Empresa = '1' AND Clave = 'Compras.CorreosSeguimientoConsultarAgencia';
GO
