/*
    NestoAPI#865 (Carlos 16/11/26): las dos filas de ParametrosUsuario (empresa '1', usuario '(defecto)') de la issue. Nada más: ni
    tablas ni columnas (el reenvío no repite un correo porque solo se reenvía el que el job acaba de GUARDAR, y el INSERT de
    ComprasCorreosSeguimiento ya es único por MensajeId; el rastro va en Modificaciones, que ya existe).

    1) 'Compras.RecalcularFechasVencidas' (corte 1): el job del correo de proveedores vencidos (L-V 8:00) pone una fecha nueva a las
       líneas de compra en el aire con la fecha pasada, con rastro en Modificaciones (Tabla 'CompraFechaRecalculada'), y el correo lo
       cuenta. El código va ENCENDIDO aunque la fila no exista: el script solo la deja a la vista para que apagarlo sea cambiar un valor.
         '1' (o vacío, o sin fila) = encendido.   '0' (o false/no/off/apagado) = apagado: el correo de siempre, sin tocar nada.
       Apagado de emergencia:
         UPDATE ParametrosUsuario SET Valor = '0', Usuario2 = 'Compras865', [Fecha Modificación] = GETDATE()
         WHERE Empresa = '1' AND Clave = 'Compras.RecalcularFechasVencidas' AND Usuario = '(defecto)';

    2) 'Compras.CorreosSeguimientoReenviar' (corte 3): el job 'compras-correos-seguimiento' reenvía a compras@ cada correo de agencia o
       proveedor que capta, con una cabecera de lo que se ha hecho. TEMPORAL: sin la fila (o con otro valor que '1') está APAGADO; este
       script lo crea encendido. Para apagarlo cuando ya funcione bien:
         UPDATE ParametrosUsuario SET Valor = '0', Usuario2 = 'Compras865', [Fecha Modificación] = GETDATE()
         WHERE Empresa = '1' AND Clave = 'Compras.CorreosSeguimientoReenviar' AND Usuario = '(defecto)';

    Idempotente: si una fila ya existe no la toca (no vuelve a encender lo que alguien haya apagado). Se puede lanzar antes o después
    de publicar. Sin GRANT nuevo. Se lanza como sa en la BD de Nesto.
*/

SET NOCOUNT ON;
GO

IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'Compras.RecalcularFechasVencidas' AND Usuario = '(defecto)')
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'Compras.RecalcularFechasVencidas', '(defecto)', '1', 'Compras865', GETDATE());
GO

IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'Compras.CorreosSeguimientoReenviar' AND Usuario = '(defecto)')
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'Compras.CorreosSeguimientoReenviar', '(defecto)', '1', 'Compras865', GETDATE());
GO

SELECT RTRIM(Clave) AS Clave, RTRIM(Usuario) AS Usuario, RTRIM(Valor) AS Valor, RTRIM(Usuario2) AS Usuario2, [Fecha Modificación]
FROM ParametrosUsuario
WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave IN ('Compras.RecalcularFechasVencidas', 'Compras.CorreosSeguimientoReenviar');
GO
