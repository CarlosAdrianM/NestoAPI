/*
    NestoAPI#831 (06/11/26) - Antes de publicar la API con f4340a9f (o justo después, fuera del horario de reparto). Ejecutar como sa en NV.

    Desde #831, para los envíos de reparto propio (agencia 14) `EnviosAgencia.FechaEntrega` es la FECHA DE REPARTO: la lista de la
    furgoneta (`GET api/RepartoPropio/Envios`), «SinCargar» y «Cargar» miran esa fecha. Hasta ahora `FechaEntrega` se grababa siempre
    como `Fecha + 1` (día de la etiqueta más uno), así que los envíos RP ya existentes dirían «reparto de mañana» y hoy no se podrían
    cargar ni saldrían en la lista del día.

    Este script deja `FechaEntrega = Fecha` en los envíos de la 14 de los últimos 30 días que no estén entregados ni devueltos
    (Estado 1 tramitado o 3 incidentado), que es lo que significaban hasta hoy (se cargan el día de la etiqueta). Los entregados (2) y
    anulados no se tocan: ya no entran en la furgoneta.

    Idempotente: solo cambia filas con FechaEntrega <> Fecha.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRAN;

SELECT COUNT(*) AFechaDeEtiqueta
FROM EnviosAgencia
WHERE Agencia = 14 AND Estado IN (1, 3) AND Fecha >= DATEADD(day, -30, CAST(GETDATE() AS date)) AND FechaEntrega <> Fecha;

UPDATE EnviosAgencia
SET FechaEntrega = Fecha
WHERE Agencia = 14 AND Estado IN (1, 3) AND Fecha >= DATEADD(day, -30, CAST(GETDATE() AS date)) AND FechaEntrega <> Fecha;

SELECT Numero, Pedido, Estado, CONVERT(varchar(10), Fecha, 120) Fecha, CONVERT(varchar(10), FechaEntrega, 120) FechaEntrega
FROM EnviosAgencia
WHERE Agencia = 14 AND Estado IN (1, 3) AND Fecha >= DATEADD(day, -7, CAST(GETDATE() AS date))
ORDER BY Numero DESC;

COMMIT;
