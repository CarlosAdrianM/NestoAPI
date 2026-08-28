/*
    Nesto#340 (A4.1) — comprobación de UNA tramitación por API, para lanzar justo después de que
    el piloto tramite el primer envío.

    Contesta a las cuatro preguntas del incidente del 28/08/2026, en orden:
      1. ¿Aceptó la agencia el envío?          -> AgenciasLlamadasWeb
      2. ¿Ha reventado algo en el servidor?     -> ELMAH
      3. ¿Se ha cerrado el envío en Nesto?      -> EnviosAgencia.Estado = 1
      4. Si llevaba reembolso, ¿está el asiento? -> Contabilidad

    Aquel día 1 y 2 dijeron que sí y que sí, y 3 dijo que no: quince envíos aceptados por GLS y
    abiertos en Nesto. Esa combinación (agencia OK + envío sin cerrar) es LA señal de alarma.
*/

SET NOCOUNT ON;
USE NV;

DECLARE @Envio int = 0;          -- <<< poner aquí el nº de envío tramitado
DECLARE @Desde datetime = DATEADD(minute, -15, GETDATE());

-- 1. Lo que se le mandó a la agencia y lo que contestó
SELECT 'AGENCIA' AS Paso, Id, RTRIM(Agencia) AS Agencia, Fecha, Exito,
       RTRIM(Usuario) AS Usuario, LEFT(RTRIM(TextoRespuestaError), 80) AS Respuesta
FROM AgenciasLlamadasWeb
WHERE Fecha >= @Desde
ORDER BY Fecha DESC;

-- 2. Errores del servidor en la misma ventana (el Limit1 salía 6 segundos después de la llamada)
SELECT 'ELMAH' AS Paso, DATEADD(hour, 2, TimeUtc) AS HoraLocal, Type,
       LEFT(Message, 90) AS Mensaje, RTRIM([User]) AS Usuario
FROM ELMAH_Error
WHERE TimeUtc >= DATEADD(minute, -15, GETUTCDATE())
ORDER BY TimeUtc DESC;

-- 3. El envío: Estado 1 = tramitado. Si sigue en 0 con la agencia diciendo OK, ALARMA.
SELECT 'ENVIO' AS Paso, Numero, Pedido, Estado, Fecha, FechaEntrega, Reembolso,
       RTRIM(CodigoBarras) AS CodigoBarras
FROM EnviosAgencia
WHERE Numero = @Envio;

-- 4. El asiento del reembolso, si lo llevaba. Debe cuadrar: 555000xx al debe, 431 al haber.
SELECT 'ASIENTO' AS Paso, c.Asiento, c.Fecha, RTRIM(c.[Nº Cuenta]) AS Cuenta,
       RTRIM(c.Concepto) AS Concepto, c.Debe, c.Haber
FROM Contabilidad c
INNER JOIN EnviosAgencia e ON e.Numero = @Envio
WHERE c.Fecha >= CONVERT(date, GETDATE())
  AND c.Concepto LIKE '%' + CAST(e.Pedido AS varchar(10)) + '%'
ORDER BY c.Asiento;

-- 5. Nada debe quedarse colgado aquí
SELECT 'PRECONTABILIDAD' AS Paso, COUNT(*) AS ApuntesSinContabilizar
FROM PreContabilidad p
INNER JOIN EnviosAgencia e ON e.Numero = @Envio
WHERE p.Concepto LIKE '%' + CAST(e.Pedido AS varchar(10)) + '%';

/*
    VUELTA ATRÁS INMEDIATA, si algo no cuadra:

    UPDATE ParametrosUsuario SET Valor = 'EF', [Fecha Modificación] = GETDATE()
    WHERE Empresa = '1' AND Clave = 'TramitarEnvioPorApi' AND Usuario = 'Enrique';

    Y si el envío se quedó aceptado por la agencia pero abierto en Nesto: NO tocarlo por SQL.
    Con ASM basta reintentar la tramitación (desde el 28/08 el "ya existe el codigo de barras"
    se trata como que ya estaba tramitada y cierra el envío, contabilizando el reembolso bien).
    Con las demás agencias todavía no hay esa red: avisar antes de tocar nada.
*/
