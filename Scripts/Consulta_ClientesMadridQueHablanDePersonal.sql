/*
    Clientes de la Comunidad de Madrid cuyos rapports de los ultimos 24 meses hablan de personal
    (contratar, empleadas, oficialas, buscar chica...). Primera lista de candidatos para la
    campana de contratacion de alumnos del SEPE (NestoAPI#464), sin esperar al dato nuevo de
    Clientes.Empleados.

    SOLO LECTURA. Una fila por cliente, con el rapport mas reciente que lo menciona y el trozo del
    comentario donde aparece, para que el vendedor sepa de que hablaba.

    Ajustes: @meses (ventana) y la lista de patrones del CTE Patrones.
*/

USE NV;

SET NOCOUNT ON;

DECLARE @meses int = 24;
DECLARE @desde datetime = DATEADD(month, -@meses, GETDATE());

WITH Patrones AS (
    SELECT patron FROM (VALUES
        ('%contrat%'),
        ('%busca%personal%'),
        ('%busca%chica%'),
        ('%busca%oficial%'),
        ('%emplead%'),
        ('%oficiala%'),
        ('%trabajadora%'),
        ('%plantilla%')
    ) AS p(patron)
),
RapportsConPersonal AS (
    SELECT
        s.Número       AS Cliente,
        s.Contacto,
        s.Fecha,
        s.Vendedor,
        s.Comentarios,
        p.patron,
        ROW_NUMBER() OVER (PARTITION BY s.Número ORDER BY s.Fecha DESC, s.NºOrden DESC) AS Orden,
        COUNT(*)     OVER (PARTITION BY s.Número) AS RapportsQueLoMencionan
    FROM SeguimientoCliente s
    JOIN Clientes c
      ON c.Empresa = s.Empresa AND c.[Nº Cliente] = s.Número AND c.Contacto = s.Contacto
    CROSS APPLY (SELECT TOP 1 patron FROM Patrones WHERE s.Comentarios LIKE patron) p
    WHERE s.Empresa = '1'
      AND s.Fecha >= @desde
      AND LEFT(LTRIM(c.CodPostal), 2) = '28'
)
SELECT
    r.Cliente,
    LTRIM(RTRIM(cp.Nombre))                       AS Nombre,
    LTRIM(RTRIM(cp.Población))                    AS Poblacion,
    LTRIM(RTRIM(cp.CodPostal))                    AS CP,
    LTRIM(RTRIM(cp.Teléfono))                     AS Telefono,
    CASE WHEN LEFT(LTRIM(cp.[CIF/NIF]), 1) LIKE '[A-HJ-NP-SUVW]' THEN 'Sociedad' ELSE 'Persona fisica' END AS FormaJuridica,
    cp.Estado                                     AS EstadoCliente,
    LTRIM(RTRIM(cp.Vendedor))                     AS VendedorFicha,
    r.RapportsQueLoMencionan,
    CONVERT(date, r.Fecha)                        AS FechaUltimoRapport,
    LTRIM(RTRIM(r.Vendedor))                      AS VendedorRapport,
    -- 60 caracteres antes y 120 despues de la palabra clave (el patron empieza por '%', se quita)
    REPLACE(REPLACE(SUBSTRING(r.Comentarios,
              CASE WHEN PATINDEX(r.patron, r.Comentarios) > 60 THEN PATINDEX(r.patron, r.Comentarios) - 60 ELSE 1 END,
              180), CHAR(13), ' '), CHAR(10), ' ') AS Extracto
FROM RapportsConPersonal r
JOIN Clientes cp
  ON cp.Empresa = '1' AND cp.[Nº Cliente] = r.Cliente AND cp.ClientePrincipal = 1
WHERE r.Orden = 1
  AND cp.Estado >= 0
ORDER BY r.RapportsQueLoMencionan DESC, r.Fecha DESC;
