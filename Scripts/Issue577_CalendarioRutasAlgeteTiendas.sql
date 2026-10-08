/*
    NestoAPI#577 (corte 3b): calendario de reposiciones en los dos sentidos (decisión de Carlos, 08/10/26).

      - Algete → Reina y Reina → Algete: lunes, miércoles y viernes.
      - Algete → Alcobendas: lunes, martes y jueves (los mismos días que tenía Alcobendas → Algete: la furgoneta deja una
        y recoge otra).
      - Alcobendas → Algete: de lunes a viernes (dos días a la semana recoge sin dejar).

    Lo que había (Scripts/Issue577_ReposicionesCalendario.sql, ya en producción): REI → ALG 1/3/5 y ALC → ALG 1/2/4, cierre
    10:00 y llegada 13:30. Este script AÑADE las filas que faltan y no toca las que ya hay:
      - ALC → ALG: miércoles (3) y viernes (5).
      - ALG → REI: 1, 3, 5.
      - ALG → ALC: 1, 2, 4.

    Horas de las rutas nuevas:
      - Cierre (@HoraCierre): 10:00, como las de tienda → Algete. A esa hora el job «reposiciones-automaticas» rellena la
        reposición sola (desde Algete nace cerrada, con los huecos reservados, por recoger en Ariadna). Si Carlos quiere
        otra hora para las que salen de Algete, cambiarla aquí antes de lanzar (o después, desde el mantenimiento del
        calendario / PUT api/Reposiciones/Calendario). Tiene que caer dentro del cron del job (L-V de 6:00 a 21:55).
      - Llegada habitual (@HoraLlegada): 13:30, la misma que la de vuelta. PENDIENTE DE CONFIRMAR con Almacén para
        Algete → tienda (no hay datos de cuándo entra en la tienda).

    Idempotente: cada fila solo se inserta si no hay ya una de esa ruta, ese día y esa hora de cierre (UQ de la tabla).
    Ejecutar en SSMS contra NV (como sa o con un login que pueda escribir en ReposicionesCalendario). Se puede lanzar antes
    o después del deploy; el job solo rellena si también están los scripts del corte 3a (ReposicionesTraspasos con la
    columna Omitida, LinPedidoVta.FechaCreacion y prdRellenarReposicionStock2).
*/

SET NOCOUNT ON;
USE NV;
GO

DECLARE @HoraCierre time(0) = '10:00';     -- cierre de las rutas nuevas: cambiar aquí si se decide otra
DECLARE @HoraLlegada time(0) = '13:30';    -- llegada habitual de Algete → tienda: PENDIENTE DE CONFIRMAR

DECLARE @Filas TABLE (Origen char(3), Destino char(3), DiaSemana tinyint);
INSERT INTO @Filas (Origen, Destino, DiaSemana)
VALUES
    ('ALC', 'ALG', 3), ('ALC', 'ALG', 5),                       -- Alcobendas → Algete pasa a lunes-viernes
    ('ALG', 'REI', 1), ('ALG', 'REI', 3), ('ALG', 'REI', 5),    -- Algete → Reina: lunes, miércoles y viernes
    ('ALG', 'ALC', 1), ('ALG', 'ALC', 2), ('ALG', 'ALC', 4);    -- Algete → Alcobendas: lunes, martes y jueves

INSERT INTO dbo.ReposicionesCalendario (Empresa, AlmacenOrigen, AlmacenDestino, DiaSemana, HoraCierre, HoraLlegadaHabitual, Activo, Usuario, FechaModificacion)
SELECT '1', f.Origen, f.Destino, f.DiaSemana, @HoraCierre, @HoraLlegada, 1, 'NestoAPI#577', GETDATE()
FROM @Filas f
WHERE NOT EXISTS (SELECT 1 FROM dbo.ReposicionesCalendario c
                  WHERE c.Empresa = '1' AND c.AlmacenOrigen = f.Origen AND c.AlmacenDestino = f.Destino
                    AND c.DiaSemana = f.DiaSemana AND c.HoraCierre = @HoraCierre);

PRINT CONCAT('Filas añadidas: ', @@ROWCOUNT);
GO

------------------------------------------------------------------------------------------------
-- VERIFICACIÓN: 14 filas activas
--   ALC→ALG 1-5 · ALG→ALC 1/2/4 · ALG→REI 1/3/5 · REI→ALG 1/3/5
------------------------------------------------------------------------------------------------
SELECT AlmacenOrigen, AlmacenDestino, DiaSemana, HoraCierre, HoraLlegadaHabitual, Activo, Usuario
FROM dbo.ReposicionesCalendario
WHERE Empresa = '1'
ORDER BY AlmacenOrigen, AlmacenDestino, DiaSemana, HoraCierre;
GO
