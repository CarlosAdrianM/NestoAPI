/*
    Embudo de Odoo, bloque diario (Carlos 11/11/26): enciende el job «embudo-bloque-diario» (todos los días a las 7:00 de Madrid,
    el siguiente bloque NV-AAAA-MM-NN de 50 clientes, por el mismo camino que el lanzamiento a mano).
    Interruptor: ParametrosUsuario empresa '1', usuario '(defecto)', clave 'EmbudoBloqueDiario'. Solo con la fila y valor '1' lanza;
    sin la fila (antes de este script) no hace nada.
    Tamaño del bloque: clave 'EmbudoTamanoBloque' (sin fila o no válido, 50; de 1 a 100, por encima se recorta a 100: el envío sale por la cuenta de Office 365 de la empresa).
    Carlos (11/11/26): se lanza en el deploy de hoy con el interruptor ENCENDIDO, para que mañana a las 7:00 salga el primer bloque.
    Para pararlo o cambiar el tamaño sin publicar ni SQL: PUT api/Clientes/Embudo/Configuracion { "Encendido": false } o
    { "TamanoBloque": 80 } (Dirección o Informática; GET para verlo).
    Idempotente: no pisa un tamaño ya cambiado. Lanzar DESPUÉS de publicar la API (antes, las filas no hacen nada). Sin GRANT nuevo.
*/

SET NOCOUNT ON;

IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'EmbudoBloqueDiario' AND Usuario = '(defecto)')
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'EmbudoBloqueDiario', '(defecto)', '1', 'EmbudoDiario', GETDATE());
ELSE
    UPDATE ParametrosUsuario SET Valor = '1', Usuario2 = 'EmbudoDiario', [Fecha Modificación] = GETDATE()
    WHERE Empresa = '1' AND Clave = 'EmbudoBloqueDiario' AND Usuario = '(defecto)' AND RTRIM(Valor) <> '1';

IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'EmbudoTamanoBloque' AND Usuario = '(defecto)')
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'EmbudoTamanoBloque', '(defecto)', '50', 'EmbudoDiario', GETDATE());
GO

SELECT RTRIM(Clave) AS Clave, RTRIM(Usuario) AS Usuario, RTRIM(Valor) AS Valor FROM ParametrosUsuario
WHERE Empresa = '1' AND Clave IN ('EmbudoBloqueDiario', 'EmbudoTamanoBloque');
GO
