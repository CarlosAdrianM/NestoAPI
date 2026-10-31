/*
    23/10/26: Paloma inscribió de prueba a la clienta 41196 en el evento 2 («Formación Agenda proactiva») a las 10:16 y la
    anuló ella misma a las 10:22. Es la inscripción Id 1 (la única de la tabla): sin pago, sin señal, sin pedido, sin cupón,
    sin propuesta de señal y sin datos de curso (comprobado antes de lanzarlo). Lanzado por Carlos el 23/10/26 a las 10:28: HECHO.

    Se borra solo si sigue siendo exactamente esa fila y no tiene nada colgando; si no, no se toca nada.
    Ejecutar en SSMS contra NV como sa.
*/
SET NOCOUNT ON;
USE NV;

SELECT 'ANTES' AS Momento, Id, EventoId, RTRIM(Cliente) AS Cliente, Estado, ImportePagado, PagoTPVId, EventoSenalId, UsuarioCreacion, FechaCreacion
FROM dbo.EventosInscripciones WHERE Id = 1;

BEGIN TRANSACTION;

DECLARE @dependientes int =
      (SELECT COUNT(*) FROM dbo.EventosInscripcionesCurso WHERE InscripcionId = 1)
    + (SELECT COUNT(*) FROM dbo.EventosSenalesPropuestas WHERE InscripcionId = 1)
    + (SELECT COUNT(*) FROM dbo.Cupones WHERE EventoInscripcionId = 1);

DELETE FROM dbo.EventosInscripciones
WHERE Id = 1 AND EventoId = 2 AND RTRIM(Cliente) = '41196' AND Estado = 'Anulada'
  AND ImportePagado IS NULL AND PagoTPVId IS NULL AND EventoSenalId IS NULL
  AND @dependientes = 0;
DECLARE @borradas int = @@ROWCOUNT;

IF @borradas = 1
BEGIN
    COMMIT TRANSACTION;
    SELECT 'HECHO: borrada la inscripción de prueba 1 (clienta 41196, evento 2).' AS Resultado;
END
ELSE
BEGIN
    ROLLBACK TRANSACTION;
    SELECT 'DESHECHO: la inscripción 1 ya no es la de prueba o tiene algo colgando (dependientes: '
        + CAST(@dependientes AS varchar(10)) + '). No se ha cambiado nada.' AS Resultado;
END

SELECT 'DESPUÉS' AS Momento, COUNT(*) AS QuedanConId1 FROM dbo.EventosInscripciones WHERE Id = 1;
