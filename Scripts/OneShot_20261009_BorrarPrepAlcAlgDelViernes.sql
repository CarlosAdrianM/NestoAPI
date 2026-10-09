/*
    09/10/26 (auditoría de la tarde): el job «reposiciones-automaticas» rellenó el viernes a las 10:25 una reposición de
    ALCOBENDAS → ALGETE (8 líneas, 20 unidades: toallas, muestras…) en el diario de salida de Alcobendas (RepoAlcAlg,
    Almacén = ALG, sin número de traspaso, Estado 3 = en preparación). Nadie la ha enviado. Es la que hizo el JOB (usuario
    ReposicionAutomatica), no la de Paloma ni la de Andre.

    Si sigue ahí el martes a las 9:00, el job encontrará «ya hay una reposición en preparación de ALC a ALG» y apuntará la
    del martes como OMITIDA (aviso en ELMAH): Paloma vería en «Enviar reposición» la del viernes como si fuera la del día,
    sin las necesidades del fin de semana. Se borran las 8 líneas para que el martes se rellene la del día.

    Las tiendas no tienen control de ubicaciones: estas líneas no tienen huecos reservados ni nada más que deshacer. La
    cabecera 2 de ReposicionesTraspasos (ALC → ALG, corte 09/10 10:00, sin traspaso) se queda como histórico: solo cuenta
    para el corte del viernes.

    SSMS contra NV, como sa. COMMIT a mano si cuadra (8 líneas, 20 unidades).
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
USE NV;
GO

SELECT 'Antes' Momento, RTRIM(p.[Número]) Ref, LEFT(RTRIM(pr.Nombre), 40) Nombre, p.Cantidad, LEFT(p.Usuario, 22) Usuario, CONVERT(varchar(16), p.[Fecha Modificación], 120) Creada
FROM PreExtrProducto p LEFT JOIN Productos pr ON pr.Empresa = p.Empresa AND pr.[Número] = p.[Número]
WHERE p.Empresa = '1' AND p.Diario = 'RepoAlcAlg' AND p.[Almacén] = 'ALG' AND p.NºTraspaso IS NULL AND p.Estado = 3;

BEGIN TRANSACTION;

DELETE PreExtrProducto
WHERE Empresa = '1' AND Diario = 'RepoAlcAlg' AND [Almacén] = 'ALG' AND NºTraspaso IS NULL AND Estado = 3
  AND Usuario LIKE '%ReposicionAutomatica%';
SELECT @@ROWCOUNT Borradas;   -- 8

SELECT 'Quedan en RepoAlcAlg sin traspaso' Que, COUNT(*) N FROM PreExtrProducto WHERE Empresa = '1' AND Diario = 'RepoAlcAlg' AND NºTraspaso IS NULL;
-- Debe salir 0.

-- Si cuadra: COMMIT. Si no: ROLLBACK.
-- COMMIT TRANSACTION;
-- ROLLBACK TRANSACTION;
