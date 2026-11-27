/*
    NestoAPI#914, correo semanal de calidad (Carlos 27/11/26): «un correo automático cada lunes, SOLO A ÉL (no a Dirección), con el resumen».

    Crea VACÍA la fila de ParametrosUsuario empresa '1', «(defecto)», clave «Calidad.CorreoResumenSemanal»: el destinatario del correo.
    Mientras esté vacía (o no exista) el job de los lunes (7:30, «calidad-correo-resumen-semanal») NO manda nada y deja una línea en ELMAH una
    sola vez. Para encenderlo, poner UNA dirección (sin comas ni punto y coma; una lista no vale) en Valor:

        UPDATE ParametrosUsuario SET Valor = '<la dirección>', Usuario2 = 'Issue914r', [Fecha Modificación] = GETDATE()
        WHERE Empresa = '1' AND Clave = 'Calidad.CorreoResumenSemanal' AND Usuario = '(defecto)';

    (O con PUT api/ParametrosUsuario desde una sesión de Dirección/Informática, que es lo que se haría sin SQL.)

    No hace falta ninguna tabla nueva: el envío de cada semana se apunta en dbo.Modificaciones (Tabla «CalidadCorreoResumenSemanal»,
    Anterior «Semana yyyy-MM-dd»), que la API ya lee y en la que ya escribe. Probarlo sin mandar nada:
    POST api/Calidad/ResumenSemanal/Enviar?ensayo=true.

    Idempotente: si la fila ya existe (vacía o con una dirección), no se toca. Se puede lanzar con la cuenta de siempre (no crea objetos).
    Vuelta atrás: DELETE de esa fila (el job vuelve a no mandar nada).
*/

SET NOCOUNT ON;
GO

IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'Calidad.CorreoResumenSemanal' AND Usuario = '(defecto)')
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'Calidad.CorreoResumenSemanal', '(defecto)', '', 'Issue914r', GETDATE());
GO

-- Comprobación (solo lectura)
SELECT RTRIM(Clave) AS Clave, RTRIM(Usuario) AS Usuario, RTRIM(ISNULL(Valor, '')) AS Valor, [Fecha Modificación]
FROM ParametrosUsuario
WHERE Empresa = '1' AND Clave = 'Calidad.CorreoResumenSemanal';
GO
