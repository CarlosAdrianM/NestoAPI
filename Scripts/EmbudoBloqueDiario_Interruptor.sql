/*
    Embudo de Odoo, bloque diario (Carlos 11/11/26): enciende el job «embudo-bloque-diario» (todos los días a las 7:00 de Madrid,
    el siguiente bloque NV-AAAA-MM-NN de 50 clientes, por el mismo camino que el lanzamiento a mano).
    Interruptor: ParametrosUsuario empresa '1', usuario '(defecto)', clave 'EmbudoBloqueDiario'. Solo con la fila y valor '1' lanza;
    sin la fila (antes de este script) no hace nada.
    Para pararlo sin publicar:
        UPDATE ParametrosUsuario SET Valor = '0', [Fecha Modificación] = GETDATE()
        WHERE Empresa = '1' AND Clave = 'EmbudoBloqueDiario' AND Usuario = '(defecto)';
    Idempotente. Lanzar DESPUÉS de publicar la API (antes, la fila no hace nada). Sin GRANT nuevo.
*/

SET NOCOUNT ON;

IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'EmbudoBloqueDiario' AND Usuario = '(defecto)')
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'EmbudoBloqueDiario', '(defecto)', '1', 'EmbudoDiario', GETDATE());
ELSE
    UPDATE ParametrosUsuario SET Valor = '1', Usuario2 = 'EmbudoDiario', [Fecha Modificación] = GETDATE()
    WHERE Empresa = '1' AND Clave = 'EmbudoBloqueDiario' AND Usuario = '(defecto)' AND RTRIM(Valor) <> '1';
GO

SELECT RTRIM(Usuario) AS Usuario, RTRIM(Valor) AS Valor FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'EmbudoBloqueDiario';
GO
